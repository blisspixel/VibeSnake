"""Detect and classify close spatial and timing outcomes.

The detector emits warning or scored events for body proximity, constrained
routes, boundary travel, late food collection, and boosted collection. Its
thresholds and bonuses are provisional balance parameters. A cooldown prevents
stationary or oscillating play from producing an unbounded event stream.
"""

from typing import Tuple, Set
from dataclasses import dataclass
from vibesnake.data import settings


@dataclass(frozen=True)
class NearMissEvent:
    """Describe one warning or scored proximity event.

    A position of ``(-1, -1)`` marks an event that is not tied to one grid
    cell. Warning events do not award points or start the detector cooldown.
    """

    type: str  # Event category identifier
    position: Tuple[int, int]  # Grid location (or -1,-1 for non-spatial)
    score_bonus: int  # Points added when the event is rewarded
    message: str  # Player feedback text
    color: Tuple[int, int, int]  # RGB for visual notification
    is_warning: bool = False  # If True, visual-only warning (no bonus/cooldown)


class NearMissDetector:
    """Emit bounded near-miss events from current run geometry and timers."""

    def __init__(self):
        self.recent_events = []  # Track recent near-misses for combo
        self.event_timeout = 3.0  # Seconds before events expire
        self.last_near_miss_time = 0.0  # Cooldown to prevent spam
        self.near_miss_cooldown = 1.5  # Minimum seconds between near-miss events

    def update(self, dt: float):
        """Update and expire old events."""
        self.recent_events = [(event, time - dt) for event, time in self.recent_events if time - dt > 0]
        # Update cooldown timer
        if self.last_near_miss_time > 0:
            self.last_near_miss_time -= dt

    def check_near_miss(
        self, head: Tuple[int, int], body_positions: Set[Tuple[int, int]], snake_length: int
    ) -> NearMissEvent | None:
        """Classify body occupancy in the eight cells around the head.

        Snakes shorter than eight segments are ignored. Two occupied neighbors
        emit an unscored warning. Three emit a one-point event and four or more
        emit a two-point event. Scored events share a 1.5-second cooldown.
        These thresholds remain provisional balance parameters.
        """
        # Skip if snake is too short (no danger possible)
        if snake_length < 8:  # Require longer snake for meaningful danger
            return None

        # Check all 8 surrounding cells
        hx, hy = head
        adjacent_cells = [
            (hx + dx, hy + dy)
            for dx in [-1, 0, 1]
            for dy in [-1, 0, 1]
            if not (dx == 0 and dy == 0)  # Skip the head itself
        ]

        # Count how many adjacent cells have body segments
        danger_count = sum(1 for cell in adjacent_cells if cell in body_positions)

        # Four or more occupied neighbors produce the higher-value event.
        if danger_count >= 4:
            # Only trigger if cooldown expired (prevent spam)
            if self.last_near_miss_time > 0:
                return None

            bonus = 2
            self.last_near_miss_time = self.near_miss_cooldown  # Start cooldown
            return NearMissEvent(
                type="near_miss",
                position=head,
                score_bonus=bonus,
                message="THREADING THE NEEDLE!",
                color=(255, 100, 255),  # Bright purple
                is_warning=False,  # Full event with bonus
            )

        # Tier 2: Near-miss - 3 sides dangerous (CLOSE CALL)
        elif danger_count >= 3:
            # Only trigger if cooldown expired (prevent spam)
            if self.last_near_miss_time > 0:
                return None

            bonus = 1  # Standard near-miss bonus
            self.last_near_miss_time = self.near_miss_cooldown  # Start cooldown
            return NearMissEvent(
                type="near_miss",
                position=head,
                score_bonus=bonus,
                message="CLOSE CALL!",
                color=(255, 200, 0),  # Gold
                is_warning=False,  # Full event with bonus
            )

        # Tier 1: Pre-warning - 2 sides dangerous (VISUAL ONLY)
        elif danger_count == 2:
            # No cooldown check - pre-warnings can display continuously
            return NearMissEvent(
                type="danger_warning",
                position=head,
                score_bonus=0,  # No bonus for pre-warning
                message="",  # No message (visual feedback only)
                color=(255, 50, 50),  # Red glow for danger
                is_warning=True,  # Flag as warning (no cooldown trigger)
            )

        return None

    def check_edge_ride(
        self, head: Tuple[int, int], direction: Tuple[int, int], snake_length: int
    ) -> NearMissEvent | None:
        """Reward motion parallel to a wrapping boundary.

        The provisional bonus is ``snake_length // 10`` clamped to 1 through
        10. No retained dataset currently establishes that length is an
        accurate proxy for the difficulty of this action.
        """
        hx, hy = head
        dx, dy = direction

        # Check if at edge AND moving parallel to it
        at_left = hx == 0 and dy != 0
        at_right = hx == settings.GRID_WIDTH - 1 and dy != 0
        at_top = hy == 0 and dx != 0
        at_bottom = hy == settings.GRID_HEIGHT - 1 and dx != 0

        if at_left or at_right or at_top or at_bottom:
            # Scale the provisional reward with length and cap score inflation.
            bonus = min(max(snake_length // 10, 1), 10)

            # Select the message from the same deterministic bonus thresholds.
            if bonus >= 8:
                message = "EDGE MASTERY!"
            elif bonus >= 5:
                message = "EDGE LORD!"
            else:
                message = "EDGE RIDE"

            return NearMissEvent(
                type="edge_ride",
                position=head,
                score_bonus=bonus,
                message=message,
                color=(100, 255, 255),  # Cyan
            )

        return None

    def check_clutch_eat(self, starvation_timer: float, starvation_max: float) -> NearMissEvent | None:
        """
        Check if food was eaten with very little time left.

        The 1.5-second window is provisional. Seeded telemetry must measure its
        trigger distribution, and human review must determine whether the cue is
        legible and earned before the threshold is treated as release tuning.

        Args:
            starvation_timer: Current starvation timer (counts up from 0)
            starvation_max: Maximum time before death (typically 30.0s)

        Returns:
            NearMissEvent if clutch moment detected (time_remaining < 1.5s)
        """
        time_remaining = starvation_max - starvation_timer

        # Tightened threshold: 1.5s (was 3.0s)
        if time_remaining < 1.5:
            bonus = 1  # Just +1 for clutch moments
            return NearMissEvent(
                type="clutch_eat",
                position=(-1, -1),  # No specific position
                score_bonus=bonus,
                message="CLUTCH!",
                color=(255, 50, 50),  # Red
            )

        return None

    def check_style_points(self, has_boost: bool) -> NearMissEvent | None:
        """
        Check if food eaten while boosting (style points).

        Args:
            has_boost: Whether boost power-up is active

        Returns:
            NearMissEvent if style moment detected
        """
        if has_boost:
            return NearMissEvent(
                type="style_points",
                position=(-1, -1),
                score_bonus=1,  # Just +1 for style
                message="ZOOMING!",
                color=(255, 150, 0),  # Orange
            )

        return None

    def add_event(self, event: NearMissEvent):
        """Track event for combo purposes."""
        self.recent_events.append((event, self.event_timeout))

    def get_combo_multiplier(self) -> float:
        """Get bonus multiplier based on recent near-miss combo."""
        event_count = len(self.recent_events)
        if event_count >= 3:
            return 2.0  # Triple near-miss = 2x multiplier
        elif event_count >= 2:
            return 1.5
        return 1.0
