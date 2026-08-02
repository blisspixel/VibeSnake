"""Behavioral tests for achievement evaluation and persistence."""

import pytest

from vibesnake.core.achievements import ACHIEVEMENTS, AchievementManager


def test_all_gameplay_achievements_unlock_and_persist():
    manager = AchievementManager()
    manager.check_all_achievements(
        score=1000,
        combo=10,
        length=35,
        time=300,
        games_played=100,
        near_misses=10,
        powerups_collected=5,
        food_eaten=5,
        wraps=3,
        hour=1,
    )
    assert len(manager.get_pending_notifications()) == 24
    assert manager.get_pending_notifications() == []
    assert not manager.check_achievement("first_bite", score=1000)
    assert not manager.check_achievement("missing", score=1000)

    assert manager.check_achievement("early_bird", hour=4)
    progress = manager.get_progress()
    assert progress["total"] == 25
    assert progress["unlocked"] == 25
    assert progress["percentage"] == 100
    assert sum(progress["by_rarity"].values()) == 25
    assert sum(progress["unlocked_by_rarity"].values()) == 25

    assert len(manager.get_achievement_list()) == 25
    assert len(manager.get_achievement_list(True)) == 25
    assert manager.get_achievement_list(False) == []

    state = manager.save_state()
    restored = AchievementManager()
    restored.load_state({**state, "removed_achievement": {"unlocked": True}})
    assert restored.get_progress()["unlocked"] == 25


def test_locked_achievement_does_not_notify():
    manager = AchievementManager()
    assert not manager.check_achievement("legend", score=999)
    assert manager.get_pending_notifications() == []
    assert len(manager.get_achievement_list(False)) == 25


@pytest.mark.parametrize(
    ("hour", "night_owl_unlocked", "early_bird_unlocked"),
    [
        (0, True, False),
        (2, True, False),
        (3, False, True),
        (5, False, True),
        (6, False, False),
    ],
)
def test_early_morning_achievements_are_adjacent_and_exclusive(hour, night_owl_unlocked, early_bird_unlocked):
    night_owl = AchievementManager()
    early_bird = AchievementManager()

    assert night_owl.check_achievement("night_owl", hour=hour) is night_owl_unlocked
    assert early_bird.check_achievement("early_bird", hour=hour) is early_bird_unlocked


def test_time_of_day_achievement_contracts_describe_distinct_ranges():
    assert ACHIEVEMENTS["night_owl"].unlock_condition == "hour >= 0 and hour < 3"
    assert ACHIEVEMENTS["early_bird"].unlock_condition == "hour >= 3 and hour < 6"
