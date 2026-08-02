"""Single-use bias for the next food respawn.

Bait records the snake-head cell at collection. The next respawn samples
every free cell with inverse-square Manhattan-distance weighting around
that cell, then the game coordinator clears the marker.
"""

from vibesnake.powerups.base import PowerUp
from vibesnake.data import settings
from vibesnake.utils.logger import get_logger
import pygame

logger = get_logger(__name__)


class BaitPowerUp(PowerUp):
    """Instant food-respawn weighting effect.

    Collection stores the current head as 'game.bait_position' and makes the
    collectible inactive. For a candidate at Manhattan distance 'd', the food
    generator uses weight '1 / (d + 1)^2'. Every free cell retains a nonzero
    probability. The gold crosshair is the non-color identifier.
    """

    def __init__(self, position, duration=0.0):
        """Create an unused Bait marker with zero effect duration."""
        # Bait is single-use instant effect, not time-based
        super().__init__(position, duration)
        self.bait_position = None  # Set on activation

    def draw(self, surface: pygame.Surface):
        """Draw the olive Bait cell with a gold ring and crosshair."""
        if not self.position:
            logger.warning("Bait position is None, skipping draw")
            return

        x, y = self.position
        cell_x = x * settings.CELL_SIZE
        cell_y = y * settings.CELL_SIZE + settings.HUD_HEIGHT

        # Layer 1: Olive/earth green background (control/stability)
        pygame.draw.rect(
            surface,
            (139, 165, 75),  # Olive/earth green
            (cell_x, cell_y, settings.CELL_SIZE, settings.CELL_SIZE),
        )

        # Layer 2: Gold target reticle (precision indicator)
        center_x = cell_x + settings.CELL_SIZE // 2
        center_y = cell_y + settings.CELL_SIZE // 2

        # Outer circle (hollow - "look through" sight)
        pygame.draw.circle(
            surface,
            (255, 215, 0),  # Gold
            (center_x, center_y),
            7,  # Radius
            2,  # Width (hollow circle)
        )

        # Layer 3: Crosshair lines (targeting precision)
        pygame.draw.line(surface, (255, 215, 0), (center_x - 4, center_y), (center_x + 4, center_y), 2)  # Horizontal
        pygame.draw.line(surface, (255, 215, 0), (center_x, center_y - 4), (center_x, center_y + 4), 2)  # Vertical

    def on_activate(self, game):
        """Store the current head as the preference center for the next food respawn.

        The effect becomes inactive immediately. Food respawn owns marker
        consumption and clears 'game.bait_position' even when the board is full.
        """
        # Capture snake head position as bait center
        self.bait_position = game.snake.get_head()
        logger.info("BAIT placed at %s - next food will spawn nearby", self.bait_position)

        # Expose to spawn system (food.py reads this)
        game.bait_position = self.bait_position

        # Mark inactive immediately (single-use, not duration-based)
        self.active = False

    def on_deactivate(self, game):
        """Leave cleanup to the food-respawn path.

        Bait is instant, so the normal timed deactivation path is not entered.
        """
        # Bait cleanup is owned by the food spawn system after consumption.
        return None
