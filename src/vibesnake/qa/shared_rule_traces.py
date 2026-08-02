"""Generate targeted Python rule traces consumed by the C# parity suite."""

from __future__ import annotations

import argparse
import json
import logging
import random
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


RULE_TRACE_SCHEMA_VERSION = 4
RULE_TRACE_CONTRACT = "core-rules-targeted-v4"
DEFAULT_RULE_FIXTURE_PATH = Path("tests/fixtures/shared/core_rules_v4.json")
_FIXTURE_RANDOM_SEED = 0x51A7E


def build_rule_fixture() -> dict[str, Any]:
    """Return targeted production traces for scoring and terminal rules."""
    specifications = [
        _score_spec("food-entry", ticks_since_last_food=0),
        _spec("food-buffered-turn", [(5, 5)], "RIGHT", (5, 4), commands=["UP"]),
        _spec(
            "queue-rejections-and-consumption",
            [(5, 5)],
            "RIGHT",
            None,
            commands=["RIGHT", "LEFT", "UP", "DOWN", "LEFT", "LEFT"],
        ),
        _spec(
            "queue-capacity",
            [(5, 5)],
            "RIGHT",
            None,
            commands=["UP", "LEFT", "DOWN", "RIGHT", "UP"],
        ),
        _score_spec("combo-before-three", combo=1),
        _score_spec("combo-threshold-three", combo=2),
        _score_spec("combo-after-three", combo=3),
        _score_spec("combo-threshold-five", combo=4),
        _score_spec("combo-after-five", combo=5),
        _score_spec("combo-before-ten", combo=8),
        _score_spec("combo-threshold-ten", combo=9),
        _score_spec("combo-after-ten", combo=10),
        _score_spec("combo-before-twenty", combo=18),
        _score_spec("combo-threshold-twenty", combo=19),
        _score_spec("combo-after-twenty-cap", combo=20),
        _score_spec("speed-bonus-last-eligible-tick", ticks_since_last_food=28),
        _score_spec("speed-bonus-exact-boundary", ticks_since_last_food=29),
        _score_spec("speed-bonus-after-boundary", ticks_since_last_food=30),
        _spec(
            "combo-window-exact-no-food",
            [(5, 5)],
            "RIGHT",
            None,
            combo=4,
            ticks_since_last_food=59,
        ),
        _spec(
            "combo-window-expired-no-food",
            [(5, 5)],
            "RIGHT",
            None,
            combo=4,
            ticks_since_last_food=60,
        ),
        _score_spec("combo-window-exact-food", combo=4, ticks_since_last_food=59),
        _score_spec(
            "expired-combo-late-food-no-speed-bonus",
            combo=4,
            ticks_since_last_food=60,
        ),
        _length_score_spec("length-exact-ten", body_length=9),
        _length_score_spec("length-first-bonus", body_length=10),
        _length_score_spec("length-above-boundary", body_length=11),
        _score_spec(
            "score-saturation-near-cap",
            score=MAXIMUM_SCORE - 1,
        ),
        _score_spec("score-at-cap", score=MAXIMUM_SCORE),
        _spec(
            "self-collision",
            [(1, 1), (1, 2), (2, 2), (2, 1)],
            "DOWN",
            None,
        ),
        _spec(
            "departing-tail-is-safe",
            [(1, 1), (1, 2), (2, 2), (2, 1)],
            "LEFT",
            None,
        ),
        _spec("horizontal-wrap", [(settings.GRID_WIDTH - 1, 10)], "RIGHT", (5, 5)),
        _spec(
            "starvation-predeadline",
            [(5, 5)],
            "RIGHT",
            None,
            starvation_ticks_elapsed=598,
        ),
        _spec(
            "starvation-deadline-food-rescue",
            [(5, 5)],
            "RIGHT",
            (6, 5),
            starvation_ticks_elapsed=599,
        ),
        _spec(
            "starvation-deadline-death",
            [(5, 5)],
            "RIGHT",
            None,
            starvation_ticks_elapsed=599,
        ),
        _spec(
            "starvation-collision-precedence",
            [(1, 1), (1, 2), (2, 2), (2, 1)],
            "DOWN",
            None,
            starvation_ticks_elapsed=599,
        ),
        _spec(
            "full-grid-victory",
            _full_grid_body(),
            "RIGHT",
            (settings.GRID_WIDTH - 1, settings.GRID_HEIGHT - 1),
        ),
    ]
    previous_random_state = random.getstate()
    random.seed(_FIXTURE_RANDOM_SEED)
    try:
        cases = [_execute_specification(specification) for specification in specifications]
    finally:
        random.setstate(previous_random_state)

    return {
        "schema_version": RULE_TRACE_SCHEMA_VERSION,
        "contract": RULE_TRACE_CONTRACT,
        "ruleset": CURRENT_RULESET.to_dict(),
        "randomness_policy": SHARED_RANDOMNESS_POLICY,
        "source_engine": "python-core-reference-v3",
        "config": {
            "width": settings.GRID_WIDTH,
            "height": settings.GRID_HEIGHT,
            "starvation_ticks": 600,
            "maximum_direction_queue": Snake.MAX_DIRECTION_QUEUE,
            "maximum_score": MAXIMUM_SCORE,
            "combo_window_ticks": 60,
            "speed_bonus_ticks": 30,
            "food_score": 10,
        },
        "case_count": len(cases),
        "comparison_scope": [
            "food_entry",
            "growth",
            "base_score",
            "score_saturation",
            "speed_bonus",
            "speed_bonus_boundaries",
            "combo_interpolation",
            "combo_expiry",
            "combo_clock_monotonicity",
            "length_bonus",
            "length_bonus_boundaries",
            "command_acceptance",
            "queue_capacity",
            "queue_consumption",
            "self_collision",
            "departing_tail",
            "edge_wrapping",
            "starvation_progress",
            "exact_starvation_deadline",
            "collision_precedence",
            "full_grid_completion",
            "food_stability_without_collection",
            "random_respawn_legality",
            "random_stream_use",
            "ordered_events",
        ],
        "excluded_scope": [
            "food_respawn_coordinate",
            "risk_bonus",
            "power_effects",
        ],
        "cases": cases,
    }


def fixture_json(fixture: dict[str, Any]) -> str:
    """Serialize a targeted rules fixture canonically."""
    return json.dumps(fixture, separators=(",", ":"), sort_keys=True) + "\n"


def check_fixture(path: Path, fixture: dict[str, Any]) -> bool:
    """Return whether the checked-in targeted fixture is current."""
    return path.is_file() and path.read_text(encoding="utf-8") == fixture_json(fixture)


def main(argv: list[str] | None = None) -> int:
    """Write or verify the targeted core rules fixture."""
    with temporary_logger_level("vibesnake", logging.WARNING):
        parser = argparse.ArgumentParser(
            prog="python -m vibesnake.qa.shared_rule_traces",
            description="Generate targeted Python rule traces consumed by native C# tests.",
        )
        parser.add_argument("--output", type=Path, default=DEFAULT_RULE_FIXTURE_PATH)
        parser.add_argument("--check", action="store_true")
        args = parser.parse_args(argv)
        fixture = build_rule_fixture()

        if args.check:
            if check_fixture(args.output, fixture):
                print(f"Shared rule fixture passed: {fixture['case_count']} targeted cases")
                return 0
            print(f"Shared rule fixture is missing or stale: {args.output}")
            return 1

        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(fixture_json(fixture), encoding="utf-8")
        print(f"Shared rule fixture written: {fixture['case_count']} targeted cases; output={args.output}")
        return 0


def _spec(
    case_id: str,
    body: list[tuple[int, int]],
    direction: str,
    food: tuple[int, int] | None,
    *,
    commands: list[str] | None = None,
    score: int = 0,
    combo: int = 0,
    ticks_since_last_food: int = 0,
    starvation_ticks_elapsed: int = 0,
) -> dict[str, Any]:
    return {
        "id": case_id,
        "initial": {
            "body": [list(point) for point in body],
            "direction": direction,
            "food": list(food) if food is not None else None,
            "score": score,
            "combo": combo,
            "ticks_since_last_food": ticks_since_last_food,
            "starvation_ticks_elapsed": starvation_ticks_elapsed,
        },
        "commands": commands or [],
    }


def _score_spec(
    case_id: str,
    *,
    score: int = 0,
    combo: int = 0,
    ticks_since_last_food: int = 29,
) -> dict[str, Any]:
    """Build a one-cell food-entry case that isolates a score boundary."""
    return _spec(
        case_id,
        [(5, 5)],
        "RIGHT",
        (6, 5),
        score=score,
        combo=combo,
        ticks_since_last_food=ticks_since_last_food,
    )


def _length_score_spec(case_id: str, *, body_length: int) -> dict[str, Any]:
    """Build a straight growth case around the length-bonus boundary."""
    body = [(x, 5) for x in range(body_length)]
    return _spec(
        case_id,
        body,
        "RIGHT",
        (body_length, 5),
        ticks_since_last_food=29,
    )


def _execute_specification(specification: dict[str, Any]) -> dict[str, Any]:
    initial = specification["initial"]
    simulation = CoreSimulation()
    body = [tuple(point) for point in initial["body"]]
    simulation.snake.body = deque(body)
    simulation.snake.positions_set = set(body)
    simulation.snake.direction = Direction[initial["direction"]]
    simulation.snake.next_directions.clear()
    simulation.food.position = tuple(initial["food"]) if initial["food"] is not None else None
    simulation.score.base_score = initial["score"]
    simulation.score.combo_count = initial["combo"]
    simulation.score.time_since_last_food = initial["ticks_since_last_food"] * simulation.step_seconds
    simulation.starvation_seconds = initial["starvation_ticks_elapsed"] * simulation.step_seconds

    commands = tuple(Direction[command] for command in specification["commands"])
    command_acceptance = [simulation.snake.queue_direction(command) for command in commands]
    initial_food = simulation.food.position
    random_state_before = random.getstate()
    record = simulation.step(())
    random_use = "unchanged" if random.getstate() == random_state_before else "advanced"
    normalized_death_cause = {
        None: None,
        "collision": "self_collision",
        "starvation": "starvation",
    }[record.death_cause]

    return {
        **specification,
        "command_acceptance": command_acceptance,
        "expected": {
            "tick": record.step,
            "direction": record.direction,
            "head": list(record.head),
            "body": [list(point) for point in simulation.snake.body],
            "pending_directions": [direction.name for direction in simulation.snake.next_directions],
            "score": record.score,
            "combo": record.combo,
            "ticks_since_last_food": round(simulation.score.time_since_last_food / simulation.step_seconds),
            "starvation_ticks_elapsed": round(simulation.starvation_seconds / simulation.step_seconds),
            "wrapped": record.wrapped,
            "ate_food": record.ate_food,
            "alive": record.alive,
            "won": record.won,
            "death_cause": normalized_death_cause,
            "food_unchanged": simulation.food.position == initial_food,
            "random_respawn": _normalize_random_respawn(simulation, ate_food=record.ate_food),
            "random_use": random_use,
            "events": [_normalize_event(event) for event in record.events],
        },
    }


def _normalize_random_respawn(simulation: CoreSimulation, *, ate_food: bool) -> str:
    """Describe random food output by contract instead of comparing coordinates."""
    if not ate_food:
        return "not_used"
    if simulation.won:
        if simulation.food.position is not None:
            raise AssertionError("a full-grid victory cannot retain food")
        return "full_grid_no_cell"

    food = simulation.food.position
    if food is None:
        raise AssertionError("a non-terminal food collection must respawn food")
    x, y = food
    if not (0 <= x < settings.GRID_WIDTH and 0 <= y < settings.GRID_HEIGHT):
        raise AssertionError("respawned food must remain inside the grid")
    if simulation.snake.occupies(food):
        raise AssertionError("respawned food must occupy a free cell")
    return "legal_free_cell"


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


def _full_grid_body() -> list[tuple[int, int]]:
    body: list[tuple[int, int]] = []
    for y in range(settings.GRID_HEIGHT):
        x_values = range(settings.GRID_WIDTH) if y % 2 == 0 else range(settings.GRID_WIDTH - 1, -1, -1)
        body.extend((x, y) for x in x_values)
    return body[:-1]


if __name__ == "__main__":
    raise SystemExit(main())
