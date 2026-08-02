"""Adaptive window presentation for the Python reference player.

Gameplay still renders to a fixed logical canvas (rules + HUD layout). The
window can be resized for phone, square, or ultrawide frames; the canvas is
letterboxed or pillarboxed into the preferred fit with nearest-neighbor scaling
so pixel art stays sharp. Default window framing prefers classic 4:3.
"""

from __future__ import annotations

import pygame

from vibesnake.rendering.theme import PALETTE


class AdaptiveDisplay:
    """Own the OS window while callers draw only on the logical canvas."""

    def __init__(
        self,
        logical_width: int,
        logical_height: int,
        *,
        fullscreen: bool = False,
        preferred_aspect: tuple[int, int] = (4, 3),
        integer_scale: bool = True,
        caption: str = "Vibe Snake",
    ) -> None:
        if logical_width <= 0 or logical_height <= 0:
            raise ValueError("logical dimensions must be positive")
        self.logical_width = logical_width
        self.logical_height = logical_height
        self.preferred_aspect = preferred_aspect
        self.integer_scale = integer_scale
        self.fullscreen = fullscreen
        self.canvas = pygame.Surface((logical_width, logical_height))
        pygame.display.set_caption(caption)
        self._open_window()

    def _default_window_size(self) -> tuple[int, int]:
        """Pick a starting window that prefers 4:3 while fitting the logical canvas."""
        aspect_w, aspect_h = self.preferred_aspect
        # Fit the logical canvas inside a preferred-aspect frame.
        frame_w = max(self.logical_width, int(self.logical_height * aspect_w / aspect_h))
        frame_h = max(self.logical_height, int(frame_w * aspect_h / aspect_w))
        # Cap very large first-open sizes on small monitors when display info exists.
        try:
            info = pygame.display.Info()
            max_w = max(640, int(info.current_w * 0.9)) if info.current_w > 0 else frame_w
            max_h = max(480, int(info.current_h * 0.9)) if info.current_h > 0 else frame_h
            scale = min(1.0, max_w / frame_w, max_h / frame_h)
            frame_w = max(320, int(frame_w * scale))
            frame_h = max(240, int(frame_h * scale))
        except pygame.error:
            pass
        return frame_w, frame_h

    def _open_window(self, size: tuple[int, int] | None = None) -> None:
        if self.fullscreen:
            pygame.display.set_mode((0, 0), pygame.FULLSCREEN)
            return
        window_size = size if size is not None else self._default_window_size()
        pygame.display.set_mode(window_size, pygame.RESIZABLE)

    def set_fullscreen(self, enabled: bool) -> None:
        """Toggle fullscreen presentation while keeping the same logical canvas."""
        self.fullscreen = enabled
        self._open_window()

    def handle_resize(self, size: tuple[int, int]) -> None:
        """Apply a user window resize in windowed mode."""
        if self.fullscreen:
            return
        width = max(320, int(size[0]))
        height = max(240, int(size[1]))
        pygame.display.set_mode((width, height), pygame.RESIZABLE)

    def present(self) -> None:
        """Scale the logical canvas into the OS window with letterbox bars."""
        window = pygame.display.get_surface()
        if window is None:
            return
        window.fill(PALETTE["void"])
        window_w, window_h = window.get_size()
        scale = min(window_w / self.logical_width, window_h / self.logical_height)
        if self.integer_scale and scale >= 1:
            scale = float(max(1, int(scale)))
        draw_w = max(1, int(self.logical_width * scale))
        draw_h = max(1, int(self.logical_height * scale))
        # pygame.transform.scale uses nearest-neighbor sampling (pixel-friendly).
        scaled = pygame.transform.scale(self.canvas, (draw_w, draw_h))
        x = (window_w - draw_w) // 2
        y = (window_h - draw_h) // 2
        window.blit(scaled, (x, y))
        pygame.display.flip()
