import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
VALIDATOR = REPOSITORY_ROOT / "scripts" / "validate_agent_plugin.py"
SOURCE_PLUGIN = REPOSITORY_ROOT / "integrations" / "vibesnake-agent-plugin"


def run_validator(plugin_root: Path, *, require_mcp: bool = False) -> subprocess.CompletedProcess[str]:
    command = [sys.executable, str(VALIDATOR), str(plugin_root)]
    if require_mcp:
        command.append("--require-mcp")
    return subprocess.run(command, check=False, capture_output=True, text=True)


def write_packaged_checksums(plugin: Path) -> None:
    files = sorted(path for path in plugin.rglob("*") if path.is_file() and path.name != "SHA256SUMS")
    lines = [
        f"{hashlib.sha256(path.read_bytes()).hexdigest()}  {path.relative_to(plugin).as_posix()}" for path in files
    ]
    (plugin / "SHA256SUMS").write_text("\n".join(lines) + "\n", encoding="utf-8")


def complete_packaged_fixture(plugin: Path) -> None:
    (plugin / "bin").mkdir(exist_ok=True)
    (plugin / "bin" / "VibeSnake.AgentHost.dll").write_bytes(b"host")
    (plugin / "LICENSE").write_text("license\n", encoding="utf-8")
    (plugin / "NOTICE").write_text("notice\n", encoding="utf-8")
    (plugin / "mcp.json").write_text(
        json.dumps(
            {
                "$schema": "https://agent-plugins.org/schemas/1.0.0/mcp.schema.json",
                "mcpServers": {
                    "vibesnake-agent": {
                        "type": "stdio",
                        "command": "dotnet",
                        "args": ["${PLUGIN_ROOT}/bin/VibeSnake.AgentHost.dll"],
                        "cwd": "${PLUGIN_ROOT}",
                    }
                },
            }
        ),
        encoding="utf-8",
    )
    write_packaged_checksums(plugin)


def test_source_plugin_and_skill_are_valid() -> None:
    result = run_validator(SOURCE_PLUGIN)

    assert result.returncode == 0, result.stdout + result.stderr
    assert "Agent Plugin validation passed" in result.stdout


def test_packaged_stdio_plugin_requires_a_contained_command(tmp_path: Path) -> None:
    plugin = tmp_path / "vibesnake-agent"
    shutil.copytree(SOURCE_PLUGIN, plugin)
    complete_packaged_fixture(plugin)

    result = run_validator(plugin, require_mcp=True)

    assert result.returncode == 0, result.stdout + result.stderr


def test_packaged_plugin_requires_complete_components_and_checksum(tmp_path: Path) -> None:
    plugin = tmp_path / "vibesnake-agent"
    shutil.copytree(SOURCE_PLUGIN, plugin)
    complete_packaged_fixture(plugin)

    for relative in (
        "plugin.json",
        "mcp.json",
        "bin/VibeSnake.AgentHost.dll",
        "skills/play-vibesnake/SKILL.md",
        "LICENSE",
        "NOTICE",
        "SHA256SUMS",
    ):
        candidate = plugin / relative
        original = candidate.read_bytes()
        candidate.unlink()
        result = run_validator(plugin, require_mcp=True)
        assert result.returncode == 1
        assert (
            "required packaged regular file is missing" in result.stdout
            or "requires a complete checksum" in result.stdout
        )
        candidate.parent.mkdir(parents=True, exist_ok=True)
        candidate.write_bytes(original)
        if relative != "SHA256SUMS":
            write_packaged_checksums(plugin)


def test_stdio_command_is_one_token_and_packaged_launch_is_exact(tmp_path: Path) -> None:
    plugin = tmp_path / "vibesnake-agent"
    shutil.copytree(SOURCE_PLUGIN, plugin)
    complete_packaged_fixture(plugin)
    configuration_path = plugin / "mcp.json"
    configuration = json.loads(configuration_path.read_text(encoding="utf-8"))
    server = configuration["mcpServers"]["vibesnake-agent"]

    server["command"] = "dotnet --info"
    configuration_path.write_text(json.dumps(configuration), encoding="utf-8")
    write_packaged_checksums(plugin)
    command_result = run_validator(plugin, require_mcp=True)
    assert command_result.returncode == 1
    assert "command must be one nonempty executable token" in command_result.stdout

    server["command"] = "dotnet"
    server["args"] = ["${PLUGIN_ROOT}/bin/missing.dll"]
    configuration_path.write_text(json.dumps(configuration), encoding="utf-8")
    write_packaged_checksums(plugin)
    argument_result = run_validator(plugin, require_mcp=True)
    assert argument_result.returncode == 1
    assert "packaged args must contain only the declared Agent Host assembly" in argument_result.stdout


def test_invalid_plugin_reports_closed_schema_and_runtime_boundaries(tmp_path: Path) -> None:
    plugin = tmp_path / "vibesnake-agent"
    shutil.copytree(SOURCE_PLUGIN, plugin)
    manifest = json.loads((plugin / "plugin.json").read_text(encoding="utf-8"))
    manifest["unexpected"] = True
    (plugin / "plugin.json").write_text(json.dumps(manifest), encoding="utf-8")
    (plugin / "mcp.json").write_text(
        json.dumps(
            {
                "$schema": "https://agent-plugins.org/schemas/1.0.0/mcp.schema.json",
                "mcpServers": {
                    "vibesnake-agent": {
                        "type": "stdio",
                        "command": "../outside/host",
                        "cwd": "C:/player-data",
                        "env": {"PLUGIN_DATA": "stolen"},
                    }
                },
            }
        ),
        encoding="utf-8",
    )

    result = run_validator(plugin, require_mcp=True)

    assert result.returncode == 1
    assert "unknown field unexpected" in result.stdout
    assert "command must be a bare token or start with ./" in result.stdout
    assert "cwd has an unsupported form" in result.stdout
    assert "env cannot override PLUGIN_ROOT or PLUGIN_DATA" in result.stdout


def test_narrow_producer_profile_rejects_http_and_experimental_skill_metadata(tmp_path: Path) -> None:
    plugin = tmp_path / "vibesnake-agent"
    shutil.copytree(SOURCE_PLUGIN, plugin)
    skill = plugin / "skills" / "play-vibesnake" / "SKILL.md"
    skill.write_text(
        skill.read_text(encoding="utf-8").replace(
            "description:",
            "metadata: experimental\ndescription:",
            1,
        ),
        encoding="utf-8",
    )
    (plugin / "mcp.json").write_text(
        json.dumps(
            {
                "$schema": "https://agent-plugins.org/schemas/1.0.0/mcp.schema.json",
                "mcpServers": {
                    "vibesnake-agent": {
                        "type": "streamable-http",
                        "url": "https://example.invalid/mcp",
                    }
                },
            }
        ),
        encoding="utf-8",
    )

    result = run_validator(plugin, require_mcp=True)

    assert result.returncode == 1
    assert "unknown frontmatter field metadata" in result.stdout
    assert "Vibe Snake's producer profile supports only stdio" in result.stdout


def test_skill_frontmatter_rejects_non_string_values_and_duplicate_keys(tmp_path: Path) -> None:
    plugin = tmp_path / "vibesnake-agent"
    shutil.copytree(SOURCE_PLUGIN, plugin)
    skill = plugin / "skills" / "play-vibesnake" / "SKILL.md"
    original = skill.read_text(encoding="utf-8")

    skill.write_text(
        original.replace(
            "description: Play deterministic Vibe Snake matches",
            "description: [not, a, scalar] # Play deterministic Vibe Snake matches",
            1,
        ),
        encoding="utf-8",
    )
    non_string = run_validator(plugin)
    assert non_string.returncode == 1
    assert "frontmatter field description must be a string" in non_string.stdout

    skill.write_text(
        original.replace("description:", "name: duplicate\ndescription:", 1),
        encoding="utf-8",
    )
    duplicate = run_validator(plugin)
    assert duplicate.returncode == 1
    assert "duplicate frontmatter field name" in duplicate.stdout


def test_missing_plugin_root_is_rejected(tmp_path: Path) -> None:
    result = run_validator(tmp_path / "missing")

    assert result.returncode == 1
    assert "plugin root must be an existing directory" in result.stdout


def test_duplicate_json_keys_are_rejected(tmp_path: Path) -> None:
    plugin = tmp_path / "vibesnake-agent"
    shutil.copytree(SOURCE_PLUGIN, plugin)
    manifest = (plugin / "plugin.json").read_text(encoding="utf-8")
    (plugin / "plugin.json").write_text(
        manifest.replace('"name":', '"name": "duplicate",\n  "name":', 1),
        encoding="utf-8",
    )

    result = run_validator(plugin)

    assert result.returncode == 1
    assert "duplicate JSON key: name" in result.stdout


def test_placeholder_cwd_cannot_escape_after_expansion(tmp_path: Path) -> None:
    plugin = tmp_path / "vibesnake-agent"
    shutil.copytree(SOURCE_PLUGIN, plugin)
    binary = plugin / "bin" / "VibeSnake.AgentHost"
    binary.parent.mkdir()
    binary.write_bytes(b"host")

    for cwd in ("${PLUGIN_ROOT}/../../escape", "${PLUGIN_DATA}/../escape"):
        (plugin / "mcp.json").write_text(
            json.dumps(
                {
                    "$schema": "https://agent-plugins.org/schemas/1.0.0/mcp.schema.json",
                    "mcpServers": {
                        "vibesnake-agent": {
                            "type": "stdio",
                            "command": "./bin/VibeSnake.AgentHost",
                            "cwd": cwd,
                        }
                    },
                }
            ),
            encoding="utf-8",
        )

        result = run_validator(plugin, require_mcp=True)

        assert result.returncode == 1
        assert "cwd has an unsupported form" in result.stdout


def test_packaged_checksum_manifest_rejects_tampering_and_missing_files(tmp_path: Path) -> None:
    plugin = tmp_path / "vibesnake-agent"
    shutil.copytree(SOURCE_PLUGIN, plugin)
    files = sorted(path for path in plugin.rglob("*") if path.is_file())
    lines = [f"{'0' * 64}  {path.relative_to(plugin).as_posix()}" for path in files[:-1]]
    (plugin / "SHA256SUMS").write_text("\n".join(lines) + "\n", encoding="utf-8")

    result = run_validator(plugin)

    assert result.returncode == 1
    assert "entries must match every packaged regular file exactly once" in result.stdout
    assert "digest mismatch" in result.stdout
