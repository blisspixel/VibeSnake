"""In-memory death-cause telemetry for the current run.

The tracker reports observed collision and starvation counts. It does not infer
balance, ability, intent, or enjoyment from those counts.
"""

from vibesnake.core.enums import DeathCause
from vibesnake.utils.logger import get_logger

logger = get_logger(__name__)


class MetricsTracker:
    """Count terminal causes while preserving a complete category total."""

    def __init__(self) -> None:
        """Create an empty run-local counter set."""
        self.deaths_this_session = 0
        self.collision_deaths = 0
        self.starvation_deaths = 0

    def record_death(
        self,
        score_at_death: int,
        cause: DeathCause = DeathCause.COLLISION,
    ) -> None:
        """Record one supported terminal cause.

        Args:
            score_at_death: Score attached to the diagnostic log entry.
            cause: Collision or starvation.

        Raises:
            ValueError: If the cause is outside the tracked categories.
        """
        if cause not in (DeathCause.COLLISION, DeathCause.STARVATION):
            raise ValueError(f"unsupported death cause: {cause!r}")

        self.deaths_this_session += 1
        if cause == DeathCause.COLLISION:
            self.collision_deaths += 1
        else:
            self.starvation_deaths += 1

        logger.info(
            "Death recorded - cause: %s, session deaths: %d, score: %d",
            cause.name,
            self.deaths_this_session,
            score_at_death,
        )

    def get_death_statistics(self) -> dict[str, int | float]:
        """Return counts and one-decimal percentages for the current run."""
        total = self.deaths_this_session
        if total == 0:
            return {
                "total_deaths": 0,
                "collision_deaths": 0,
                "starvation_deaths": 0,
                "collision_percent": 0.0,
                "starvation_percent": 0.0,
            }

        return {
            "total_deaths": total,
            "collision_deaths": self.collision_deaths,
            "starvation_deaths": self.starvation_deaths,
            "collision_percent": round(self.collision_deaths / total * 100, 1),
            "starvation_percent": round(self.starvation_deaths / total * 100, 1),
        }
