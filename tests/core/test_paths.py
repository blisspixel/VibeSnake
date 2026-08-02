"""Tests for portable save-directory resolution and legacy migration."""

import json
import logging

import vibesnake.data.paths as paths

from vibesnake.data.paths import (
    LEGACY_MIGRATION_MARKER,
    _migrate_legacy_saves,
    _platform_data_dir,
    get_data_dir,
)


def test_platform_data_directories_follow_os_conventions(tmp_path):
    assert _platform_data_dir("win32", {"LOCALAPPDATA": str(tmp_path)}, tmp_path) == tmp_path / "VibeSnake"
    assert _platform_data_dir("darwin", {}, tmp_path) == tmp_path / "Library" / "Application Support" / "VibeSnake"
    assert _platform_data_dir("linux", {"XDG_DATA_HOME": str(tmp_path)}, tmp_path) == tmp_path / "vibesnake"
    assert _platform_data_dir("linux", {}, tmp_path) == tmp_path / ".local" / "share" / "vibesnake"


def test_relative_platform_environment_paths_are_ignored(tmp_path):
    assert _platform_data_dir("win32", {"LOCALAPPDATA": "relative/data"}, tmp_path) == (
        tmp_path / "AppData" / "Local" / "VibeSnake"
    )
    assert _platform_data_dir("linux", {"XDG_DATA_HOME": "relative/data"}, tmp_path) == (
        tmp_path / ".local" / "share" / "vibesnake"
    )


def test_explicit_data_directory_never_triggers_implicit_migration(tmp_path):
    portable = tmp_path / "portable"
    assert get_data_dir(portable) == portable
    assert not portable.exists()


def test_legacy_migration_copies_known_saves_only_once(tmp_path):
    legacy = tmp_path / "legacy"
    target = tmp_path / "target"
    legacy.mkdir()
    (legacy / "player_profile.json").write_text('{"name": "ADA"}', encoding="utf-8")
    (legacy / "unrelated.json").write_text('{"keep": true}', encoding="utf-8")

    _migrate_legacy_saves(target, legacy)

    assert json.loads((target / "player_profile.json").read_text(encoding="utf-8"))["name"] == "ADA"
    assert not (target / "unrelated.json").exists()
    marker = json.loads((target / LEGACY_MIGRATION_MARKER).read_text(encoding="utf-8"))
    assert marker["copied_files"] == ["player_profile.json"]

    (legacy / "player_profile.json").write_text('{"name": "CHANGED"}', encoding="utf-8")
    _migrate_legacy_saves(target, legacy)
    assert json.loads((target / "player_profile.json").read_text(encoding="utf-8"))["name"] == "ADA"


def test_migration_failure_never_reactivates_legacy_storage(monkeypatch, tmp_path, caplog):
    target = tmp_path / "os-user-data"
    legacy = tmp_path / "source-checkout-data"
    legacy.mkdir()
    monkeypatch.delenv("VIBESNAKE_DATA_DIR")
    monkeypatch.setattr(paths, "_platform_data_dir", lambda *_: target)
    monkeypatch.setattr(paths, "_legacy_data_dir", lambda: legacy)

    def fail_migration(_target):
        raise OSError("simulated migration failure")

    monkeypatch.setattr(paths, "_migrate_legacy_saves", fail_migration)

    with caplog.at_level(logging.WARNING, logger="vibesnake.data.paths"):
        resolved = get_data_dir()

    assert resolved == target
    assert resolved != legacy
    assert "continuing with the OS user-data directory" in caplog.text
