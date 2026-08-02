"""Temporary doubled movement cadence.

Boost halves the logic-tick interval without changing movement distance,
scoring, growth, collision, or random-selection rules. Its control cost
and tactical value remain balance questions for fixed-seed QA.
"""

from vibesnake.powerups.base import PowerUp
from vibesnake.powerups.cadence import clear_cadence_factor, set_cadence_factor
from vibesnake.data import settings
from vibesnake.utils.logger import get_logger
import pygame

logger = get_logger(__name__)


class BoostPowerUp(PowerUp):
    """Four-second movement-cadence effect.

    Collection sets 'logic_tick_override' to half the configured interval.
    Expiry clears the override. The grid icon is an orange square with three
    gold horizontal lines; the active-effect stack uses the letter 'B'.
    """

    def __init__(self, position, duration=4.0):
        """Create Boost with a four-second default duration."""
        super().__init__(position, duration)

    def draw(self, surface: pygame.Surface):
        """Draw the orange Boost cell and its three gold speed lines."""
        if not self.position:
            logger.warning("Boost position is None, skipping draw")
            return

        x, y = self.position
        cell_x = x * settings.CELL_SIZE
        cell_y = y * settings.CELL_SIZE + settings.HUD_HEIGHT

        # Base: Bright orange square (#FF8C00)
        pygame.draw.rect(surface, (255, 140, 0), (cell_x, cell_y, settings.CELL_SIZE, settings.CELL_SIZE))

        # Motion effect: Gold speed lines (#FFD700)
        line_color = (255, 215, 0)
        pygame.draw.line(surface, line_color, (cell_x + 2, cell_y + 5), (cell_x + 8, cell_y + 5), 2)
        pygame.draw.line(surface, line_color, (cell_x + 2, cell_y + 10), (cell_x + 10, cell_y + 10), 2)
        pygame.draw.line(surface, line_color, (cell_x + 2, cell_y + 15), (cell_x + 8, cell_y + 15), 2)

    def on_activate(self, game):
        """Halve the logic-tick interval and register the active-effect indicator."""
        logger.info("BOOST activated - speed doubled (zoom zoom)")
        set_cadence_factor(game, "boost", 0.5)

        if hasattr(game, "visual_effects"):
            game.visual_effects.add_stacked_powerup(
                name="Boost", color=(255, 140, 0), duration=self.duration, icon_char="B"
            )
            game.visual_effects.trigger_hitstop(0.05)

    def on_deactivate(self, game):
        """Remove the Boost factor without clearing other cadence effects."""
        logger.info("Boost deactivated - back to normal speed")
        clear_cadence_factor(game, "boost")

        if hasattr(game, "visual_effects"):
            game.visual_effects.remove_stacked_powerup("Boost")
