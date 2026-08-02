"""Scoring, combo interpolation, and score-break accounting.

The score combines food value, combo multiplier, and explicit speed, proximity,
and length bonuses. Constants are design parameters, not validated measures of
skill or enjoyment. Score mutations are deterministic and saturate at the same
portable ceiling as the native rules kernel.
"""

from typing import Optional
from vibesnake.utils.logger import get_logger

logger = get_logger(__name__)

MAXIMUM_SCORE = 2_000_000_000


class ScoreManager:
    """Track score and a time-bounded, smoothly interpolated food combo.

    Speed and proximity bonuses are explicit caller inputs. Length contributes an
    integer-truncated ``(length - 10) * log(length) / 2`` bonus. The arithmetic is
    reproducible, while all parameter values remain subject to balance review.
    """

    # Combo multiplier function - piecewise exponential with cap
    # Mathematical notation: M: ℕ → {1.0, 2.0, 3.0, 5.0, 10.0}
    # Implementation: Dict for O(log k) lookup vs O(k) if-else chain
    COMBO_THRESHOLDS = {
        0: 1.0,  # Baseline: No combo active (identity multiplier)
        3: 2.0,  # First milestone: +100% reward (doubling point)
        5: 3.0,  # Second milestone: +200% total
        10: 5.0,  # Expert tier: +400% total (significant jump)
        20: 10.0,  # Capped tier: +900% total.
    }

    def __init__(self, base_food_points: int = 10, combo_time_threshold: float = 3.0):
        """
        Initialize score manager.

        Args:
            base_food_points: Points per food without modifiers
            combo_time_threshold: Max seconds between food to maintain combo
        """
        self.base_score: int = 0
        self.combo_count: int = 0
        self.time_since_last_food: float = 0.0
        self.base_food_points = base_food_points
        self.combo_time_threshold = combo_time_threshold

        logger.info("ScoreManager initialized - combos enabled")

    @property
    def combo_multiplier(self) -> float:
        """Return the linearly interpolated multiplier, capped at 10x.

        The interpolation points are ``COMBO_THRESHOLDS``. Counts between two
        points use the fraction of that interval, so every food advances the
        multiplier without a discontinuous threshold jump.
        """
        combo = self.combo_count

        # Get sorted thresholds for interpolation
        sorted_thresholds = sorted(self.COMBO_THRESHOLDS.items())

        # Find which tier we're in
        for i in range(len(sorted_thresholds) - 1):
            lower_threshold, lower_mult = sorted_thresholds[i]
            upper_threshold, upper_mult = sorted_thresholds[i + 1]

            # If combo is between these two thresholds, interpolate
            if lower_threshold <= combo < upper_threshold:
                # Linear interpolation parameter: [0, 1]
                t = (combo - lower_threshold) / (upper_threshold - lower_threshold)
                # Interpolated multiplier
                multiplier = lower_mult + (upper_mult - lower_mult) * t
                return multiplier

        # If combo >= max threshold, return max multiplier (capped)
        max_threshold, max_mult = sorted_thresholds[-1]
        if combo >= max_threshold:
            return max_mult

        # Fallback (should never reach here if thresholds start at 0)
        return 1.0

    def update(self, dt: float):
        """
        Update combo timer.

        Called every frame. Breaks combo if too much time passes without food.

        Args:
            dt: Delta time in seconds
        """
        self.time_since_last_food += dt

        # Break combo if threshold exceeded
        if self.time_since_last_food > self.combo_time_threshold and self.combo_count > 0:
            logger.info("Combo broken - %d food streak ended", self.combo_count)
            self.combo_count = 0

    def add_food_score(
        self, speed_bonus: bool = False, risk_bonus: bool = False, snake_length: Optional[int] = None
    ) -> int:
        """Award points for food and return the amount added.

        The base award uses the post-increment combo multiplier. Optional speed
        and risk awards add 50 and 25 percent of the base food value. Lengths
        above ten add ``int((length - 10) * log(length) / 2)``. This formula is
        a balance parameter, not a validated model of collision risk.

        Args:
            speed_bonus: True if eaten within speed bonus window (< 1.5s)
            risk_bonus: True if eaten near obstacle/tail (proximity bonus)
            snake_length: Current snake length for difficulty compensation

        Returns:
            Total points awarded this food (base + bonuses)
        """
        # Reset timer and increment combo
        self.time_since_last_food = 0.0
        self.combo_count += 1

        # Base points with combo multiplier
        points = int(self.base_food_points * self.combo_multiplier)

        # Speed bonus (fast eating)
        if speed_bonus:
            speed_points = int(self.base_food_points * 0.5)
            points += speed_points
            logger.debug("Speed bonus: +%d points", speed_points)

        # Risk bonus (dangerous eating)
        if risk_bonus:
            risk_points = int(self.base_food_points * 0.25)
            points += risk_points
            logger.debug("Risk bonus: +%d points", risk_points)

        # Length bonus uses the exact formula shared with the native rules kernel.
        if snake_length and snake_length > 10:
            import math

            length_bonus = int((snake_length - 10) * math.log(snake_length) / 2)
            points += length_bonus
            logger.debug("Length bonus: +%d points (length %d)", length_bonus, snake_length)

        awarded_points = min(points, max(0, MAXIMUM_SCORE - self.base_score))
        self.base_score += awarded_points

        logger.info(
            "Food eaten - Score: %d (+%d) | Combo: %d (%.1fx) | Speed: %s | Risk: %s",
            self.base_score,
            awarded_points,
            self.combo_count,
            self.combo_multiplier,
            "YES" if speed_bonus else "NO",
            "YES" if risk_bonus else "NO",
        )

        return awarded_points

    def add_bonus_score(self, bonus: int):
        """
        Add bonus points (e.g., from near-miss events, trick shots).

        Args:
            bonus: Bonus points to add
        """
        if bonus < 0:
            raise ValueError("bonus must not be negative")

        awarded_bonus = min(bonus, max(0, MAXIMUM_SCORE - self.base_score))
        self.base_score += awarded_bonus
        logger.debug("Bonus score added: +%d points (Total: %d)", awarded_bonus, self.base_score)

    def break_combo_on_death(self) -> int:
        """
        Break combo when player dies.

        Returns:
            Combo count that was lost
        """
        lost_combo = self.combo_count
        if lost_combo > 0:
            logger.warning("Death - combo lost: %d food streak", lost_combo)
        self.combo_count = 0
        self.time_since_last_food = 0.0
        return lost_combo

    def get_display_info(self) -> dict:
        """
        Get scoring info for HUD display.

        Returns:
            Dict with score, combo, multiplier for rendering
        """
        return {"score": self.base_score, "combo": self.combo_count, "multiplier": self.combo_multiplier}
