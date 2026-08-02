"""Contracts for rights-safe procedural audio fallbacks."""

from pathlib import Path

import pygame
import pytest

from vibesnake.audio import manager
from vibesnake.audio.radio_manager import RadioManager
from vibesnake.data import settings


def test_missing_authored_cue_uses_the_requested_fallback(tmp_path: Path, monkeypatch) -> None:
    sentinel = object()
    calls = []
    monkeypatch.setattr(manager, "mixer_ready", True)
    monkeypatch.setattr(
        manager,
        "_synthesize_tone",
        lambda *contract: calls.append(contract) or sentinel,
    )

    sound = manager.load_sound(str(tmp_path / "missing.wav"), (440.0, 880.0, 0.1))

    assert sound is sentinel
    assert calls == [(440.0, 880.0, 0.1)]


def test_procedural_cue_uses_the_active_mixer_format() -> None:
    if not manager.mixer_ready:
        pytest.skip("SDL audio mixer is unavailable")

    sound = manager._synthesize_tone(440.0, 660.0, 0.05)

    assert isinstance(sound, pygame.mixer.Sound)
    assert 0.04 <= sound.get_length() <= 0.06


def test_radio_defaults_to_the_explicit_audio_overlay(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setattr(settings, "AUDIO_DIR", str(tmp_path))

    radio = RadioManager()

    assert radio.radio_dir == tmp_path / "radio"
    assert radio.available_stations == []
