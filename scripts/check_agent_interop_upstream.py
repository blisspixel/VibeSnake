"""Verify the byte integrity of reviewed remote Agent Plugins pins."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import urllib.error
import urllib.request
from collections.abc import Callable
from pathlib import Path
from typing import Any


BASELINE_RELATIVE_PATH = Path("integrations/agent-interop-baseline.json")
MAXIMUM_BASELINE_BYTES = 65_536
SHA256 = re.compile(r"^[0-9a-f]{64}$")


def _reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def load_baseline(path: Path) -> dict[str, Any]:
    payload = path.read_bytes()
    if len(payload) > MAXIMUM_BASELINE_BYTES:
        raise ValueError(f"interoperability baseline exceeds {MAXIMUM_BASELINE_BYTES} bytes")
    loaded = json.loads(payload.decode("utf-8"), object_pairs_hook=_reject_duplicate_keys)
    if not isinstance(loaded, dict):
        raise ValueError("the interoperability baseline root must be an object")
    return loaded


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
            headers={"User-Agent": "VibeSnake-interop-drift/0.3"},
        )
        with urllib.request.urlopen(request, timeout=30) as response:
            return response.read()

    fetch_bytes = fetch or default_fetch
    errors: list[str] = []
    upstreams = (
        ("specification", plugins.get("spec_source_url"), plugins.get("spec_source_sha256")),
        ("plugin schema", plugins.get("plugin_schema_url"), plugins.get("plugin_schema_sha256")),
        ("mcp schema", plugins.get("mcp_schema_url"), plugins.get("mcp_schema_sha256")),
    )
    for label, url, expected in upstreams:
        if (
            not isinstance(url, str)
            or not url.startswith("https://")
            or not isinstance(expected, str)
            or SHA256.fullmatch(expected) is None
        ):
            errors.append(f"agent_plugins {label} pin is incomplete")
            continue
        try:
            actual = hashlib.sha256(fetch_bytes(url)).hexdigest()
        except (OSError, TimeoutError, urllib.error.URLError) as exception:
            errors.append(f"could not fetch {label} {url}: {exception}")
            continue
        if actual != expected:
            errors.append(f"upstream {label} digest changed: expected {expected}, got {actual}")
    return tuple(errors)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--repository-root",
        type=Path,
        default=Path(__file__).resolve().parents[1],
    )
    arguments = parser.parse_args()
    repository_root = arguments.repository_root.resolve()
    try:
        baseline = load_baseline(repository_root / BASELINE_RELATIVE_PATH)
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as exception:
        print(f"Agent interoperability upstream check failed: {exception}")
        return 1

    errors = check_upstream(baseline)
    if errors:
        print("Agent interoperability upstream check failed:")
        for error in errors:
            print(f"  {error}")
        return 1

    print(f"Agent interoperability upstream specification and schema pins passed: {repository_root}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
