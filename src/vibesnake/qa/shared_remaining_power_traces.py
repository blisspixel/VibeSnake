"""Generate targeted remaining-power traces consumed by the native parity suite.

Covers Slow-Mo, Boost, Magnet, Bait, Gluttony, and Segment Detach contracts
that are not already covered by Shield, Phase Shift, or Last Stand fixtures.
"""

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
from vibesnake.powerups.bait import BaitPowerUp
from vibesnake.powerups.base import PowerUp
from vibesnake.powerups.boost import BoostPowerUp
from vibesnake.powerups.gluttony import GluttonyPowerUp
from vibesnake.powerups.magnet import MagnetPowerUp
from vibesnake.powerups.manager import PowerUpManager
from vibesnake.powerups.segmentdetach import SegmentDetachPowerUp
from vibesnake.powerups.slowmo import SlowMoPowerUp
from vibesnake.qa.contracts import CURRENT_RULESET
from vibesnake.utils.logger import temporary_logger_level

REMAINING_TRACE_SCHEMA_VERSION = 1
REMAINING_TRACE_CONTRACT = "remaining-powers-rules-targeted-v1"
REMAINING_RANDOMNESS_POLICY = "positions-and-power-state-injected-v1"
DEFAULT_REMAINING_FIXTURE_PATH = Path("tests/fixtures/shared/remaining_powers_rules_v1.json")
STEP_SECONDS = settings.LOGIC_TICK
POWER_VISIBLE_TICKS = round(6.0 / STEP_SECONDS)
STARVATION_TICKS = round(30.0 / STEP_SECONDS)
SLOW_MO_DURATION_TICKS = round(6.0 / STEP_SECONDS)
BOOST_DURATION_TICKS = round(4.0 / STEP_SECONDS)
MAGNET_DURATION_TICKS = round(6.0 / STEP_SECONDS)
GLUTTONY_DURATION_TICKS = round(5.0 / STEP_SECONDS)
SEGMENT_DETACH_OBSTACLE_TICKS = round(10.0 / STEP_SECONDS)

_POWER_CLASSES: dict[str, type[PowerUp]] = {
    "slow_mo": SlowMoPowerUp,
    "boost": BoostPowerUp,
    "magnet": MagnetPowerUp,
    "bait": BaitPowerUp,
    "gluttony": GluttonyPowerUp,
    "segment_detach": SegmentDetachPowerUp,
}

_DURATION_TICKS: dict[str, int] = {
    "slow_mo": SLOW_MO_DURATION_TICKS,
    "boost": BOOST_DURATION_TICKS,
    "magnet": MAGNET_DURATION_TICKS,
    "gluttony": GLUTTONY_DURATION_TICKS,
}


class _RemainingPowerReferenceGame:
    """Minimal state surface used by production remaining-power code."""

    def __init__(
        self,
        body: list[tuple[int, int]],
        direction: Direction,
        food: tuple[int, int] | None,
    ) -> None:
        self.snake = Snake()
        self.snake.body = deque(body)
        self.snake.positions_set = set(body)
        self.snake.direction = direction
        self.snake.next_directions.clear()
        self.powerups = PowerUpManager()
        self.food_position = food
        self.magnet_active = False
        self.snake_gluttony_active = False
        self.bait_position: tuple[int, int] | None = None
        self.detached_segments: list[tuple[int, int]] = []
        self.detached_segments_timer = 0.0
        self.logic_tick_override: float | None = None
        self._logic_tick_factors: dict[str, float] = {}
        self.session_powerups_collected = 0
        self.sound_on = False
        self.volume = 0.0


def build_remaining_power_fixture() -> dict[str, Any]:
    """Return deterministic one-step traces for the remaining power contracts."""
    long_body = [(0, 1), (1, 1), (2, 1), (3, 1), (4, 1), (5, 1)]
    specifications = [
        _spec(
            "slow-mo-collect-on-entry",
            [(5, 5)],
            "RIGHT",
            pickup_kind="slow_mo",
            pickup_position=(6, 5),
            pickup_visibility_ticks=10,
        ),
        _spec(
            "boost-collect-on-entry",
            [(5, 5)],
            "RIGHT",
            pickup_kind="boost",
            pickup_position=(6, 5),
            pickup_visibility_ticks=10,
        ),
        _spec(
            "magnet-collect-on-entry",
            [(5, 5)],
            "RIGHT",
            pickup_kind="magnet",
            pickup_position=(6, 5),
            pickup_visibility_ticks=10,
        ),
        _spec(
            "magnet-pull-food-toward-head",
            [(2, 2)],
            "RIGHT",
            food=(6, 5),
            magnet_ticks_remaining=3,
        ),
        _spec(
            "gluttony-collect-on-entry",
            [(5, 5)],
            "RIGHT",
            pickup_kind="gluttony",
            pickup_position=(6, 5),
            pickup_visibility_ticks=10,
        ),
        _spec(
            "gluttony-eat-without-growth",
            [(1, 1), (2, 1)],
            "RIGHT",
            food=(3, 1),
            gluttony_ticks_remaining=3,
            skip_food_after_eat=True,
        ),
        _spec(
            "bait-collect-on-entry",
            [(5, 5)],
            "RIGHT",
            pickup_kind="bait",
            pickup_position=(6, 5),
            pickup_visibility_ticks=10,
        ),
        _spec(
            "segment-detach-on-entry",
            long_body,
            "RIGHT",
            pickup_kind="segment_detach",
            pickup_position=(6, 1),
            pickup_visibility_ticks=10,
            food=(20, 20),
        ),
        _spec(
            "tempo-compose-active-countdown",
            [(5, 5)],
            "RIGHT",
            slow_mo_ticks_remaining=3,
            boost_ticks_remaining=2,
        ),
    ]
    previous_random_state = random.getstate()
    try:
        cases = [_execute_specification(specification) for specification in specifications]
    finally:
        random.setstate(previous_random_state)

    return {
        "schema_version": REMAINING_TRACE_SCHEMA_VERSION,
        "contract": REMAINING_TRACE_CONTRACT,
        "ruleset": CURRENT_RULESET.to_dict(),
        "randomness_policy": REMAINING_RANDOMNESS_POLICY,
        "source_engine": "python-production-remaining-powers-v1",
        "config": {
            "width": settings.GRID_WIDTH,
            "height": settings.GRID_HEIGHT,
            "starvation_ticks": STARVATION_TICKS,
            "power_visible_ticks": POWER_VISIBLE_TICKS,
            "slow_mo_duration_ticks": SLOW_MO_DURATION_TICKS,
            "boost_duration_ticks": BOOST_DURATION_TICKS,
            "magnet_duration_ticks": MAGNET_DURATION_TICKS,
            "gluttony_duration_ticks": GLUTTONY_DURATION_TICKS,
            "segment_detach_obstacle_ticks": SEGMENT_DETACH_OBSTACLE_TICKS,
            "segment_detach_max_segments": 5,
        },
        "case_count": len(cases),
        "comparison_scope": [
            "pickup_identity",
            "collection_on_entry",
            "activation",
            "duration_countdown",
            "magnet_pull",
            "gluttony_no_growth",
            "bait_mark",
            "segment_detach_obstacles",
            "tempo_compose",
            "ordered_power_events",
        ],
        "excluded_scope": [
            "random_spawn_position",
            "spawn_schedule",
            "presentation_feedback",
            "food_respawn_position_after_eat",
            "shield_phase_last_stand",
        ],
        "cases": cases,
    }


def fixture_json(fixture: dict[str, Any]) -> str:
    """Serialize a remaining-power fixture canonically."""
    return json.dumps(fixture, separators=(",", ":"), sort_keys=True) + "\n"


def check_fixture(path: Path, fixture: dict[str, Any]) -> bool:
    """Return whether the checked-in remaining-power fixture is current."""
    return path.is_file() and path.read_text(encoding="utf-8") == fixture_json(fixture)


def main(argv: list[str] | None = None) -> int:
    """Write or verify the targeted remaining-power fixture."""
    with temporary_logger_level("vibesnake", logging.WARNING):
        parser = argparse.ArgumentParser(
            prog="python -m vibesnake.qa.shared_remaining_power_traces",
            description="Generate production Python remaining-power traces for native tests.",
        )
        parser.add_argument("--output", type=Path, default=DEFAULT_REMAINING_FIXTURE_PATH)
        parser.add_argument("--check", action="store_true")
        arguments = parser.parse_args(argv)
        fixture = build_remaining_power_fixture()

        if arguments.check:
            if check_fixture(arguments.output, fixture):
                print(f"Shared remaining-power fixture passed: {fixture['case_count']} targeted cases")
                return 0
            print(f"Shared remaining-power fixture is missing or stale: {arguments.output}")
            return 1

        arguments.output.parent.mkdir(parents=True, exist_ok=True)
        arguments.output.write_text(fixture_json(fixture), encoding="utf-8")
        print(
            f"Shared remaining-power fixture written: {fixture['case_count']} targeted cases; output={arguments.output}"
        )
        return 0


def _spec(
    case_id: str,
    body: list[tuple[int, int]],
    direction: str,
    *,
    food: tuple[int, int] = (20, 20),
    pickup_kind: str | None = None,
    pickup_position: tuple[int, int] | None = None,
    pickup_visibility_ticks: int = 0,
    slow_mo_ticks_remaining: int = 0,
    boost_ticks_remaining: int = 0,
    magnet_ticks_remaining: int = 0,
    gluttony_ticks_remaining: int = 0,
    bait_position: tuple[int, int] | None = None,
    skip_food_after_eat: bool = False,
) -> dict[str, Any]:
    return {
        "id": case_id,
        "skip_food_after_eat": skip_food_after_eat,
        "initial": {
            "body": [list(point) for point in body],
            "direction": direction,
            "food": list(food) if food is not None else None,
            "starvation_ticks_elapsed": 0,
            "pickup": (
                {
                    "kind": pickup_kind,
                    "position": list(pickup_position),
                    "visibility_ticks_remaining": pickup_visibility_ticks,
                }
                if pickup_kind is not None and pickup_position is not None
                else None
            ),
            "slow_mo_ticks_remaining": slow_mo_ticks_remaining,
            "boost_ticks_remaining": boost_ticks_remaining,
            "magnet_ticks_remaining": magnet_ticks_remaining,
            "gluttony_ticks_remaining": gluttony_ticks_remaining,
            "bait_position": list(bait_position) if bait_position is not None else None,
            "detached_obstacles": [],
            "detached_obstacle_ticks_remaining": 0,
        },
    }


def _execute_specification(specification: dict[str, Any]) -> dict[str, Any]:
    initial = specification["initial"]
    body = [tuple(point) for point in initial["body"]]
    food = tuple(initial["food"]) if initial["food"] is not None else None
    game = _RemainingPowerReferenceGame(body, Direction[initial["direction"]], food)
    installed = _install_power_state(game, initial)
    events: list[dict[str, Any]] = []

    was_pickup = installed is not None and installed.active and not installed.activated
    game.powerups.update(STEP_SECONDS, game)
    if installed is not None and not installed.active and was_pickup:
        events.append(
            _power_event(
                "power_expired",
                power=_kind_for_instance(installed),
                position=installed.position,
            )
        )

    if game.magnet_active and game.food_position is not None:
        head = game.snake.get_head()
        if game.food_position != head:
            fx, fy = game.food_position
            sx, sy = head
            dx = 0 if fx == sx else (1 if sx > fx else -1)
            dy = 0 if fy == sy else (1 if sy > fy else -1)
            candidate = (fx + dx, fy + dy)
            blocked = game.snake.positions_set | set(game.detached_segments) | game.powerups.collectible_positions()
            if candidate not in blocked:
                game.food_position = candidate

    ate_food = game.food_position is not None and game.snake.peek_next_head() == game.food_position
    grow = ate_food and not game.snake_gluttony_active
    alive, wrapped = game.snake.move(grow=grow)
    death_cause: str | None = None

    if alive and game.snake.get_head() in game.detached_segments:
        alive = False

    if alive:
        events.append(_event("moved", position=game.snake.get_head()))
        if wrapped:
            events.append(_event("wrapped", position=game.snake.get_head()))
        collected = game.powerups.collect_at(game.snake.get_head(), game)
        if collected is not None:
            kind = _kind_for_instance(collected)
            events.append(_power_event("power_collected", power=kind, position=collected.position))
            activation_value = _activation_value(kind, game, collected)
            events.append(
                _power_event(
                    "power_activated",
                    power=kind,
                    value=activation_value,
                    position=game.snake.get_head() if kind == "bait" else None,
                )
            )
        if ate_food:
            events.append(_event("ate_food", position=game.snake.get_head()))
            # Match native scoring on post-move length (head already appended).
            awarded = _food_points(
                snake_length=len(game.snake.body),
                next_combo_count=1,
                ticks_since_last_food=0,
            )
            events.append(_event("score_changed", value=awarded))
            events.append(_event("hunger_reset", value=STARVATION_TICKS))
            # Respawn coordinates are non-deterministic across engines.
            if specification.get("skip_food_after_eat", False):
                game.food_position = None
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

    starvation_ticks_elapsed = initial["starvation_ticks_elapsed"] + (0 if ate_food and alive else 1)

    expected: dict[str, Any] = {
        "tick": 1,
        "head": list(game.snake.get_head()),
        "body": [list(point) for point in game.snake.body],
        "alive": alive,
        "death_cause": death_cause,
        "starvation_ticks_elapsed": starvation_ticks_elapsed,
        "pickup": _normalize_pickup(next(iter(game.powerups.collectible_powerups()), None)),
        "food": list(game.food_position) if game.food_position is not None else None,
        "slow_mo_ticks_remaining": _remaining_ticks(game, SlowMoPowerUp, initial["slow_mo_ticks_remaining"]),
        "boost_ticks_remaining": _remaining_ticks(game, BoostPowerUp, initial["boost_ticks_remaining"]),
        "magnet_ticks_remaining": _remaining_ticks(game, MagnetPowerUp, initial["magnet_ticks_remaining"]),
        "gluttony_ticks_remaining": _remaining_ticks(game, GluttonyPowerUp, initial["gluttony_ticks_remaining"]),
        "bait_position": list(game.bait_position) if game.bait_position is not None else None,
        "detached_obstacles": [list(point) for point in game.detached_segments],
        "detached_obstacle_ticks_remaining": (
            round(game.detached_segments_timer / STEP_SECONDS) if game.detached_segments else 0
        ),
        "movement_cadence_numerator": 2
        if _has_active(game, SlowMoPowerUp) or initial["slow_mo_ticks_remaining"] > 1
        else 1,
        "movement_cadence_denominator": 2
        if _has_active(game, BoostPowerUp) or initial["boost_ticks_remaining"] > 1
        else 1,
        "events": events,
        "skip_food": bool(specification.get("skip_food_after_eat", False)),
    }

    # Correct cadence for active timers after countdown (post-step remaining).
    expected["movement_cadence_numerator"] = 2 if expected["slow_mo_ticks_remaining"] > 0 else 1
    expected["movement_cadence_denominator"] = 2 if expected["boost_ticks_remaining"] > 0 else 1

    return {**specification, "expected": expected}


def _install_power_state(
    game: _RemainingPowerReferenceGame,
    initial: dict[str, Any],
) -> PowerUp | None:
    if initial["bait_position"] is not None:
        game.bait_position = tuple(initial["bait_position"])

    pickup = initial["pickup"]
    if pickup is not None:
        kind = pickup["kind"]
        power_cls = _POWER_CLASSES[kind]
        power = power_cls(tuple(pickup["position"]))
        power.visible_duration = POWER_VISIBLE_TICKS * STEP_SECONDS
        power.visible_timer = power.visible_duration - (pickup["visibility_ticks_remaining"] * STEP_SECONDS)
        game.powerups.active_powerups.append(power)
        return power

    installed: PowerUp | None = None
    if initial["slow_mo_ticks_remaining"] > 0:
        installed = _activate_timed(game, SlowMoPowerUp, initial["slow_mo_ticks_remaining"])
    if initial["boost_ticks_remaining"] > 0:
        installed = _activate_timed(game, BoostPowerUp, initial["boost_ticks_remaining"]) or installed
    if initial["magnet_ticks_remaining"] > 0:
        installed = _activate_timed(game, MagnetPowerUp, initial["magnet_ticks_remaining"]) or installed
        game.magnet_active = True
    if initial["gluttony_ticks_remaining"] > 0:
        installed = _activate_timed(game, GluttonyPowerUp, initial["gluttony_ticks_remaining"]) or installed
        game.snake_gluttony_active = True
    return installed


def _activate_timed(
    game: _RemainingPowerReferenceGame,
    power_cls: type[PowerUp],
    ticks_remaining: int,
) -> PowerUp:
    power = power_cls((0, 0))
    game.powerups.active_powerups.append(power)
    power.activate(game)
    power.timer = power.duration - (ticks_remaining * STEP_SECONDS)
    return power


def _remaining_ticks(
    game: _RemainingPowerReferenceGame,
    power_cls: type[PowerUp],
    initial_ticks: int,
) -> int:
    active = next(
        (
            powerup
            for powerup in game.powerups.active_powerups
            if isinstance(powerup, power_cls) and powerup.active and powerup.activated
        ),
        None,
    )
    if active is None:
        # Instant effects and cleared flags.
        if power_cls is MagnetPowerUp and not game.magnet_active:
            return 0
        if power_cls is GluttonyPowerUp and not game.snake_gluttony_active:
            return 0
        if initial_ticks <= 0:
            return 0
        return max(0, initial_ticks - 1)
    return round((active.duration - active.timer) / STEP_SECONDS)


def _has_active(game: _RemainingPowerReferenceGame, power_cls: type[PowerUp]) -> bool:
    return any(
        isinstance(powerup, power_cls) and powerup.active and powerup.activated
        for powerup in game.powerups.active_powerups
    )


def _activation_value(kind: str, game: _RemainingPowerReferenceGame, power: PowerUp) -> int:
    if kind in _DURATION_TICKS:
        return _DURATION_TICKS[kind]
    if kind == "bait":
        return 0
    if kind == "segment_detach":
        return len(getattr(power, "detached_segments", []) or game.detached_segments)
    return 0


def _kind_for_instance(power: PowerUp) -> str:
    for kind, cls in _POWER_CLASSES.items():
        if isinstance(power, cls):
            return kind
    raise TypeError(f"Unsupported power type: {type(power)!r}")


def _normalize_pickup(powerup: PowerUp | None) -> dict[str, Any] | None:
    if powerup is None:
        return None
    return {
        "kind": _kind_for_instance(powerup),
        "position": list(powerup.position),
        "visibility_ticks_remaining": round((powerup.visible_duration - powerup.visible_timer) / STEP_SECONDS),
    }


def _food_points(
    *,
    snake_length: int,
    next_combo_count: int,
    ticks_since_last_food: int,
    food_score: int = 10,
    speed_bonus_ticks: int = 30,
) -> int:
    """Mirror native CalculateFoodPoints for injected parity cases."""
    thresholds = ((0, 1.0), (3, 2.0), (5, 3.0), (10, 5.0), (20, 10.0))
    multiplier = thresholds[-1][1]
    for index in range(len(thresholds) - 1):
        lower = thresholds[index]
        upper = thresholds[index + 1]
        if lower[0] <= next_combo_count < upper[0]:
            progress = (next_combo_count - lower[0]) / (upper[0] - lower[0])
            multiplier = lower[1] + ((upper[1] - lower[1]) * progress)
            break
    points = int(food_score * multiplier)
    if ticks_since_last_food < speed_bonus_ticks:
        points += int(food_score * 0.5)
    if snake_length > 10:
        import math

        points += int((snake_length - 10) * math.log(snake_length) / 2.0)
    return points


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
    power: str,
    position: tuple[int, int] | None = None,
    value: int | None = None,
    death_cause: str | None = None,
) -> dict[str, Any]:
    event = _event(kind, position=position, value=value, death_cause=death_cause)
    event["power"] = power
    return event


if __name__ == "__main__":
    raise SystemExit(main())
