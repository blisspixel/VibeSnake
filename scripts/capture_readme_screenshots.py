"""Capture and verify native Godot screenshots used by the root README."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
from typing import Any, Sequence


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
SCREENSHOT_DIRECTORY = REPOSITORY_ROOT / "docs" / "images" / "screenshots"
MANIFEST_PATH = SCREENSHOT_DIRECTORY / "manifest.json"
README_PATH = REPOSITORY_ROOT / "README.md"
SCREENSHOT_SPECS = (
    ("main-menu.png", "Main menu", "MENU"),
    ("powers-run.png", "Vibe mode gameplay", "RUNNING"),
    ("customization.png", "Customization", "COSMETICS"),
    ("ai-channel.png", "AI channel", "SPECTATOR"),
)
_PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
_TEXT_FINGERPRINT_SUFFIXES = frozenset({".cs", ".godot", ".json", ".md", ".py", ".svg", ".tscn", ".txt"})
_GAME_FINGERPRINT_SUFFIXES = _TEXT_FINGERPRINT_SUFFIXES | {".png"}


class ScreenshotEvidenceError(ValueError):
    """Raised when README screenshot evidence is missing, stale, or malformed."""


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _fingerprint_payload(path: Path) -> bytes:
    data = path.read_bytes()
    if path.suffix.lower() in _TEXT_FINGERPRINT_SUFFIXES:
        return data.replace(b"\r\n", b"\n").replace(b"\r", b"\n")
    return data


def _source_paths() -> tuple[Path, ...]:
    paths = {Path(__file__).resolve()}
    paths.update(
        path
        for path in (REPOSITORY_ROOT / "game").rglob("*")
        if path.is_file()
        and path.suffix.lower() in _GAME_FINGERPRINT_SUFFIXES
        and ".godot" not in path.parts
        and "bin" not in path.parts
        and "obj" not in path.parts
    )
    for source_root in (
        REPOSITORY_ROOT / "native" / "src" / "VibeSnake.Rules",
        REPOSITORY_ROOT / "native" / "src" / "VibeSnake.Persistence",
    ):
        paths.update(path for path in source_root.rglob("*.cs") if "bin" not in path.parts and "obj" not in path.parts)
    paths.add(REPOSITORY_ROOT / "config" / "content_inventory.json")
    return tuple(sorted(path.resolve() for path in paths if path.is_file()))


def _source_fingerprint() -> str:
    digest = hashlib.sha256()
    for path in _source_paths():
        relative_path = path.relative_to(REPOSITORY_ROOT).as_posix()
        digest.update(relative_path.encode("utf-8"))
        digest.update(b"\0")
        digest.update(_fingerprint_payload(path))
        digest.update(b"\0")
    return digest.hexdigest()


def _png_dimensions(path: Path) -> tuple[int, int]:
    with path.open("rb") as source:
        header = source.read(24)
    if len(header) != 24 or header[:8] != _PNG_SIGNATURE or header[12:16] != b"IHDR":
        raise ScreenshotEvidenceError(f"not a supported PNG screenshot: {path}")
    return struct.unpack(">II", header[16:24])


def _require_object(value: Any, location: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise ScreenshotEvidenceError(f"{location} must be a JSON object")
    return value


def _load_manifest() -> dict[str, Any]:
    try:
        document = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise ScreenshotEvidenceError(f"cannot read screenshot manifest: {error}") from error
    manifest = _require_object(document, "screenshot manifest")
    expected_fields = {"schemaVersion", "generator", "sourceSha256", "screenshots"}
    if set(manifest) != expected_fields:
        raise ScreenshotEvidenceError("screenshot manifest fields do not match schema 1")
    if manifest["schemaVersion"] != 1:
        raise ScreenshotEvidenceError("unsupported screenshot manifest schema")
    if manifest["generator"] != "scripts/capture_readme_screenshots.py":
        raise ScreenshotEvidenceError("screenshot manifest generator is invalid")
    if not isinstance(manifest["sourceSha256"], str) or len(manifest["sourceSha256"]) != 64:
        raise ScreenshotEvidenceError("screenshot manifest sourceSha256 is invalid")
    if not isinstance(manifest["screenshots"], list):
        raise ScreenshotEvidenceError("screenshot manifest screenshots must be an array")
    return manifest


def verify_screenshots() -> None:
    """Validate screenshot hashes, dimensions, source freshness, and README links."""
    manifest = _load_manifest()
    current_source_hash = _source_fingerprint()
    if manifest["sourceSha256"] != current_source_hash:
        raise ScreenshotEvidenceError("README screenshots are stale relative to current native presentation source")

    expected_names = {spec[0] for spec in SCREENSHOT_SPECS}
    records: dict[str, dict[str, Any]] = {}
    for index, value in enumerate(manifest["screenshots"]):
        record = _require_object(value, f"screenshot record {index}")
        expected_fields = {"file", "label", "state", "width", "height", "sha256"}
        if set(record) != expected_fields:
            raise ScreenshotEvidenceError(f"screenshot record {index} has invalid fields")
        file_name = record["file"]
        if not isinstance(file_name, str) or Path(file_name).name != file_name:
            raise ScreenshotEvidenceError(f"screenshot record {index} has an unsafe file name")
        if file_name in records:
            raise ScreenshotEvidenceError(f"duplicate screenshot record: {file_name}")
        records[file_name] = record
    if set(records) != expected_names:
        raise ScreenshotEvidenceError("screenshot manifest does not match the required README set")

    readme = README_PATH.read_text(encoding="utf-8")
    for file_name, label, state in SCREENSHOT_SPECS:
        record = records[file_name]
        if record["label"] != label or record["state"] != state:
            raise ScreenshotEvidenceError(f"screenshot metadata mismatch: {file_name}")
        path = SCREENSHOT_DIRECTORY / file_name
        if not path.is_file():
            raise ScreenshotEvidenceError(f"missing README screenshot: {path}")
        width, height = _png_dimensions(path)
        if (width, height) != (1280, 720):
            raise ScreenshotEvidenceError(f"screenshot is not 1280x720: {file_name}")
        if (record["width"], record["height"]) != (width, height):
            raise ScreenshotEvidenceError(f"screenshot dimensions changed: {file_name}")
        if record["sha256"] != _sha256(path):
            raise ScreenshotEvidenceError(f"screenshot hash changed: {file_name}")
        relative_path = path.relative_to(REPOSITORY_ROOT).as_posix()
        if relative_path not in readme:
            raise ScreenshotEvidenceError(f"README does not reference {relative_path}")


def _resolve_godot_executable(explicit: str | None) -> Path:
    candidates: list[Path] = []
    configured = explicit or os.environ.get("VIBESNAKE_GODOT_EXECUTABLE")
    if configured:
        candidates.append(Path(configured))
    tools_root = REPOSITORY_ROOT / ".tools" / "godot" / "4.7.1"
    if sys.platform == "win32":
        candidates.extend(sorted(tools_root.rglob("*console.exe")))
        candidates.extend(sorted(tools_root.rglob("Godot*.exe")))
    elif sys.platform == "darwin":
        candidates.extend(sorted(tools_root.rglob("Godot_mono.app/Contents/MacOS/Godot")))
    else:
        candidates.extend(sorted(tools_root.rglob("Godot*")))
    for command in ("godot-mono", "godot"):
        resolved = shutil.which(command)
        if resolved:
            candidates.append(Path(resolved))
    for candidate in candidates:
        if candidate.is_file():
            return candidate.resolve()
    raise ScreenshotEvidenceError(
        "Godot 4.7.1 executable not found; run scripts/install_godot.ps1 or pass --godot-executable"
    )


def capture_screenshots(godot_executable: str | None) -> None:
    """Render four canonical README screenshots from the native Godot game."""
    godot = _resolve_godot_executable(godot_executable)
    build = subprocess.run(
        [
            "dotnet",
            "build",
            str(REPOSITORY_ROOT / "game" / "VibeSnake.Game.sln"),
            "--nologo",
        ],
        cwd=REPOSITORY_ROOT,
        text=True,
        capture_output=True,
        check=False,
        timeout=180,
    )
    if build.returncode != 0:
        raise ScreenshotEvidenceError("native screenshot build failed:\n" + build.stdout + build.stderr)

    SCREENSHOT_DIRECTORY.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="vibesnake-readme-") as data_directory:
        command = [
            str(godot),
            "--path",
            str(REPOSITORY_ROOT / "game"),
            "--rendering-method",
            "gl_compatibility",
            "--",
            f"--readme-capture-dir={SCREENSHOT_DIRECTORY}",
            f"--smoke-user-data-root={data_directory}",
        ]
        capture = subprocess.run(
            command,
            cwd=REPOSITORY_ROOT,
            text=True,
            capture_output=True,
            check=False,
            timeout=120,
        )
    output = capture.stdout + capture.stderr
    if capture.returncode != 0 or "VIBESNAKE_README_CAPTURE_OK count=4" not in output:
        raise ScreenshotEvidenceError("native Godot screenshot capture failed:\n" + output)

    records = []
    for file_name, label, state in SCREENSHOT_SPECS:
        path = SCREENSHOT_DIRECTORY / file_name
        if not path.is_file():
            raise ScreenshotEvidenceError(f"native capture did not write {file_name}")
        width, height = _png_dimensions(path)
        records.append(
            {
                "file": file_name,
                "label": label,
                "state": state,
                "width": width,
                "height": height,
                "sha256": _sha256(path),
            }
        )
    manifest = {
        "schemaVersion": 1,
        "generator": "scripts/capture_readme_screenshots.py",
        "sourceSha256": _source_fingerprint(),
        "screenshots": records,
    }
    MANIFEST_PATH.write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check",
        action="store_true",
        help="verify committed screenshots against native presentation source",
    )
    parser.add_argument(
        "--godot-executable",
        help="path to the pinned Godot 4.7.1 executable used for capture",
    )
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    """Capture screenshots or validate the committed evidence set."""
    arguments = _parser().parse_args(argv)
    try:
        if arguments.check:
            verify_screenshots()
            print(f"README screenshots verified: {len(SCREENSHOT_SPECS)} native captures")
        else:
            capture_screenshots(arguments.godot_executable)
            verify_screenshots()
            print(f"README screenshots captured: {len(SCREENSHOT_SPECS)} native captures")
    except (OSError, subprocess.SubprocessError, ScreenshotEvidenceError) as error:
        print(f"README screenshot validation failed: {error}")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
