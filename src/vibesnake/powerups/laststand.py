"""Held, single-use recovery from an otherwise terminal event.

Last Stand can intercept collision or starvation death. Recovery keeps the
score, shrinks the snake to half length rounded up, resets starvation, and
grants three seconds of collision immunity.
"""

from vibesnake.powerups.base import PowerUp
from vibesnake.data import settings
from vibesnake.utils.logger import get_logger
import pygame

logger = get_logger(__name__)


class LastStandPowerUp(PowerUp):
    """Passive recovery held until death resolution consumes it.

    Collection sets 'last_stand_held' and displays an 'L' indicator. The
    overridden update method prevents timed expiry. Consumption clears the
    held state; the coordinator owns shrink, starvation reset, and recovery
    immunity. Whether holding it creates worthwhile route choices requires
    observation rather than source-level claims.
    """

    def __init__(self, position, duration=0.0):
        """Create an unheld Last Stand effect with no timer duration."""
        # Last Stand is passive (held indefinitely, not duration-based)
        super().__init__(position, duration)
        self.is_held = False  # Set to True on collection

    def draw(self, surface: pygame.Surface):
        """Draw the orange-red Last Stand cell with a gold upward-arrow mark."""
        if not self.position:
            logger.warning("LastStand position is None, skipping draw")
            return

        x, y = self.position
        cell_x = x * settings.CELL_SIZE
        cell_y = y * settings.CELL_SIZE + settings.HUD_HEIGHT

        # Layer 1: Orange-red background (fire/phoenix theme)
        pygame.draw.rect(
            surface,
            (255, 69, 0),  # Orange-red (fire)
            (cell_x, cell_y, settings.CELL_SIZE, settings.CELL_SIZE),
        )

        # Layer 2: Gold upward arrow (resurrection symbol)
        center_x = cell_x + settings.CELL_SIZE // 2
        center_y = cell_y + settings.CELL_SIZE // 2

        # Upward arrow polygon (7 points forming tapered arrow)
        arrow_points = [
            (center_x, center_y - 6),  # Top point (apex)
            (center_x - 4, center_y - 2),  # Left wing outer
            (center_x - 2, center_y - 2),  # Left wing inner
            (center_x - 2, center_y + 4),  # Left base
            (center_x + 2, center_y + 4),  # Right base
            (center_x + 2, center_y - 2),  # Right wing inner
            (center_x + 4, center_y - 2),  # Right wing outer
        ]
        pygame.draw.polygon(surface, (255, 215, 0), arrow_points)  # Gold

        # Layer 3: Flame sparkles (energy/fire effect)
        pygame.draw.circle(surface, (255, 255, 100), (cell_x + 3, cell_y + 3), 1)
        pygame.draw.circle(surface, (255, 255, 100), (cell_x + 17, cell_y + 3), 1)
        pygame.draw.circle(surface, (255, 255, 100), (cell_x + 3, cell_y + 17), 1)
        pygame.draw.circle(surface, (255, 255, 100), (cell_x + 17, cell_y + 17), 1)

    def on_activate(self, game):
        """Mark Last Stand as held and add its persistent indicator."""
        self.is_held = True
        logger.info("Last Stand collected and held")
        game.last_stand_held = True

        # Keep active while held (no expiration, waits for death trigger)
        # active flag stays True until death event consumes it

        # Add to visual stack (passive - no timer, shows until used)
        if hasattr(game, "visual_effects"):
            game.visual_effects.add_stacked_powerup(
                name="L.Stand",
                color=(255, 69, 0),  # Orange-red (fire)
                duration=9999.0,  # Passive - effectively infinite until triggered
                icon_char="L",
            )
            # Brief collection pause.
            game.visual_effects.trigger_hitstop(0.05)

    def update(self, dt, game):
        """Advance only the pre-collection visibility timer.

        Once collected, Last Stand remains active until explicit consumption.
        """
        if not self.activated:
            super().update(dt, game)

    def on_deactivate(self, game):
        """Clear held state and remove its indicator after consumption."""
        logger.info("Last Stand consumed - second chance used")
        game.last_stand_held = False
        self.is_held = False

        # Remove from visual stack (symmetric to on_activate)
        if hasattr(game, "visual_effects"):
            game.visual_effects.remove_stacked_powerup("L.Stand")
