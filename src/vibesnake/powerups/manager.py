"""Spawn, activate, expire, consume, and draw the nine power-up types."""

import random
from typing import List, Set, Tuple, Type
import pygame

from vibesnake.powerups.shield import ShieldPowerUp
from vibesnake.powerups.slowmo import SlowMoPowerUp
from vibesnake.powerups.magnet import MagnetPowerUp
from vibesnake.powerups.boost import BoostPowerUp
from vibesnake.powerups.phaseshift import PhaseShiftPowerUp
from vibesnake.powerups.gluttony import GluttonyPowerUp
from vibesnake.powerups.bait import BaitPowerUp
from vibesnake.powerups.laststand import LastStandPowerUp
from vibesnake.powerups.segmentdetach import SegmentDetachPowerUp
from vibesnake.powerups.base import PowerUp
from vibesnake.data import settings
from vibesnake.data.config import load_config
from vibesnake.utils.logger import get_logger

logger = get_logger(__name__)

config = load_config()

POWERUP_SPAWN_INTERVAL = config["powerups"].get("spawn_interval", 15.0)
POWERUPS_ENABLED = config["powerups"].get("enabled", True)
POWERUP_VISIBLE_DURATION = config["powerups"].get("visible_duration", 6.0)

# The reference runtime samples this registry uniformly. It suppresses a type
# while an instance of that type is active unless every type is already active.
POWERUP_TYPES = [
    ShieldPowerUp,  # Defense: Invulnerability to one collision
    SlowMoPowerUp,  # Defense: Temporal dilation (easier maneuvering)
    MagnetPowerUp,  # Control: Attract food to snake head
    BoostPowerUp,  # Mobility: Temporary speed increase
    PhaseShiftPowerUp,  # Mobility: Cross self and detached obstacles temporarily
    GluttonyPowerUp,  # Gambit: Score from food without adding body length
    BaitPowerUp,  # Control: Bias the next food respawn toward pickup location
    LastStandPowerUp,  # Gambit: Survive one fatal event and shrink by half
    SegmentDetachPowerUp,  # Control: Remove tail segments (reduce collision surface)
]


class PowerUpManager:
    """Coordinate the power-up lifecycle for the Python reference game.

    At most one active, uncollected power-up is present. Collected effects may
    overlap until their own timers expire or game rules consume them.
    """

    def __init__(self):
        """Initialize an empty registry and a stopped spawn accumulator."""
        self.active_powerups: List[PowerUp] = []
        self.spawn_timer = 0.0

    def collectible_powerups(self) -> tuple[PowerUp, ...]:
        """Return active pickups that still exist on the board."""
        return tuple(powerup for powerup in self.active_powerups if powerup.active and not powerup.activated)

    def collectible_positions(self) -> set[Tuple[int, int]]:
        """Return grid cells occupied by active, uncollected pickups."""
        return {powerup.position for powerup in self.collectible_powerups()}

    def has_active_effect(self, powerup_type: Type[PowerUp]) -> bool:
        """Return whether a collected effect of ``powerup_type`` is active."""
        return any(
            isinstance(powerup, powerup_type) and powerup.active and powerup.activated
            for powerup in self.active_powerups
        )

    def discard_collectibles(self) -> int:
        """Remove board pickups while preserving every collected effect."""
        collectibles = self.collectible_powerups()
        for powerup in collectibles:
            powerup.active = False
            self.active_powerups.remove(powerup)
        return len(collectibles)

    def update(self, dt: float, game):
        """Advance spawning and effect timers, then collect at the snake head."""
        # Phase 1: Spawn timing
        self.spawn_timer += dt

        # Occupancy constraint: only spawn if no uncollected power-ups exist
        uncollected = self.collectible_powerups()
        if self.spawn_timer >= POWERUP_SPAWN_INTERVAL and POWERUPS_ENABLED:
            if not uncollected:
                occupied = set(game.snake.positions_set)
                food_position = getattr(getattr(game, "food", None), "position", None)
                if food_position is not None:
                    occupied.add(food_position)
                occupied.update(getattr(game, "detached_segments", []))
                self.spawn(occupied)
                self.spawn_timer = 0.0

        # Phase 2: Lifecycle updates
        # Use [:] to create shallow copy (safe mutation during iteration)
        for powerup in self.active_powerups[:]:
            powerup.update(dt, game)
            if not powerup.active:
                logger.debug("%s removed from active power-ups", type(powerup).__name__)
                self.active_powerups.remove(powerup)

        # Phase 3: Collection at the current head. The game coordinator also
        # calls this immediately after a successful movement so activation is
        # tied to the entry step rather than a later render frame.
        self.collect_at(game.snake.get_head(), game)

    def collect_at(self, position: Tuple[int, int], game) -> PowerUp | None:
        """Collect and activate the visible power-up at ``position`` once."""
        for powerup in self.active_powerups:
            if powerup.active and not powerup.activated and powerup.position == position:
                logger.info("Player collected %s at %s", type(powerup).__name__, powerup.position)
                powerup.activate(game)
                if hasattr(game, "session_powerups_collected"):
                    game.session_powerups_collected += 1
                return powerup
        return None

    def consume(self, powerup_type: Type[PowerUp], game) -> bool:
        """Consume the first active effect of a given type."""
        for powerup in self.active_powerups[:]:
            if isinstance(powerup, powerup_type) and powerup.active and powerup.activated:
                powerup.deactivate(game)
                self.active_powerups.remove(powerup)
                return True
        return False

    def spawn(self, occupied: Set[Tuple[int, int]]):
        """Spawn one uniformly selected available type on a free grid cell.

        Occupied cells and active uncollected pickups are excluded. A full grid
        is reported and left unchanged. Global Python randomness remains a
        known reference-runtime limitation until the versioned random stream is
        introduced by the native parity milestone.
        """
        # Step 1: Generate complete spatial domain (all grid cells)
        all_cells = {(x, y) for x in range(settings.GRID_WIDTH) for y in range(settings.GRID_HEIGHT)}

        # Step 2: Compute occupied positions (snake + food + existing power-ups)
        occupied_cells = occupied | self.collectible_positions()

        # Step 3: Find free cells via set difference
        free_cells = list(all_cells - occupied_cells)

        # Defensive check: ensure free space exists
        if not free_cells:
            logger.warning("No free cells available to spawn power-up - grid full")
            return

        # Step 4: Uniform random selection of position and type
        position = random.choice(free_cells)
        available_types = [
            powerup_type
            for powerup_type in POWERUP_TYPES
            if not any(isinstance(powerup, powerup_type) and powerup.active for powerup in self.active_powerups)
        ]
        powerup_cls = random.choice(available_types or POWERUP_TYPES)

        # Instantiate and register power-up
        powerup = powerup_cls(position)
        powerup.visible_duration = POWERUP_VISIBLE_DURATION
        self.active_powerups.append(powerup)

        logger.info("Spawned %s at %s", powerup_cls.__name__, position)

    def draw(self, surface: pygame.Surface):
        """Draw active, uncollected pickups and isolate rendering failures."""
        # Filter to uncollected power-ups (SPAWNED state only)
        # Render each with defensive error handling
        for powerup in self.collectible_powerups():
            try:
                powerup.draw(surface)
            except Exception as e:
                # Log but continue (don't crash entire render loop)
                logger.error("Failed to draw %s: %s", type(powerup).__name__, e)
