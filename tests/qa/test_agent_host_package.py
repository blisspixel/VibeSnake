"""Contracts for the AA-10 self-contained Agent Host package."""

from __future__ import annotations

import hashlib
import json
import subprocess
import sys
from pathlib import Path
from typing import Any

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
VALIDATOR = REPOSITORY_ROOT / "scripts" / "validate_agent_host_package.py"
HOST_LOCKS = (
    "native/src/VibeSnake.AgentPlay/packages.lock.json",
    "native/src/VibeSnake.Persistence/packages.lock.json",
    "native/src/VibeSnake.Rules/packages.lock.json",
    "native/tools/VibeSnake.AgentHost/packages.lock.json",
)


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


def _sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _dump(payload: dict[str, Any]) -> str:
    return json.dumps(payload, indent=2) + "\n"


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


def _inventory(**overrides: object) -> dict[str, object]:
    sources = [{"path": path, "sha256": "a" * 64} for path in HOST_LOCKS]
    lock_set = "\n".join(f"{source['path']}={source['sha256']}" for source in sources)
    payload: dict[str, object] = {
        "schema": "vibesnake-agent-host-inventory-v1",
        "generated_from_locks_only": True,
        "host_version": "0.17.0",
        "runtime_identifier": "linux-x64",
        "source_revision": "b" * 40,
        "source_dirty": False,
        "lock_set_sha256": hashlib.sha256(lock_set.encode()).hexdigest(),
        "dotnet_sdk": "10.0.303",
        "sources": sources,
        "packages": [
            {
                "ecosystem": "nuget",
                "name": "Microsoft.Extensions.Hosting",
                "version": "10.0.11",
                "dependency_types": ["direct"],
                "source_locks": [HOST_LOCKS[-1]],
                "content_hashes": ["c" * 64],
                "frameworks": ["net10.0"],
            },
            {
                "ecosystem": "nuget",
                "name": "ModelContextProtocol",
                "version": "2.2.0",
                "dependency_types": ["direct"],
                "source_locks": [HOST_LOCKS[-1]],
                "content_hashes": ["d" * 64],
                "frameworks": ["net10.0"],
            },
        ],
    }
    payload.update(overrides)
    return payload


def _provenance(root: Path, inventory: dict[str, object], **overrides: object) -> dict[str, object]:
    executable = "VibeSnake.AgentHost"
    if (root / "VibeSnake.AgentHost.exe").is_file():
        executable = "VibeSnake.AgentHost.exe"
    payload: dict[str, object] = {
        "schema": "vibesnake-agent-host-provenance-v1",
        "host_name": "vibesnake-agent-host",
        "host_version": inventory["host_version"],
        "runtime_identifier": inventory["runtime_identifier"],
        "source_revision": inventory["source_revision"],
        "source_dirty": inventory["source_dirty"],
        "self_contained": True,
        "signing": "unsigned",
        "publication_eligible": False,
        "executable_sha256": _sha(root / executable),
        "manifest_sha256": _sha(root / "host-manifest.json"),
        "inventory_sha256": _sha(root / "host-inventory.json"),
        "lock_set_sha256": inventory["lock_set_sha256"],
        "dotnet_sdk": inventory["dotnet_sdk"],
    }
    payload.update(overrides)
    return payload


def _package(
    root: Path,
    *,
    manifest_overrides: dict[str, object] | None = None,
    inventory_overrides: dict[str, object] | None = None,
    provenance_overrides: dict[str, object] | None = None,
) -> Path:
    manifest = _manifest(**(manifest_overrides or {}))
    executable = str(manifest["executable"])
    _write(root / executable, b"host")
    _write(root / "LICENSE", "license\n")
    _write(root / "NOTICE", "notice\n")
    _write(root / "INSTALL.txt", "install\n")
    _write(root / "host-manifest.json", _dump(manifest))
    inventory = _inventory(
        **{
            "host_version": manifest["host_version"],
            "runtime_identifier": manifest["runtime_identifier"],
            **(inventory_overrides or {}),
        }
    )
    _write(root / "host-inventory.json", _dump(inventory))
    provenance = _provenance(root, inventory, **(provenance_overrides or {}))
    _write(root / "host-provenance.json", _dump(provenance))
    _write(root / "SHA256SUMS", _checksums(root))
    return root


def test_a_closed_unsigned_host_package_passes(tmp_path: Path) -> None:
    completed = run_validator(_package(tmp_path))
    assert completed.returncode == 0, completed.stdout + completed.stderr
    assert "Agent Host package validation passed:" in completed.stdout


def test_publication_eligible_stays_false(tmp_path: Path) -> None:
    completed = run_validator(_package(tmp_path, manifest_overrides={"publication_eligible": True}))
    assert completed.returncode == 1
    assert "publication_eligible must stay false" in completed.stdout


def test_framework_dependent_packages_are_rejected(tmp_path: Path) -> None:
    completed = run_validator(
        _package(tmp_path, manifest_overrides={"self_contained": False, "framework_dependent": True})
    )
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
    completed = run_validator(
        _package(
            tmp_path,
            manifest_overrides={"runtime_identifier": "win-x64", "executable": "VibeSnake.AgentHost"},
        )
    )
    assert completed.returncode == 1
    assert "Windows packages must declare a .exe host" in completed.stdout


def test_unknown_manifest_fields_are_rejected(tmp_path: Path) -> None:
    completed = run_validator(_package(tmp_path, manifest_overrides={"grade": "A+"}))
    assert completed.returncode == 1
    assert "unknown fields" in completed.stdout


def test_missing_inventory_is_rejected(tmp_path: Path) -> None:
    root = _package(tmp_path)
    (root / "host-inventory.json").unlink()
    (root / "SHA256SUMS").write_text(_checksums(root), encoding="utf-8")
    completed = run_validator(root)
    assert completed.returncode == 1
    assert "host-inventory.json: required packaged regular file is missing" in completed.stdout


def test_python_packages_are_rejected_from_the_host_inventory(tmp_path: Path) -> None:
    inventory = _inventory()
    packages = list(inventory["packages"])
    packages.append(
        {
            "ecosystem": "python",
            "name": "ruff",
            "version": "0.0.0",
            "dependency_types": ["direct"],
            "source_locks": [HOST_LOCKS[-1]],
            "content_hashes": ["e" * 64],
            "frameworks": ["net10.0"],
        }
    )
    completed = run_validator(_package(tmp_path, inventory_overrides={"packages": packages}))
    assert completed.returncode == 1
    assert "ecosystem must be nuget" in completed.stdout


def test_provenance_must_hash_the_declared_host(tmp_path: Path) -> None:
    completed = run_validator(_package(tmp_path, provenance_overrides={"executable_sha256": "f" * 64}))
    assert completed.returncode == 1
    assert "executable_sha256 must match the declared host" in completed.stdout


def test_provenance_publication_eligible_stays_false(tmp_path: Path) -> None:
    completed = run_validator(_package(tmp_path, provenance_overrides={"publication_eligible": True}))
    assert completed.returncode == 1
    assert "host-provenance.json: publication_eligible must stay false" in completed.stdout


def test_inventory_requires_the_mcp_sdk(tmp_path: Path) -> None:
    inventory = _inventory()
    packages = [package for package in inventory["packages"] if package["name"] != "ModelContextProtocol"]
    completed = run_validator(_package(tmp_path, inventory_overrides={"packages": packages}))
    assert completed.returncode == 1
    assert "missing required package ModelContextProtocol" in completed.stdout
