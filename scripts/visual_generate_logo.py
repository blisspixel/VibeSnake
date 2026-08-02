"""Verify the handcrafted Vibe Snake brand logo committed under assets/images."""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import struct
from typing import Sequence


OUTPUT_PATH = Path(__file__).resolve().parents[1] / "assets" / "images" / "logo.png"
# Preferred Snakev2 brand mark (1024x1024 pixel-art snake on gold).
EXPECTED_WIDTH = 1024
EXPECTED_HEIGHT = 1024
EXPECTED_SHA256 = "2ca74991f5b6e83a6da178ff6a63673884425610844a55b29ba35bc89b4a901c"
_PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for chunk in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _png_dimensions(path: Path) -> tuple[int, int]:
    with path.open("rb") as source:
        header = source.read(24)
    if len(header) != 24 or header[:8] != _PNG_SIGNATURE or header[12:16] != b"IHDR":
        raise ValueError(f"not a supported PNG logo: {path}")
    return struct.unpack(">II", header[16:24])


def verify_logo() -> None:
    """Require the committed brand logo to match the preferred mark contract."""
    if not OUTPUT_PATH.is_file():
        raise FileNotFoundError(f"logo is missing: {OUTPUT_PATH}")
    width, height = _png_dimensions(OUTPUT_PATH)
    if (width, height) != (EXPECTED_WIDTH, EXPECTED_HEIGHT):
        raise ValueError(f"logo dimensions must be {EXPECTED_WIDTH}x{EXPECTED_HEIGHT}, got {width}x{height}")
    digest = _sha256(OUTPUT_PATH)
    if digest != EXPECTED_SHA256:
        raise ValueError(
            "logo bytes do not match the preferred brand mark; "
            "restore assets/images/logo.png from the approved Snakev2 mark"
        )


def main(argv: Sequence[str] | None = None) -> int:
    """Verify the committed logo (no procedural regeneration)."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check",
        action="store_true",
        help="fail when the committed logo is missing, resized, or not the preferred brand mark",
    )
    # Accept bare invocation as verify for CI/docs compatibility with older generators.
    arguments = parser.parse_args(argv)
    try:
        verify_logo()
    except (OSError, ValueError) as error:
        print(f"Deterministic logo is missing or stale: {error}")
        return 1
    if arguments.check:
        print("Deterministic logo check passed.")
    else:
        print(f"Preferred brand logo verified: {OUTPUT_PATH}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
