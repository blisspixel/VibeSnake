"""Temporary food attraction toward the snake head.

While Magnet is active, each gameplay frame moves food by at most one
column and one row toward the current head. The effect does not change
food scoring, growth, collision, or random respawn rules.
"""

from vibesnake.powerups.base import PowerUp
from vibesnake.data import settings
from vibesnake.utils.logger import get_logger
import pygame
from vibesnake.audio.manager import MAGNET_SOUND

logger = get_logger(__name__)


class MagnetPowerUp(PowerUp):
    """Six-second food-attraction effect.

    Collection sets 'magnet_active' and may play the authored Magnet cue.
    Expiry clears the flag. The grid icon combines a deep-pink fill with a
    gold border; the active-effect stack uses the letter 'M'. Combination
    value remains unvalidated player-experience work.
    """

    def __init__(self, position, duration=6.0):
        """Create Magnet with a six-second default duration."""
        super().__init__(position, duration)

    def draw(self, surface: pygame.Surface):
        """Draw the deep-pink Magnet cell with a gold border."""
        if not self.position:
            logger.warning("Magnet position is None, skipping draw")
            return

        x, y = self.position
        cell_rect = pygame.Rect(
            x * settings.CELL_SIZE, y * settings.CELL_SIZE + settings.HUD_HEIGHT, settings.CELL_SIZE, settings.CELL_SIZE
        )

        # Layer 1: Inner fill (deep pink #FF1493)
        pygame.draw.rect(surface, (255, 20, 147), cell_rect)

        # Layer 2: Gold border (#FFD700) - 2px width
        pygame.draw.rect(surface, (255, 215, 0), cell_rect, width=2)

    def on_activate(self, game):
        """Enable food attraction and register its audio and visual indicators.

        Audio failure is logged and cannot prevent the gameplay flag from being
        set.
        """
        logger.info("Magnet activated - food will be pulled toward snake")
        game.magnet_active = True

        # Audio feedback (defensive - don't crash if sound fails)
        if game.sound_on and MAGNET_SOUND:
            try:
                MAGNET_SOUND.set_volume(getattr(game, "volume", settings.SOUND_VOLUME))
                MAGNET_SOUND.play()
            except Exception as e:
                logger.error("Failed to play magnet sound: %s", e)

        # Visual feedback stack
        if hasattr(game, "visual_effects"):
            game.visual_effects.add_stacked_powerup(
                name="Magnet",
                color=(255, 20, 147),  # Deep pink
                duration=self.duration,
                icon_char="M",
            )
            # Hitstop: brief pause for collection satisfaction
            game.visual_effects.trigger_hitstop(0.05)

    def on_deactivate(self, game):
        """Disable food attraction and remove its indicator."""
        logger.info("Magnet deactivated")
        game.magnet_active = False

        # Remove visual feedback
        if hasattr(game, "visual_effects"):
            game.visual_effects.remove_stacked_powerup("Magnet")
