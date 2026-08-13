"""Validate the pinned agent-interoperability baseline and optional upstream drift."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import urllib.error
import urllib.request
from collections.abc import Callable
from datetime import UTC, date, datetime
from pathlib import Path
from typing import Any


BASELINE_RELATIVE_PATH = Path("integrations/agent-interop-baseline.json")
EXPECTED_SCHEMA = "vibesnake-agent-interop-baseline-v1"
SHA256 = re.compile(r"^[0-9a-f]{64}$")
SEMVER = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+$")
UTC_TIMESTAMP = re.compile(r"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$")
ROOT_KEYS = {
    "schema",
    "reviewed_on",
    "next_review_on",
    "mcp",
    "agent_plugins",
    "agent_skill",
    "okf",
    "mcp_apps",
    "public_contract_history",
}
MCP_KEYS = {
    "protocol_version",
    "sdk_package",
    "sdk_version",
    "host_version",
    "transport",
    "session_model",
}
PLUGIN_KEYS = {
    "spec_version",
    "maturity",
    "plugin_version",
    "plugin_schema_url",
    "plugin_schema_sha256",
    "mcp_schema_url",
    "mcp_schema_sha256",
}
HOST_CONTRACT_PATHS = (
    Path("native/src/VibeSnake.AgentPlay/AgentBurstPolicy.cs"),
    Path("native/src/VibeSnake.AgentPlay/AgentContracts.cs"),
    Path("native/tools/VibeSnake.AgentHost/AgentHostContracts.cs"),
    Path("native/tools/VibeSnake.AgentHost/AgentResources.cs"),
    Path("native/tools/VibeSnake.AgentHost/McpAgentTools.cs"),
)
PLUGIN_CONTRACT_PATHS = (
    Path("integrations/vibesnake-agent-plugin/skills/play-vibesnake/SKILL.md"),
    Path("scripts/package_agent_plugin.ps1"),
)


def _reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def load_baseline(path: Path) -> dict[str, Any]:
    loaded = json.loads(
        path.read_text(encoding="utf-8"),
        object_pairs_hook=_reject_duplicate_keys,
    )
    if not isinstance(loaded, dict):
        raise ValueError("the interoperability baseline root must be an object")
    return loaded


def _absolute_date(value: object, field: str, errors: list[str]) -> date | None:
    if not isinstance(value, str):
        errors.append(f"{field} must be an absolute YYYY-MM-DD date")
        return None
    try:
        parsed = date.fromisoformat(value)
    except ValueError:
        errors.append(f"{field} must be an absolute YYYY-MM-DD date")
        return None
    if len(value) != 10 or parsed.isoformat() != value:
        errors.append(f"{field} must be an absolute YYYY-MM-DD date")
        return None
    return parsed


def _utc_timestamp(value: object, field: str, errors: list[str]) -> None:
    if not isinstance(value, str) or UTC_TIMESTAMP.fullmatch(value) is None:
        errors.append(f"{field} must be a canonical RFC 3339 UTC datetime")
        return
    try:
        datetime.fromisoformat(value[:-1] + "+00:00")
    except ValueError:
        errors.append(f"{field} must be a canonical RFC 3339 UTC datetime")


def _require_keys(value: dict[str, Any], expected: set[str], field: str, errors: list[str]) -> None:
    actual = set(value)
    if actual != expected:
        errors.append(f"{field} keys must be exactly {sorted(expected)}; got {sorted(actual)}")


def _normalized_source(path: Path) -> bytes:
    return path.read_text(encoding="utf-8").replace("\r\n", "\n").encode()


def calculate_contract_digests(repository_root: Path) -> dict[str, str]:
    """Hash the reviewed public host and packaged-plugin contract sources."""
    program = (repository_root / "native/tools/VibeSnake.AgentHost/Program.cs").read_text(encoding="utf-8")
    protocol = re.search(r'McpProtocolVersion = "([^"]+)"', program)
    if protocol is None:
        raise ValueError("could not extract MCP protocol for public-contract digest")

    host_hasher = hashlib.sha256()
    host_hasher.update(f"protocol={protocol.group(1)}\n".encode())
    for relative in HOST_CONTRACT_PATHS:
        host_hasher.update(relative.as_posix().encode() + b"\n")
        host_hasher.update(_normalized_source(repository_root / relative))

    plugin_path = repository_root / "integrations/vibesnake-agent-plugin/plugin.json"
    plugin = json.loads(
        plugin_path.read_text(encoding="utf-8"),
        object_pairs_hook=_reject_duplicate_keys,
    )
    plugin.pop("version", None)
    plugin_hasher = hashlib.sha256()
    plugin_hasher.update(json.dumps(plugin, sort_keys=True, separators=(",", ":")).encode())
    for relative in PLUGIN_CONTRACT_PATHS:
        plugin_hasher.update(relative.as_posix().encode() + b"\n")
        plugin_hasher.update(_normalized_source(repository_root / relative))

    return {
        "host": host_hasher.hexdigest(),
        "plugin": plugin_hasher.hexdigest(),
    }


def _check_history(
    history: object,
    kind: str,
    current_version: object,
    current_digest: str,
    errors: list[str],
) -> None:
    if not isinstance(history, list) or not history:
        errors.append(f"public_contract_history.{kind} must be a nonempty array")
        return
    seen: set[str] = set()
    for index, entry in enumerate(history):
        field = f"public_contract_history.{kind}[{index}]"
        if not isinstance(entry, dict):
            errors.append(f"{field} must be an object")
            continue
        _require_keys(entry, {"version", "sha256"}, field, errors)
        version = entry.get("version")
        digest = entry.get("sha256")
        if not isinstance(version, str) or SEMVER.fullmatch(version) is None:
            errors.append(f"{field}.version must be SemVer core")
        elif version in seen:
            errors.append(f"{field}.version must be unique")
        else:
            seen.add(version)
        if not isinstance(digest, str) or SHA256.fullmatch(digest) is None:
            errors.append(f"{field}.sha256 must be a lowercase SHA-256 digest")
    latest = history[-1]
    if isinstance(latest, dict):
        if latest.get("version") != current_version:
            errors.append(f"public_contract_history.{kind} latest version must match {current_version!r}")
        if latest.get("sha256") != current_digest:
            errors.append(f"public {kind} contract changed without a matching versioned digest entry")


def _match(text: str, pattern: str, label: str, errors: list[str]) -> str | None:
    found = re.search(pattern, text, flags=re.DOTALL)
    if found is None:
        errors.append(f"could not extract {label} from its canonical source")
        return None
    return found.group(1)


def check_baseline(repository_root: Path, as_of: date) -> tuple[str, ...]:
    errors: list[str] = []
    baseline_path = repository_root / BASELINE_RELATIVE_PATH
    try:
        baseline = load_baseline(baseline_path)
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as exception:
        return (f"could not load {BASELINE_RELATIVE_PATH.as_posix()}: {exception}",)

    _require_keys(baseline, ROOT_KEYS, "baseline", errors)
    if baseline.get("schema") != EXPECTED_SCHEMA:
        errors.append(f"schema must be {EXPECTED_SCHEMA}")
    reviewed = _absolute_date(baseline.get("reviewed_on"), "reviewed_on", errors)
    next_review = _absolute_date(baseline.get("next_review_on"), "next_review_on", errors)
    if reviewed is not None and next_review is not None and reviewed >= next_review:
        errors.append("next_review_on must be after reviewed_on")
    if next_review is not None and as_of >= next_review:
        errors.append(
            f"interoperability baseline is stale: as-of {as_of.isoformat()} reached "
            f"next_review_on {next_review.isoformat()}"
        )

    mcp = baseline.get("mcp")
    plugins = baseline.get("agent_plugins")
    skill = baseline.get("agent_skill")
    okf = baseline.get("okf")
    apps = baseline.get("mcp_apps")
    history = baseline.get("public_contract_history")
    if not isinstance(mcp, dict):
        errors.append("mcp must be an object")
        mcp = {}
    if not isinstance(plugins, dict):
        errors.append("agent_plugins must be an object")
        plugins = {}
    if not isinstance(skill, dict):
        errors.append("agent_skill must be an object")
        skill = {}
    if not isinstance(okf, dict):
        errors.append("okf must be an object")
        okf = {}
    if not isinstance(apps, dict):
        errors.append("mcp_apps must be an object")
        apps = {}
    if not isinstance(history, dict):
        errors.append("public_contract_history must be an object")
        history = {}

    _require_keys(mcp, MCP_KEYS, "mcp", errors)
    _require_keys(plugins, PLUGIN_KEYS, "agent_plugins", errors)
    _require_keys(skill, {"profile", "fields"}, "agent_skill", errors)
    _require_keys(okf, {"spec_version", "generated_at", "verified_at", "stale_after"}, "okf", errors)
    _require_keys(apps, {"tracked_version", "status"}, "mcp_apps", errors)
    _require_keys(history, {"host", "plugin"}, "public_contract_history", errors)

    required_values = (
        ("mcp.sdk_package", mcp.get("sdk_package"), "ModelContextProtocol"),
        ("mcp.transport", mcp.get("transport"), "stdio"),
        ("mcp.session_model", mcp.get("session_model"), "stateless"),
        ("agent_plugins.spec_version", plugins.get("spec_version"), "1.0.0"),
        ("agent_plugins.maturity", plugins.get("maturity"), "working-draft"),
        ("agent_skill.profile", skill.get("profile"), "minimal-non-experimental"),
        ("okf.spec_version", okf.get("spec_version"), "0.2"),
        ("mcp_apps.status", apps.get("status"), "tracked-only"),
    )
    for field, actual, expected in required_values:
        if actual != expected:
            errors.append(f"{field} must be {expected!r}")
    if skill.get("fields") != ["name", "description", "markdown-body"]:
        errors.append("agent_skill.fields must be the reviewed minimal ordered field set")
    for field, value in (
        ("mcp.host_version", mcp.get("host_version")),
        ("mcp.sdk_version", mcp.get("sdk_version")),
        ("agent_plugins.plugin_version", plugins.get("plugin_version")),
    ):
        if not isinstance(value, str) or SEMVER.fullmatch(value) is None:
            errors.append(f"{field} must be SemVer core")

    stale_after = _absolute_date(okf.get("stale_after"), "okf.stale_after", errors)
    if stale_after is not None and next_review is not None and stale_after != next_review:
        errors.append("okf.stale_after must equal next_review_on")
    _utc_timestamp(okf.get("generated_at"), "okf.generated_at", errors)
    _utc_timestamp(okf.get("verified_at"), "okf.verified_at", errors)

    for field in ("plugin_schema_sha256", "mcp_schema_sha256"):
        value = plugins.get(field)
        if not isinstance(value, str) or SHA256.fullmatch(value) is None:
            errors.append(f"agent_plugins.{field} must be a lowercase SHA-256 digest")

    program = (repository_root / "native/tools/VibeSnake.AgentHost/Program.cs").read_text(encoding="utf-8")
    host_project = (repository_root / "native/tools/VibeSnake.AgentHost/VibeSnake.AgentHost.csproj").read_text(
        encoding="utf-8"
    )
    plugin_path = repository_root / "integrations/vibesnake-agent-plugin/plugin.json"
    plugin = json.loads(plugin_path.read_text(encoding="utf-8"))
    package_script = (repository_root / "scripts/package_agent_plugin.ps1").read_text(encoding="utf-8")
    engineering_doc = (repository_root / "docs/engineering/AGENT_PLAY.md").read_text(encoding="utf-8")

    actual_protocol = _match(program, r'McpProtocolVersion = "([^"]+)"', "MCP protocol", errors)
    actual_host = _match(program, r'HostVersion = "([^"]+)"', "host version", errors)
    actual_sdk = _match(
        host_project,
        r'<PackageReference Include="ModelContextProtocol" Version="([^"]+)"',
        "MCP SDK version",
        errors,
    )
    actual_mcp_schema = _match(
        package_script,
        r'\$schema\'\s*=\s*"(https://agent-plugins\.org/schemas/[^"]+/mcp\.schema\.json)"',
        "assembled MCP schema URL",
        errors,
    )
    comparisons = (
        ("mcp.protocol_version", mcp.get("protocol_version"), actual_protocol),
        ("mcp.host_version", mcp.get("host_version"), actual_host),
        ("mcp.sdk_version", mcp.get("sdk_version"), actual_sdk),
        ("agent_plugins.plugin_version", plugins.get("plugin_version"), plugin.get("version")),
        ("agent_plugins.plugin_schema_url", plugins.get("plugin_schema_url"), plugin.get("$schema")),
        ("agent_plugins.mcp_schema_url", plugins.get("mcp_schema_url"), actual_mcp_schema),
    )
    for field, expected, actual in comparisons:
        if expected != actual:
            errors.append(f"{field}={expected!r} does not match canonical source {actual!r}")

    try:
        digests = calculate_contract_digests(repository_root)
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as exception:
        errors.append(f"could not calculate public contract digests: {exception}")
    else:
        _check_history(history.get("host"), "host", mcp.get("host_version"), digests["host"], errors)
        _check_history(
            history.get("plugin"),
            "plugin",
            plugins.get("plugin_version"),
            digests["plugin"],
            errors,
        )

    required_doc_values = (
        mcp.get("protocol_version"),
        mcp.get("sdk_version"),
        mcp.get("host_version"),
        plugins.get("spec_version"),
        plugins.get("plugin_version"),
        plugins.get("plugin_schema_sha256"),
        plugins.get("mcp_schema_sha256"),
        okf.get("spec_version"),
        baseline.get("reviewed_on"),
        plugins.get("maturity"),
        skill.get("profile"),
        apps.get("tracked_version"),
    )
    for value in required_doc_values:
        if not isinstance(value, str) or value not in engineering_doc:
            errors.append(f"AGENT_PLAY.md does not publish baseline value {value!r}")
    for forbidden in ("initialize with exactly", "stable initialize revision"):
        if forbidden in engineering_doc:
            errors.append(f"AGENT_PLAY.md contains obsolete MCP wording: {forbidden}")

    return tuple(errors)


def check_upstream(
    baseline: dict[str, Any],
    fetch: Callable[[str], bytes] | None = None,
) -> tuple[str, ...]:
    plugins = baseline.get("agent_plugins")
    if not isinstance(plugins, dict):
        return ("agent_plugins must be an object",)

    def default_fetch(url: str) -> bytes:
        request = urllib.request.Request(
            url,
            headers={"User-Agent": "VibeSnake-interop-drift/0.2"},
        )
        with urllib.request.urlopen(request, timeout=30) as response:
            return response.read()

    fetch_bytes = fetch or default_fetch
    errors: list[str] = []
    for prefix in ("plugin", "mcp"):
        url = plugins.get(f"{prefix}_schema_url")
        expected = plugins.get(f"{prefix}_schema_sha256")
        if not isinstance(url, str) or not isinstance(expected, str):
            errors.append(f"agent_plugins {prefix} schema pin is incomplete")
            continue
        try:
            actual = hashlib.sha256(fetch_bytes(url)).hexdigest()
        except (OSError, TimeoutError, urllib.error.URLError) as exception:
            errors.append(f"could not fetch {prefix} schema {url}: {exception}")
            continue
        if actual != expected:
            errors.append(f"upstream {prefix} schema digest changed: expected {expected}, got {actual}")
    return tuple(errors)


def _parse_as_of(value: str) -> date:
    try:
        parsed = date.fromisoformat(value)
    except ValueError as exception:
        raise argparse.ArgumentTypeError("--as-of must be YYYY-MM-DD") from exception
    if parsed.isoformat() != value:
        raise argparse.ArgumentTypeError("--as-of must be YYYY-MM-DD")
    return parsed


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--repository-root",
        type=Path,
        default=Path(__file__).resolve().parents[1],
    )
    parser.add_argument("--as-of", type=_parse_as_of, default=None)
    parser.add_argument("--check-upstream", action="store_true")
    arguments = parser.parse_args()
    repository_root = arguments.repository_root.resolve()
    as_of = arguments.as_of or datetime.now(UTC).date()
    errors = list(check_baseline(repository_root, as_of))
    if arguments.check_upstream and not errors:
        baseline = load_baseline(repository_root / BASELINE_RELATIVE_PATH)
        errors.extend(check_upstream(baseline))
    if errors:
        print("Agent interoperability check failed:")
        for error in errors:
            print(f"  {error}")
        return 1
    suffix = " with upstream schema drift" if arguments.check_upstream else ""
    print(f"Agent interoperability baseline passed{suffix}: {repository_root}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
