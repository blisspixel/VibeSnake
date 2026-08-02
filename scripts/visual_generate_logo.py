"""Generate the deterministic, project-authored Vibe Snake logo."""

from __future__ import annotations

import argparse
from io import BytesIO
from pathlib import Path
from typing import Sequence

from PIL import Image, ImageDraw

from _generated_assets import write_atomic


CANVAS_SIZE = 1024
OUTPUT_PATH = Path(__file__).resolve().parents[1] / "assets" / "images" / "logo.png"
GLYPHS = {
    "A": ("01110", "10001", "10001", "11111", "10001", "10001", "10001"),
    "B": ("11110", "10001", "10001", "11110", "10001", "10001", "11110"),
    "E": ("11111", "10000", "10000", "11110", "10000", "10000", "11111"),
    "I": ("11111", "00100", "00100", "00100", "00100", "00100", "11111"),
    "K": ("10001", "10010", "10100", "11000", "10100", "10010", "10001"),
    "N": ("10001", "11001", "11001", "10101", "10011", "10011", "10001"),
    "S": ("01111", "10000", "10000", "01110", "00001", "00001", "11110"),
    "V": ("10001", "10001", "10001", "10001", "10001", "01010", "00100"),
}


def _draw_pixel_text(
    draw: ImageDraw.ImageDraw,
    text: str,
    *,
    center_x: int,
    top: int,
    scale: int,
    color: tuple[int, int, int],
    shadow: tuple[int, int, int],
) -> None:
    character_width = 5 * scale
    spacing = scale
    total_width = len(text) * character_width + (len(text) - 1) * spacing
    left = center_x - total_width // 2

    for offset_x, offset_y, fill in ((scale // 3, scale // 3, shadow), (0, 0, color)):
        for character_index, character in enumerate(text):
            glyph = GLYPHS[character]
            glyph_left = left + character_index * (character_width + spacing) + offset_x
            for row_index, row in enumerate(glyph):
                for column_index, pixel in enumerate(row):
                    if pixel == "1":
                        x0 = glyph_left + column_index * scale
                        y0 = top + row_index * scale + offset_y
                        draw.rectangle((x0, y0, x0 + scale - 2, y0 + scale - 2), fill=fill)


def render_logo() -> bytes:
    """Render canonical 1024-pixel logo bytes without external fonts or assets."""
    image = Image.new("RGB", (CANVAS_SIZE, CANVAS_SIZE), (5, 13, 28))
    draw = ImageDraw.Draw(image)

    for coordinate in range(0, CANVAS_SIZE, 32):
        grid_color = (11, 31, 52) if coordinate % 128 else (14, 42, 67)
        draw.line((coordinate, 0, coordinate, CANVAS_SIZE), fill=grid_color, width=1)
        draw.line((0, coordinate, CANVAS_SIZE, coordinate), fill=grid_color, width=1)

    draw.rectangle((24, 24, 999, 999), outline=(34, 247, 199), width=4)
    draw.rectangle((35, 35, 988, 988), outline=(255, 55, 196), width=2)

    for radius, color in ((170, (31, 9, 62)), (120, (49, 12, 75)), (70, (69, 17, 86))):
        draw.ellipse((800 - radius, 260 - radius, 800 + radius, 260 + radius), outline=color, width=12)

    snake_path = ((160, 480), (160, 195), (390, 195), (390, 455), (690, 455), (690, 285), (785, 285))
    draw.line(snake_path, fill=(1, 6, 16), width=112, joint="curve")
    draw.line(snake_path, fill=(22, 157, 91), width=78, joint="curve")
    draw.line(snake_path, fill=(70, 237, 91), width=54, joint="curve")

    for x, y in snake_path[:-1]:
        draw.rectangle((x - 8, y - 39, x + 8, y + 39), fill=(112, 255, 111))

    draw.rounded_rectangle((660, 190, 920, 380), radius=36, fill=(1, 6, 16))
    draw.rounded_rectangle((676, 206, 904, 364), radius=26, fill=(53, 218, 90))
    draw.rectangle((676, 314, 904, 348), fill=(22, 157, 91))
    draw.rectangle((828, 228, 858, 258), fill=(5, 13, 28))
    draw.rectangle((835, 231, 850, 246), fill=(255, 240, 112))
    draw.polygon(((904, 316), (955, 333), (904, 350)), fill=(255, 55, 196))

    draw.rectangle((820, 430, 878, 488), fill=(1, 6, 16))
    draw.rectangle((832, 442, 866, 476), fill=(255, 149, 58))
    draw.rectangle((842, 452, 856, 466), fill=(255, 240, 112))

    _draw_pixel_text(
        draw,
        "VIBE",
        center_x=CANVAS_SIZE // 2,
        top=590,
        scale=24,
        color=(255, 240, 112),
        shadow=(255, 55, 196),
    )
    _draw_pixel_text(
        draw,
        "SNAKE",
        center_x=CANVAS_SIZE // 2,
        top=785,
        scale=20,
        color=(34, 247, 199),
        shadow=(13, 84, 78),
    )

    for y in range(48, 976, 8):
        draw.line((48, y, 976, y), fill=(4, 11, 24), width=1)

    output = BytesIO()
    image.save(output, format="PNG", optimize=False, compress_level=9)
    return output.getvalue()


def check_or_write_logo(*, check: bool) -> bool:
    """Return whether the logo is stale, writing canonical bytes when requested."""
    expected = render_logo()
    try:
        current = OUTPUT_PATH.read_bytes()
    except OSError:
        current = b""
    stale = current != expected
    if stale and not check:
        OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
        write_atomic(OUTPUT_PATH, expected)
    return stale


def main(argv: Sequence[str] | None = None) -> int:
    """Generate the logo or verify that the committed bytes are current."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="fail when the logo is missing or stale")
    arguments = parser.parse_args(argv)
    stale = check_or_write_logo(check=arguments.check)
    if arguments.check and stale:
        print(f"Deterministic logo is missing or stale: {OUTPUT_PATH}")
        return 1
    if not arguments.check:
        print(f"Deterministic logo written: {OUTPUT_PATH}")
    else:
        print("Deterministic logo check passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
