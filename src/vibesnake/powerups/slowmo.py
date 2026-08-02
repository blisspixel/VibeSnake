"""Temporary halved movement cadence.

Slow-Mo doubles the logic-tick interval without changing movement
distance, scoring, growth, collision, or random-selection rules. Its
readability and tactical value remain balance and accessibility gates.
"""

from vibesnake.powerups.base import PowerUp
from vibesnake.powerups.cadence import clear_cadence_factor, set_cadence_factor
from vibesnake.data import settings
from vibesnake.utils.logger import get_logger
import pygame

logger = get_logger(__name__)


class SlowMoPowerUp(PowerUp):
    """Six-second movement-cadence effect.

    Collection sets 'logic_tick_override' to twice the configured interval.
    Expiry clears the override. The grid icon is a yellow square; the
    active-effect stack uses the letter 'T'.
    """

    def __init__(self, position, duration=6.0):
        """Create Slow-Mo with a six-second default duration."""
        super().__init__(position, duration)

    def draw(self, surface: pygame.Surface):
        """Draw the yellow Slow-Mo grid cell."""
        if not self.position:
            logger.warning("SlowMo position is None, skipping draw")
            return

        x, y = self.position
        pygame.draw.rect(
            surface,
            (255, 255, 0),  # Bright yellow (full red + full green)
            (
                x * settings.CELL_SIZE,
                y * settings.CELL_SIZE + settings.HUD_HEIGHT,
                settings.CELL_SIZE,
                settings.CELL_SIZE,
            ),
        )

    def on_activate(self, game):
        """Double the logic-tick interval and register the active-effect indicator."""
        logger.info("Slow-Mo activated - game speed reduced by 50%%")
        set_cadence_factor(game, "slowmo", 2.0)

        # Visual feedback stack
        if hasattr(game, "visual_effects"):
            game.visual_effects.add_stacked_powerup(
                name="SlowMo",
                color=(255, 255, 0),
                duration=self.duration,
                icon_char="T",  # "T" for Time
            )
            game.visual_effects.trigger_hitstop(0.05)

    def on_deactivate(self, game):
        """Remove the Slow-Mo factor without clearing other cadence effects."""
        logger.info("Slow-Mo deactivated - normal speed restored")
        clear_cadence_factor(game, "slowmo")

        # Remove visual feedback
        if hasattr(game, "visual_effects"):
            game.visual_effects.remove_stacked_powerup("SlowMo")
