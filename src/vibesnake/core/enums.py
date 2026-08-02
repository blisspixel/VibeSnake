"""Game-flow states, terminal causes, and cardinal grid directions."""

from enum import Enum, auto
from typing import Tuple


class GameState(Enum):
    """Named states accepted by the Python reference coordinator."""

    MENU = auto()
    HELP = auto()
    SETTINGS = auto()  # Game settings menu
    HIGH_SCORES = auto()  # View high score table
    CUSTOMIZE = auto()  # Snake customization menu
    ACHIEVEMENTS = auto()  # View achievements and progress
    CHANNEL_BROWSER = auto()  # Browse AI streamer channels before watching
    LETS_PLAY = auto()  # Watch an AI personality play autonomously
    RUNNING = auto()
    PAUSED = auto()
    NAME_ENTRY = auto()  # Enter name for high score
    GAME_OVER = auto()


class DeathCause(Enum):
    """Terminal causes recorded separately for descriptive telemetry.

    Cause ratios help locate seeds and cohorts for review. They do not, by
    themselves, establish difficulty, frustration, or player enjoyment.
    """

    COLLISION = auto()
    STARVATION = auto()


class Direction(Enum):
    """Cardinal unit vectors in screen coordinates, where positive y is down."""

    UP = (0, -1)
    DOWN = (0, 1)
    LEFT = (-1, 0)
    RIGHT = (1, 0)

    def vector(self) -> Tuple[int, int]:
        """Return this direction as a ``(dx, dy)`` unit vector."""
        return self.value

    @staticmethod
    def opposite(direction) -> "Direction":
        """Return the cardinal inverse of ``direction``."""
        opposites = {
            Direction.UP: Direction.DOWN,
            Direction.DOWN: Direction.UP,
            Direction.LEFT: Direction.RIGHT,
            Direction.RIGHT: Direction.LEFT,
        }
        return opposites[direction]
