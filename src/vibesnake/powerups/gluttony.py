"""Temporary food collection without snake growth.

Gluttony preserves scoring, combo, starvation reset, progression, and food
respawn behavior. It changes only whether collection adds a body segment.
"""

from vibesnake.powerups.base import PowerUp
from vibesnake.data import settings
from vibesnake.utils.logger import get_logger
import pygame

logger = get_logger(__name__)


class GluttonyPowerUp(PowerUp):
    """Five-second growth-bypass effect.

    Collection sets 'snake_gluttony_active'. The movement step then consumes
    food without setting 'grow=True'. Expiry clears the flag. The crimson coin
    icon and 'G' indicator identify the effect. Its route value and power
    combinations require fixed-seed evaluation.
    """

    def __init__(self, position, duration=5.0):
        """Create Gluttony with a five-second default duration."""
        super().__init__(position, duration)

    def draw(self, surface: pygame.Surface):
        """Draw the crimson Gluttony cell with its gold coin mark."""
        if not self.position:
            logger.warning("Gluttony position is None, skipping draw")
            return

        x, y = self.position
        cell_x = x * settings.CELL_SIZE
        cell_y = y * settings.CELL_SIZE + settings.HUD_HEIGHT

        # Layer 1: Crimson red background (danger/gambit indicator)
        pygame.draw.rect(
            surface,
            (220, 20, 60),  # Crimson red
            (cell_x, cell_y, settings.CELL_SIZE, settings.CELL_SIZE),
        )

        # Layer 2: Gold coin outer ring (reward symbol)
        center_x = cell_x + settings.CELL_SIZE // 2
        center_y = cell_y + settings.CELL_SIZE // 2
        pygame.draw.circle(
            surface,
            (255, 215, 0),  # Gold
            (center_x, center_y),
            6,  # Outer radius
        )

        # Layer 3: Light yellow coin center (depth/shine effect)
        pygame.draw.circle(
            surface,
            (255, 255, 150),  # Light yellow (metallic sheen)
            (center_x, center_y),
            4,  # Inner radius (creates concentric ring)
        )

        # Layer 4: Vertical line (stylized dollar sign)
        pygame.draw.line(surface, (220, 20, 60), (center_x, center_y - 3), (center_x, center_y + 3), 2)

    def on_activate(self, game):
        """Disable growth on food collection and register the active indicator."""
        logger.info("GLUTTONY activated - score without growing")
        game.snake_gluttony_active = True

        # Visual feedback stack
        if hasattr(game, "visual_effects"):
            game.visual_effects.add_stacked_powerup(
                name="Glutton",
                color=(220, 20, 60),  # Crimson red
                duration=self.duration,
                icon_char="G",
            )
            # Brief collection pause.
            game.visual_effects.trigger_hitstop(0.05)

    def on_deactivate(self, game):
        """Restore growth on collection and remove the indicator."""
        logger.info("Gluttony deactivated - back to growing normally")
        game.snake_gluttony_active = False

        # Remove from visual stack (symmetric to on_activate)
        if hasattr(game, "visual_effects"):
            game.visual_effects.remove_stacked_powerup("Glutton")
