"""Retro-modern presentation tokens for the Python reference UI.

Palette and type are intentionally limited so menus read like a polished arcade
cabinet rather than a flat 2010s utility panel. Drawing helpers keep hard edges
and pixel-scale text so the brand logo and board share one visual language.
"""

from __future__ import annotations

import pygame


# Limited CRT-adjacent palette with one neon accent family.
PALETTE = {
    "void": (8, 6, 18),
    "panel": (18, 14, 36),
    "panel_hi": (32, 26, 58),
    "ink": (6, 4, 12),
    "text": (240, 236, 255),
    "muted": (148, 140, 180),
    "dim": (88, 82, 118),
    "accent": (70, 255, 170),
    "accent_hot": (255, 80, 180),
    "accent_gold": (255, 214, 70),
    "accent_sky": (90, 200, 255),
    "danger": (255, 72, 96),
    "border": (255, 255, 255),
}


def ensure_font_module() -> None:
    """Initialize the Pygame font module when a surface is about to be rendered."""
    if not pygame.font.get_init():
        pygame.font.init()


def pixel_font(px: int, *, bold: bool = False) -> pygame.font.Font:
    """Return a non-antialiased font sized for nearest-neighbor upscaling."""
    ensure_font_module()
    # Prefer a monospaced system face when present; fall back to the default bitmap.
    for name in ("Consolas", "Courier New", "Liberation Mono", "DejaVu Sans Mono", "monospace"):
        path = pygame.font.match_font(name, bold=bold)
        if path:
            font = pygame.font.Font(path, max(8, px))
            font.set_bold(bold)
            return font
    font = pygame.font.Font(None, max(10, px + 4))
    font.set_bold(bold)
    return font


def render_pixel_text(
    text: str,
    *,
    color: tuple[int, int, int],
    scale: int = 2,
    bold: bool = False,
    base_px: int = 12,
) -> pygame.Surface:
    """Render crisp pixel-style text by drawing small, then scaling with nearest neighbor."""
    font = pixel_font(base_px, bold=bold)
    # antialias=False keeps hard glyph edges before the integer scale step.
    glyph = font.render(text, False, color)
    if scale <= 1:
        return glyph
    return pygame.transform.scale(glyph, (glyph.get_width() * scale, glyph.get_height() * scale))


def draw_panel(
    surface: pygame.Surface,
    rect: pygame.Rect,
    *,
    fill: tuple[int, int, int] | None = None,
    border: tuple[int, int, int] | None = None,
    border_width: int = 2,
    shadow: bool = True,
) -> None:
    """Draw a hard-edged arcade panel with optional drop shadow."""
    fill_color = fill if fill is not None else PALETTE["panel"]
    border_color = border if border is not None else PALETTE["border"]
    if shadow:
        shadow_rect = rect.move(3, 3)
        pygame.draw.rect(surface, PALETTE["ink"], shadow_rect)
    pygame.draw.rect(surface, fill_color, rect)
    if border_width > 0:
        pygame.draw.rect(surface, border_color, rect, border_width)
        # Inner highlight for a cheap "beveled cart" look without soft radii.
        inner = rect.inflate(-border_width * 2, -border_width * 2)
        if inner.width > 2 and inner.height > 2:
            pygame.draw.line(surface, PALETTE["panel_hi"], inner.topleft, inner.topright, 1)


def draw_pixel_grid(surface: pygame.Surface, *, step: int = 16, color: tuple[int, int, int] | None = None) -> None:
    """Draw a subtle pixel grid for menu backdrops."""
    grid = color if color is not None else (20, 16, 40)
    width, height = surface.get_size()
    for x in range(0, width, step):
        pygame.draw.line(surface, grid, (x, 0), (x, height))
    for y in range(0, height, step):
        pygame.draw.line(surface, grid, (0, y), (width, y))
