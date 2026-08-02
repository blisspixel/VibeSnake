"""Behavioral contracts for risk-reward event detection."""

from dataclasses import FrozenInstanceError

import pytest

from vibesnake.core.near_miss import NearMissDetector, NearMissEvent
from vibesnake.data import settings


@pytest.mark.parametrize(
    ("adjacent_count", "event_type", "bonus", "message", "is_warning"),
    [
        (0, None, None, None, None),
        (1, None, None, None, None),
        (2, "danger_warning", 0, "", True),
        (3, "near_miss", 1, "CLOSE CALL!", False),
        (4, "near_miss", 2, "THREADING THE NEEDLE!", False),
    ],
)
def test_body_proximity_has_distinct_warning_and_reward_tiers(adjacent_count, event_type, bonus, message, is_warning):
    detector = NearMissDetector()
    head = (10, 10)
    adjacent_cells = [(11, 10), (10, 11), (9, 10), (10, 9)]

    event = detector.check_near_miss(head, set(adjacent_cells[:adjacent_count]), snake_length=10)

    if event_type is None:
        assert event is None
        return
    assert event is not None
    assert (event.type, event.score_bonus, event.message, event.is_warning) == (
        event_type,
        bonus,
        message,
        is_warning,
    )


def test_body_proximity_requires_a_long_enough_snake():
    detector = NearMissDetector()
    body = {(11, 10), (10, 11), (9, 10), (10, 9)}

    assert detector.check_near_miss((10, 10), body, snake_length=7) is None


def test_reward_cooldown_blocks_events_but_not_warnings():
    detector = NearMissDetector()
    reward_body = {(11, 10), (10, 11), (9, 10)}
    warning_body = {(11, 10), (10, 11)}

    assert detector.check_near_miss((10, 10), reward_body, 10) is not None
    assert detector.check_near_miss((10, 10), reward_body, 10) is None
    assert detector.check_near_miss((10, 10), warning_body, 10).is_warning

    detector.update(detector.near_miss_cooldown)
    assert detector.check_near_miss((10, 10), reward_body, 10) is not None


@pytest.mark.parametrize(
    ("snake_length", "expected_bonus", "expected_message"),
    [
        (1, 1, "EDGE RIDE"),
        (20, 2, "EDGE RIDE"),
        (50, 5, "EDGE LORD!"),
        (80, 8, "EDGE MASTERY!"),
        (100, 10, "EDGE MASTERY!"),
        (200, 10, "EDGE MASTERY!"),
    ],
)
def test_edge_ride_reward_scales_and_caps(snake_length, expected_bonus, expected_message):
    event = NearMissDetector().check_edge_ride((0, 15), (0, 1), snake_length)

    assert event is not None
    assert (event.type, event.score_bonus, event.message) == (
        "edge_ride",
        expected_bonus,
        expected_message,
    )


@pytest.mark.parametrize(
    ("head", "direction"),
    [
        ((0, 15), (0, 1)),
        ((settings.GRID_WIDTH - 1, 15), (0, -1)),
        ((32, 0), (1, 0)),
        ((32, settings.GRID_HEIGHT - 1), (-1, 0)),
    ],
)
def test_edge_ride_detects_parallel_motion_on_every_edge(head, direction):
    assert NearMissDetector().check_edge_ride(head, direction, 50) is not None


@pytest.mark.parametrize(
    ("head", "direction"),
    [
        ((32, 16), (0, 1)),
        ((1, 16), (0, 1)),
        ((0, 15), (1, 0)),
        ((32, 0), (0, 1)),
    ],
)
def test_edge_ride_ignores_non_edges_and_perpendicular_motion(head, direction):
    assert NearMissDetector().check_edge_ride(head, direction, 50) is None


@pytest.mark.parametrize(
    ("time_remaining", "expected"),
    [(2.0, False), (1.5, False), (1.49, True), (1.0, True), (0.0, True)],
)
def test_clutch_eat_uses_strict_remaining_time_boundary(time_remaining, expected):
    starvation_max = 30.0
    event = NearMissDetector().check_clutch_eat(starvation_max - time_remaining, starvation_max)

    assert (event is not None) is expected
    if event is not None:
        assert (event.type, event.score_bonus, event.message) == ("clutch_eat", 1, "CLUTCH!")


def test_style_points_only_reward_active_boost():
    detector = NearMissDetector()

    assert detector.check_style_points(False) is None
    assert detector.check_style_points(True).type == "style_points"


def test_recent_events_drive_bounded_combo_and_expire():
    detector = NearMissDetector()
    events = [NearMissEvent("near_miss", (index, index), 1, "CLOSE CALL!", (255, 200, 0)) for index in range(4)]

    assert detector.get_combo_multiplier() == 1.0
    detector.add_event(events[0])
    assert detector.get_combo_multiplier() == 1.0
    detector.add_event(events[1])
    assert detector.get_combo_multiplier() == 1.5
    detector.add_event(events[2])
    detector.add_event(events[3])
    assert detector.get_combo_multiplier() == 2.0

    detector.update(detector.event_timeout)
    assert detector.recent_events == []
    assert detector.get_combo_multiplier() == 1.0


def test_near_miss_events_are_immutable_values():
    event = NearMissEvent("near_miss", (10, 10), 1, "CLOSE CALL!", (255, 200, 0))

    with pytest.raises(FrozenInstanceError):
        event.score_bonus = 2
