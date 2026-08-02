"""Filesystem locations used by persistent game data."""

import os
import shutil
import sys
from pathlib import Path
from typing import Mapping, Optional, Union

from vibesnake.data.json_store import atomic_write_json
from vibesnake.utils.logger import get_logger


PathLike = Union[str, Path]
logger = get_logger(__name__)
APP_DIRECTORY_NAME = "VibeSnake"
LEGACY_MIGRATION_MARKER = ".legacy-data-migrated-v1.json"
SAVE_FILE_NAMES = (
    "player_profile.json",
    "customization.json",
    "high_scores.json",
    "highscore.json",
    "preferences.json",
)


def _platform_data_dir(
    platform_name: str,
    environment: Mapping[str, str],
    home: Path,
) -> Path:
    """Resolve the conventional per-user data directory for a platform."""
    if platform_name == "win32":
        local_app_data = environment.get("LOCALAPPDATA")
        if local_app_data:
            configured_root = Path(local_app_data).expanduser()
            if configured_root.is_absolute():
                return configured_root / APP_DIRECTORY_NAME
        return home / "AppData" / "Local" / APP_DIRECTORY_NAME

    if platform_name == "darwin":
        return home / "Library" / "Application Support" / APP_DIRECTORY_NAME

    xdg_data_home = environment.get("XDG_DATA_HOME")
    if xdg_data_home:
        configured_root = Path(xdg_data_home).expanduser()
        if configured_root.is_absolute():
            return configured_root / APP_DIRECTORY_NAME.lower()
    return home / ".local" / "share" / APP_DIRECTORY_NAME.lower()


def _legacy_data_dir() -> Path:
    """Return the source-checkout data directory used before schema version 1."""
    return Path(__file__).resolve().parents[3] / "data"


def _migrate_legacy_saves(target: Path, legacy: Optional[Path] = None) -> None:
    """Copy known legacy saves once without deleting or overwriting originals."""
    legacy = legacy or _legacy_data_dir()
    marker = target / LEGACY_MIGRATION_MARKER
    if marker.exists() or not legacy.exists() or legacy.resolve() == target.resolve():
        return

    target.mkdir(parents=True, exist_ok=True)
    copied_files = []
    for file_name in SAVE_FILE_NAMES:
        source = legacy / file_name
        destination = target / file_name
        if source.is_file() and not destination.exists():
            shutil.copy2(source, destination)
            copied_files.append(file_name)

    atomic_write_json(
        marker,
        {
            "schema_version": 1,
            "source": str(legacy),
            "copied_files": copied_files,
        },
    )


def get_data_dir(override: Optional[PathLike] = None) -> Path:
    """Return the directory used for saves and player settings.

    Tests and portable launchers can set ``VIBESNAKE_DATA_DIR``. Normal runs use
    the operating system's per-user data directory. On first use, known save
    files are copied from the former source-checkout location without deleting
    the originals.
    """
    if override is not None:
        return Path(override)

    configured = os.environ.get("VIBESNAKE_DATA_DIR")
    if configured:
        return Path(configured).expanduser()

    target = _platform_data_dir(sys.platform, os.environ, Path.home())
    try:
        _migrate_legacy_saves(target)
    except OSError as error:
        logger.warning(
            "Legacy save migration failed; continuing with the OS user-data directory: %s",
            error,
        )
    return target
