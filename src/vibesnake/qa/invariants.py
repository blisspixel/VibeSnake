"""Per-step invariants for automated gameplay campaigns."""

from __future__ import annotations

import math

from vibesnake.core.enums import Direction
from vibesnake.data import settings
from vibesnake.qa.models import InvariantFailure, StepRecord
from vibesnake.qa.simulation import CoreSimulation


def check_invariants(
    simulation: CoreSimulation,
    record: StepRecord | None = None,
    previous: StepRecord | None = None,
) -> list[InvariantFailure]:
    """Return every violated reference-core contract."""
    step = simulation.step_count
    failures: list[InvariantFailure] = []

    def fail(code: str, message: str) -> None:
        failures.append(InvariantFailure(code=code, message=message, step=step))

    body = list(simulation.snake.body)
    body_set = set(body)

    if not body:
        fail("snake.empty", "snake body must contain a head")
        return failures

    if body_set != simulation.snake.positions_set:
        fail("snake.index_desync", "positions_set must equal set(body)")

    if len(body_set) != len(body):
        fail("snake.overlap", "reference runs may not contain overlapping segments")

    for position in body:
        if not _valid_cell(position):
            fail("snake.out_of_bounds", f"snake cell is outside the grid: {position!r}")

    if not isinstance(simulation.snake.direction, Direction):
        fail("snake.invalid_direction", "snake direction must be a Direction")

    for queued in simulation.snake.next_directions:
        if not isinstance(queued, Direction):
            fail("input.invalid_queue_entry", "every queued input must be a Direction")
    if len(simulation.snake.next_directions) > simulation.snake.MAX_DIRECTION_QUEUE:
        fail("input.queue_overflow", "direction queue exceeded its bounded capacity")

    food = simulation.food.position
    if food is None:
        if len(body_set) < settings.GRID_WIDTH * settings.GRID_HEIGHT:
            fail("food.missing", "food may be absent only when the grid is full")
    elif not _valid_cell(food):
        fail("food.out_of_bounds", f"food is outside the grid: {food!r}")
    elif food in body_set and food != simulation.snake.get_head():
        fail("food.inside_body", "food may overlap only the head awaiting collection")

    if simulation.score.base_score < 0:
        fail("score.negative", "score must be nonnegative")
    if simulation.score.combo_count < 0:
        fail("score.negative_combo", "combo count must be nonnegative")
    if simulation.score.combo_count > simulation.food_eaten:
        fail("score.impossible_combo", "combo count cannot exceed collected food")
    if not math.isfinite(simulation.score.time_since_last_food):
        fail("score.invalid_timer", "combo timer must be finite")
    elif simulation.score.time_since_last_food < 0:
        fail("score.negative_timer", "combo timer must be nonnegative")

    if not math.isfinite(simulation.starvation_seconds):
        fail("starvation.invalid_timer", "starvation timer must be finite")
    elif simulation.starvation_seconds < 0:
        fail("starvation.negative_timer", "starvation timer must be nonnegative")

    expected_length = 1 + simulation.food_eaten
    if len(body) != expected_length:
        fail(
            "snake.growth_mismatch",
            f"length {len(body)} does not match food count {simulation.food_eaten}",
        )

    if previous is not None and record is not None:
        if record.score < previous.score:
            fail("score.decreased", "score must be monotonic within a run")
        previous_direction = Direction[previous.direction]
        current_direction = Direction[record.direction]
        if current_direction == Direction.opposite(previous_direction):
            fail("input.reversed", "direction changed by 180 degrees in one tick")

    if record is not None:
        if record.alive != simulation.alive:
            fail("record.alive_mismatch", "recorded alive state differs from simulation")
        if record.won != simulation.won:
            fail("record.won_mismatch", "recorded win state differs from simulation")
        if record.death_cause != simulation.death_cause:
            fail("record.death_mismatch", "recorded death cause differs from simulation")

    if simulation.won and (simulation.alive or simulation.death_cause is not None):
        fail("terminal.invalid_win", "a won run must be terminal without a death cause")

    return failures


def _valid_cell(position: object) -> bool:
    """Return whether an object is a valid integer grid coordinate."""
    if not isinstance(position, tuple) or len(position) != 2:
        return False
    x, y = position
    return (
        isinstance(x, int)
        and not isinstance(x, bool)
        and isinstance(y, int)
        and not isinstance(y, bool)
        and 0 <= x < settings.GRID_WIDTH
        and 0 <= y < settings.GRID_HEIGHT
    )
