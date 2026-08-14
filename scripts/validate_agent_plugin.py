"""Validate the checked-in or packaged Vibe Snake Agent Plugin."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path
from pathlib import PurePosixPath
from typing import Any

import yaml

PLUGIN_SCHEMA = "https://agent-plugins.org/schemas/1.0.0/plugin.schema.json"
MCP_SCHEMA = "https://agent-plugins.org/schemas/1.0.0/mcp.schema.json"
PLUGIN_FIELDS = {
    "$schema",
    "name",
    "version",
    "description",
    "author",
    "homepage",
    "repository",
    "license",
    "keywords",
    "extensions",
}
SKILL_FIELDS = {
    "name",
    "description",
}
PLUGIN_NAME = re.compile(r"^(?!.*(?:--|\.\.))[a-z0-9](?:[a-z0-9.-]{0,62}[a-z0-9])?$")
SKILL_NAME = re.compile(r"^(?!.*--)[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$")
SHA256 = re.compile(r"^[0-9a-f]{64}$")
COMMAND_TOKEN = re.compile(r"^[^\s\x00-\x1f\x7f]+$")
PACKAGED_SERVER_NAME = "vibesnake-agent"
PACKAGED_HOST_ARGUMENT = "${PLUGIN_ROOT}/bin/VibeSnake.AgentHost.dll"
PACKAGED_REQUIRED_FILES = (
    "plugin.json",
    "mcp.json",
    "skills/play-vibesnake/SKILL.md",
    "LICENSE",
    "NOTICE",
    "bin/VibeSnake.AgentHost.dll",
)


class _UniqueKeyLoader(yaml.SafeLoader):
    """Safe YAML loader that rejects duplicate mapping keys."""


def _construct_unique_mapping(
    loader: _UniqueKeyLoader,
    node: yaml.MappingNode,
    deep: bool = False,
) -> dict[Any, Any]:
    mapping: dict[Any, Any] = {}
    for key_node, value_node in node.value:
        key = loader.construct_object(key_node, deep=deep)
        if not isinstance(key, str):
            raise ValueError("frontmatter keys must be strings")
        if key in mapping:
            raise ValueError(f"duplicate frontmatter field {key}")
        mapping[key] = loader.construct_object(value_node, deep=deep)
    return mapping


_UniqueKeyLoader.add_constructor(
    yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG,
    _construct_unique_mapping,
)


def _reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    value: dict[str, Any] = {}
    for key, item in pairs:
        if key in value:
            raise ValueError(f"duplicate JSON key: {key}")
        value[key] = item
    return value


def _load_object(path: Path, problems: list[str]) -> dict[str, Any] | None:
    try:
        value = json.loads(
            path.read_text(encoding="utf-8"),
            object_pairs_hook=_reject_duplicate_keys,
        )
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as exception:
        problems.append(f"{path.name}: unreadable JSON: {exception}")
        return None
    if not isinstance(value, dict):
        problems.append(f"{path.name}: root must be an object")
        return None
    return value


def _reject_unknown(value: dict[str, Any], allowed: set[str], label: str, problems: list[str]) -> None:
    for field in sorted(set(value) - allowed):
        problems.append(f"{label}: unknown field {field}")


def _validate_manifest(root: Path, problems: list[str]) -> None:
    path = root / "plugin.json"
    if not path.is_file():
        problems.append("plugin.json: required regular file is missing")
        return
    value = _load_object(path, problems)
    if value is None:
        return
    _reject_unknown(value, PLUGIN_FIELDS, "plugin.json", problems)
    if value.get("$schema") != PLUGIN_SCHEMA:
        problems.append("plugin.json: unsupported or missing Agent Plugins schema")
    name = value.get("name")
    if not isinstance(name, str) or PLUGIN_NAME.fullmatch(name) is None:
        problems.append("plugin.json: name violates Agent Plugins 1.0.0 constraints")
    for field in ("version", "description", "homepage", "repository", "license"):
        if field in value and not isinstance(value[field], str):
            problems.append(f"plugin.json: {field} must be a string")
    author = value.get("author")
    if author is not None:
        if not isinstance(author, dict):
            problems.append("plugin.json: author must be an object")
        else:
            _reject_unknown(author, {"name", "email", "url"}, "plugin.json author", problems)
            if any(not isinstance(item, str) for item in author.values()):
                problems.append("plugin.json: author values must be strings")
    keywords = value.get("keywords")
    if keywords is not None and (not isinstance(keywords, list) or any(not isinstance(item, str) for item in keywords)):
        problems.append("plugin.json: keywords must be an array of strings")
    extensions = value.get("extensions")
    if extensions is not None and (
        not isinstance(extensions, dict) or any(not isinstance(item, dict) for item in extensions.values())
    ):
        problems.append("plugin.json: extensions must map namespaces to objects")


def _parse_skill_frontmatter(path: Path, problems: list[str]) -> dict[str, str] | None:
    try:
        lines = path.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeError) as exception:
        problems.append(f"{path}: unreadable skill: {exception}")
        return None
    if not lines or lines[0] != "---":
        problems.append(f"{path}: YAML frontmatter must start on the first line")
        return None
    try:
        end = lines.index("---", 1)
    except ValueError:
        problems.append(f"{path}: YAML frontmatter is not closed")
        return None
    frontmatter = "\n".join(lines[1:end])
    try:
        loaded = yaml.load(frontmatter, Loader=_UniqueKeyLoader)
    except (yaml.YAMLError, ValueError) as exception:
        problems.append(f"{path}: invalid YAML frontmatter: {exception}")
        return None
    if not isinstance(loaded, dict):
        problems.append(f"{path}: YAML frontmatter must be a mapping")
        return None
    fields: dict[str, str] = {}
    for key, value in loaded.items():
        if key not in SKILL_FIELDS:
            problems.append(f"{path}: unknown frontmatter field {key}")
            continue
        if not isinstance(value, str):
            problems.append(f"{path}: frontmatter field {key} must be a string")
            continue
        fields[key] = value
    if not any(line.strip() for line in lines[end + 1 :]):
        problems.append(f"{path}: Markdown instructions are required")
    return fields


def _validate_skills(root: Path, problems: list[str]) -> None:
    skills = root / "skills"
    if not skills.exists():
        return
    if not skills.is_dir():
        problems.append("skills: fixed component location must be a directory")
        return
    for child in sorted(skills.iterdir()):
        if not child.is_dir():
            continue
        path = child / "SKILL.md"
        if not path.is_file():
            continue
        fields = _parse_skill_frontmatter(path, problems)
        if fields is None:
            continue
        name = fields.get("name", "")
        description = fields.get("description", "")
        if SKILL_NAME.fullmatch(name) is None or name != child.name:
            problems.append(f"{path}: name must be valid and match its parent directory")
        if not 1 <= len(description) <= 1024:
            problems.append(f"{path}: description must contain 1 through 1024 characters")


def _is_contained(root: Path, candidate: Path) -> bool:
    try:
        candidate.resolve(strict=False).relative_to(root.resolve(strict=True))
    except (OSError, ValueError):
        return False
    return True


def _validate_stdio(root: Path, label: str, value: dict[str, Any], problems: list[str]) -> None:
    allowed = {"type", "command", "args", "env", "cwd"}
    _reject_unknown(value, allowed, label, problems)
    command = value.get("command")
    if not isinstance(command, str) or COMMAND_TOKEN.fullmatch(command) is None:
        problems.append(f"{label}: command must be one nonempty executable token")
    elif command.startswith("./"):
        executable = root / command[2:]
        if not _is_contained(root, executable):
            problems.append(f"{label}: command escapes the plugin root")
        elif not executable.is_file():
            problems.append(f"{label}: packaged command does not exist")
    elif "/" in command or "\\" in command:
        problems.append(f"{label}: command must be a bare token or start with ./")
    args = value.get("args")
    if args is not None and (not isinstance(args, list) or any(not isinstance(item, str) for item in args)):
        problems.append(f"{label}: args must be an array of strings")
    env = value.get("env")
    if env is not None:
        if not isinstance(env, dict) or any(
            not isinstance(key, str) or not isinstance(item, str) for key, item in env.items()
        ):
            problems.append(f"{label}: env must map strings to strings")
        elif any(key.upper() in {"PLUGIN_ROOT", "PLUGIN_DATA"} for key in env):
            problems.append(f"{label}: env cannot override PLUGIN_ROOT or PLUGIN_DATA")
    cwd = value.get("cwd")
    if cwd is not None:
        if not isinstance(cwd, str):
            problems.append(f"{label}: cwd must be a string")
        elif cwd.startswith("./"):
            if not _is_contained(root, root / cwd[2:]):
                problems.append(f"{label}: cwd escapes the plugin root")
        elif not _is_safe_placeholder_path(cwd):
            problems.append(f"{label}: cwd has an unsupported form")


def _is_safe_placeholder_path(value: str) -> bool:
    for placeholder in ("${PLUGIN_ROOT}", "${PLUGIN_DATA}"):
        if value == placeholder:
            return True
        prefix = placeholder + "/"
        if value.startswith(prefix):
            suffix = value[len(prefix) :]
            path = PurePosixPath(suffix)
            return (
                bool(suffix)
                and "\\" not in suffix
                and not path.is_absolute()
                and all(part not in {"", ".", ".."} for part in path.parts)
            )
    return False


def _validate_mcp(root: Path, require_mcp: bool, problems: list[str]) -> None:
    path = root / "mcp.json"
    if not path.exists():
        if require_mcp:
            problems.append("mcp.json: packaged plugin requires an MCP configuration")
        return
    if not path.is_file():
        problems.append("mcp.json: fixed component location must be a regular file")
        return
    value = _load_object(path, problems)
    if value is None:
        return
    _reject_unknown(value, {"$schema", "mcpServers"}, "mcp.json", problems)
    if value.get("$schema") != MCP_SCHEMA:
        problems.append("mcp.json: schema must match Agent Plugins 1.0.0")
    servers = value.get("mcpServers")
    if not isinstance(servers, dict):
        problems.append("mcp.json: mcpServers must be an object")
        return
    if require_mcp and not servers:
        problems.append("mcp.json: packaged plugin must declare a server")
    if require_mcp and set(servers) != {PACKAGED_SERVER_NAME}:
        problems.append("mcp.json: packaged plugin must declare exactly the vibesnake-agent server")
    for name, server in servers.items():
        label = f"mcp.json server {name}"
        if not isinstance(server, dict):
            problems.append(f"{label}: configuration must be an object")
            continue
        server_type = server.get("type")
        if server_type == "stdio":
            _validate_stdio(root, label, server, problems)
            if require_mcp and name == PACKAGED_SERVER_NAME:
                if server.get("command") != "dotnet":
                    problems.append(f"{label}: packaged command must be dotnet")
                if server.get("args") != [PACKAGED_HOST_ARGUMENT]:
                    problems.append(f"{label}: packaged args must contain only the declared Agent Host assembly")
                if server.get("cwd") != "${PLUGIN_ROOT}":
                    problems.append(f"{label}: packaged cwd must be ${{PLUGIN_ROOT}}")
        else:
            problems.append(f"{label}: Vibe Snake's producer profile supports only stdio")


def _validate_packaged_components(root: Path, problems: list[str]) -> None:
    for relative in PACKAGED_REQUIRED_FILES:
        if not (root / relative).is_file():
            problems.append(f"{relative}: required packaged regular file is missing")


def _validate_checksums(root: Path, required: bool, problems: list[str]) -> None:
    checksum_path = root / "SHA256SUMS"
    if not checksum_path.exists():
        if required:
            problems.append("SHA256SUMS: packaged plugin requires a complete checksum manifest")
        return
    if not checksum_path.is_file():
        problems.append("SHA256SUMS: fixed component location must be a regular file")
        return
    try:
        lines = checksum_path.read_text(encoding="utf-8").splitlines()
    except (OSError, UnicodeError) as exception:
        problems.append(f"SHA256SUMS: unreadable checksum list: {exception}")
        return

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


def validate_plugin(root: Path, require_mcp: bool = False) -> tuple[str, ...]:
    """Return deterministic producer-conformance problems for one plugin root."""
    root = root.resolve()
    problems: list[str] = []
    if not root.is_dir():
        return ("plugin root must be an existing directory",)
    for path in root.rglob("*"):
        if path.is_symlink() or not _is_contained(root, path):
            problems.append(f"{path.relative_to(root)}: link or path escapes are not allowed")
    _validate_manifest(root, problems)
    _validate_skills(root, problems)
    _validate_mcp(root, require_mcp, problems)
    if require_mcp:
        _validate_packaged_components(root, problems)
    _validate_checksums(root, require_mcp, problems)
    return tuple(problems)


def main() -> int:
    """Validate a plugin directory and print bounded diagnostics."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("plugin_root", type=Path)
    parser.add_argument("--require-mcp", action="store_true")
    arguments = parser.parse_args()
    problems = validate_plugin(arguments.plugin_root, arguments.require_mcp)
    if problems:
        print("Agent Plugin validation failed:")
        for problem in problems:
            print(f"  {problem}")
        return 1
    print(f"Agent Plugin validation passed: {arguments.plugin_root.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
