"""Generate deterministic pixel-art badges for the eight radio stations."""

from __future__ import annotations

import argparse
from io import BytesIO
import math
from pathlib import Path
import random
from typing import Any, Sequence

from PIL import Image, ImageDraw, ImageFont

from _generated_assets import write_atomic


BADGE_SIZE = (300, 300)
OUTPUT_DIRECTORY = Path(__file__).resolve().parents[1] / "assets" / "images" / "radio_badges"
STATIONS: tuple[dict[str, Any], ...] = (
    {
        "key": "flow_signal",
        "name": "Flow Signal",
        "tagline": "Future Focus",
        "colors": {"bg": "#1a0033", "accent1": "#ff00ff", "accent2": "#00ffff", "text": "#ffffff"},
        "style": "gradient_wave",
    },
    {
        "key": "chaos_theory",
        "name": "Chaos Theory",
        "tagline": "All Hiss",
        "colors": {"bg": "#000000", "accent1": "#ffd700", "accent2": "#ff4500", "text": "#ffffff"},
        "style": "vinyl",
    },
    {
        "key": "global_coil",
        "name": "Global Coil",
        "tagline": "One Rhythm",
        "colors": {"bg": "#004d00", "accent1": "#00ff00", "accent2": "#ffff00", "text": "#ffffff"},
        "style": "radial",
    },
    {
        "key": "ourotron",
        "name": "Ourotron",
        "tagline": "Retrowave",
        "colors": {"bg": "#0a0020", "accent1": "#ff006e", "accent2": "#8338ec", "text": "#00f5ff"},
        "style": "retro_grid",
    },
    {
        "key": "the_pit",
        "name": "The Pit",
        "tagline": "Venom Bass",
        "colors": {"bg": "#000000", "accent1": "#00ff00", "accent2": "#39ff14", "text": "#ffffff"},
        "style": "waveform",
    },
    {
        "key": "the_bureau",
        "name": "The Bureau",
        "tagline": "Signal News",
        "colors": {"bg": "#1a1a2e", "accent1": "#ff0000", "accent2": "#ffffff", "text": "#ffffff"},
        "style": "news",
    },
    {
        "key": "the_strike",
        "name": "The Strike",
        "tagline": "Molten Rock",
        "colors": {"bg": "#2d1b2e", "accent1": "#ff6b9d", "accent2": "#c9ada7", "text": "#faf3dd"},
        "style": "tape_deck",
    },
    {
        "key": "underground_scales",
        "name": "Underground",
        "tagline": "Scales",
        "colors": {"bg": "#0d1b2a", "accent1": "#00b4d8", "accent2": "#90e0ef", "text": "#caf0f8"},
        "style": "enso",
    },
)


def hex_to_rgb(hex_color: str) -> tuple[int, int, int]:
    """Convert a six-digit hexadecimal color to an RGB tuple."""
    value = hex_color.removeprefix("#")
    if len(value) != 6:
        raise ValueError(f"expected six hexadecimal digits, received {hex_color!r}")
    return int(value[0:2], 16), int(value[2:4], 16), int(value[4:6], 16)


def _gradient(image: Image.Image, color1: str, color2: str) -> None:
    draw = ImageDraw.Draw(image)
    first = hex_to_rgb(color1)
    second = hex_to_rgb(color2)
    denominator = max(1, image.height - 1)
    for y in range(image.height):
        ratio = y / denominator
        color = tuple(round(first[index] * (1 - ratio) + second[index] * ratio) for index in range(3))
        draw.line(((0, y), (image.width - 1, y)), fill=color)


def _pixel_text_surface(text: str, color: tuple[int, int, int], preferred_scale: int, max_width: int) -> Image.Image:
    """Render text from Pillow 12.3's embedded Aileron Regular subset."""
    font = ImageFont.load_default()
    if not isinstance(font, ImageFont.FreeTypeFont) or font.getname() != (
        "Aileron",
        "Regular",
    ):
        raise RuntimeError("Pillow did not provide the attributed Aileron Regular font")
    left, top, right, bottom = font.getbbox(text)
    width = max(1, right - left)
    height = max(1, bottom - top)
    scale = min(preferred_scale, max(1, max_width // width))
    glyph = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    ImageDraw.Draw(glyph).text((-left, -top), text, font=font, fill=(*color, 255))
    return glyph.resize((width * scale, height * scale), Image.Resampling.NEAREST)


def _draw_centered_text(image: Image.Image, text: str, y: int, color: str, preferred_scale: int) -> None:
    maximum_width = image.width - 30
    foreground = _pixel_text_surface(text, hex_to_rgb(color), preferred_scale, maximum_width)
    shadow = _pixel_text_surface(text, (0, 0, 0), preferred_scale, maximum_width)
    x = (image.width - foreground.width) // 2
    image.paste(shadow, (x + 3, y + 3), shadow)
    image.paste(foreground, (x, y), foreground)


def _draw_style(image: Image.Image, station: dict[str, Any]) -> None:
    draw = ImageDraw.Draw(image)
    width, height = image.size
    colors = station["colors"]
    accent1 = hex_to_rgb(colors["accent1"])
    accent2 = hex_to_rgb(colors["accent2"])
    style = station["style"]

    if style == "gradient_wave":
        _gradient(image, colors["bg"], colors["accent1"])
        draw = ImageDraw.Draw(image)
        for wave_index in range(3):
            points = [
                (x, height // 2 + wave_index * 20 + round(30 * math.sin(x / 20 + wave_index * 2))) for x in range(width)
            ]
            draw.line(points, fill=accent2, width=3)
    elif style == "vinyl":
        center = (width // 2, height // 2)
        for radius in (140, 120, 100, 40):
            color = accent1 if radius > 50 else hex_to_rgb(colors["bg"])
            draw.ellipse(
                (center[0] - radius, center[1] - radius, center[0] + radius, center[1] + radius),
                outline=color,
                width=3,
            )
    elif style == "radial":
        for angle in (30, 90, 150, 210, 270, 330):
            radians = math.radians(angle)
            end = (width // 2 + round(120 * math.cos(radians)), height // 2 + round(120 * math.sin(radians)))
            draw.line(((width // 2, height // 2), end), fill=accent1, width=4)
    elif style == "retro_grid":
        _gradient(image, colors["bg"], colors["accent2"])
        draw = ImageDraw.Draw(image)
        vanishing_y = height // 3
        for index in range(5):
            y = height - index * (height // 6)
            if y > vanishing_y:
                draw.line(((0, y), (width // 2, vanishing_y), (width, y)), fill=accent1, width=2)
        for index in range(8):
            x = index * (width // 7)
            draw.line(((x, vanishing_y), (x, height)), fill=accent1, width=2)
    elif style == "tape_deck":
        for reel_x in (width // 3, 2 * width // 3):
            draw.ellipse((reel_x - 40, 110, reel_x + 40, 190), outline=accent1, width=4)
            draw.ellipse((reel_x - 15, 135, reel_x + 15, 165), fill=accent2)
    elif style == "waveform":
        generator = random.Random(station["key"])
        for x in range(0, width, 10):
            spike = generator.randint(20, 100)
            draw.line(((x, height // 2 - spike), (x, height // 2 + spike)), fill=accent1, width=3)
    elif style == "news":
        draw.rectangle((0, 120, width, 180), fill=accent1)
        draw.line(((0, 114), (width, 114)), fill=accent2, width=3)
        draw.line(((0, 186), (width, 186)), fill=accent2, width=3)
    elif style == "enso":
        _gradient(image, colors["bg"], colors["accent1"])
        draw = ImageDraw.Draw(image)
        center = (width // 2, height // 2)
        points = [
            (
                center[0] + round(100 * math.cos(math.radians(angle))),
                center[1] + round(100 * math.sin(math.radians(angle))),
            )
            for angle in range(30, 330, 2)
        ]
        draw.line(points, fill=accent2, width=8)
    else:
        raise ValueError(f"unknown station badge style: {style}")


def render_station_badge(station: dict[str, Any]) -> bytes:
    """Render one station definition to canonical PNG bytes."""
    image = Image.new("RGB", BADGE_SIZE, hex_to_rgb(station["colors"]["bg"]))
    _draw_style(image, station)
    draw = ImageDraw.Draw(image)
    draw.rectangle((5, 5, image.width - 6, image.height - 6), outline=hex_to_rgb(station["colors"]["text"]), width=3)
    _draw_centered_text(image, station["name"], image.height // 4, station["colors"]["text"], 4)
    _draw_centered_text(image, station["tagline"], 3 * image.height // 4, station["colors"]["text"], 3)

    output = BytesIO()
    image.save(output, format="PNG", optimize=False, compress_level=9)
    return output.getvalue()


def check_or_write_badges(*, check: bool) -> list[Path]:
    """Return missing or stale badge paths, writing canonical bytes when asked."""
    if not check:
        OUTPUT_DIRECTORY.mkdir(parents=True, exist_ok=True)

    stale_paths: list[Path] = []
    expected_paths = {OUTPUT_DIRECTORY / f"{station['key']}_badge.png" for station in STATIONS}
    unexpected_paths = set(OUTPUT_DIRECTORY.glob("*_badge.png")) - expected_paths
    if check:
        stale_paths.extend(sorted(unexpected_paths))
    else:
        for path in unexpected_paths:
            path.unlink()

    for station in STATIONS:
        output_path = OUTPUT_DIRECTORY / f"{station['key']}_badge.png"
        expected = render_station_badge(station)
        if check:
            try:
                actual = output_path.read_bytes()
            except OSError:
                stale_paths.append(output_path)
                continue
            if actual != expected:
                stale_paths.append(output_path)
        else:
            write_atomic(output_path, expected)
    return stale_paths


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="fail if checked-in badge bytes are stale")
    args = parser.parse_args(argv)

    stale_paths = check_or_write_badges(check=args.check)
    if stale_paths:
        for path in stale_paths:
            print(f"Stale station badge: {path.relative_to(OUTPUT_DIRECTORY.parent.parent)}")
        print("Regenerate with: python scripts/visual_generate_badges.py")
        return 1
    action = "verified" if args.check else "generated"
    print(f"Station badges {action}: files={len(STATIONS)} size={BADGE_SIZE[0]}x{BADGE_SIZE[1]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
