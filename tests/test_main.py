"""Tests for the package entry point."""

from vibesnake import __main__


def test_main_runs_game_and_quits(monkeypatch):
    calls = []

    class StubGame:
        def run(self):
            calls.append("run")

    monkeypatch.setattr(__main__, "Game", StubGame)
    monkeypatch.setattr(__main__.pygame, "quit", lambda: calls.append("quit"))
    status = __main__.main()
    assert calls == ["run", "quit"]
    assert status == 0


def test_main_reports_crash_and_still_quits(monkeypatch):
    calls = []

    class CrashingGame:
        def run(self):
            raise RuntimeError("test crash")

    monkeypatch.setattr(__main__, "Game", CrashingGame)
    monkeypatch.setattr(__main__.traceback, "print_exc", lambda: calls.append("reported"))
    monkeypatch.setattr(__main__.pygame, "quit", lambda: calls.append("quit"))
    status = __main__.main()
    assert calls == ["reported", "quit"]
    assert status == 1
