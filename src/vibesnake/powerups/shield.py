"""Timed, single-use protection from a fatal collision.

Shield preserves movement and scoring rules. It expires after its duration
or is consumed immediately when collision resolution prevents a death. It
does not prevent starvation.
"""

from vibesnake.powerups.base import PowerUp
from vibesnake.data import settings
from vibesnake.utils.logger import get_logger
import pygame

logger = get_logger(__name__)


class ShieldPowerUp(PowerUp):
    """Five-second collision-protection effect.

    Collection sets 'snake_is_shielded'. Collision resolution consumes the
    matching effect before continuing the run. Expiry clears unused
    protection. The cyan grid cell and 'S' indicator communicate availability.
    Route value remains a fixed-seed observation question.
    """

    def __init__(self, position, duration=5.0):
        """Create Shield with a five-second default duration."""
        super().__init__(position, duration)

    def draw(self, surface: pygame.Surface):
        """Draw the cyan Shield grid cell."""
        if not self.position:
            logger.warning("Shield position is None, skipping draw")
            return

        x, y = self.position
        pygame.draw.rect(
            surface,
            (0, 255, 255),  # Cyan (RGB: full green + full blue)
            (
                x * settings.CELL_SIZE,
                y * settings.CELL_SIZE + settings.HUD_HEIGHT,
                settings.CELL_SIZE,
                settings.CELL_SIZE,
            ),
        )

    def on_activate(self, game):
        """Enable one collision block and add its indicator."""
        logger.info("Shield activated - player protected from one collision")
        game.snake_is_shielded = True

        # Register the active-effect indicator.
        if hasattr(game, "visual_effects"):
            game.visual_effects.add_stacked_powerup(
                name="Shield",
                color=(0, 255, 255),  # Cyan
                duration=self.duration,
                icon_char="S",
            )
            # Brief collection pause.
            game.visual_effects.trigger_hitstop(0.05)

    def on_deactivate(self, game):
        """Clear collision protection and remove its indicator."""
        logger.info("Shield deactivated")
        game.snake_is_shielded = False

        # Remove from visual stack (symmetric to on_activate)
        if hasattr(game, "visual_effects"):
            game.visual_effects.remove_stacked_powerup("Shield")
