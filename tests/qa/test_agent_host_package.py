"""Contracts for the AA-10 self-contained Agent Host package."""

from __future__ import annotations

import hashlib
import json
import subprocess
import sys
from pathlib import Path

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
VALIDATOR = REPOSITORY_ROOT / "scripts" / "validate_agent_host_package.py"


def run_validator(package_root: Path) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [sys.executable, str(VALIDATOR), str(package_root)],
        check=False,
        capture_output=True,
        text=True,
    )


def _write(path: Path, content: str | bytes) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    if isinstance(content, bytes):
        path.write_bytes(content)
        return
    path.write_text(content, encoding="utf-8")


def _checksums(root: Path) -> str:
    lines: list[str] = []
    for path in sorted(root.rglob("*")):
        if not path.is_file() or path.name == "SHA256SUMS":
            continue
        relative = path.relative_to(root).as_posix()
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        lines.append(f"{digest}  {relative}")
    return "\n".join(lines) + "\n"


def _manifest(**overrides: object) -> dict[str, object]:
    payload: dict[str, object] = {
        "schema": "vibesnake-agent-host-package-v1",
        "host_name": "vibesnake-agent-host",
        "host_version": "0.17.0",
        "runtime_identifier": "linux-x64",
        "self_contained": True,
        "framework_dependent": False,
        "publication_eligible": False,
        "executable": "VibeSnake.AgentHost",
        "protocol_version": "2026-07-28",
        "transport": "stdio",
        "user_data_policy": "godot-app-userdata",
        "signing": "unsigned",
    }
    payload.update(overrides)
    return payload


def _package(root: Path, **overrides: object) -> Path:
    executable = str(overrides.get("executable", "VibeSnake.AgentHost"))
    _write(root / executable, b"host")
    _write(root / "LICENSE", "license\n")
    _write(root / "NOTICE", "notice\n")
    _write(root / "INSTALL.txt", "install\n")
    _write(root / "host-manifest.json", json.dumps(_manifest(**overrides), indent=2) + "\n")
    _write(root / "SHA256SUMS", _checksums(root))
    return root


def test_a_closed_unsigned_host_package_passes(tmp_path: Path) -> None:
    completed = run_validator(_package(tmp_path))
    assert completed.returncode == 0, completed.stdout + completed.stderr
    assert "Agent Host package validation passed:" in completed.stdout


def test_publication_eligible_stays_false(tmp_path: Path) -> None:
    completed = run_validator(_package(tmp_path, publication_eligible=True))
    assert completed.returncode == 1
    assert "publication_eligible must stay false" in completed.stdout


def test_framework_dependent_packages_are_rejected(tmp_path: Path) -> None:
    completed = run_validator(_package(tmp_path, self_contained=False, framework_dependent=True))
    assert completed.returncode == 1
    assert "self_contained must be true" in completed.stdout
    assert "framework_dependent must be false" in completed.stdout


def test_player_data_and_plugin_files_are_rejected(tmp_path: Path) -> None:
    root = _package(tmp_path)
    (root / "preferences.json").write_text("{}\n", encoding="utf-8")
    (root / "mcp.json").write_text("{}\n", encoding="utf-8")
    (root / "SHA256SUMS").write_text(_checksums(root), encoding="utf-8")
    completed = run_validator(root)
    assert completed.returncode == 1
    assert "player or plugin files" in completed.stdout


def test_checksums_must_cover_every_file(tmp_path: Path) -> None:
    root = _package(tmp_path)
    (root / "extra.bin").write_bytes(b"extra")
    completed = run_validator(root)
    assert completed.returncode == 1
    assert "every packaged regular file" in completed.stdout


def test_windows_packages_require_an_exe_name(tmp_path: Path) -> None:
    completed = run_validator(_package(tmp_path, runtime_identifier="win-x64", executable="VibeSnake.AgentHost"))
    assert completed.returncode == 1
    assert "Windows packages must declare a .exe host" in completed.stdout


def test_unknown_manifest_fields_are_rejected(tmp_path: Path) -> None:
    completed = run_validator(_package(tmp_path, grade="A+"))
    assert completed.returncode == 1
    assert "unknown fields" in completed.stdout
