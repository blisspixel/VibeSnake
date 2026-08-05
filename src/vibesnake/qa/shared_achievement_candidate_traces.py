"""Generate dual-runtime achievement_candidate traces with the product flag on.

Default core_rules fixtures keep EnableAchievementCandidates off (PD-009).
This corpus deliberately enables emission on both runtimes for parity.
"""

from __future__ import annotations

import argparse
import json
import logging
from collections import deque
from pathlib import Path
from typing import Any

from vibesnake.core.enums import Direction
from vibesnake.core.scoring import MAXIMUM_SCORE
from vibesnake.core.snake import Snake
from vibesnake.data import settings
from vibesnake.qa.contracts import CURRENT_RULESET, SHARED_RANDOMNESS_POLICY
from vibesnake.qa.models import StepEvent
from vibesnake.qa.simulation import CoreSimulation
from vibesnake.utils.logger import temporary_logger_level

ACHIEVEMENT_TRACE_SCHEMA_VERSION = 1
ACHIEVEMENT_TRACE_CONTRACT = "achievement-candidates-targeted-v1"
DEFAULT_FIXTURE_PATH = Path("tests/fixtures/shared/achievement_candidates_rules_v1.json")
STARVATION_TICKS = 600


def build_achievement_candidate_fixture() -> dict[str, Any]:
    """Return terminal one-step traces with achievement_candidate events enabled."""
    specifications = [
        _spec(
            "starvation-score-candidates",
            body=[(5, 5)],
            direction="RIGHT",
            food=(10, 10),
            score=150,
            combo=0,
            starvation_ticks_elapsed=STARVATION_TICKS - 1,
        ),
        _spec(
            "starvation-suppresses-already-unlocked",
            body=[(5, 5)],
            direction="RIGHT",
            food=(10, 10),
            score=150,
            combo=0,
            starvation_ticks_elapsed=STARVATION_TICKS - 1,
            already_unlocked=["first_bite", "century"],
        ),
        _spec(
            "starvation-zero-score-no-candidates",
            body=[(5, 5)],
            direction="RIGHT",
            food=(10, 10),
            score=0,
            combo=0,
            starvation_ticks_elapsed=STARVATION_TICKS - 1,
        ),
        _spec(
            "self-collision-score-candidates",
            body=[(1, 1), (1, 2), (2, 2), (2, 1)],
            direction="DOWN",
            food=(10, 10),
            score=120,
            combo=0,
            starvation_ticks_elapsed=0,
        ),
    ]
    cases = [_execute_specification(spec) for spec in specifications]
    return {
        "schema_version": ACHIEVEMENT_TRACE_SCHEMA_VERSION,
        "contract": ACHIEVEMENT_TRACE_CONTRACT,
        "ruleset": CURRENT_RULESET.to_dict(),
        "randomness_policy": SHARED_RANDOMNESS_POLICY,
        "source_engine": "python-core-reference-v3",
        "config": {
            "width": settings.GRID_WIDTH,
            "height": settings.GRID_HEIGHT,
            "starvation_ticks": STARVATION_TICKS,
            "maximum_direction_queue": Snake.MAX_DIRECTION_QUEUE,
            "maximum_score": MAXIMUM_SCORE,
            "combo_window_ticks": 60,
            "speed_bonus_ticks": 30,
            "food_score": 10,
            "enable_achievement_candidates": True,
        },
        "case_count": len(cases),
        "comparison_scope": [
            "terminal_achievement_candidates",
            "already_unlocked_suppression",
            "ordered_events",
        ],
        "excluded_scope": [
            "default_flag_off_corpus",
            "profile_lifetime_achievements",
        ],
        "cases": cases,
    }


def fixture_json(fixture: dict[str, Any]) -> str:
    """Serialize the achievement-candidate fixture canonically."""
    return json.dumps(fixture, separators=(",", ":"), sort_keys=True) + "\n"


def check_fixture(path: Path, fixture: dict[str, Any]) -> bool:
    """Return whether the checked-in fixture is current."""
    return path.is_file() and path.read_text(encoding="utf-8") == fixture_json(fixture)


def main(argv: list[str] | None = None) -> int:
    """Write or verify the achievement-candidate dual-runtime fixture."""
    with temporary_logger_level("vibesnake", logging.WARNING):
        parser = argparse.ArgumentParser(
            prog="python -m vibesnake.qa.shared_achievement_candidate_traces",
            description=(
                "Generate achievement_candidate dual-runtime traces "
                "(product flag enabled)."
            ),
        )
        parser.add_argument("--output", type=Path, default=DEFAULT_FIXTURE_PATH)
        parser.add_argument("--check", action="store_true")
        args = parser.parse_args(argv)
        fixture = build_achievement_candidate_fixture()

        if args.check:
            if check_fixture(args.output, fixture):
                print(
                    "Shared achievement-candidate fixture passed: "
                    f"{fixture['case_count']} targeted cases"
                )
                return 0
            print(f"Shared achievement-candidate fixture drift: {args.output}")
            return 1

        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(fixture_json(fixture), encoding="utf-8")
        print(
            f"Wrote {args.output} ({fixture['case_count']} achievement-candidate cases)"
        )
        return 0


def _spec(
    case_id: str,
    *,
    body: list[tuple[int, int]],
    direction: str,
    food: tuple[int, int] | None,
    score: int = 0,
    combo: int = 0,
    starvation_ticks_elapsed: int = 0,
    already_unlocked: list[str] | None = None,
) -> dict[str, Any]:
    return {
        "id": case_id,
        "initial": {
            "body": [list(point) for point in body],
            "direction": direction,
            "food": list(food) if food is not None else None,
            "score": score,
            "combo": combo,
            "ticks_since_last_food": 0,
            "starvation_ticks_elapsed": starvation_ticks_elapsed,
            "already_unlocked": list(already_unlocked or ()),
        },
        "commands": [],
    }


def _execute_specification(specification: dict[str, Any]) -> dict[str, Any]:
    initial = specification["initial"]
    already = frozenset(initial.get("already_unlocked") or ())
    simulation = CoreSimulation(
        enable_achievement_candidates=True,
        already_unlocked_achievements=already,
    )
    body = [tuple(point) for point in initial["body"]]
    simulation.snake.body = deque(body)
    simulation.snake.positions_set = set(body)
    simulation.snake.direction = Direction[initial["direction"]]
    simulation.snake.next_directions.clear()
    simulation.food.position = (
        tuple(initial["food"]) if initial["food"] is not None else None
    )
    simulation.score.base_score = initial["score"]
    simulation.score.combo_count = initial["combo"]
    simulation.score.time_since_last_food = (
        initial["ticks_since_last_food"] * simulation.step_seconds
    )
    simulation.starvation_seconds = (
        initial["starvation_ticks_elapsed"] * simulation.step_seconds
    )

    record = simulation.step(())
    normalized_death_cause = {
        None: None,
        "collision": "self_collision",
        "starvation": "starvation",
    }[record.death_cause]

    return {
        **specification,
        "expected": {
            "tick": record.step,
            "direction": record.direction,
            "head": list(record.head),
            "body": [list(point) for point in simulation.snake.body],
            "score": record.score,
            "alive": record.alive,
            "won": record.won,
            "death_cause": normalized_death_cause,
            "events": [_normalize_event(event) for event in record.events],
        },
    }


def _normalize_event(event: StepEvent) -> dict[str, Any]:
    normalized: dict[str, Any] = {"kind": event.kind}
    if event.position is not None:
        normalized["position"] = list(event.position)
    if event.direction is not None:
        normalized["direction"] = event.direction
    if event.value is not None:
        normalized["value"] = event.value
    if event.death_cause is not None:
        normalized["death_cause"] = event.death_cause
    return normalized


if __name__ == "__main__":
    raise SystemExit(main())
