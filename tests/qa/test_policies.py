"""Tests for automated gameplay policies."""

import random

import pytest

from vibesnake.core.enums import Direction
from vibesnake.qa.policies import get_policy, safe_directions
from vibesnake.qa.simulation import CoreSimulation


def test_safe_directions_exclude_an_immediate_reversal():
    random.seed(20)
    simulation = CoreSimulation()

    choices = safe_directions(simulation)

    assert Direction.LEFT not in choices
    assert Direction.RIGHT in choices


def test_every_public_policy_returns_directions():
    random.seed(21)
    simulation = CoreSimulation()
    policy_rng = random.Random(21)

    for name in ("food-seeking", "survival", "input-chaos"):
        commands = get_policy(name)(simulation, policy_rng)
        assert all(isinstance(command, Direction) for command in commands)


def test_unknown_policy_is_rejected():
    with pytest.raises(ValueError, match="unknown QA policy"):
        get_policy("not-a-policy")
