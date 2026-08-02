"""Tests for the reference core simulation adapter."""

import random

import pytest

from vibesnake.core.enums import Direction
from vibesnake.qa.simulation import CoreSimulation


def test_step_seconds_must_be_positive():
    with pytest.raises(ValueError, match="greater than zero"):
        CoreSimulation(step_seconds=0)


def test_finished_simulation_cannot_advance():
    random.seed(60)
    simulation = CoreSimulation(step_seconds=30.0)
    record = simulation.step(())

    assert not record.alive
    assert record.death_cause == "starvation"
    with pytest.raises(RuntimeError, match="finished simulation"):
        simulation.step(())


def test_food_is_collected_on_entry_not_one_step_late():
    random.seed(61)
    simulation = CoreSimulation()
    next_head = simulation.snake.peek_next_head()
    simulation.food.position = next_head

    record = simulation.step(())

    assert record.ate_food
    assert record.head == next_head
    assert record.length == 2
    assert record.food_eaten == 1
    assert record.score > 0
    assert record.direction == Direction.RIGHT.name


def test_food_on_exact_starvation_deadline_rescues_the_run():
    random.seed(62)
    simulation = CoreSimulation()
    simulation.starvation_seconds = simulation.starvation_limit_seconds - simulation.step_seconds
    simulation.food.position = simulation.snake.peek_next_head()

    record = simulation.step(())

    assert record.alive
    assert record.ate_food
    assert record.death_cause is None
    assert simulation.starvation_seconds == 0.0
    assert [event.kind for event in record.events] == [
        "moved",
        "ate_food",
        "score_changed",
        "hunger_reset",
    ]


def test_starvation_deadline_moves_before_death():
    random.seed(63)
    simulation = CoreSimulation()
    simulation.starvation_seconds = simulation.starvation_limit_seconds - simulation.step_seconds
    simulation.food.position = (0, 0)
    starting_head = simulation.snake.get_head()

    record = simulation.step(())

    assert not record.alive
    assert record.head != starting_head
    assert record.death_cause == "starvation"
    assert [event.kind for event in record.events] == ["moved", "died"]
