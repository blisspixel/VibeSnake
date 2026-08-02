"""Generate targeted Shield traces consumed by the native parity suite."""

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
from vibesnake.powerups.shield import ShieldPowerUp
from vibesnake.qa.contracts import CURRENT_RULESET
from vibesnake.utils.logger import temporary_logger_level


POWER_TRACE_SCHEMA_VERSION = 1
POWER_TRACE_CONTRACT = "shield-rules-targeted-v1"
POWER_RANDOMNESS_POLICY = "positions-and-power-state-injected-v1"
DEFAULT_POWER_FIXTURE_PATH = Path("tests/fixtures/shared/shield_rules_v1.json")
STEP_SECONDS = settings.LOGIC_TICK
SHIELD_DURATION_TICKS = round(5.0 / STEP_SECONDS)
POWER_VISIBLE_TICKS = round(6.0 / STEP_SECONDS)
STARVATION_TICKS = round(30.0 / STEP_SECONDS)


class _ShieldReferenceGame:
    """Minimal state surface used by production Shield and manager code."""

    def __init__(self, body: list[tuple[int, int]], direction: Direction) -> None:
        self.snake = Snake()
        self.snake.body = deque(body)
        self.snake.positions_set = set(body)
        self.snake.direction = direction
        self.snake.next_directions.clear()
        self.powerups = PowerUpManager()
        self.snake_is_shielded = False
        self.session_powerups_collected = 0


def build_power_fixture() -> dict[str, Any]:
    """Return deterministic one-step traces for the production Shield contract."""
    collision_body = [(1, 1), (1, 2), (2, 2), (2, 1)]
    specifications = [
        _spec(
            "shield-collect-on-entry",
            [(5, 5)],
            "RIGHT",
            pickup_position=(6, 5),
            pickup_visibility_ticks=10,
        ),
        _spec(
            "shield-pickup-expiry",
            [(5, 5)],
            "RIGHT",
            pickup_position=(6, 5),
            pickup_visibility_ticks=1,
        ),
        _spec(
            "shield-active-countdown",
            [(5, 5)],
            "RIGHT",
            shield_ticks_remaining=2,
        ),
        _spec(
            "shield-active-expiry",
            [(5, 5)],
            "RIGHT",
            shield_ticks_remaining=1,
        ),
        _spec(
            "shield-collision-consumption",
            collision_body,
            "DOWN",
            shield_ticks_remaining=2,
        ),
        _spec(
            "shield-collision-at-starvation-deadline",
            collision_body,
            "DOWN",
            starvation_ticks_elapsed=STARVATION_TICKS - 1,
            shield_ticks_remaining=2,
        ),
        _spec(
            "shield-expiry-before-collision",
            collision_body,
            "DOWN",
            shield_ticks_remaining=1,
        ),
        _spec(
            "shield-does-not-block-starvation",
            [(5, 5)],
            "RIGHT",
            starvation_ticks_elapsed=STARVATION_TICKS - 1,
            shield_ticks_remaining=2,
        ),
    ]
    previous_random_state = random.getstate()
    try:
        cases = [_execute_specification(specification) for specification in specifications]
    finally:
        random.setstate(previous_random_state)

    return {
        "schema_version": POWER_TRACE_SCHEMA_VERSION,
        "contract": POWER_TRACE_CONTRACT,
        "ruleset": CURRENT_RULESET.to_dict(),
        "randomness_policy": POWER_RANDOMNESS_POLICY,
        "source_engine": "python-production-shield-v1",
        "config": {
            "width": settings.GRID_WIDTH,
            "height": settings.GRID_HEIGHT,
            "starvation_ticks": STARVATION_TICKS,
            "power_visible_ticks": POWER_VISIBLE_TICKS,
            "shield_duration_ticks": SHIELD_DURATION_TICKS,
        },
        "case_count": len(cases),
        "comparison_scope": [
            "pickup_identity",
            "collection_on_entry",
            "activation",
            "duration_countdown",
            "pickup_expiry",
            "effect_expiry",
            "self_collision_consumption",
            "collision_prevention",
            "starvation_bypass",
            "ordered_power_events",
        ],
        "excluded_scope": [
            "random_spawn_position",
            "spawn_schedule",
            "presentation_feedback",
            "other_power_types",
        ],
        "cases": cases,
    }


def fixture_json(fixture: dict[str, Any]) -> str:
    """Serialize a Shield fixture canonically."""
    return json.dumps(fixture, separators=(",", ":"), sort_keys=True) + "\n"


def check_fixture(path: Path, fixture: dict[str, Any]) -> bool:
    """Return whether the checked-in Shield fixture is current."""
    return path.is_file() and path.read_text(encoding="utf-8") == fixture_json(fixture)


def main(argv: list[str] | None = None) -> int:
    """Write or verify the targeted Shield fixture."""
    with temporary_logger_level("vibesnake", logging.WARNING):
        parser = argparse.ArgumentParser(
            prog="python -m vibesnake.qa.shared_power_traces",
            description="Generate production Python Shield traces consumed by native tests.",
        )
        parser.add_argument("--output", type=Path, default=DEFAULT_POWER_FIXTURE_PATH)
        parser.add_argument("--check", action="store_true")
        arguments = parser.parse_args(argv)
        fixture = build_power_fixture()

        if arguments.check:
            if check_fixture(arguments.output, fixture):
                print(f"Shared Shield fixture passed: {fixture['case_count']} targeted cases")
                return 0
            print(f"Shared Shield fixture is missing or stale: {arguments.output}")
            return 1

        arguments.output.parent.mkdir(parents=True, exist_ok=True)
        arguments.output.write_text(fixture_json(fixture), encoding="utf-8")
        print(f"Shared Shield fixture written: {fixture['case_count']} targeted cases; output={arguments.output}")
        return 0


def _spec(
    case_id: str,
    body: list[tuple[int, int]],
    direction: str,
    *,
    pickup_position: tuple[int, int] | None = None,
    pickup_visibility_ticks: int = 0,
    shield_ticks_remaining: int = 0,
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
                    "kind": "shield",
                    "position": list(pickup_position),
                    "visibility_ticks_remaining": pickup_visibility_ticks,
                }
                if pickup_position is not None
                else None
            ),
            "shield_ticks_remaining": shield_ticks_remaining,
        },
    }


def _execute_specification(specification: dict[str, Any]) -> dict[str, Any]:
    initial = specification["initial"]
    body = [tuple(point) for point in initial["body"]]
    game = _ShieldReferenceGame(body, Direction[initial["direction"]])
    shield = _install_shield_state(game, initial)
    events: list[dict[str, Any]] = []

    was_pickup = shield is not None and shield.active and not shield.activated
    game.powerups.update(STEP_SECONDS, game)
    if shield is not None and not shield.active:
        events.append(
            _power_event(
                "power_expired",
                position=shield.position if was_pickup else None,
            )
        )

    alive, wrapped = game.snake.move()
    death_cause: str | None = None
    if alive:
        events.append(_event("moved", position=game.snake.get_head()))
        if wrapped:
            events.append(_event("wrapped", position=game.snake.get_head()))
        collected = game.powerups.collect_at(game.snake.get_head(), game)
        if collected is not None:
            events.append(_power_event("power_collected", position=collected.position))
            events.append(_power_event("power_activated", value=SHIELD_DURATION_TICKS))
    else:
        if wrapped:
            events.append(_event("wrapped", position=game.snake.peek_next_head()))

    if not alive and game.snake_is_shielded:
        if not game.powerups.consume(ShieldPowerUp, game):
            raise AssertionError("active Shield state could not be consumed")
        alive = True
        events.append(_power_event("power_consumed"))
        events.append(
            _power_event(
                "collision_prevented",
                position=game.snake.peek_next_head(),
                death_cause="self_collision",
            )
        )
    elif not alive:
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

    active_shield = next(
        (
            powerup
            for powerup in game.powerups.active_powerups
            if isinstance(powerup, ShieldPowerUp) and powerup.active and powerup.activated
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
            "shield_ticks_remaining": _remaining_ticks(active_shield),
            "events": events,
        },
    }


def _install_shield_state(
    game: _ShieldReferenceGame,
    initial: dict[str, Any],
) -> ShieldPowerUp | None:
    pickup = initial["pickup"]
    shield_ticks_remaining = initial["shield_ticks_remaining"]
    if pickup is not None and shield_ticks_remaining > 0:
        raise AssertionError("a Shield pickup cannot coexist with an active Shield")
    if pickup is not None:
        shield = ShieldPowerUp(tuple(pickup["position"]))
        shield.visible_timer = shield.visible_duration - (pickup["visibility_ticks_remaining"] * STEP_SECONDS)
        game.powerups.active_powerups.append(shield)
        return shield
    if shield_ticks_remaining > 0:
        shield = ShieldPowerUp((0, 0))
        game.powerups.active_powerups.append(shield)
        shield.activate(game)
        shield.timer = shield.duration - (shield_ticks_remaining * STEP_SECONDS)
        return shield
    return None


def _normalize_pickup(powerup: ShieldPowerUp | None) -> dict[str, Any] | None:
    if powerup is None:
        return None
    return {
        "kind": "shield",
        "position": list(powerup.position),
        "visibility_ticks_remaining": round((powerup.visible_duration - powerup.visible_timer) / STEP_SECONDS),
    }


def _remaining_ticks(powerup: ShieldPowerUp | None) -> int:
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
    event["power"] = "shield"
    return event


if __name__ == "__main__":
    raise SystemExit(main())
