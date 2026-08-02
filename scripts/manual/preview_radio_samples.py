"""Interactively preview representative radio tracks through the runtime decoder."""

from __future__ import annotations

import argparse
import os
from pathlib import Path

os.environ.setdefault("PYGAME_HIDE_SUPPORT_PROMPT", "1")

import pygame


PROJECT_ROOT = Path(__file__).resolve().parents[2]
PUBLIC_ASSET_DIRECTORY = (PROJECT_ROOT / "assets").resolve()
LOCAL_ARCHIVE_DIRECTORY = (PROJECT_ROOT / "archive").resolve()
DEFAULT_RADIO_DIRECTORY = LOCAL_ARCHIVE_DIRECTORY / "source-assets" / "audio" / "unverified-runtime" / "radio"
SAMPLES = (
    ("The Flow Signal", "flow_signal_crystalline_frequency.mp3"),
    ("Chaos Theory", "jazz_attractor_coil.mp3"),
    ("The Global Coil", "global_coil_dancehall_fang.mp3"),
    ("Ourotron", "synthwave_cipher_molt.mp3"),
    ("The Pit", "dance_glide_algorithm.mp3"),
    ("The Bureau", "the_bureau_bebop_bulletin.mp3"),
    ("The Strike", "rock_clockwork_venom.mp3"),
    ("Underground Scales", "underground_scales_baile_beats.mp3"),
)


def require_review_directory(path: Path) -> Path:
    """Accept only the ignored archive or a workspace outside public source."""
    resolved = path.expanduser().resolve()
    if resolved.is_relative_to(PUBLIC_ASSET_DIRECTORY):
        raise ValueError("radio review must not use the public assets tree")
    if resolved.is_relative_to(PROJECT_ROOT) and not resolved.is_relative_to(LOCAL_ARCHIVE_DIRECTORY):
        raise ValueError("radio review must use the ignored archive")
    return resolved


def available_samples(radio_directory: Path) -> list[tuple[str, Path]]:
    """Return configured samples that exist and are non-empty."""
    return [
        (station, path)
        for station, filename in SAMPLES
        if (path := radio_directory / filename).is_file() and path.stat().st_size > 0
    ]


def preview(samples: list[tuple[str, Path]]) -> None:
    """Play each sample until the reviewer advances or exits."""
    pygame.mixer.init()
    try:
        for station, path in samples:
            choice = input(f"Press Enter to play {station}, or type q to stop: ").strip().lower()
            if choice == "q":
                return

            pygame.mixer.music.load(path)
            pygame.mixer.music.play()
            input(f"Playing {path.name}. Press Enter to stop and continue: ")
            pygame.mixer.music.stop()
    finally:
        pygame.mixer.music.stop()
        pygame.mixer.quit()


def main() -> int:
    """List or preview the configured cross-station sample set."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--list", action="store_true", help="List available samples without playing them.")
    parser.add_argument(
        "--directory",
        type=Path,
        default=Path(os.environ.get("VIBESNAKE_RADIO_REVIEW_DIR", DEFAULT_RADIO_DIRECTORY)),
        help="Ignored or external directory containing review-only radio candidates.",
    )
    args = parser.parse_args()

    try:
        radio_directory = require_review_directory(args.directory)
    except ValueError as error:
        parser.error(str(error))
    samples = available_samples(radio_directory)
    if not samples:
        print(f"No configured samples are available under {radio_directory}")
        return 1

    for station, path in samples:
        print(f"{station}: {path.name}")

    if not args.list:
        preview(samples)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
