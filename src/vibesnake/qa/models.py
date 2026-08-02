"""Serializable contracts for seeded gameplay QA campaigns."""

from __future__ import annotations

from dataclasses import asdict, dataclass, field
import math
from typing import Any


QA_REPORT_SCHEMA_VERSION = 2
QA_ENGINE_ID = "python-core-reference-v2"


@dataclass(frozen=True)
class Scenario:
    """One reproducible policy and seed combination."""

    policy: str
    seed: int
    max_steps: int = 500
    step_seconds: float = 0.05

    def __post_init__(self) -> None:
        """Reject scenarios that cannot execute meaningful simulation work."""
        if not isinstance(self.policy, str) or not self.policy.strip():
            raise ValueError("policy must be non-empty")
        if isinstance(self.seed, bool) or not isinstance(self.seed, int):
            raise ValueError("seed must be an integer")
        if isinstance(self.max_steps, bool) or not isinstance(self.max_steps, int) or self.max_steps <= 0:
            raise ValueError("max_steps must be positive")
        if (
            isinstance(self.step_seconds, bool)
            or not isinstance(self.step_seconds, (int, float))
            or not math.isfinite(self.step_seconds)
            or self.step_seconds <= 0
        ):
            raise ValueError("step_seconds must be a positive finite number")

    @property
    def scenario_id(self) -> str:
        """Return a stable human-readable scenario identifier."""
        return f"{self.policy}:seed-{self.seed}:steps-{self.max_steps}"


@dataclass(frozen=True)
class StepEvent:
    """One ordered rules event with normalized optional detail."""

    kind: str
    position: tuple[int, int] | None = None
    direction: str | None = None
    value: int | None = None
    death_cause: str | None = None


@dataclass(frozen=True)
class StepRecord:
    """Compact observable state after one simulation step."""

    step: int
    commands: tuple[str, ...]
    direction: str
    head: tuple[int, int]
    food: tuple[int, int] | None
    length: int
    score: int
    combo: int
    starvation_seconds: float
    food_eaten: int
    wrapped: bool
    ate_food: bool
    alive: bool
    won: bool
    death_cause: str | None
    events: tuple[StepEvent, ...]

    def canonical_dict(self) -> dict[str, Any]:
        """Return the stable subset used for deterministic trace hashing."""
        return asdict(self)


@dataclass(frozen=True)
class InvariantFailure:
    """One contract violation with enough data to reproduce it."""

    code: str
    message: str
    step: int


@dataclass
class ScenarioResult:
    """Outcome and trace for one scenario."""

    scenario: Scenario
    passed: bool
    steps_executed: int
    food_eaten: int
    score: int
    final_length: int
    wraps: int
    won: bool
    death_cause: str | None
    trace_hash: str
    actions: list[tuple[str, ...]] = field(default_factory=list)
    failures: list[InvariantFailure] = field(default_factory=list)

    def to_dict(self) -> dict[str, Any]:
        """Convert the result into a JSON-compatible object."""
        data = asdict(self)
        data["scenario"]["scenario_id"] = self.scenario.scenario_id
        return data


@dataclass(frozen=True)
class CampaignReport:
    """Machine-readable result for a collection of seeded scenarios."""

    generated_at_utc: str
    scenarios: list[ScenarioResult]
    aggregates: dict[str, Any]
    schema_version: int = QA_REPORT_SCHEMA_VERSION
    engine: str = QA_ENGINE_ID

    @property
    def passed(self) -> bool:
        """Return whether every scenario satisfied its contracts."""
        return bool(self.scenarios) and all(result.passed for result in self.scenarios)

    def to_dict(self) -> dict[str, Any]:
        """Convert the campaign into a JSON-compatible object."""
        return {
            "schema_version": self.schema_version,
            "engine": self.engine,
            "generated_at_utc": self.generated_at_utc,
            "passed": self.passed,
            "aggregates": self.aggregates,
            "scenarios": [result.to_dict() for result in self.scenarios],
        }
