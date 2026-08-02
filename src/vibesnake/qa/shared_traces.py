"""Generate versioned movement traces shared by Python and C# tests."""

from __future__ import annotations

import argparse
import json
import random
from collections.abc import Iterable
from pathlib import Path
from typing import Any

from vibesnake.core.enums import Direction
from vibesnake.core.snake import Snake
from vibesnake.data import settings
from vibesnake.qa.contracts import CURRENT_RULESET, SHARED_RANDOMNESS_POLICY


SHARED_TRACE_SCHEMA_VERSION = 2
SHARED_TRACE_CONTRACT = "movement-input-long-v2"
DEFAULT_CASE_COUNT = 100
DEFAULT_STEPS_PER_CASE = 256
DEFAULT_FIXTURE_PATH = Path("tests/fixtures/shared/core_movement_v2.json")
_COMMAND_SEED_MASK = 0xC0115EED
DIRECTION_SYMBOLS = {
    "UP": "U",
    "RIGHT": "R",
    "DOWN": "D",
    "LEFT": "L",
}
STEP_ENCODING = (
    "command_symbols",
    "command_acceptance_bits",
    "direction_symbol",
    "head_x",
    "head_y",
    "body_length",
    "pending_direction_symbols",
    "wrapped",
    "alive",
)


def build_movement_fixture(
    case_count: int = DEFAULT_CASE_COUNT,
    steps_per_case: int = DEFAULT_STEPS_PER_CASE,
) -> dict[str, Any]:
    """Run production Snake movement and return a normalized trace corpus."""
    if case_count <= 0:
        raise ValueError("case_count must be greater than zero")
    if steps_per_case <= 0:
        raise ValueError("steps_per_case must be greater than zero")

    cases = [_build_case(seed=seed, steps_per_case=steps_per_case) for seed in range(case_count)]
    return {
        "schema_version": SHARED_TRACE_SCHEMA_VERSION,
        "contract": SHARED_TRACE_CONTRACT,
        "ruleset": CURRENT_RULESET.to_dict(),
        "randomness_policy": SHARED_RANDOMNESS_POLICY,
        "source_engine": "python-production-snake-v2",
        "case_count": case_count,
        "steps_per_case": steps_per_case,
        "total_steps": case_count * steps_per_case,
        "grid": {
            "width": settings.GRID_WIDTH,
            "height": settings.GRID_HEIGHT,
        },
        "direction_symbols": DIRECTION_SYMBOLS,
        "step_encoding": list(STEP_ENCODING),
        "comparison_scope": [
            "bounded_direction_queue",
            "command_acceptance",
            "duplicate_rejection",
            "reversal_rejection",
            "overflow_rejection",
            "direction_consumption",
            "head_position",
            "body_length",
            "edge_wrapping",
            "survival",
        ],
        "excluded_scope": [
            "food",
            "growth",
            "score",
            "combo",
            "starvation",
            "collision",
            "random_stream",
        ],
        "cases": cases,
    }


def fixture_json(fixture: dict[str, Any]) -> str:
    """Serialize a fixture in its canonical checked-in representation."""
    return json.dumps(fixture, separators=(",", ":"), sort_keys=True) + "\n"


def check_fixture(path: Path, fixture: dict[str, Any]) -> bool:
    """Return whether a checked-in fixture exactly matches regeneration."""
    return path.is_file() and path.read_text(encoding="utf-8") == fixture_json(fixture)


def main(argv: list[str] | None = None) -> int:
    """Write or verify the shared movement fixture."""
    parser = argparse.ArgumentParser(
        prog="python -m vibesnake.qa.shared_traces",
        description="Generate Python movement traces consumed by native C# tests.",
    )
    parser.add_argument("--output", type=Path, default=DEFAULT_FIXTURE_PATH)
    parser.add_argument("--cases", type=_positive_int, default=DEFAULT_CASE_COUNT)
    parser.add_argument("--steps", type=_positive_int, default=DEFAULT_STEPS_PER_CASE)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args(argv)

    fixture = build_movement_fixture(args.cases, args.steps)
    if args.check:
        if check_fixture(args.output, fixture):
            print(f"Shared trace fixture passed: {args.cases} cases, {args.cases * args.steps} steps")
            return 0
        print(f"Shared trace fixture is missing or stale: {args.output}")
        return 1

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(fixture_json(fixture), encoding="utf-8")
    print(f"Shared trace fixture written: {args.cases} cases, {args.cases * args.steps} steps; output={args.output}")
    return 0


def _build_case(seed: int, steps_per_case: int) -> dict[str, Any]:
    snake = Snake()
    initial_body = [list(point) for point in snake.body]
    initial_direction = snake.direction.name
    command_rng = random.Random(seed ^ _COMMAND_SEED_MASK)
    directions = tuple(Direction)
    trace_steps: list[list[Any]] = []

    for step_index in range(steps_per_case):
        if step_index < min(40, steps_per_case):
            commands: tuple[Direction, ...] = ()
        else:
            commands = tuple(command_rng.choice(directions) for _ in range(command_rng.randrange(6)))

        command_acceptance = tuple(snake.queue_direction(command) for command in commands)

        alive, wrapped = snake.move(grow=False)
        trace_steps.append(
            [
                _direction_symbols(commands),
                "".join("1" if accepted else "0" for accepted in command_acceptance),
                DIRECTION_SYMBOLS[snake.direction.name],
                *snake.get_head(),
                len(snake.body),
                _direction_symbols(snake.next_directions),
                wrapped,
                alive,
            ]
        )
        if not alive:
            break

    return {
        "id": f"movement-seed-{seed:03d}",
        "seed": seed,
        "initial": {
            "body": initial_body,
            "direction": initial_direction,
        },
        "steps": trace_steps,
    }


def _direction_symbols(directions: Iterable[Direction]) -> str:
    """Encode an ordered direction collection without verbose repeated keys."""
    return "".join(DIRECTION_SYMBOLS[direction.name] for direction in directions)


def _positive_int(value: str) -> int:
    parsed = int(value)
    if parsed <= 0:
        raise argparse.ArgumentTypeError("value must be greater than zero")
    return parsed


if __name__ == "__main__":
    raise SystemExit(main())
