"""Tests for gameplay QA invariants."""

import random

from hypothesis import given, settings as hypothesis_settings
from hypothesis import strategies as st

from vibesnake.core.enums import Direction
from vibesnake.qa.invariants import check_invariants
from vibesnake.qa.simulation import CoreSimulation


def test_initial_reference_state_satisfies_every_invariant():
    random.seed(10)
    simulation = CoreSimulation()

    assert check_invariants(simulation) == []


def test_body_index_desynchronization_is_reported():
    random.seed(11)
    simulation = CoreSimulation()
    simulation.snake.positions_set.clear()

    failures = check_invariants(simulation)

    assert {failure.code for failure in failures} >= {"snake.index_desync"}


def test_direction_queue_rejects_overflow_and_stale_reversals():
    random.seed(12)
    simulation = CoreSimulation()

    for command in (
        Direction.UP,
        Direction.DOWN,
        Direction.LEFT,
        Direction.DOWN,
        Direction.RIGHT,
        Direction.UP,
    ):
        simulation.snake.queue_direction(command)

    assert len(simulation.snake.next_directions) <= simulation.snake.MAX_DIRECTION_QUEUE
    queued = list(simulation.snake.next_directions)
    effective = simulation.snake.direction
    for command in queued:
        assert command != Direction.opposite(effective)
        effective = command


@hypothesis_settings(max_examples=40, deadline=None)
@given(
    seed=st.integers(min_value=0, max_value=2**32 - 1),
    commands=st.lists(st.sampled_from(tuple(Direction)), min_size=1, max_size=150),
)
def test_generated_input_sequences_preserve_core_invariants(seed, commands):
    previous_random_state = random.getstate()
    random.seed(seed)
    try:
        simulation = CoreSimulation()
        previous = None
        for command in commands:
            record = simulation.step((command,))
            assert check_invariants(simulation, record, previous) == []
            previous = record
            if not simulation.alive:
                break
    finally:
        random.setstate(previous_random_state)
