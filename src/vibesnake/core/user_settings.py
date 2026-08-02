"""Persistent player preferences that are separate from balance configuration."""

import json
from pathlib import Path
from typing import Optional, Union

from vibesnake.data.json_store import (
    UnsupportedSchemaVersionError,
    atomic_write_json,
    backup_corrupt_file,
)
from vibesnake.data.paths import get_data_dir


class UserSettings:
    """Versioned repository for audio and display preferences."""

    SCHEMA_VERSION = 1

    def __init__(
        self,
        data_dir: Optional[Union[str, Path]] = None,
        default_sound_enabled: bool = True,
        default_volume: float = 0.8,
    ):
        self.data_dir = get_data_dir(data_dir)
        self.settings_file = self.data_dir / "preferences.json"
        self.sound_enabled = bool(default_sound_enabled)
        self.volume = self._clamp_volume(default_volume, 0.8)
        self.fullscreen = False
        self._write_blocked = False
        self._load()

    @staticmethod
    def _clamp_volume(value, fallback: float) -> float:
        if isinstance(value, bool) or not isinstance(value, (int, float)):
            return fallback
        return min(1.0, max(0.0, float(value)))

    def _load(self) -> None:
        if not self.settings_file.exists():
            return

        try:
            with open(self.settings_file, "r", encoding="utf-8") as stream:
                data = json.load(stream)
            if not isinstance(data, dict):
                raise ValueError("preferences root must be a JSON object")

            schema_version = int(data.get("schema_version", 0))
            if schema_version > self.SCHEMA_VERSION:
                self._write_blocked = True
                raise UnsupportedSchemaVersionError(
                    f"preferences schema {schema_version} is newer than supported {self.SCHEMA_VERSION}"
                )

            if isinstance(data.get("sound_enabled"), bool):
                self.sound_enabled = data["sound_enabled"]
            self.volume = self._clamp_volume(data.get("volume"), self.volume)
            if isinstance(data.get("fullscreen"), bool):
                self.fullscreen = data["fullscreen"]

            if schema_version < self.SCHEMA_VERSION:
                self.save()
        except UnsupportedSchemaVersionError as error:
            print(f"[Preferences] Failed to load: {error}")
        except Exception as error:
            backup = backup_corrupt_file(self.settings_file)
            print(f"[Preferences] Failed to load: {error}")
            if backup:
                print(f"[Preferences] Preserved unreadable save at {backup.name}")

    def save(self) -> None:
        if self._write_blocked:
            print("[Preferences] Save skipped because the file uses a newer schema")
            return
        try:
            atomic_write_json(
                self.settings_file,
                {
                    "schema_version": self.SCHEMA_VERSION,
                    "sound_enabled": self.sound_enabled,
                    "volume": self.volume,
                    "fullscreen": self.fullscreen,
                },
            )
        except Exception as error:
            print(f"[Preferences] Failed to save: {error}")
