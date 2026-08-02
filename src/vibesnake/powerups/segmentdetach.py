"""Instant tail removal that creates temporary board obstacles.

Segment Detach removes up to five oldest tail cells while preserving at
least the head. Removed cells become collision and spawn obstacles for ten
seconds. Phase Shift bypasses their collision check.
"""

from vibesnake.powerups.base import PowerUp
from vibesnake.data import settings
from vibesnake.utils.logger import get_logger
import pygame

logger = get_logger(__name__)


class SegmentDetachPowerUp(PowerUp):
    """Single-use tail-to-obstacle conversion.

    Collection removes 'min(5, length - 1)' cells from the deque's tail end,
    updates the snake occupancy set, merges unique obstacle cells into the
    current list, and resets their shared timer to ten seconds. The game loop
    owns obstacle expiry. A blue broken-chain icon identifies the collectible.
    """

    def __init__(self, position, duration=0.0):
        """Create Segment Detach with no effect timer and an empty removal record."""
        # SegmentDetach is instant single-use, not duration-based
        super().__init__(position, duration)
        self.detached_segments = []  # Populated on activation

    def draw(self, surface: pygame.Surface):
        """Draw the blue Segment Detach cell and broken-chain mark."""
        if not self.position:
            logger.warning("SegmentDetach position is None, skipping draw")
            return

        x, y = self.position
        cell_x = x * settings.CELL_SIZE
        cell_y = y * settings.CELL_SIZE + settings.HUD_HEIGHT

        # Layer 1: Cornflower blue background (steel/obstacle theme)
        pygame.draw.rect(
            surface,
            (100, 149, 237),  # Cornflower blue (steel/obstacle)
            (cell_x, cell_y, settings.CELL_SIZE, settings.CELL_SIZE),
        )

        # Layer 2: Chain links (separated segments)
        center_x = cell_x + settings.CELL_SIZE // 2
        center_y = cell_y + settings.CELL_SIZE // 2

        # Left chain link (5×4px rectangle)
        pygame.draw.rect(
            surface,
            (200, 200, 200),  # Light gray (metal)
            (cell_x + 3, center_y - 2, 5, 4),
        )

        # Right chain link (5×4px rectangle)
        pygame.draw.rect(
            surface,
            (200, 200, 200),  # Light gray (metal)
            (cell_x + 12, center_y - 2, 5, 4),
        )

        # Layer 3: Break lines (red separation indicator)
        pygame.draw.line(
            surface,
            (255, 100, 100),  # Red break
            (center_x - 2, center_y - 4),
            (center_x - 2, center_y + 4),
            2,
        )
        pygame.draw.line(
            surface,
            (255, 100, 100),  # Red break
            (center_x + 2, center_y - 4),
            (center_x + 2, center_y + 4),
            2,
        )

    def on_activate(self, game):
        """Remove eligible tail cells and merge them into the temporary obstacle set.

        The effect becomes inactive immediately. A one-cell snake is left
        unchanged.
        """
        snake_length = len(game.snake.body)
        detach_count = min(5, snake_length - 1)  # Keep at least head

        if detach_count > 0:
            # Snake.body is ordered tail to head, so remove oldest tail cells.
            self.detached_segments = [game.snake.body.popleft() for _ in range(detach_count)]
            for position in self.detached_segments:
                if position not in game.snake.body:
                    game.snake.positions_set.discard(position)
            logger.info("SEGMENT DETACH activated - detaching %d segments as obstacles", detach_count)

            # Preserve any obstacles from an earlier detachment without duplicates.
            existing = list(getattr(game, "detached_segments", []))
            game.detached_segments = list(dict.fromkeys(existing + self.detached_segments))
            game.detached_segments_timer = 10.0  # Obstacles last 10 seconds
        else:
            logger.warning("Segment Detach activated but snake too small to detach")

        # Mark inactive immediately (single-use, not duration-based)
        self.active = False

    def on_deactivate(self, game):
        """Leave obstacle cleanup to the game-loop timer.

        Segment Detach is instant, so the normal timed deactivation path is not
        entered.
        """
        # Detached-obstacle cleanup is owned by the game loop timer.
        return None
