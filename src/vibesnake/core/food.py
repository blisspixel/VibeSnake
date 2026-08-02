"""Collision-free food placement on the bounded gameplay grid."""

import random
from typing import Optional, Set, Tuple
import pygame

from vibesnake.data import settings
from vibesnake.core.exceptions import GridFullException


class Food:
    """Food whose position is always a currently free grid cell.

    Placement enumerates the fixed-size board and samples uniformly from the
    remaining cells. This bounded O(width * height) operation is simple,
    unbiased, and cannot develop the latency spikes of rejection sampling as
    the board fills. A full board raises :class:`GridFullException`.
    """

    def __init__(self, occupied: Set[Tuple[int, int]]):
        """
        Initialize food at random unoccupied position.

        **Complexity:** O(W×H) - must compute free cell set

        Args:
            occupied: Set of grid positions currently occupied (snake, power-ups, etc.)

        **Precondition:** occupied ⊂ grid_cells (all occupied positions are valid)

        **Postcondition:**
        - position is in (grid_cells minus occupied) when a free cell exists
        - a full board raises :class:`GridFullException`
        """
        self.position: Tuple[int, int] = self._generate(occupied)

    def _generate(
        self,
        occupied: Set[Tuple[int, int]],
        preferred_position: Optional[Tuple[int, int]] = None,
    ) -> Tuple[int, int]:
        """Return a free cell, biased toward ``preferred_position`` when set.

        The unbiased path gives every free cell equal probability. The
        preferred path applies inverse-square Manhattan-distance weights while
        preserving a nonzero probability for every free cell.
        """
        occupied_count = len(occupied)
        grid_size = settings.GRID_WIDTH * settings.GRID_HEIGHT
        free_cells = [
            (x, y) for x in range(settings.GRID_WIDTH) for y in range(settings.GRID_HEIGHT) if (x, y) not in occupied
        ]
        if not free_cells:
            raise GridFullException(occupied_count, grid_size)

        if preferred_position is not None:
            bait_x, bait_y = preferred_position
            weights = [1.0 / ((abs(x - bait_x) + abs(y - bait_y) + 1) ** 2) for x, y in free_cells]
            return random.choices(free_cells, weights=weights, k=1)[0]
        return random.choice(free_cells)

    def respawn(
        self,
        occupied: Set[Tuple[int, int]],
        preferred_position: Optional[Tuple[int, int]] = None,
    ):
        """
        Relocate food to new random unoccupied position.

        Called when food consumed by snake (collision detected).

        **Complexity:** O(W×H) - delegates to _generate()

        Args:
            occupied: Updated set of occupied positions (includes new snake head)
            preferred_position: Optional bait location used to weight nearby free cells

        **Side Effect:** Mutates self.position to new random location

        **Precondition:** occupied reflects current game state (synchronized)

        **Postcondition:**
            - Old position may be reused (small probability = 1/(W×H - k))
            - New position guaranteed free if any free cells exist
        """
        self.position = self._generate(occupied, preferred_position)

    def draw(self, surface: pygame.Surface):
        """
        Render food sprite on game surface.

        **Rendering:**
        - Visual: Grid-aligned solid cell with the configured food color
        - Color: settings.RED (configurable)
        - Position: Grid coordinates converted to screen pixels
        - Offset: +HUD_HEIGHT to account for UI top bar

        **Complexity:** O(1) - single draw call to pygame

        **Coordinate Transformation:**
            screen_x = grid_x × CELL_SIZE
            screen_y = grid_y × CELL_SIZE + HUD_HEIGHT

        **Visibility Check:**
        Only renders if position attribute exists and is valid.
        Silent no-op when spawn failed (prevents error).

        Args:
            surface: Pygame surface to draw on (game window)

        **Side Effect:** Modifies surface pixel buffer (intended)

        **Precondition:** pygame initialized, surface is valid

        """
        if hasattr(self, "position") and self.position:
            x, y = self.position
            pygame.draw.rect(
                surface,
                settings.RED,
                (
                    x * settings.CELL_SIZE,
                    y * settings.CELL_SIZE + settings.HUD_HEIGHT,
                    settings.CELL_SIZE,
                    settings.CELL_SIZE,
                ),
            )
