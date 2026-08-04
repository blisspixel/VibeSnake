"""Generate targeted Last Stand traces consumed by the native parity suite."""

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
from vibesnake.powerups.laststand import LastStandPowerUp
from vibesnake.powerups.manager import PowerUpManager
from vibesnake.qa.contracts import CURRENT_RULESET
from vibesnake.utils.logger import temporary_logger_level


LAST_STAND_TRACE_SCHEMA_VERSION = 1
LAST_STAND_TRACE_CONTRACT = "last-stand-rules-targeted-v1"
LAST_STAND_RANDOMNESS_POLICY = "positions-and-power-state-injected-v1"
DEFAULT_LAST_STAND_FIXTURE_PATH = Path("tests/fixtures/shared/last_stand_rules_v1.json")
STEP_SECONDS = settings.LOGIC_TICK
LAST_STAND_RECOVERY_TICKS = round(3.0 / STEP_SECONDS)
POWER_VISIBLE_TICKS = round(6.0 / STEP_SECONDS)
STARVATION_TICKS = round(30.0 / STEP_SECONDS)


class _LastStandReferenceGame:
    """Minimal state surface used by production Last Stand code."""

    def __init__(self, body: list[tuple[int, int]], direction: Direction) -> None:
        self.snake = Snake()
        self.snake.body = deque(body)
        self.snake.positions_set = set(body)
        self.snake.direction = direction
        self.snake.next_directions.clear()
        self.powerups = PowerUpManager()
        self.last_stand_held = False
        self.revival_invulnerability_timer = 0.0
        self.session_powerups_collected = 0


def build_last_stand_fixture() -> dict[str, Any]:
    """Return deterministic one-step traces for the production Last Stand contract."""
    collision_body = [(1, 1), (1, 2), (2, 2), (2, 1), (3, 1)]
    specifications = [
        _spec(
            "last-stand-collect-on-entry",
            [(5, 5)],
            "RIGHT",
            pickup_position=(6, 5),
            pickup_visibility_ticks=10,
        ),
        _spec(
            "last-stand-collision-revive",
            collision_body,
            "LEFT",
            last_stand_held=True,
        ),
        _spec(
            "last-stand-recovery-blocks-collision",
            [(1, 1), (1, 2), (2, 2), (2, 1)],
            "DOWN",
            recovery_ticks_remaining=2,
        ),
        _spec(
            "last-stand-starvation-revive",
            [(5, 5), (6, 5), (7, 5), (8, 5)],
            "RIGHT",
            starvation_ticks_elapsed=STARVATION_TICKS - 1,
            last_stand_held=True,
        ),
        _spec(
            "last-stand-recovery-expiry",
            [(5, 5)],
            "RIGHT",
            recovery_ticks_remaining=1,
        ),
    ]
    previous_random_state = random.getstate()
    try:
        cases = [_execute_specification(specification) for specification in specifications]
    finally:
        random.setstate(previous_random_state)

    return {
        "schema_version": LAST_STAND_TRACE_SCHEMA_VERSION,
        "contract": LAST_STAND_TRACE_CONTRACT,
        "ruleset": CURRENT_RULESET.to_dict(),
        "randomness_policy": LAST_STAND_RANDOMNESS_POLICY,
        "source_engine": "python-production-last-stand-v1",
        "config": {
            "width": settings.GRID_WIDTH,
            "height": settings.GRID_HEIGHT,
            "starvation_ticks": STARVATION_TICKS,
            "power_visible_ticks": POWER_VISIBLE_TICKS,
            "last_stand_recovery_ticks": LAST_STAND_RECOVERY_TICKS,
        },
        "case_count": len(cases),
        "comparison_scope": [
            "pickup_identity",
            "collection_on_entry",
            "held_activation",
            "collision_revive",
            "body_shrink",
            "starvation_revive",
            "recovery_immunity",
            "recovery_expiry",
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
    """Serialize a Last Stand fixture canonically."""
    return json.dumps(fixture, separators=(",", ":"), sort_keys=True) + "\n"


def check_fixture(path: Path, fixture: dict[str, Any]) -> bool:
    """Return whether the checked-in Last Stand fixture is current."""
    return path.is_file() and path.read_text(encoding="utf-8") == fixture_json(fixture)


def main(argv: list[str] | None = None) -> int:
    """Write or verify the targeted Last Stand fixture."""
    with temporary_logger_level("vibesnake", logging.WARNING):
        parser = argparse.ArgumentParser(
            prog="python -m vibesnake.qa.shared_last_stand_traces",
            description="Generate production Python Last Stand traces consumed by native tests.",
        )
        parser.add_argument("--output", type=Path, default=DEFAULT_LAST_STAND_FIXTURE_PATH)
        parser.add_argument("--check", action="store_true")
        arguments = parser.parse_args(argv)
        fixture = build_last_stand_fixture()

        if arguments.check:
            if check_fixture(arguments.output, fixture):
                print(f"Shared Last Stand fixture passed: {fixture['case_count']} targeted cases")
                return 0
            print(f"Shared Last Stand fixture is missing or stale: {arguments.output}")
            return 1

        arguments.output.parent.mkdir(parents=True, exist_ok=True)
        arguments.output.write_text(fixture_json(fixture), encoding="utf-8")
        print(f"Shared Last Stand fixture written: {fixture['case_count']} targeted cases; output={arguments.output}")
        return 0


def _spec(
    case_id: str,
    body: list[tuple[int, int]],
    direction: str,
    *,
    pickup_position: tuple[int, int] | None = None,
    pickup_visibility_ticks: int = 0,
    last_stand_held: bool = False,
    recovery_ticks_remaining: int = 0,
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
                    "kind": "last_stand",
                    "position": list(pickup_position),
                    "visibility_ticks_remaining": pickup_visibility_ticks,
                }
                if pickup_position is not None
                else None
            ),
            "last_stand_held": last_stand_held,
            "recovery_ticks_remaining": recovery_ticks_remaining,
        },
    }


def _execute_specification(specification: dict[str, Any]) -> dict[str, Any]:
    initial = specification["initial"]
    body = [tuple(point) for point in initial["body"]]
    game = _LastStandReferenceGame(body, Direction[initial["direction"]])
    power = _install_last_stand_state(game, initial)
    events: list[dict[str, Any]] = []

    was_pickup = power is not None and power.active and not power.activated
    game.powerups.update(STEP_SECONDS, game)
    if power is not None and not power.active and was_pickup:
        events.append(_power_event("power_expired", position=power.position))

    # Recovery timer advances once per rules step before resolution.
    recovery_before = game.revival_invulnerability_timer
    if recovery_before > 0.0:
        game.revival_invulnerability_timer = max(0.0, recovery_before - STEP_SECONDS)
        if game.revival_invulnerability_timer == 0.0 and recovery_before > 0.0:
            events.append(_power_event("power_expired"))

    alive, wrapped = game.snake.move()
    death_cause: str | None = None
    if alive:
        events.append(_event("moved", position=game.snake.get_head()))
        if wrapped:
            events.append(_event("wrapped", position=game.snake.get_head()))
        collected = game.powerups.collect_at(game.snake.get_head(), game)
        if collected is not None:
            events.append(_power_event("power_collected", position=collected.position))
            events.append(_power_event("power_activated", value=0))
    else:
        if wrapped:
            events.append(_event("wrapped", position=game.snake.peek_next_head()))
        if game.revival_invulnerability_timer > 0.0:
            alive = True
            events.append(
                _power_event(
                    "collision_prevented",
                    position=game.snake.peek_next_head(),
                    death_cause="self_collision",
                )
            )
        elif game.last_stand_held:
            _apply_revive(game, events, death_cause="self_collision")
            alive = True
        else:
            death_cause = "self_collision"
            events.append(
                _event(
                    "died",
                    position=game.snake.peek_next_head(),
                    death_cause=death_cause,
                )
            )

    # Non-food steps advance starvation unless Last Stand just reset hunger.
    starvation_ticks_elapsed = initial["starvation_ticks_elapsed"]
    revived_this_step = any(
        event.get("kind") == "power_consumed" and event.get("power") == "last_stand" for event in events
    )
    if alive and not revived_this_step:
        starvation_ticks_elapsed += 1
        if starvation_ticks_elapsed >= STARVATION_TICKS:
            if game.last_stand_held:
                _apply_revive(game, events, death_cause="starvation")
                starvation_ticks_elapsed = 0
            else:
                alive = False
                death_cause = "starvation"
                events.append(
                    _event(
                        "died",
                        position=game.snake.get_head(),
                        death_cause=death_cause,
                    )
                )
    elif revived_this_step:
        starvation_ticks_elapsed = 0

    pickup = next(iter(game.powerups.collectible_powerups()), None)
    recovery_ticks = round(game.revival_invulnerability_timer / STEP_SECONDS)
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
            "last_stand_held": game.last_stand_held,
            "recovery_ticks_remaining": recovery_ticks,
            "events": events,
        },
    }


def _apply_revive(
    game: _LastStandReferenceGame,
    events: list[dict[str, Any]],
    *,
    death_cause: str,
) -> None:
    consumed = game.powerups.consume(LastStandPowerUp, game)
    if not consumed and game.last_stand_held:
        game.last_stand_held = False
    target_length = max(1, (len(game.snake.body) + 1) // 2)
    while len(game.snake.body) > target_length:
        game.snake.body.popleft()
    game.snake.positions_set = set(game.snake.body)
    game.revival_invulnerability_timer = LAST_STAND_RECOVERY_TICKS * STEP_SECONDS
    game.last_stand_held = False
    events.append(_power_event("power_consumed"))
    events.append(
        _power_event(
            "collision_prevented",
            position=game.snake.get_head() if death_cause == "starvation" else game.snake.peek_next_head(),
            death_cause=death_cause,
        )
    )
    events.append(_event("hunger_reset", value=STARVATION_TICKS))
    events.append(_power_event("power_activated", value=LAST_STAND_RECOVERY_TICKS))


def _install_last_stand_state(
    game: _LastStandReferenceGame,
    initial: dict[str, Any],
) -> LastStandPowerUp | None:
    pickup = initial["pickup"]
    last_stand_held = initial["last_stand_held"]
    recovery_ticks = initial["recovery_ticks_remaining"]
    if pickup is not None and last_stand_held:
        raise AssertionError("a Last Stand pickup cannot coexist with a held Last Stand")
    if pickup is not None:
        power = LastStandPowerUp(tuple(pickup["position"]))
        power.visible_timer = power.visible_duration - (pickup["visibility_ticks_remaining"] * STEP_SECONDS)
        game.powerups.active_powerups.append(power)
        return power
    if last_stand_held:
        power = LastStandPowerUp((0, 0))
        game.powerups.active_powerups.append(power)
        power.activate(game)
        return power
    if recovery_ticks > 0:
        game.revival_invulnerability_timer = recovery_ticks * STEP_SECONDS
    return None


def _normalize_pickup(powerup: LastStandPowerUp | None) -> dict[str, Any] | None:
    if powerup is None:
        return None
    return {
        "kind": "last_stand",
        "position": list(powerup.position),
        "visibility_ticks_remaining": round((powerup.visible_duration - powerup.visible_timer) / STEP_SECONDS),
    }


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
    event["power"] = "last_stand"
    return event


if __name__ == "__main__":
    raise SystemExit(main())
