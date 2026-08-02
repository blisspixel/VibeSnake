"""Shared lifecycle for collectible and held power-ups.

A power-up starts visible and uncollected. Collection invokes one
activation hook. Timed effects invoke one cleanup hook when their
duration expires. Instant effects may become inactive during activation.
Uncollected items expire after 'visible_duration'.
"""

from abc import ABC, abstractmethod
from typing import Tuple
import pygame
from vibesnake.data import settings
from vibesnake.utils.logger import get_logger

logger = get_logger(__name__)


class PowerUp(ABC):
    """Base lifecycle for Python reference power-ups.

    The observable states are visible, activated, and inactive. Only the
    visibility timer runs before collection; only the effect timer runs
    afterward. 'active=False' is terminal, and 'activate' cannot apply an
    effect twice. Concrete classes own their game-state mutations and cleanup.
    Tactical value is a design hypothesis until fixed-seed QA and play
    observation provide evidence.
    """

    def __init__(self, position: Tuple[int, int], duration: float = 5.0, visible_duration: float = 6.0):
        """Create a visible power-up with independent visibility and effect timers.

        'position' and both durations are stored without validation. The spawning
        system is responsible for providing a legal grid position.
        """
        self.position = position
        self.duration = duration  # Effect duration after activation
        self.visible_duration = visible_duration  # How long it stays visible before disappearing
        self.timer = 0.0  # Effect elapsed time (activated state)
        self.visible_timer = 0.0  # Visibility elapsed time (spawned state)
        self.active = True  # Master flag: in-game (visible or running)
        self.activated = False  # State flag: collected by player

    @property
    def x(self) -> int:
        """Return the grid column."""
        return self.position[0]

    @property
    def y(self) -> int:
        """Return the grid row."""
        return self.position[1]

    def draw(self, surface: pygame.Surface):
        """Draw the fallback yellow grid cell below the fixed HUD.

        Concrete power-ups may override this with a distinct shape and color.
        """
        x, y = self.position
        pygame.draw.rect(
            surface,
            settings.YELLOW,
            (
                x * settings.CELL_SIZE,
                y * settings.CELL_SIZE + settings.HUD_HEIGHT,
                settings.CELL_SIZE,
                settings.CELL_SIZE,
            ),
        )

    def activate(self, game) -> None:
        """Apply the effect once when a visible power-up is collected.

        State changes precede the hook so the concrete effect observes
        'activated=True' and a zeroed effect timer.
        """
        if self.active and not self.activated:
            self.activated = True
            self.timer = 0.0
            self.on_activate(game)

    def deactivate(self, game) -> bool:
        """End an active effect once and invoke its cleanup hook."""
        if not self.active:
            return False

        self.active = False
        if self.activated:
            self.on_deactivate(game)
        return True

    def update(self, dt: float, game) -> None:
        """Advance the timer for the current lifecycle state.

        An uncollected item becomes inactive at its visibility deadline. A
        collected timed effect is deactivated at its effect deadline.
        """
        if not self.active:
            return

        if not self.activated:
            # SPAWNED state: track visibility window
            self.visible_timer += dt
            if self.visible_timer >= self.visible_duration:
                self.active = False  # Expired without being collected
                logger.debug("%s expired uncollected", type(self).__name__)
        else:
            # ACTIVATED state: track effect duration
            self.timer += dt
            if self.timer >= self.duration:
                self.deactivate(game)
                logger.info("%s effect ended after %.1fs", type(self).__name__, self.duration)

    @abstractmethod
    def on_activate(self, game):
        """Apply the concrete effect to game state."""
        raise NotImplementedError

    @abstractmethod
    def on_deactivate(self, game):
        """Remove state owned by the concrete effect.

        Instant effects whose cleanup is owned by another subsystem may implement
        an intentional no-op.
        """
        raise NotImplementedError
