"""Capture and verify current-build screenshots used by the root README."""

from __future__ import annotations

import argparse
from collections import deque
import hashlib
import json
import os
from pathlib import Path
import random
import struct
import tempfile
from typing import Any, Sequence

from _checkout import promote_checkout_source


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
SOURCE_ROOT = promote_checkout_source(REPOSITORY_ROOT)

SCREENSHOT_DIRECTORY = REPOSITORY_ROOT / "docs" / "images" / "screenshots"
MANIFEST_PATH = SCREENSHOT_DIRECTORY / "manifest.json"
README_PATH = REPOSITORY_ROOT / "README.md"
SCREENSHOT_SPECS = (
    ("main-menu.png", "Main menu", "MENU"),
    ("vibe-run.png", "Vibe run", "RUNNING"),
    ("ai-lets-play.png", "AI Lets Play", "LETS_PLAY"),
)
_PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


class ScreenshotEvidenceError(ValueError):
    """Raised when README screenshot evidence is missing, stale, or malformed."""


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _source_paths() -> tuple[Path, ...]:
    paths = {Path(__file__).resolve()}
    paths.update((REPOSITORY_ROOT / "src" / "vibesnake").rglob("*.py"))
    for pattern in ("*.json", "*.png"):
        paths.update((REPOSITORY_ROOT / "assets").rglob(pattern))
    return tuple(sorted(path.resolve() for path in paths if path.is_file()))


def _source_fingerprint() -> str:
    digest = hashlib.sha256()
    for path in _source_paths():
        relative_path = path.relative_to(REPOSITORY_ROOT).as_posix()
        digest.update(relative_path.encode("utf-8"))
        digest.update(b"\0")
        with path.open("rb") as source:
            for chunk in iter(lambda: source.read(1024 * 1024), b""):
                digest.update(chunk)
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
        raise ScreenshotEvidenceError("README screenshots are stale relative to current presentation source")

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
        if (record["width"], record["height"]) != (width, height):
            raise ScreenshotEvidenceError(f"screenshot dimensions changed: {file_name}")
        if record["sha256"] != _sha256(path):
            raise ScreenshotEvidenceError(f"screenshot hash changed: {file_name}")
        relative_path = path.relative_to(REPOSITORY_ROOT).as_posix()
        if relative_path not in readme:
            raise ScreenshotEvidenceError(f"README does not reference {relative_path}")


def _stage_gameplay(game: Any, *, ai_mode: bool) -> None:
    from vibesnake.core.enums import Direction, GameState
    from vibesnake.powerups.magnet import MagnetPowerUp
    from vibesnake.powerups.shield import ShieldPowerUp

    body = deque(
        [
            (22, 21),
            (23, 21),
            (24, 21),
            (25, 21),
            (26, 21),
            (27, 21),
            (28, 21),
            (29, 21),
            (30, 21),
            (31, 21),
            (31, 20),
            (31, 19),
            (32, 19),
            (33, 19),
            (34, 19),
            (35, 19),
            (36, 19),
        ]
    )
    game.snake.body = body
    game.snake.positions_set = set(body)
    game.snake.direction = Direction.RIGHT
    game.snake.animation_time = 2.4
    game.snake.hue_shift = 12
    game.food.position = (48, 13)
    game.score_manager.base_score = 2840 if not ai_mode else 1960
    game.score_manager.combo_count = 7 if not ai_mode else 5
    game.score_manager.time_since_last_food = 0.7
    game.starvation_timer = 18.5
    game.session_food_eaten = 24
    game.session_wraps = 6
    game.detached_segments = [(44, 18), (44, 19), (44, 20)]
    game.detached_segments_timer = 6.2

    shield = ShieldPowerUp((0, 0))
    shield.activate(game)
    shield.timer = 1.4
    magnet = MagnetPowerUp((45, 13))
    magnet.visible_timer = 1.7
    game.powerups.active_powerups = [shield, magnet]
    game.visual_effects.add_score_popup(760, 315, "+180 CLEAN LINE", (120, 255, 220))
    if game.radio is not None:
        game.radio.current_station_index = 0 if ai_mode else 7
        game.radio.is_playing = True
    game.state = GameState.LETS_PLAY if ai_mode else GameState.RUNNING


def capture_screenshots() -> None:
    """Render the three canonical README screenshots from the production game."""
    os.environ.setdefault("PYGAME_HIDE_SUPPORT_PROMPT", "1")
    os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
    os.environ.setdefault("SDL_AUDIODRIVER", "dummy")

    with tempfile.TemporaryDirectory(prefix="vibesnake-readme-") as data_directory:
        os.environ["VIBESNAKE_DATA_DIR"] = data_directory
        isolated_audio_directory = Path(data_directory) / "audio"
        isolated_audio_directory.mkdir()
        os.environ["VIBESNAKE_AUDIO_DIR"] = str(isolated_audio_directory)
        random.seed(0x51A6E)

        import pygame
        import vibesnake

        from vibesnake.core.enums import GameState
        from vibesnake.core.game_state import Game
        from vibesnake.data import settings

        package_path = Path(vibesnake.__file__).resolve()
        expected_logo = (REPOSITORY_ROOT / "assets" / "images" / "logo.png").resolve()
        if not package_path.is_relative_to(SOURCE_ROOT.resolve()):
            raise ScreenshotEvidenceError(f"capture imported code outside the checkout: {package_path}")
        if Path(settings.LOGO_PATH).resolve() != expected_logo or not expected_logo.is_file():
            raise ScreenshotEvidenceError("capture did not resolve the canonical checkout logo")

        pygame.init()
        SCREENSHOT_DIRECTORY.mkdir(parents=True, exist_ok=True)
        game = Game()
        if game.radio is not None:
            game.radio.stop()
            game.radio.set_volume(0.0)
            game.radio.play_current_station(random_track=False)
        game.sound_on = False

        game.state = GameState.MENU
        game.draw()
        pygame.image.save(game.screen, SCREENSHOT_DIRECTORY / "main-menu.png")

        game.reset()
        _stage_gameplay(game, ai_mode=False)
        game.draw()
        pygame.image.save(game.screen, SCREENSHOT_DIRECTORY / "vibe-run.png")

        game.start_lets_play_mode("power_hunter")
        _stage_gameplay(game, ai_mode=True)
        game.draw()
        pygame.image.save(game.screen, SCREENSHOT_DIRECTORY / "ai-lets-play.png")
        if game.radio is not None:
            game.radio.stop()
        pygame.quit()

    records = []
    for file_name, label, state in SCREENSHOT_SPECS:
        path = SCREENSHOT_DIRECTORY / file_name
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
        help="verify that committed screenshots match their manifest and current source",
    )
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    """Capture screenshots or validate the committed evidence set."""
    arguments = _parser().parse_args(argv)
    try:
        if arguments.check:
            verify_screenshots()
            print(f"README screenshots verified: {len(SCREENSHOT_SPECS)} current captures")
        else:
            capture_screenshots()
            verify_screenshots()
            print(f"README screenshots captured: {len(SCREENSHOT_SPECS)} current captures")
    except ScreenshotEvidenceError as error:
        print(f"README screenshot validation failed: {error}")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
