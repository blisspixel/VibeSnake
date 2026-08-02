"""Deterministic automated player policies for QA, not shipped opponents."""

from __future__ import annotations

import random
from collections.abc import Callable

from vibesnake.core.enums import Direction
from vibesnake.data import settings
from vibesnake.qa.simulation import CoreSimulation


Policy = Callable[[CoreSimulation, random.Random], tuple[Direction, ...]]
POLICY_NAMES = ("food-seeking", "survival", "input-chaos")
_DIRECTION_ORDER = (
    Direction.UP,
    Direction.RIGHT,
    Direction.DOWN,
    Direction.LEFT,
)


def get_policy(name: str) -> Policy:
    """Resolve a public policy name."""
    policies: dict[str, Policy] = {
        "food-seeking": food_seeking_policy,
        "survival": survival_policy,
        "input-chaos": input_chaos_policy,
    }
    try:
        return policies[name]
    except KeyError as error:
        raise ValueError(f"unknown QA policy: {name}") from error


def safe_directions(simulation: CoreSimulation) -> tuple[Direction, ...]:
    """Return legal directions that do not immediately hit the body."""
    current = simulation.snake.direction
    head_x, head_y = simulation.snake.get_head()
    grow_pending = simulation.food.position == simulation.snake.get_head()
    safe: list[Direction] = []

    for direction in _DIRECTION_ORDER:
        if direction == Direction.opposite(current):
            continue
        dx, dy = direction.vector()
        new_head = (
            (head_x + dx) % settings.GRID_WIDTH,
            (head_y + dy) % settings.GRID_HEIGHT,
        )
        moving_onto_tail = not grow_pending and new_head == simulation.snake.body[0]
        if new_head not in simulation.snake.positions_set or moving_onto_tail:
            safe.append(direction)

    return tuple(safe)


def food_seeking_policy(
    simulation: CoreSimulation,
    policy_rng: random.Random,
) -> tuple[Direction, ...]:
    """Choose a safe move with the shortest wrapped distance to food."""
    candidates = safe_directions(simulation)
    if not candidates:
        return (simulation.snake.direction,)
    if simulation.food.position is None:
        return (policy_rng.choice(candidates),)

    head_x, head_y = simulation.snake.get_head()
    food_x, food_y = simulation.food.position

    def distance(direction: Direction) -> int:
        dx, dy = direction.vector()
        next_x = (head_x + dx) % settings.GRID_WIDTH
        next_y = (head_y + dy) % settings.GRID_HEIGHT
        delta_x = abs(next_x - food_x)
        delta_y = abs(next_y - food_y)
        return min(delta_x, settings.GRID_WIDTH - delta_x) + min(
            delta_y,
            settings.GRID_HEIGHT - delta_y,
        )

    best_distance = min(distance(direction) for direction in candidates)
    best = tuple(direction for direction in candidates if distance(direction) == best_distance)
    return (policy_rng.choice(best),)


def survival_policy(
    simulation: CoreSimulation,
    policy_rng: random.Random,
) -> tuple[Direction, ...]:
    """Choose any immediately safe direction to explore survival paths."""
    candidates = safe_directions(simulation)
    if not candidates:
        return (simulation.snake.direction,)
    return (policy_rng.choice(candidates),)


def input_chaos_policy(
    simulation: CoreSimulation,
    policy_rng: random.Random,
) -> tuple[Direction, ...]:
    """Generate bursts containing duplicates and illegal reversals."""
    command_count = policy_rng.randint(0, 4)
    return tuple(policy_rng.choice(_DIRECTION_ORDER) for _ in range(command_count))
