"""In-world radio station discovery, playback, and crossfades.

Handles:
- Station switching with smooth crossfades
- Music playback and looping
- Station selection retained for the current process
- Hotkey integration for quick station changes
"""

import pygame
import random
from pathlib import Path
from dataclasses import dataclass
from typing import Optional, List
from vibesnake.data import settings


@dataclass
class RadioStation:
    """Radio station configuration."""

    key: str
    name: str
    filename: str  # Base filename pattern (e.g., "vibe_fm")
    description: str
    genre: str
    playlist: Optional[List[Path]] = None


class RadioManager:
    """
    Manages The Vibe Snake Radio Network with crossfade transitions.

    Features:
    - 8 radio stations in a diegetic snake world
    - Smooth crossfade transitions (0.5s default)
    - Station selection retained for subsequent managers in the same process
    - Random station on startup
    - Hotkey support (1-8 or [ and ])
    - OFF state in station cycling (M key or cycle through all)
    """

    # The Serpentine Broadcast Network - 8 frequencies from the post-Molt world
    STATIONS = [
        RadioStation(
            key="flow_signal",
            name="The Flow Signal",
            filename="vibe_fm.mp3",
            description="Ministry of Focus - For snakes who groove, not chase",
            genre="Chill / Focus / Study",
        ),
        RadioStation(
            key="chaos_theory",
            name="Chaos Theory",
            filename="snake_jazz.mp3",
            description="Improvisational Order - All hiss. No miss.",
            genre="Jazz / Bossa / Fusion",
        ),
        RadioStation(
            key="global_coil",
            name="The Global Coil",
            filename="reptile_radio.mp3",
            description="Warm-Blooded Movement - One scale, one rhythm",
            genre="World / Afrobeat / Reggaeton",
        ),
        RadioStation(
            key="ourotron",
            name="Ourotron",
            filename="synthwave_serpent.mp3",
            description="Order of Retrowave - Where the future sheds its past",
            genre="Synthwave / Outrun / Retro",
        ),
        RadioStation(
            key="the_pit",
            name="The Pit",
            filename="venom_bass.mp3",
            description="Venom Syndicate - Where the drop sheds its skin",
            genre="Bass / DnB / Trap",
        ),
        RadioStation(
            key="the_bureau",
            name="The Bureau",
            filename="snake_news.mp3",
            description="Information Comfort Division - All facts. No mammals.",
            genre="News / Talk / Jazz",
        ),
        RadioStation(
            key="the_strike",
            name="The Strike",
            filename="rock_station.mp3",
            description="Molten Core Collective - Fast. Loud. Venomous.",
            genre="Rock / Metal / Alt",
        ),
        RadioStation(
            key="underground_scales",
            name="Underground Scales",
            filename="hiphop_station.mp3",
            description="Thermal Rhythm Division - Bars. Beats. Biology.",
            genre="Hip-Hop / EDM / Beats",
        ),
    ]

    def __init__(self, radio_dir: Optional[Path] = None):
        """
        Initialize radio manager.

        Args:
            radio_dir: Optional directory containing station MP3 files.
        """
        self.radio_dir = radio_dir or Path(settings.AUDIO_DIR) / "radio"
        self.current_station_index = 0
        self.current_track_index = 0  # GTA-style: index within station's playlist
        self.is_playing = False
        self.failed_tracks = set()
        self.crossfade_duration = 0.5  # seconds
        self.volume = settings.MUSIC_VOLUME if hasattr(settings, "MUSIC_VOLUME") else 0.5

        # Load last station from settings if available
        if hasattr(settings, "LAST_RADIO_STATION"):
            self._load_saved_station()
        else:
            # Start with random station
            self.current_station_index = random.randint(0, len(self.STATIONS) - 1)

        # Verify stations exist and build playlists
        self._verify_station_files()

    def _verify_station_files(self):
        """Discover each station playlist without requiring an optional pack."""
        self.available_stations = []

        # Map each station to both its canonical prefix and any legacy prefixes.
        # The library contains tracks from multiple naming generations. Keeping the
        # mapping here makes every curated track discoverable without renaming files.
        file_prefix_map = {
            "flow_signal": ["flow_signal_", "ambient_", "chill_"],
            "chaos_theory": ["chaos_theory_", "jazz_"],
            "global_coil": ["global_coil_", "world_", "soul_"],
            "ourotron": ["ourotron_", "synthwave_"],
            "the_pit": ["the_pit_", "dance_"],
            "the_bureau": ["the_bureau_"],
            "the_strike": ["the_strike_", "rock_"],
            "underground_scales": ["underground_scales_", "hiphop_"],
        }

        for i, station in enumerate(self.STATIONS):
            station_key = station.key
            prefixes = file_prefix_map.get(station_key, [station_key])

            # Handle both list of prefixes and single prefix
            if isinstance(prefixes, str):
                prefixes = [prefixes]

            playlist = set()

            # Find all tracks matching the station's accepted prefixes.
            for prefix in prefixes:
                for track_file in self.radio_dir.glob(f"{prefix}*.mp3"):
                    playlist.add(track_file)

            if playlist:
                # Sort alphabetically for consistent ordering
                station.playlist = sorted(playlist)
                self.available_stations.append(i)
                print(f"[Radio] {station.name}: {len(playlist)} track(s)")
        if not self.available_stations:
            print("[Radio] Optional radio pack is not installed; procedural cues remain available")
        else:
            total_tracks = sum(len(self.STATIONS[i].playlist) for i in self.available_stations)
            print(
                f"[Radio] Found {len(self.available_stations)}/{len(self.STATIONS)} stations ({total_tracks} total tracks)"
            )

    def _load_saved_station(self):
        """Load last played station from settings."""
        try:
            saved_key = settings.LAST_RADIO_STATION
            for i, station in enumerate(self.STATIONS):
                if station.key == saved_key:
                    self.current_station_index = i
                    print(f"[Radio] Restored station: {station.name}")
                    return
        except Exception as e:
            print(f"[Radio] Failed to load saved station: {e}")

        # Fallback to random if loading failed
        self.current_station_index = random.randint(0, len(self.STATIONS) - 1)

    def _save_station(self):
        """Retain the current station in process-local settings state."""
        try:
            station = self.STATIONS[self.current_station_index]
            settings.LAST_RADIO_STATION = station.key
        except Exception as e:
            print(f"[Radio] Failed to save station: {e}")

    def get_current_station(self) -> RadioStation:
        """Get currently selected radio station."""
        return self.STATIONS[self.current_station_index]

    def get_station_path(self, station: RadioStation) -> Path:
        """Get the selected track path, including the legacy single-file fallback."""
        if station.playlist and len(station.playlist) > 0:
            # Return current track from playlist
            track_index = self.current_track_index % len(station.playlist)
            return station.playlist[track_index]
        # Fallback to old behavior (single file)
        return self.radio_dir / station.filename

    def play_current_station(self, random_track: bool = True):
        """
        Start playing the current station playlist.

        Args:
            random_track: If True, pick a random track from the playlist
        """
        if not self.available_stations:
            print("[Radio] No stations available to play")
            return False

        # Make sure current index is in available stations
        if self.current_station_index not in self.available_stations:
            self.current_station_index = self.available_stations[0]

        station = self.get_current_station()

        playlist = station.playlist or [self.radio_dir / station.filename]
        if random_track and len(playlist) > 1:
            first_index = random.randint(0, len(playlist) - 1)
        else:
            first_index = self.current_track_index % len(playlist)

        candidate_indices = [(first_index + offset) % len(playlist) for offset in range(len(playlist))]
        for track_index in candidate_indices:
            station_path = playlist[track_index]
            if station_path in self.failed_tracks:
                continue

            try:
                pygame.mixer.music.load(str(station_path))
            except Exception as error:
                self.failed_tracks.add(station_path)
                print(f"[Radio] Skipping unreadable track {station_path.name} on {station.name}: {error}")
                continue

            try:
                pygame.mixer.music.set_volume(self.volume)
                # Play once so update() can advance after the track finishes.
                pygame.mixer.music.play(loops=0)
            except Exception as error:
                self.is_playing = False
                print(f"[Radio] Playback unavailable for {station.name}: {error}")
                return False

            self.current_track_index = track_index
            self.is_playing = True
            track_name = station_path.stem
            if len(playlist) > 1:
                print(f"[Radio] {station.name}: {track_name} ({track_index + 1}/{len(playlist)})")
            else:
                print(f"[Radio] Now playing: {station.name} - {station.description}")

            self._save_station()
            return True

        self.is_playing = False
        print(f"[Radio] No readable tracks remain on {station.name}")
        return False

    def stop(self):
        """Stop radio playback."""
        if self.is_playing:
            pygame.mixer.music.fadeout(int(self.crossfade_duration * 1000))
            self.is_playing = False
            print("[Radio] Stopped playback")

    def switch_station(self, station_index: int):
        """
        Switch to a specific station by index with crossfade.

        Args:
            station_index: Index of station in STATIONS list (0-8)
        """
        if station_index < 0 or station_index >= len(self.STATIONS):
            print(f"[Radio] Invalid station index: {station_index}")
            return

        if station_index not in self.available_stations:
            print(f"[Radio] Station {self.STATIONS[station_index].name} not available")
            return

        if station_index == self.current_station_index:
            print("[Radio] Already on this station")
            return

        # Crossfade transition
        if self.is_playing:
            pygame.mixer.music.fadeout(int(self.crossfade_duration * 1000))

        self.current_station_index = station_index
        self.play_current_station()

    def next_station(self):
        """Switch to next available station (wraps around). Includes OFF state."""
        if not self.available_stations:
            return

        # If currently OFF, turn on with first available station
        if not self.is_playing:
            next_index = self.available_stations[0]
            self.switch_station(next_index)
            return

        # Find next available station
        current_pos = (
            self.available_stations.index(self.current_station_index)
            if self.current_station_index in self.available_stations
            else -1
        )
        next_pos = (current_pos + 1) % len(self.available_stations)

        # After cycling through all stations, go to OFF
        if next_pos == 0:
            self.stop()
            print("[Radio] Switched to OFF")
        else:
            next_index = self.available_stations[next_pos]
            self.switch_station(next_index)

    def previous_station(self):
        """Switch to previous available station (wraps around). Includes OFF state."""
        if not self.available_stations:
            return

        # If currently OFF, turn on with last available station
        if not self.is_playing:
            prev_index = self.available_stations[-1]
            self.switch_station(prev_index)
            return

        # Find previous available station
        current_pos = (
            self.available_stations.index(self.current_station_index)
            if self.current_station_index in self.available_stations
            else -1
        )
        prev_pos = (current_pos - 1) % len(self.available_stations)

        # Before wrapping to last station, go to OFF
        if current_pos == 0:
            self.stop()
            print("[Radio] Switched to OFF")
        else:
            prev_index = self.available_stations[prev_pos]
            self.switch_station(prev_index)

    def update(self):
        """
        Update radio manager (call each frame).
        Handles GTA-style auto-play of next track when current song ends.
        """
        if self.is_playing and not pygame.mixer.music.get_busy():
            # Current track finished, play next track in playlist
            station = self.get_current_station()
            if station.playlist and len(station.playlist) > 1:
                # Move to next track in playlist (randomly)
                self.current_track_index = random.randint(0, len(station.playlist) - 1)
                self.play_current_station(random_track=False)  # Don't re-randomize

    def set_volume(self, volume: float):
        """
        Set radio volume.

        Args:
            volume: Volume level (0.0 to 1.0)
        """
        self.volume = max(0.0, min(1.0, volume))
        if self.is_playing:
            pygame.mixer.music.set_volume(self.volume)

    def toggle_playback(self):
        """Toggle radio playback on/off."""
        if self.is_playing:
            self.stop()
        else:
            self.play_current_station()

    def get_station_info_text(self) -> str:
        """Get formatted text for current station (for HUD display)."""
        if not self.is_playing:
            return "Radio: OFF"

        station = self.get_current_station()
        return f"Radio: {station.name} ({station.genre})"

    def handle_number_key(self, number: int):
        """
        Handle number key press (1-9) to switch stations.

        Args:
            number: Number key pressed (1-9)
        """
        if 1 <= number <= 9:
            station_index = number - 1  # Convert to 0-indexed
            self.switch_station(station_index)


# Global radio manager instance (initialized when audio system loads)
_radio_manager: Optional[RadioManager] = None


def get_radio_manager() -> Optional[RadioManager]:
    """Get the global radio manager instance."""
    return _radio_manager


def initialize_radio(radio_dir: Optional[Path] = None) -> RadioManager:
    """
    Initialize the global radio manager.

    Args:
        radio_dir: Directory containing radio station files

    Returns:
        RadioManager instance
    """
    global _radio_manager
    if _radio_manager is None:
        _radio_manager = RadioManager(radio_dir)
    return _radio_manager


def play_radio():
    """Start playing the radio (convenience function)."""
    manager = get_radio_manager()
    if manager:
        manager.play_current_station()


def stop_radio():
    """Stop the radio (convenience function)."""
    manager = get_radio_manager()
    if manager:
        manager.stop()
