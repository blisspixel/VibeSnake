"""Tests for profile and leaderboard save data."""

import json

import pytest

from vibesnake.core.customization import CustomizationManager
from vibesnake.core.high_scores import HighScoreEntry, HighScoreTable
from vibesnake.core.player_profile import PlayerProfile
from vibesnake.core.user_settings import UserSettings
from vibesnake.data.json_store import atomic_write_json


def test_player_profile_round_trip_and_reset(tmp_path):
    profile = PlayerProfile(tmp_path)
    assert not profile.has_profile()
    assert profile.get_name() == "Anonymous"

    profile.create_profile("")
    assert profile.has_profile()
    assert profile.get_name() == "Anonymous"
    assert profile.created_date
    profile.update_last_played()
    profile.increment_games()
    profile.increment_apples_eaten()
    profile.increment_wall_rides()
    profile.update_score(100, 5)
    profile.update_score(50, 2)
    profile.update_achievement_state({"first_bite": {"unlocked": True, "unlock_time": 1.0}})

    restored = PlayerProfile(tmp_path)
    assert restored.total_games == 1
    assert restored.apples_eaten == 1
    assert restored.wall_rides == 1
    assert restored.highest_score == 100
    assert restored.highest_combo == 5
    assert restored.total_score == 150
    assert restored.achievement_state["first_bite"]["unlocked"]

    restored.reset_profile()
    assert not restored.profile_file.exists()
    assert restored.apples_eaten == 0
    assert restored.wall_rides == 0
    assert restored.achievement_state == {}


def test_profile_unlock_requirements_and_corrupt_save(tmp_path):
    profile = PlayerProfile(tmp_path)
    profile.apples_eaten = 10
    profile.wall_rides = 10
    profile.total_games = 10
    profile.highest_combo = 10
    profile.highest_score = 10

    assert profile.check_unlocked("legacy", 100)
    assert profile.check_unlocked("free", ("free", 0, "Free"))
    for requirement in (
        ("apples_eaten", 10, ""),
        ("wall_rides", 10, ""),
        ("games_played", 10, ""),
        ("highest_combo", 10, ""),
        ("highest_score", 10, ""),
    ):
        assert profile.check_unlocked("item", requirement)
    assert not profile.check_unlocked("item", ("highest_score", 11, ""))
    assert not profile.check_unlocked("unknown", ("future_stat", 999, ""))
    assert not profile.check_unlocked("unknown", object())
    assert not profile.check_unlocked("invalid", ("highest_score", True, ""))
    assert not profile.check_unlocked("invalid", ("highest_score", -1, ""))

    profile.profile_file.write_text("not json", encoding="utf-8")
    corrupt = PlayerProfile(tmp_path)
    assert not corrupt.has_profile()
    assert (tmp_path / "player_profile.json.corrupt.bak").exists()


def test_high_score_table_ranking_round_trip_and_clear(tmp_path):
    entry = HighScoreEntry("ADA", 10, "2026-01-01T00:00:00")
    assert HighScoreEntry.from_dict(entry.to_dict()).score == 10
    assert HighScoreEntry("BOB", 20).timestamp

    table = HighScoreTable(tmp_path)
    for index in range(10):
        table.add_score(f"P{index}", index * 10)
    assert len(table.get_top_scores()) == 10
    assert table.get_top_scores(2)[0].score == 90
    assert not table.is_high_score(0)
    assert table.is_high_score(95)
    assert table.get_rank(95) == 1
    assert table.get_rank(85) == 2
    assert table.get_rank(0) is None

    rank = table.add_score("WINNER", 100)
    assert rank == 1
    assert len(table.scores) == 10

    restored = HighScoreTable(tmp_path)
    assert restored.scores[0].name == "WINNER"
    restored.clear_scores()
    assert restored.get_top_scores() == []


def test_high_score_table_handles_corrupt_file(tmp_path):
    path = tmp_path / "high_scores.json"
    path.write_text("not json", encoding="utf-8")
    table = HighScoreTable(tmp_path)
    assert table.scores == []
    assert (tmp_path / "high_scores.json.corrupt.bak").exists()


def test_high_score_table_migrates_legacy_hud_file_once(tmp_path):
    legacy_file = tmp_path / "highscore.json"
    legacy_file.write_text(
        json.dumps(
            {
                "high_score": 250,
                "name": "ADA",
                "date": "2026-01-02T03:04:05",
            }
        ),
        encoding="utf-8",
    )

    table = HighScoreTable(tmp_path)

    assert [(entry.name, entry.score) for entry in table.scores] == [("ADA", 250)]
    payload = json.loads((tmp_path / "high_scores.json").read_text(encoding="utf-8"))
    assert payload["schema_version"] == HighScoreTable.SCHEMA_VERSION
    assert payload["migrations"]["legacy_highscore_json"]

    table.clear_scores()
    restored = HighScoreTable(tmp_path)
    assert restored.scores == []


def test_high_score_table_merges_legacy_score_with_existing_table(tmp_path):
    canonical = {
        "scores": [
            {"name": "BOB", "score": 100, "timestamp": "2026-01-01T00:00:00"},
        ],
    }
    (tmp_path / "high_scores.json").write_text(json.dumps(canonical), encoding="utf-8")
    (tmp_path / "highscore.json").write_text("150", encoding="utf-8")

    table = HighScoreTable(tmp_path)

    assert [(entry.name, entry.score) for entry in table.scores] == [
        ("Anonymous", 150),
        ("BOB", 100),
    ]


def test_unversioned_profile_is_validated_and_migrated(tmp_path):
    profile_file = tmp_path / "player_profile.json"
    profile_file.write_text(
        json.dumps(
            {
                "name": "ADA",
                "total_games": "7",
                "highest_score": -10,
                "achievements": [],
            }
        ),
        encoding="utf-8",
    )

    profile = PlayerProfile(tmp_path)

    assert profile.player_name == "ADA"
    assert profile.total_games == 7
    assert profile.highest_score == 0
    assert profile.achievement_state == {}
    payload = json.loads(profile_file.read_text(encoding="utf-8"))
    assert payload["schema_version"] == PlayerProfile.SCHEMA_VERSION


def test_unversioned_customization_is_migrated_and_bounded(tmp_path):
    customization_file = tmp_path / "customization.json"
    customization_file.write_text(
        json.dumps(
            {
                "current": {"base_color": [1, 2, 3], "future_field": "ignored"},
                "loadouts": [{"trail": "none"} for _ in range(7)],
            }
        ),
        encoding="utf-8",
    )

    manager = CustomizationManager(tmp_path)

    assert manager.current_customization.base_color == [1, 2, 3]
    assert len(manager.loadouts) == 5
    payload = json.loads(customization_file.read_text(encoding="utf-8"))
    assert payload["schema_version"] == CustomizationManager.SCHEMA_VERSION


def test_future_save_schemas_fall_back_without_overwriting(tmp_path):
    profile_file = tmp_path / "player_profile.json"
    customization_file = tmp_path / "customization.json"
    leaderboard_file = tmp_path / "high_scores.json"
    future_profile = {"schema_version": 999, "name": "FUTURE"}
    future_customization = {"schema_version": 999, "current": {"trail": "fire"}}
    future_leaderboard = {"schema_version": 999, "scores": [{"name": "FUTURE", "score": 999}]}
    profile_file.write_text(json.dumps(future_profile), encoding="utf-8")
    customization_file.write_text(json.dumps(future_customization), encoding="utf-8")
    leaderboard_file.write_text(json.dumps(future_leaderboard), encoding="utf-8")

    profile = PlayerProfile(tmp_path)
    customization = CustomizationManager(tmp_path)
    leaderboard = HighScoreTable(tmp_path)

    assert not profile.has_profile()
    assert customization.current_customization.trail == "none"
    assert leaderboard.scores == []
    profile.increment_games()
    customization.save_loadout(0)
    leaderboard.add_score("CURRENT", 1)
    assert json.loads(profile_file.read_text(encoding="utf-8")) == future_profile
    assert json.loads(customization_file.read_text(encoding="utf-8")) == future_customization
    assert json.loads(leaderboard_file.read_text(encoding="utf-8")) == future_leaderboard
    assert list(tmp_path.glob("*.corrupt*.bak")) == []


def test_corrupt_customization_is_backed_up_before_defaults(tmp_path):
    customization_file = tmp_path / "customization.json"
    customization_file.write_text("not json", encoding="utf-8")

    manager = CustomizationManager(tmp_path)

    assert manager.current_customization.trail == "none"
    assert manager.loadouts == []
    assert (tmp_path / "customization.json.corrupt.bak").read_text(encoding="utf-8") == "not json"


def test_atomic_json_write_preserves_previous_file_on_serialization_failure(tmp_path):
    save_file = tmp_path / "save.json"
    atomic_write_json(save_file, {"value": 1})

    with pytest.raises(TypeError):
        atomic_write_json(save_file, {"not_json": {1, 2, 3}})

    assert json.loads(save_file.read_text(encoding="utf-8")) == {"value": 1}
    assert list(tmp_path.glob("*.tmp")) == []


def test_user_preferences_round_trip_and_validate_values(tmp_path):
    preferences = UserSettings(tmp_path, default_sound_enabled=True, default_volume=0.8)
    preferences.sound_enabled = False
    preferences.volume = 0.35
    preferences.fullscreen = True
    preferences.save()

    restored = UserSettings(tmp_path)

    assert not restored.sound_enabled
    assert restored.volume == pytest.approx(0.35)
    assert restored.fullscreen
    payload = json.loads(restored.settings_file.read_text(encoding="utf-8"))
    assert payload["schema_version"] == UserSettings.SCHEMA_VERSION


def test_corrupt_preferences_use_one_refreshable_backup(tmp_path):
    preferences_file = tmp_path / "preferences.json"
    preferences_file.write_text("not json", encoding="utf-8")

    preferences = UserSettings(tmp_path, default_sound_enabled=False, default_volume=0.4)
    UserSettings(tmp_path)

    assert not preferences.sound_enabled
    assert preferences.volume == pytest.approx(0.4)
    backup = tmp_path / "preferences.json.corrupt.bak"
    assert backup.read_text(encoding="utf-8") == "not json"
    assert list(tmp_path.glob("preferences.json.corrupt*.bak")) == [backup]

    preferences_file.write_text("different invalid data", encoding="utf-8")
    UserSettings(tmp_path)

    assert backup.read_text(encoding="utf-8") == "different invalid data"
    assert list(tmp_path.glob("preferences.json.corrupt*.bak")) == [backup]
