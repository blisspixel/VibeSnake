"""
Comprehensive tests for radio station management system.

Tests station switching, playlist handling, crossfades, and integration.
"""

from pathlib import Path

import pytest
import pygame
from vibesnake.audio.radio_manager import RadioManager, RadioStation


@pytest.fixture
def radio_manager(tmp_path):
    """Create a radio manager with a temporary directory."""
    pygame.mixer.init()
    radio_dir = tmp_path / "radio"
    radio_dir.mkdir()

    # Create some dummy MP3 files for testing (GTA-style: matching key pattern)
    for i in range(3):
        # Create files that match the station key pattern for playlist detection
        (radio_dir / f"test_{i}.mp3").write_bytes(b"fake mp3 data")

    # Mock the STATIONS list to use our test files
    original_stations = RadioManager.STATIONS
    RadioManager.STATIONS = [
        RadioStation(
            key=f"test_{i}",
            name=f"Test Station {i}",
            filename=f"test_{i}.mp3",  # Updated to match key pattern
            description=f"Test station {i}",
            genre="Test",
        )
        for i in range(3)
    ]

    manager = RadioManager(radio_dir)

    yield manager

    # Restore original stations
    RadioManager.STATIONS = original_stations
    pygame.mixer.quit()


class TestRadioManagerInitialization:
    """Test radio manager initialization."""

    def test_initializes_with_random_station(self, radio_manager):
        """Should start with a random station index."""
        assert 0 <= radio_manager.current_station_index < len(radio_manager.STATIONS)

    def test_finds_available_stations(self, radio_manager):
        """Should detect which station files exist."""
        assert len(radio_manager.available_stations) == 3

    def test_not_playing_initially(self, radio_manager):
        """Should not be playing on initialization."""
        assert radio_manager.is_playing is False

    def test_has_default_volume(self, radio_manager):
        """Should have a default volume set."""
        assert 0.0 <= radio_manager.volume <= 1.0


class TestStationSwitching:
    """Test station switching functionality."""

    def test_switch_to_valid_station(self, radio_manager):
        """Should switch to a valid station index."""
        radio_manager.current_station_index = 0
        radio_manager.switch_station(1)
        assert radio_manager.current_station_index == 1

    def test_switch_to_invalid_station_does_nothing(self, radio_manager):
        """Should not switch to invalid station index."""
        initial = radio_manager.current_station_index
        radio_manager.switch_station(999)  # Invalid index
        assert radio_manager.current_station_index == initial

    def test_switch_to_same_station_does_nothing(self, radio_manager):
        """Should not re-switch to current station."""
        radio_manager.current_station_index = 1
        radio_manager.switch_station(1)  # Same station
        # Should print "Already on this station" but not crash
        assert radio_manager.current_station_index == 1

    def test_next_station_cycles_forward(self, radio_manager):
        """Should cycle to next available station."""
        # Properly switch to station 0 first and mark as playing
        radio_manager.switch_station(0)
        radio_manager.is_playing = True  # Simulate that music is playing
        radio_manager.next_station()
        assert radio_manager.current_station_index == 1

    def test_next_station_wraps_around(self, radio_manager):
        """Should wrap around to first station after last."""
        radio_manager.current_station_index = 2  # Last station
        radio_manager.next_station()
        assert radio_manager.current_station_index == 0  # Should wrap

    def test_previous_station_cycles_backward(self, radio_manager):
        """Should cycle to previous available station."""
        # Properly switch to station 2 first and mark as playing
        radio_manager.switch_station(2)
        radio_manager.is_playing = True  # Simulate that music is playing
        radio_manager.previous_station()
        assert radio_manager.current_station_index == 1

    def test_previous_station_wraps_around(self, radio_manager):
        """Should wrap around to last station before first."""
        radio_manager.current_station_index = 0  # First station
        radio_manager.previous_station()
        assert radio_manager.current_station_index == 2  # Should wrap to last

    def test_handle_number_key_valid(self, radio_manager):
        """Should switch to station by number key."""
        radio_manager.handle_number_key(2)  # Key 2 = station index 1
        assert radio_manager.current_station_index == 1

    def test_handle_number_key_invalid(self, radio_manager):
        """Should ignore invalid number keys."""
        initial = radio_manager.current_station_index
        radio_manager.handle_number_key(10)  # Invalid
        assert radio_manager.current_station_index == initial


class TestVolumeControl:
    """Test volume control functionality."""

    def test_set_volume_valid(self, radio_manager):
        """Should set volume within valid range."""
        radio_manager.set_volume(0.7)
        assert radio_manager.volume == 0.7

    def test_set_volume_clamps_high(self, radio_manager):
        """Should clamp volume to maximum 1.0."""
        radio_manager.set_volume(1.5)
        assert radio_manager.volume == 1.0

    def test_set_volume_clamps_low(self, radio_manager):
        """Should clamp volume to minimum 0.0."""
        radio_manager.set_volume(-0.5)
        assert radio_manager.volume == 0.0


class TestStationInfo:
    """Test station information retrieval."""

    def test_get_current_station(self, radio_manager):
        """Should return current RadioStation object."""
        radio_manager.current_station_index = 1
        station = radio_manager.get_current_station()
        assert isinstance(station, RadioStation)
        assert station.key == "test_1"

    def test_get_station_path(self, radio_manager):
        """Should return correct path to station file."""
        station = radio_manager.get_current_station()
        path = radio_manager.get_station_path(station)
        assert path.name == station.filename
        assert path.parent == radio_manager.radio_dir

    def test_get_station_info_text_when_playing(self, radio_manager):
        """Should return formatted info text when playing."""
        radio_manager.is_playing = True
        radio_manager.current_station_index = 0
        info = radio_manager.get_station_info_text()
        assert "Test Station 0" in info
        assert "Test" in info  # Genre

    def test_get_station_info_text_when_stopped(self, radio_manager):
        """Should return OFF message when not playing."""
        radio_manager.is_playing = False
        info = radio_manager.get_station_info_text()
        assert info == "Radio: OFF"


class TestPlaybackControl:
    """Test playback control."""

    def test_toggle_playback_starts_when_stopped(self, radio_manager, monkeypatch):
        """Should start playback when toggled from stopped."""
        radio_manager.is_playing = False
        calls = []

        def play_current_station():
            calls.append("play")
            radio_manager.is_playing = True
            return True

        monkeypatch.setattr(radio_manager, "play_current_station", play_current_station)

        radio_manager.toggle_playback()

        assert calls == ["play"]
        assert radio_manager.is_playing is True

    def test_toggle_playback_stops_when_playing(self, radio_manager):
        """Should stop playback when toggled from playing."""
        radio_manager.is_playing = True
        radio_manager.toggle_playback()
        assert radio_manager.is_playing is False

    def test_corrupt_track_falls_back_to_next_track(self, radio_manager, monkeypatch):
        station = radio_manager.STATIONS[0]
        station.playlist = [radio_manager.radio_dir / "bad.mp3", radio_manager.radio_dir / "good.mp3"]
        radio_manager.current_station_index = 0
        radio_manager.available_stations = [0]
        loaded = []

        def load_track(path):
            loaded.append(Path(path).name)
            if Path(path).name == "bad.mp3":
                raise pygame.error("bad tags")

        monkeypatch.setattr("vibesnake.audio.radio_manager.random.randint", lambda *_: 0)
        monkeypatch.setattr(pygame.mixer.music, "load", load_track)
        monkeypatch.setattr(pygame.mixer.music, "set_volume", lambda *_: None)
        monkeypatch.setattr(pygame.mixer.music, "play", lambda **_: None)

        assert radio_manager.play_current_station()
        assert radio_manager.is_playing
        assert radio_manager.current_track_index == 1
        assert loaded == ["bad.mp3", "good.mp3"]
        assert station.playlist[0] in radio_manager.failed_tracks

    def test_station_turns_off_after_every_track_fails(self, radio_manager, monkeypatch):
        station = radio_manager.STATIONS[0]
        station.playlist = [radio_manager.radio_dir / "bad-a.mp3", radio_manager.radio_dir / "bad-b.mp3"]
        radio_manager.current_station_index = 0
        radio_manager.available_stations = [0]
        monkeypatch.setattr("vibesnake.audio.radio_manager.random.randint", lambda *_: 0)
        monkeypatch.setattr(
            pygame.mixer.music,
            "load",
            lambda *_: (_ for _ in ()).throw(pygame.error("bad tags")),
        )

        assert not radio_manager.play_current_station()
        assert not radio_manager.is_playing
        assert radio_manager.failed_tracks == set(station.playlist)


class TestEdgeCases:
    """Test edge cases and error handling."""

    def test_no_available_stations(self, tmp_path):
        """Should handle case with no available stations."""
        pygame.mixer.init()
        radio_dir = tmp_path / "empty_radio"
        radio_dir.mkdir()

        manager = RadioManager(radio_dir)
        assert len(manager.available_stations) == 0

        pygame.mixer.quit()

    def test_handles_missing_station_file_gracefully(self, radio_manager):
        """Should not crash when trying to play missing file."""
        # Try to switch to a station that doesn't exist
        radio_manager.current_station_index = 0
        # Should handle missing file gracefully in play_current_station
        # (will print warning but not crash)


class TestIntegration:
    """Integration tests for complete workflows."""

    def test_complete_station_cycle(self, radio_manager):
        """Should be able to cycle through all stations and reach OFF."""
        # Start at station 0 for deterministic test
        radio_manager.switch_station(0)
        radio_manager.is_playing = True
        # Cycle through all stations - after N cycles from position 0, wraps to OFF
        for _ in range(len(radio_manager.available_stations)):
            radio_manager.next_station()

        # Should be OFF now (after cycling through all stations)
        assert not radio_manager.is_playing

    def test_direct_selection_then_cycling(self, radio_manager, monkeypatch):
        """Should work correctly after direct selection."""

        def simulate_playback(*_args, **_kwargs):
            radio_manager.is_playing = True
            return True

        monkeypatch.setattr(radio_manager, "play_current_station", simulate_playback)

        # Direct select station 2 and mark as playing
        radio_manager.handle_number_key(2)
        assert radio_manager.current_station_index == 1

        # Then cycle forward
        radio_manager.next_station()
        assert radio_manager.current_station_index == 2

        # And backward
        radio_manager.previous_station()
        assert radio_manager.current_station_index == 1
