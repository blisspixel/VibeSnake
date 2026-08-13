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


def test_source_plugin_and_skill_are_valid() -> None:
    result = run_validator(SOURCE_PLUGIN)

    assert result.returncode == 0, result.stdout + result.stderr
    assert "Agent Plugin validation passed" in result.stdout


def test_packaged_stdio_plugin_requires_a_contained_command(tmp_path: Path) -> None:
    plugin = tmp_path / "vibesnake-agent"
    shutil.copytree(SOURCE_PLUGIN, plugin)
    binary = plugin / "bin" / "VibeSnake.AgentHost"
    binary.parent.mkdir()
    binary.write_bytes(b"host")
    (plugin / "mcp.json").write_text(
        json.dumps(
            {
                "$schema": "https://agent-plugins.org/schemas/1.0.0/mcp.schema.json",
                "mcpServers": {
                    "vibesnake-agent": {
                        "type": "stdio",
                        "command": "./bin/VibeSnake.AgentHost",
                        "cwd": "${PLUGIN_ROOT}",
                    }
                },
            }
        ),
        encoding="utf-8",
    )

    result = run_validator(plugin, require_mcp=True)

    assert result.returncode == 0, result.stdout + result.stderr


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


def test_missing_plugin_root_is_rejected(tmp_path: Path) -> None:
    result = run_validator(tmp_path / "missing")

    assert result.returncode == 1
    assert "plugin root must be an existing directory" in result.stdout
