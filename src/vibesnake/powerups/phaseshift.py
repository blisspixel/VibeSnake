"""Temporary bypass for body and detached-obstacle collisions.

Phase Shift leaves movement cadence, scoring, starvation, and food rules
unchanged. Screen edges use the mode's normal wrapping behavior.
"""

from vibesnake.powerups.base import PowerUp
from vibesnake.data import settings
from vibesnake.utils.logger import get_logger
import pygame

logger = get_logger(__name__)


class PhaseShiftPowerUp(PowerUp):
    """Five-second collision-rule override.

    While active, the movement resolver ignores self-collision and the game
    coordinator skips detached-segment collision. Expiry restores both checks.
    The layered purple icon and 'P' indicator distinguish the effect without
    relying on animation alone.
    """

    def __init__(self, position, duration=5.0):
        """Create Phase Shift with a five-second default duration."""
        super().__init__(position, duration)

    def draw(self, surface: pygame.Surface):
        """Draw the layered purple Phase Shift cell with three white phase lines."""
        if not self.position:
            logger.warning("PhaseShift position is None, skipping draw")
            return

        x, y = self.position
        cell_x = x * settings.CELL_SIZE
        cell_y = y * settings.CELL_SIZE + settings.HUD_HEIGHT

        # Layer 1: Purple/magenta outer square (solid border)
        pygame.draw.rect(
            surface,
            (200, 50, 255),  # Vibrant purple/magenta
            (cell_x, cell_y, settings.CELL_SIZE, settings.CELL_SIZE),
        )

        # Layer 2: Inner glow (ghost effect - lighter shade)
        inner_margin = 3
        pygame.draw.rect(
            surface,
            (255, 200, 255),  # Light pink/magenta (translucent appearance)
            (
                cell_x + inner_margin,
                cell_y + inner_margin,
                settings.CELL_SIZE - inner_margin * 2,
                settings.CELL_SIZE - inner_margin * 2,
            ),
        )

        # Layer 3: Phase lines (energy/vibration effect)
        line_color = (255, 255, 255)  # White
        pygame.draw.line(surface, line_color, (cell_x + 3, cell_y + 5), (cell_x + 17, cell_y + 5), 1)
        pygame.draw.line(surface, line_color, (cell_x + 5, cell_y + 10), (cell_x + 15, cell_y + 10), 1)
        pygame.draw.line(surface, line_color, (cell_x + 3, cell_y + 15), (cell_x + 17, cell_y + 15), 1)

    def on_activate(self, game):
        """Enable body and detached-obstacle collision bypass and add its indicator."""
        logger.info("PHASE SHIFT activated - ghost mode engaged")
        game.snake_phase_shift_active = True

        # Visual feedback stack
        if hasattr(game, "visual_effects"):
            game.visual_effects.add_stacked_powerup(
                name="Phase",
                color=(200, 50, 255),  # Vibrant purple/magenta
                duration=self.duration,
                icon_char="P",
            )
            # Brief collection pause.
            game.visual_effects.trigger_hitstop(0.05)

    def on_deactivate(self, game):
        """Restore collision checks and remove the indicator."""
        logger.info("Phase Shift deactivated - back to solid form")
        game.snake_phase_shift_active = False

        # Remove from visual stack (symmetric to on_activate)
        if hasattr(game, "visual_effects"):
            game.visual_effects.remove_stacked_powerup("Phase")
