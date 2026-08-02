"""Schema-versioned local identity, progress counters, and unlock state.

The profile stores only player-visible local progression needed by the current
game. It uses the operating system's user-data directory, validates persisted
types, writes atomically, backs up corrupt input, and refuses unsupported future
schemas. Counters must never be treated as evidence of enjoyment by themselves.
"""

import json
from pathlib import Path
from datetime import datetime
from typing import Optional, Union

from vibesnake.data.json_store import (
    UnsupportedSchemaVersionError,
    atomic_write_json,
    backup_corrupt_file,
)
from vibesnake.data.paths import get_data_dir


class PlayerProfile:
    """Persist local player identity, visible counters, and unlock state.

    Data is schema-versioned and stored in the operating system's user-data
    directory unless a test path is injected. Writes are atomic. Invalid
    current-schema data is backed up before defaults are used, and unsupported
    future schemas are rejected. No profile data is transmitted.
    """

    SCHEMA_VERSION = 1

    def __init__(self, data_dir: Optional[Union[str, Path]] = None):
        """Initialize defaults, resolve the data path, and load a saved profile.

        Args:
            data_dir: Optional storage directory override, primarily for tests.
        """
        self.data_dir = get_data_dir(data_dir)
        self.profile_file = self.data_dir / "player_profile.json"

        self.player_name = ""
        self.created_date = None
        self.last_played = None
        self.total_games = 0

        # Progression stats for unlocking customizations
        self.highest_score = 0
        self.highest_combo = 0
        self.total_score = 0

        # Gameplay stats for interesting unlocks
        self.apples_eaten = 0
        self.wall_rides = 0
        self.achievement_state = {}
        self._write_blocked = False

        self._load_profile()

    def _load_profile(self):
        """Load player profile from file."""
        if self.profile_file.exists():
            try:
                with open(self.profile_file, "r", encoding="utf-8") as f:
                    data = json.load(f)
                    if not isinstance(data, dict):
                        raise ValueError("profile root must be a JSON object")

                    schema_version = int(data.get("schema_version", 0))
                    if schema_version > self.SCHEMA_VERSION:
                        self._write_blocked = True
                        raise UnsupportedSchemaVersionError(
                            f"profile schema {schema_version} is newer than supported {self.SCHEMA_VERSION}"
                        )

                    name = data.get("name", "")
                    self.player_name = name if isinstance(name, str) else ""
                    self.created_date = data.get("created_date")
                    self.last_played = data.get("last_played")
                    self.total_games = self._nonnegative_int(data.get("total_games", 0))

                    # Load progression stats
                    self.highest_score = self._nonnegative_int(data.get("highest_score", 0))
                    self.highest_combo = self._nonnegative_int(data.get("highest_combo", 0))
                    self.total_score = self._nonnegative_int(data.get("total_score", 0))

                    # Load gameplay stats
                    self.apples_eaten = self._nonnegative_int(data.get("apples_eaten", 0))
                    self.wall_rides = self._nonnegative_int(data.get("wall_rides", 0))
                    achievements = data.get("achievements", {})
                    self.achievement_state = achievements if isinstance(achievements, dict) else {}

                    print(f"[Profile] Loaded player: {self.player_name}")
                if schema_version < self.SCHEMA_VERSION:
                    self._save_profile()
            except UnsupportedSchemaVersionError as e:
                print(f"[Profile] Failed to load: {e}")
            except Exception as e:
                backup = backup_corrupt_file(self.profile_file)
                print(f"[Profile] Failed to load: {e}")
                if backup:
                    print(f"[Profile] Preserved unreadable save at {backup.name}")

    @staticmethod
    def _nonnegative_int(value) -> int:
        """Coerce a persisted counter to a safe nonnegative integer."""
        if isinstance(value, bool):
            return 0
        try:
            return max(0, int(value))
        except (TypeError, ValueError):
            return 0

    def _save_profile(self):
        """Save player profile to file."""
        if self._write_blocked:
            print("[Profile] Save skipped because the file uses a newer schema")
            return
        try:
            atomic_write_json(
                self.profile_file,
                {
                    "schema_version": self.SCHEMA_VERSION,
                    "name": self.player_name,
                    "created_date": self.created_date,
                    "last_played": self.last_played,
                    "total_games": self.total_games,
                    "highest_score": self.highest_score,
                    "highest_combo": self.highest_combo,
                    "total_score": self.total_score,
                    "apples_eaten": self.apples_eaten,
                    "wall_rides": self.wall_rides,
                    "achievements": self.achievement_state,
                },
            )
            print(f"[Profile] Saved player: {self.player_name}")
        except Exception as e:
            print(f"[Profile] Failed to save: {e}")

    def has_profile(self) -> bool:
        """Check if player has created a profile."""
        return bool(self.player_name)

    def create_profile(self, name: str):
        """
        Create new player profile.

        Args:
            name: Player's chosen name
        """
        self.player_name = name if name else "Anonymous"
        self.created_date = datetime.now().isoformat()
        self.last_played = datetime.now().isoformat()
        self.total_games = 0
        self._save_profile()

    def update_last_played(self):
        """Update last played timestamp."""
        self.last_played = datetime.now().isoformat()
        self._save_profile()

    def increment_games(self):
        """Increment total games counter."""
        self.total_games += 1
        self._save_profile()

    def increment_apples_eaten(self):
        """Increment apples eaten counter."""
        self.apples_eaten += 1
        # Don't save on every apple - will be saved at end of game

    def increment_wall_rides(self):
        """Increment wall rides counter."""
        self.wall_rides += 1
        # Don't save on every wall ride - will be saved at end of game

    def update_score(self, score: int, combo: int):
        """
        Update progression stats after a game.

        Args:
            score: Final score for this game
            combo: Highest combo achieved this game
        """
        self.total_score += score
        if score > self.highest_score:
            self.highest_score = score
            print(f"[Profile] New high score: {score}!")
        if combo > self.highest_combo:
            self.highest_combo = combo
            print(f"[Profile] New high combo: {combo}x!")
        self._save_profile()

    def update_achievement_state(self, state: dict):
        """Persist serialized achievement progress with the player profile."""
        self.achievement_state = state
        self._save_profile()

    def check_unlocked(self, item_name: str, requirement_tuple) -> bool:
        """Return whether a cosmetic requirement is satisfied.

        Integer requirements are a legacy free-item format. Current requirements
        are tuples containing a known stat key, a non-negative integer threshold,
        and optionally a display description. ``free`` always unlocks. Unknown or
        malformed current requirements fail closed so corrupt content cannot grant
        items accidentally.

        Args:
            item_name: Customization identifier retained by the compatibility API
            requirement_tuple: Legacy integer or current requirement tuple.
        """
        if type(requirement_tuple) is int:
            return True

        if not isinstance(requirement_tuple, tuple) or len(requirement_tuple) < 2:
            return False

        req_type, req_value = requirement_tuple[0], requirement_tuple[1]
        if req_type == "free":
            return True
        if type(req_value) is not int or req_value < 0:
            return False

        current_values = {
            "apples_eaten": self.apples_eaten,
            "wall_rides": self.wall_rides,
            "games_played": self.total_games,
            "highest_combo": self.highest_combo,
            "highest_score": self.highest_score,
        }
        current_value = current_values.get(req_type)
        return current_value is not None and current_value >= req_value

    def get_name(self) -> str:
        """Get player name."""
        return self.player_name if self.player_name else "Anonymous"

    def reset_profile(self):
        """Reset/delete player profile."""
        self._write_blocked = False
        self.player_name = ""
        self.created_date = None
        self.last_played = None
        self.total_games = 0
        self.highest_score = 0
        self.highest_combo = 0
        self.total_score = 0
        self.apples_eaten = 0
        self.wall_rides = 0
        self.achievement_state = {}

        if self.profile_file.exists():
            try:
                self.profile_file.unlink()
                print("[Profile] Profile deleted")
            except Exception as e:
                print(f"[Profile] Failed to delete profile: {e}")
