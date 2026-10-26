"""Tests for the player CLI surface."""

from pathlib import Path
from types import SimpleNamespace

import pytest

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


@pytest.mark.parametrize("platform,launcher", [("win32", "play.ps1"), ("linux", "play.sh")])
def test_play_dispatches_to_native_launcher(monkeypatch, tmp_path, platform, launcher):
    (tmp_path / launcher).touch()
    monkeypatch.setattr(cli.sys, "platform", platform)
    monkeypatch.setattr(cli, "find_checkout_root", lambda: tmp_path)
    calls = []

    def launch(command, **kwargs):
        calls.append((command, kwargs))
        return SimpleNamespace(returncode=7)

    monkeypatch.setattr(cli.subprocess, "run", launch)
    assert cli.main(["play"]) == 7
    expected = ["pwsh", "-NoProfile", "-File"] if platform == "win32" else []
    assert calls == [(expected + [str(tmp_path / launcher)], {"cwd": tmp_path, "check": False})]


@pytest.mark.parametrize("has_checkout", [False, True])
def test_play_requires_native_checkout(monkeypatch, tmp_path, capsys, has_checkout):
    monkeypatch.setattr(cli, "find_checkout_root", lambda: tmp_path if has_checkout else None)
    assert cli.main(["play"]) == 1
    assert "source checkout" in capsys.readouterr().err


def test_play_reports_launcher_failure(monkeypatch, tmp_path, capsys):
    (tmp_path / "play.ps1").touch()
    monkeypatch.setattr(cli.sys, "platform", "win32")
    monkeypatch.setattr(cli, "find_checkout_root", lambda: tmp_path)

    def missing_shell(*args, **kwargs):
        raise FileNotFoundError("pwsh")

    monkeypatch.setattr(cli.subprocess, "run", missing_shell)
    assert cli.main(["play"]) == 1
    assert "prerequisites" in capsys.readouterr().err
