"""Generate targeted Phase Shift traces consumed by the native parity suite."""

from __future__ import annotations

import argparse
from collections import deque
import json
import logging
from pathlib import Path
import random
from typing import Any

from vibesnake.core.enums import Direction
from vibesnake.core.snake import Snake
from vibesnake.data import settings
from vibesnake.powerups.manager import PowerUpManager
from vibesnake.powerups.phaseshift import PhaseShiftPowerUp
from vibesnake.qa.contracts import CURRENT_RULESET
from vibesnake.utils.logger import temporary_logger_level


PHASE_TRACE_SCHEMA_VERSION = 1
PHASE_TRACE_CONTRACT = "phase-shift-rules-targeted-v1"
PHASE_RANDOMNESS_POLICY = "positions-and-power-state-injected-v1"
DEFAULT_PHASE_FIXTURE_PATH = Path("tests/fixtures/shared/phase_shift_rules_v1.json")
STEP_SECONDS = settings.LOGIC_TICK
PHASE_SHIFT_DURATION_TICKS = round(5.0 / STEP_SECONDS)
POWER_VISIBLE_TICKS = round(6.0 / STEP_SECONDS)
STARVATION_TICKS = round(30.0 / STEP_SECONDS)


class _PhaseShiftReferenceGame:
    """Minimal state surface used by production Phase Shift code."""

    def __init__(self, body: list[tuple[int, int]], direction: Direction) -> None:
        self.snake = Snake()
        self.snake.body = deque(body)
        self.snake.positions_set = set(body)
        self.snake.direction = direction
        self.snake.next_directions.clear()
        self.powerups = PowerUpManager()
        self.snake_phase_shift_active = False
        self.session_powerups_collected = 0


def build_phase_shift_fixture() -> dict[str, Any]:
    """Return deterministic one-step traces for the production Phase Shift contract."""
    collision_body = [(1, 1), (1, 2), (2, 2), (2, 1)]
    specifications = [
        _spec(
            "phase-shift-collect-on-entry",
            [(5, 5)],
            "RIGHT",
            pickup_position=(6, 5),
            pickup_visibility_ticks=10,
        ),
        _spec(
            "phase-shift-pickup-expiry",
            [(5, 5)],
            "RIGHT",
            pickup_position=(6, 5),
            pickup_visibility_ticks=1,
        ),
        _spec(
            "phase-shift-active-countdown",
            [(5, 5)],
            "RIGHT",
            phase_shift_ticks_remaining=2,
        ),
        _spec(
            "phase-shift-active-expiry-before-collision",
            collision_body,
            "DOWN",
            phase_shift_ticks_remaining=1,
        ),
        _spec(
            "phase-shift-body-overlap",
            collision_body,
            "DOWN",
            phase_shift_ticks_remaining=2,
        ),
        _spec(
            "phase-shift-does-not-block-starvation",
            [(5, 5)],
            "RIGHT",
            starvation_ticks_elapsed=STARVATION_TICKS - 1,
            phase_shift_ticks_remaining=2,
        ),
    ]
    previous_random_state = random.getstate()
    try:
        cases = [_execute_specification(specification) for specification in specifications]
    finally:
        random.setstate(previous_random_state)

    return {
        "schema_version": PHASE_TRACE_SCHEMA_VERSION,
        "contract": PHASE_TRACE_CONTRACT,
        "ruleset": CURRENT_RULESET.to_dict(),
        "randomness_policy": PHASE_RANDOMNESS_POLICY,
        "source_engine": "python-production-phase-shift-v1",
        "config": {
            "width": settings.GRID_WIDTH,
            "height": settings.GRID_HEIGHT,
            "starvation_ticks": STARVATION_TICKS,
            "power_visible_ticks": POWER_VISIBLE_TICKS,
            "phase_shift_duration_ticks": PHASE_SHIFT_DURATION_TICKS,
        },
        "case_count": len(cases),
        "comparison_scope": [
            "pickup_identity",
            "collection_on_entry",
            "activation",
            "duration_countdown",
            "pickup_expiry",
            "effect_expiry",
            "self_collision_phasing",
            "body_overlap",
            "starvation_bypass",
            "ordered_power_events",
        ],
        "excluded_scope": [
            "random_spawn_position",
            "spawn_schedule",
            "presentation_feedback",
            "detached_obstacles",
            "other_power_types",
        ],
        "cases": cases,
    }


def fixture_json(fixture: dict[str, Any]) -> str:
    """Serialize a Phase Shift fixture canonically."""
    return json.dumps(fixture, separators=(",", ":"), sort_keys=True) + "\n"


def check_fixture(path: Path, fixture: dict[str, Any]) -> bool:
    """Return whether the checked-in Phase Shift fixture is current."""
    return path.is_file() and path.read_text(encoding="utf-8") == fixture_json(fixture)


def main(argv: list[str] | None = None) -> int:
    """Write or verify the targeted Phase Shift fixture."""
    with temporary_logger_level("vibesnake", logging.WARNING):
        parser = argparse.ArgumentParser(
            prog="python -m vibesnake.qa.shared_phase_shift_traces",
            description="Generate production Python Phase Shift traces consumed by native tests.",
        )
        parser.add_argument("--output", type=Path, default=DEFAULT_PHASE_FIXTURE_PATH)
        parser.add_argument("--check", action="store_true")
        arguments = parser.parse_args(argv)
        fixture = build_phase_shift_fixture()

        if arguments.check:
            if check_fixture(arguments.output, fixture):
                print(f"Shared Phase Shift fixture passed: {fixture['case_count']} targeted cases")
                return 0
            print(f"Shared Phase Shift fixture is missing or stale: {arguments.output}")
            return 1

        arguments.output.parent.mkdir(parents=True, exist_ok=True)
        arguments.output.write_text(fixture_json(fixture), encoding="utf-8")
        print(
            f"Shared Phase Shift fixture written: {fixture['case_count']} targeted cases; "
            f"output={arguments.output}"
        )
        return 0


def _spec(
    case_id: str,
    body: list[tuple[int, int]],
    direction: str,
    *,
    pickup_position: tuple[int, int] | None = None,
    pickup_visibility_ticks: int = 0,
    phase_shift_ticks_remaining: int = 0,
    starvation_ticks_elapsed: int = 0,
) -> dict[str, Any]:
    return {
        "id": case_id,
        "initial": {
            "body": [list(point) for point in body],
            "direction": direction,
            "food": [20, 20],
            "starvation_ticks_elapsed": starvation_ticks_elapsed,
            "pickup": (
                {
                    "kind": "phase_shift",
                    "position": list(pickup_position),
                    "visibility_ticks_remaining": pickup_visibility_ticks,
                }
                if pickup_position is not None
                else None
            ),
            "phase_shift_ticks_remaining": phase_shift_ticks_remaining,
        },
    }


def _execute_specification(specification: dict[str, Any]) -> dict[str, Any]:
    initial = specification["initial"]
    body = [tuple(point) for point in initial["body"]]
    game = _PhaseShiftReferenceGame(body, Direction[initial["direction"]])
    phase = _install_phase_shift_state(game, initial)
    events: list[dict[str, Any]] = []

    was_pickup = phase is not None and phase.active and not phase.activated
    game.powerups.update(STEP_SECONDS, game)
    if phase is not None and not phase.active:
        events.append(
            _power_event(
                "power_expired",
                position=phase.position if was_pickup else None,
            )
        )

    alive, wrapped = game.snake.move(ignore_self_collision=game.snake_phase_shift_active)
    death_cause: str | None = None
    if alive:
        events.append(_event("moved", position=game.snake.get_head()))
        if wrapped:
            events.append(_event("wrapped", position=game.snake.get_head()))
        collected = game.powerups.collect_at(game.snake.get_head(), game)
        if collected is not None:
            events.append(_power_event("power_collected", position=collected.position))
            events.append(_power_event("power_activated", value=PHASE_SHIFT_DURATION_TICKS))
    else:
        if wrapped:
            events.append(_event("wrapped", position=game.snake.peek_next_head()))
        death_cause = "self_collision"
        events.append(
            _event(
                "died",
                position=game.snake.peek_next_head(),
                death_cause=death_cause,
            )
        )

    starvation_ticks_elapsed = initial["starvation_ticks_elapsed"] + 1
    if alive and starvation_ticks_elapsed >= STARVATION_TICKS:
        alive = False
        death_cause = "starvation"
        events.append(
            _event(
                "died",
                position=game.snake.get_head(),
                death_cause=death_cause,
            )
        )

    active_phase = next(
        (
            powerup
            for powerup in game.powerups.active_powerups
            if isinstance(powerup, PhaseShiftPowerUp) and powerup.active and powerup.activated
        ),
        None,
    )
    pickup = next(iter(game.powerups.collectible_powerups()), None)
    return {
        **specification,
        "expected": {
            "tick": 1,
            "head": list(game.snake.get_head()),
            "body": [list(point) for point in game.snake.body],
            "alive": alive,
            "death_cause": death_cause,
            "starvation_ticks_elapsed": starvation_ticks_elapsed,
            "pickup": _normalize_pickup(pickup),
            "phase_shift_ticks_remaining": _remaining_ticks(active_phase),
            "events": events,
        },
    }


def _install_phase_shift_state(
    game: _PhaseShiftReferenceGame,
    initial: dict[str, Any],
) -> PhaseShiftPowerUp | None:
    pickup = initial["pickup"]
    phase_shift_ticks_remaining = initial["phase_shift_ticks_remaining"]
    if pickup is not None and phase_shift_ticks_remaining > 0:
        raise AssertionError("a Phase Shift pickup cannot coexist with an active Phase Shift")
    if pickup is not None:
        phase = PhaseShiftPowerUp(tuple(pickup["position"]))
        phase.visible_timer = phase.visible_duration - (pickup["visibility_ticks_remaining"] * STEP_SECONDS)
        game.powerups.active_powerups.append(phase)
        return phase
    if phase_shift_ticks_remaining > 0:
        phase = PhaseShiftPowerUp((0, 0))
        game.powerups.active_powerups.append(phase)
        phase.activate(game)
        phase.timer = phase.duration - (phase_shift_ticks_remaining * STEP_SECONDS)
        return phase
    return None


def _normalize_pickup(powerup: PhaseShiftPowerUp | None) -> dict[str, Any] | None:
    if powerup is None:
        return None
    return {
        "kind": "phase_shift",
        "position": list(powerup.position),
        "visibility_ticks_remaining": round((powerup.visible_duration - powerup.visible_timer) / STEP_SECONDS),
    }


def _remaining_ticks(powerup: PhaseShiftPowerUp | None) -> int:
    if powerup is None:
        return 0
    return round((powerup.duration - powerup.timer) / STEP_SECONDS)


def _event(
    kind: str,
    *,
    position: tuple[int, int] | None = None,
    value: int | None = None,
    death_cause: str | None = None,
) -> dict[str, Any]:
    event: dict[str, Any] = {"kind": kind}
    if position is not None:
        event["position"] = list(position)
    if value is not None:
        event["value"] = value
    if death_cause is not None:
        event["death_cause"] = death_cause
    return event


def _power_event(
    kind: str,
    *,
    position: tuple[int, int] | None = None,
    value: int | None = None,
    death_cause: str | None = None,
) -> dict[str, Any]:
    event = _event(kind, position=position, value=value, death_cause=death_cause)
    event["power"] = "phase_shift"
    return event


if __name__ == "__main__":
    raise SystemExit(main())
