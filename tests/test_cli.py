"""Tests for the player CLI surface."""

from pathlib import Path

from vibesnake import cli
from vibesnake.checkout import find_checkout_root, radio_track_count
from vibesnake.update import UpdateError, update_checkout


def test_find_checkout_root_from_repo():
    root = find_checkout_root(Path(__file__).resolve())
    assert root is not None
    assert (root / "pyproject.toml").is_file()
    assert (root / "assets").is_dir()


def test_radio_track_count_reports_station_library():
    root = find_checkout_root(Path(__file__).resolve())
    assert root is not None
    assert radio_track_count(root) >= 8


def test_version_command(capsys):
    status = cli.main(["version"])
    captured = capsys.readouterr()
    assert status == 0
    assert "vibe-snake" in captured.out


def test_doctor_command_on_checkout(capsys):
    status = cli.main(["doctor"])
    captured = capsys.readouterr()
    assert status == 0
    assert "ready to play" in captured.out
    assert "radio" in captured.out.lower()


def test_update_dry_run(capsys):
    status = cli.main(["update", "--dry-run"])
    captured = capsys.readouterr()
    assert status == 0
    assert "dry-run complete" in captured.out


def test_status_command(monkeypatch, capsys):
    monkeypatch.setattr(
        "vibesnake.cli.checkout_status",
        lambda branch="main", remote="origin": {
            "root": "C:/repo",
            "branch": "main",
            "local": "abc1234",
            "remote": "abc1234",
            "ahead": "0",
            "behind": "0",
            "state": "current",
            "dirty": "no",
            "remote_ref": "origin/main",
        },
    )
    status = cli.main(["status"])
    captured = capsys.readouterr()
    assert status == 0
    assert "state    current" in captured.out
    assert "matches the remote branch" in captured.out


def test_update_rejects_dirty_tree(tmp_path, monkeypatch):
    root = find_checkout_root(Path(__file__).resolve())
    assert root is not None

    def fake_git(checkout, *args):
        joined = " ".join(args)
        if joined == "rev-parse --short HEAD":
            return "abc1234"
        if joined == "rev-parse --abbrev-ref HEAD":
            return "main"
        if joined.startswith("status"):
            return " M README.md"
        raise AssertionError(joined)

    monkeypatch.setattr("vibesnake.update._require_git", lambda checkout: None)
    monkeypatch.setattr("vibesnake.update._git", fake_git)
    try:
        update_checkout(root, dry_run=False)
        raised = False
    except UpdateError as error:
        raised = True
        assert "local changes" in str(error)
    assert raised


def test_play_dispatches_to_game(monkeypatch):
    calls: list[str] = []

    class StubGame:
        def run(self):
            calls.append("run")

    monkeypatch.setattr("vibesnake.core.game_state.Game", StubGame)
    monkeypatch.setattr("pygame.quit", lambda: calls.append("quit"))
    status = cli.main(["play"])
    assert status == 0
    assert calls == ["run", "quit"]
