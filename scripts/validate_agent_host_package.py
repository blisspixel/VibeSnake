"""Validate a self-contained Vibe Snake Agent Host package."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path
from typing import Any

SCHEMA = "vibesnake-agent-host-package-v1"
HOST_NAME = "vibesnake-agent-host"
PROTOCOL_VERSION = "2026-07-28"
TRANSPORT = "stdio"
USER_DATA_POLICY = "godot-app-userdata"
SIGNING = "unsigned"
RID_PATTERN = re.compile(r"^(win|osx|linux)-(x64|arm64)$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")
VERSION_PATTERN = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+$")
MANIFEST_FIELDS = {
    "schema",
    "host_name",
    "host_version",
    "runtime_identifier",
    "self_contained",
    "framework_dependent",
    "publication_eligible",
    "executable",
    "protocol_version",
    "transport",
    "user_data_policy",
    "signing",
}
REQUIRED_FILES = (
    "LICENSE",
    "NOTICE",
    "INSTALL.txt",
    "host-manifest.json",
    "host-inventory.json",
    "host-provenance.json",
)
INVENTORY_SCHEMA = "vibesnake-agent-host-inventory-v1"
PROVENANCE_SCHEMA = "vibesnake-agent-host-provenance-v1"
INVENTORY_FIELDS = {
    "schema",
    "generated_from_locks_only",
    "host_version",
    "runtime_identifier",
    "source_revision",
    "source_dirty",
    "lock_set_sha256",
    "dotnet_sdk",
    "sources",
    "packages",
}
INVENTORY_SOURCE_FIELDS = {"path", "sha256"}
INVENTORY_PACKAGE_FIELDS = {
    "ecosystem",
    "name",
    "version",
    "dependency_types",
    "source_locks",
    "content_hashes",
    "frameworks",
}
PROVENANCE_FIELDS = {
    "schema",
    "host_name",
    "host_version",
    "runtime_identifier",
    "source_revision",
    "source_dirty",
    "self_contained",
    "signing",
    "publication_eligible",
    "executable_sha256",
    "manifest_sha256",
    "inventory_sha256",
    "lock_set_sha256",
    "dotnet_sdk",
}
HOST_LOCK_PATHS = (
    "native/src/VibeSnake.AgentPlay/packages.lock.json",
    "native/src/VibeSnake.Persistence/packages.lock.json",
    "native/src/VibeSnake.Rules/packages.lock.json",
    "native/tools/VibeSnake.AgentHost/packages.lock.json",
)
REQUIRED_PACKAGES = ("Microsoft.Extensions.Hosting", "ModelContextProtocol")
REVISION_PATTERN = re.compile(r"^[0-9a-f]{40}$")
SDK_PATTERN = re.compile(r"^[0-9]+\.[0-9]+\.[0-9]+$")
FORBIDDEN_NAMES = {
    "preferences.json",
    "agent_passports.json",
    "exhibition_archive.json",
    "mcp.json",
}


def _reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def _is_contained(root: Path, candidate: Path) -> bool:
    try:
        candidate.resolve().relative_to(root.resolve())
    except ValueError:
        return False
    return True


def _load_object(path: Path, label: str, problems: list[str]) -> dict[str, Any] | None:
    try:
        loaded = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=_reject_duplicate_keys)
    except (OSError, UnicodeError, ValueError) as exception:
        problems.append(f"{label}: unreadable: {exception}")
        return None
    if not isinstance(loaded, dict):
        problems.append(f"{label}: root must be an object")
        return None
    return loaded


def _require_fields(value: dict[str, Any], expected: set[str], label: str, problems: list[str]) -> None:
    extra = set(value) - expected
    missing = expected - set(value)
    if extra:
        problems.append(f"{label}: unknown fields: " + ", ".join(sorted(extra)))
    if missing:
        problems.append(f"{label}: missing fields: " + ", ".join(sorted(missing)))


def _string_list(value: object, label: str, problems: list[str]) -> list[str]:
    if not isinstance(value, list) or any(not isinstance(item, str) or not item for item in value):
        problems.append(f"{label} must be an array of non-empty strings")
        return []
    return list(value)


def _validate_inventory_and_provenance(
    root: Path,
    manifest: dict[str, Any] | None,
    problems: list[str],
) -> None:
    inventory_path = root / "host-inventory.json"
    provenance_path = root / "host-provenance.json"
    inventory = _load_object(inventory_path, "host-inventory.json", problems) if inventory_path.is_file() else None
    provenance = _load_object(provenance_path, "host-provenance.json", problems) if provenance_path.is_file() else None

    source_paths: list[str] = []
    lock_set_sha256: str | None = None
    if isinstance(inventory, dict):
        _require_fields(inventory, INVENTORY_FIELDS, "host-inventory.json", problems)
        if inventory.get("schema") != INVENTORY_SCHEMA:
            problems.append("host-inventory.json: schema must be vibesnake-agent-host-inventory-v1")
        if inventory.get("generated_from_locks_only") is not True:
            problems.append("host-inventory.json: generated_from_locks_only must be true")
        host_version = inventory.get("host_version")
        if not isinstance(host_version, str) or VERSION_PATTERN.fullmatch(host_version) is None:
            problems.append("host-inventory.json: host_version must be dotted numeric SemVer")
        rid = inventory.get("runtime_identifier")
        if not isinstance(rid, str) or RID_PATTERN.fullmatch(rid) is None:
            problems.append("host-inventory.json: runtime_identifier must be a closed desktop RID")
        source_revision = inventory.get("source_revision")
        if not isinstance(source_revision, str) or REVISION_PATTERN.fullmatch(source_revision) is None:
            problems.append("host-inventory.json: source_revision must be a 40-character lowercase SHA-1")
        if not isinstance(inventory.get("source_dirty"), bool):
            problems.append("host-inventory.json: source_dirty must be a boolean")
        lock_set_sha256 = inventory.get("lock_set_sha256")
        if not isinstance(lock_set_sha256, str) or SHA256.fullmatch(lock_set_sha256) is None:
            problems.append("host-inventory.json: lock_set_sha256 must be a lowercase SHA-256")
            lock_set_sha256 = None
        sdk = inventory.get("dotnet_sdk")
        if not isinstance(sdk, str) or SDK_PATTERN.fullmatch(sdk) is None:
            problems.append("host-inventory.json: dotnet_sdk must be dotted numeric SemVer")
        sources = inventory.get("sources")
        if not isinstance(sources, list) or not sources:
            problems.append("host-inventory.json: sources must list the host lock closure")
        else:
            seen_paths: set[str] = set()
            lock_set_lines: list[str] = []
            for index, source in enumerate(sources):
                label = f"host-inventory.json sources[{index}]"
                if not isinstance(source, dict):
                    problems.append(f"{label} must be an object")
                    continue
                _require_fields(source, INVENTORY_SOURCE_FIELDS, label, problems)
                path = source.get("path")
                digest = source.get("sha256")
                if not isinstance(path, str) or "\\" in path or path != path.replace("\\", "/"):
                    problems.append(f"{label}: path must be a repository-relative POSIX lock path")
                    continue
                if path in seen_paths:
                    problems.append(f"{label}: duplicate path {path}")
                    continue
                seen_paths.add(path)
                if not isinstance(digest, str) or SHA256.fullmatch(digest) is None:
                    problems.append(f"{label}: sha256 must be a lowercase SHA-256")
                    continue
                source_paths.append(path)
                lock_set_lines.append(f"{path}={digest}")
            if tuple(source_paths) != HOST_LOCK_PATHS:
                problems.append("host-inventory.json: sources must be the exact host lock closure in path order")
            if lock_set_sha256 is not None:
                recomputed = hashlib.sha256("\n".join(lock_set_lines).encode()).hexdigest()
                if recomputed != lock_set_sha256:
                    problems.append("host-inventory.json: lock_set_sha256 must match the ordered source list")
        packages = inventory.get("packages")
        if not isinstance(packages, list) or not packages:
            problems.append("host-inventory.json: packages must list locked NuGet packages")
        else:
            names: list[str] = []
            seen_keys: set[str] = set()
            previous: tuple[str, str] | None = None
            for index, package in enumerate(packages):
                label = f"host-inventory.json packages[{index}]"
                if not isinstance(package, dict):
                    problems.append(f"{label} must be an object")
                    continue
                _require_fields(package, INVENTORY_PACKAGE_FIELDS, label, problems)
                if package.get("ecosystem") != "nuget":
                    problems.append(f"{label}: ecosystem must be nuget")
                name = package.get("name")
                version = package.get("version")
                if not isinstance(name, str) or not name:
                    problems.append(f"{label}: name must be a non-empty string")
                    continue
                if not isinstance(version, str) or not version:
                    problems.append(f"{label}: version must be a non-empty string")
                    continue
                key = f"{name.lower()}|{version}"
                if key in seen_keys:
                    problems.append(f"{label}: duplicate package {name} {version}")
                    continue
                seen_keys.add(key)
                names.append(name)
                current = (name.lower(), version)
                if previous is not None and current < previous:
                    problems.append("host-inventory.json: packages must be sorted by name then version")
                previous = current
                for field in ("dependency_types", "source_locks", "content_hashes", "frameworks"):
                    values = _string_list(package.get(field), f"{label}.{field}", problems)
                    if field == "source_locks":
                        for source_lock in values:
                            if source_paths and source_lock not in source_paths:
                                problems.append(f"{label}: source lock is outside the host lock closure")
                    if field == "frameworks":
                        rid = inventory.get("runtime_identifier")
                        allowed = {"net10.0"}
                        if isinstance(rid, str):
                            allowed.add(f"net10.0/{rid}")
                        for framework in values:
                            if framework not in allowed:
                                problems.append(f"{label}: framework {framework} is outside this package RID")
            for required in REQUIRED_PACKAGES:
                if required not in names:
                    problems.append(f"host-inventory.json: missing required package {required}")

    if isinstance(provenance, dict):
        _require_fields(provenance, PROVENANCE_FIELDS, "host-provenance.json", problems)
        if provenance.get("schema") != PROVENANCE_SCHEMA:
            problems.append("host-provenance.json: schema must be vibesnake-agent-host-provenance-v1")
        if provenance.get("host_name") != HOST_NAME:
            problems.append("host-provenance.json: host_name must be vibesnake-agent-host")
        if provenance.get("self_contained") is not True:
            problems.append("host-provenance.json: self_contained must be true")
        if provenance.get("signing") != SIGNING:
            problems.append("host-provenance.json: signing must be unsigned")
        if provenance.get("publication_eligible") is not False:
            problems.append("host-provenance.json: publication_eligible must stay false until signing exists")
        host_version = provenance.get("host_version")
        if not isinstance(host_version, str) or VERSION_PATTERN.fullmatch(host_version) is None:
            problems.append("host-provenance.json: host_version must be dotted numeric SemVer")
        rid = provenance.get("runtime_identifier")
        if not isinstance(rid, str) or RID_PATTERN.fullmatch(rid) is None:
            problems.append("host-provenance.json: runtime_identifier must be a closed desktop RID")
        source_revision = provenance.get("source_revision")
        if not isinstance(source_revision, str) or REVISION_PATTERN.fullmatch(source_revision) is None:
            problems.append("host-provenance.json: source_revision must be a 40-character lowercase SHA-1")
        if not isinstance(provenance.get("source_dirty"), bool):
            problems.append("host-provenance.json: source_dirty must be a boolean")
        sdk = provenance.get("dotnet_sdk")
        if not isinstance(sdk, str) or SDK_PATTERN.fullmatch(sdk) is None:
            problems.append("host-provenance.json: dotnet_sdk must be dotted numeric SemVer")
        for field in ("executable_sha256", "manifest_sha256", "inventory_sha256", "lock_set_sha256"):
            digest = provenance.get(field)
            if not isinstance(digest, str) or SHA256.fullmatch(digest) is None:
                problems.append(f"host-provenance.json: {field} must be a lowercase SHA-256")
        if isinstance(manifest, dict):
            for field in (
                "host_version",
                "runtime_identifier",
            ):
                if provenance.get(field) != manifest.get(field):
                    problems.append(f"host-provenance.json: {field} must match host-manifest.json")
        if isinstance(inventory, dict):
            for field in (
                "host_version",
                "runtime_identifier",
                "source_revision",
                "source_dirty",
                "lock_set_sha256",
                "dotnet_sdk",
            ):
                if provenance.get(field) != inventory.get(field):
                    problems.append(f"host-provenance.json: {field} must match host-inventory.json")
        executable_name = manifest.get("executable") if isinstance(manifest, dict) else None
        if isinstance(executable_name, str) and (root / executable_name).is_file():
            actual = hashlib.sha256((root / executable_name).read_bytes()).hexdigest()
            if provenance.get("executable_sha256") != actual:
                problems.append("host-provenance.json: executable_sha256 must match the declared host")
        if (root / "host-manifest.json").is_file():
            actual = hashlib.sha256((root / "host-manifest.json").read_bytes()).hexdigest()
            if provenance.get("manifest_sha256") != actual:
                problems.append("host-provenance.json: manifest_sha256 must match host-manifest.json")
        if inventory_path.is_file():
            actual = hashlib.sha256(inventory_path.read_bytes()).hexdigest()
            if provenance.get("inventory_sha256") != actual:
                problems.append("host-provenance.json: inventory_sha256 must match host-inventory.json")


def validate_host_package(root: Path) -> tuple[str, ...]:
    """Return deterministic problems for one self-contained host package."""
    root = root.resolve()
    problems: list[str] = []
    if not root.is_dir():
        return ("host package root must be an existing directory",)

    for path in root.rglob("*"):
        if path.is_symlink() or not _is_contained(root, path):
            problems.append(f"{path.relative_to(root).as_posix()}: link or path escapes are not allowed")
        if path.is_file() and path.suffix.lower() == ".pdb":
            problems.append(f"{path.relative_to(root).as_posix()}: debug symbols are not part of this package")
        if path.name in FORBIDDEN_NAMES:
            problems.append(
                f"{path.relative_to(root).as_posix()}: player or plugin files do not belong in a host package"
            )

    for relative in REQUIRED_FILES:
        if not (root / relative).is_file():
            problems.append(f"{relative}: required packaged regular file is missing")

    manifest_path = root / "host-manifest.json"
    manifest: dict[str, Any] | None = None
    if manifest_path.is_file():
        try:
            manifest = json.loads(
                manifest_path.read_text(encoding="utf-8"),
                object_pairs_hook=_reject_duplicate_keys,
            )
        except (OSError, UnicodeError, ValueError) as exception:
            problems.append(f"host-manifest.json: unreadable: {exception}")
            manifest = None

    if isinstance(manifest, dict):
        extra = set(manifest) - MANIFEST_FIELDS
        missing = MANIFEST_FIELDS - set(manifest)
        if extra:
            problems.append("host-manifest.json: unknown fields: " + ", ".join(sorted(extra)))
        if missing:
            problems.append("host-manifest.json: missing fields: " + ", ".join(sorted(missing)))
        if manifest.get("schema") != SCHEMA:
            problems.append("host-manifest.json: schema must be vibesnake-agent-host-package-v1")
        if manifest.get("host_name") != HOST_NAME:
            problems.append("host-manifest.json: host_name must be vibesnake-agent-host")
        host_version = manifest.get("host_version")
        if not isinstance(host_version, str) or VERSION_PATTERN.fullmatch(host_version) is None:
            problems.append("host-manifest.json: host_version must be dotted numeric SemVer")
        rid = manifest.get("runtime_identifier")
        if not isinstance(rid, str) or RID_PATTERN.fullmatch(rid) is None:
            problems.append("host-manifest.json: runtime_identifier must be a closed desktop RID")
        if manifest.get("self_contained") is not True:
            problems.append("host-manifest.json: self_contained must be true")
        if manifest.get("framework_dependent") is not False:
            problems.append("host-manifest.json: framework_dependent must be false")
        if manifest.get("publication_eligible") is not False:
            problems.append("host-manifest.json: publication_eligible must stay false until signing exists")
        if manifest.get("protocol_version") != PROTOCOL_VERSION:
            problems.append("host-manifest.json: protocol_version must be 2026-07-28")
        if manifest.get("transport") != TRANSPORT:
            problems.append("host-manifest.json: transport must be stdio")
        if manifest.get("user_data_policy") != USER_DATA_POLICY:
            problems.append("host-manifest.json: user_data_policy must be godot-app-userdata")
        if manifest.get("signing") != SIGNING:
            problems.append("host-manifest.json: signing must be unsigned")
        executable = manifest.get("executable")
        if not isinstance(executable, str) or not executable or "\\" in executable or "/" in executable:
            problems.append("host-manifest.json: executable must be a file name in the package root")
        elif not (root / executable).is_file():
            problems.append(f"{executable}: declared host executable is missing")
        elif isinstance(rid, str):
            expects_exe = rid.startswith("win-")
            if expects_exe and not executable.endswith(".exe"):
                problems.append("host-manifest.json: Windows packages must declare a .exe host")
            if not expects_exe and executable.endswith(".exe"):
                problems.append("host-manifest.json: non-Windows packages must not declare a .exe host")

    _validate_inventory_and_provenance(root, manifest, problems)

    checksum_path = root / "SHA256SUMS"
    if not checksum_path.is_file():
        problems.append("SHA256SUMS: packaged host requires a complete checksum manifest")
        return tuple(problems)

    try:
        lines = checksum_path.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeError) as exception:
        problems.append(f"SHA256SUMS: unreadable checksum list: {exception}")
        return tuple(problems)

    expected: dict[str, str] = {}
    for line_number, line in enumerate(lines, start=1):
        digest, separator, relative = line.partition("  ")
        candidate = root / relative
        if (
            not separator
            or SHA256.fullmatch(digest) is None
            or not relative
            or "\\" in relative
            or not _is_contained(root, candidate)
            or not candidate.is_file()
            or relative == "SHA256SUMS"
        ):
            problems.append(f"SHA256SUMS:{line_number}: invalid checksum entry")
            continue
        if relative in expected:
            problems.append(f"SHA256SUMS:{line_number}: duplicate path {relative}")
            continue
        expected[relative] = digest

    actual_paths = {
        path.relative_to(root).as_posix() for path in root.rglob("*") if path.is_file() and path != checksum_path
    }
    if set(expected) != actual_paths:
        problems.append("SHA256SUMS: entries must match every packaged regular file exactly once")
    for relative, digest in expected.items():
        candidate = root / relative
        if candidate.is_file() and hashlib.sha256(candidate.read_bytes()).hexdigest() != digest:
            problems.append(f"SHA256SUMS: digest mismatch for {relative}")

    return tuple(problems)


def main() -> int:
    """Validate one host package directory and print bounded diagnostics."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package_root", type=Path)
    arguments = parser.parse_args()
    problems = validate_host_package(arguments.package_root)
    if problems:
        print("Agent Host package validation failed:")
        for problem in problems:
            print(f"  {problem}")
        return 1
    print(f"Agent Host package validation passed: {arguments.package_root.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
