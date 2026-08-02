"""Tests for the package entry point."""

from vibesnake import __main__
from vibesnake import cli


def test_main_module_dispatches_to_cli(monkeypatch):
    monkeypatch.setattr(cli, "main", lambda argv=None: 7)
    assert __main__.main(["version"]) == 7


def test_play_runs_game_and_quits(monkeypatch):
    calls = []

    class StubGame:
        def run(self):
            calls.append("run")

    monkeypatch.setattr("vibesnake.core.game_state.Game", StubGame)
    monkeypatch.setattr("pygame.quit", lambda: calls.append("quit"))
    status = cli.main([])
    assert calls == ["run", "quit"]
    assert status == 0


def test_play_reports_crash_and_still_quits(monkeypatch):
    calls = []

    class CrashingGame:
        def run(self):
            raise RuntimeError("test crash")

    monkeypatch.setattr("vibesnake.core.game_state.Game", CrashingGame)
    monkeypatch.setattr("traceback.print_exc", lambda: calls.append("reported"))
    monkeypatch.setattr("pygame.quit", lambda: calls.append("quit"))
    status = cli.main(["play"])
    assert calls == ["reported", "quit"]
    assert status == 1
