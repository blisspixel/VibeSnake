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
REQUIRED_FILES = ("LICENSE", "NOTICE", "INSTALL.txt", "host-manifest.json")
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
