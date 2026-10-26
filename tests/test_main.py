"""Tests for the package entry point."""

from vibesnake import __main__
from vibesnake import cli


def test_main_module_dispatches_to_cli(monkeypatch):
    monkeypatch.setattr(cli, "main", lambda argv=None: 7)
    assert __main__.main(["version"]) == 7


def test_default_command_starts_native_play(monkeypatch):
    calls = []
    monkeypatch.setattr(cli, "run_play", lambda: calls.append("native") or 0)
    assert __main__.main([]) == 0
    assert calls == ["native"]


def test_play_propagates_native_failure(monkeypatch):
    monkeypatch.setattr(cli, "run_play", lambda: 7)
    assert __main__.main(["play"]) == 7
