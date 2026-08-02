"""Locate the Vibe Snake source checkout used for play and updates."""

from __future__ import annotations

from pathlib import Path
import tomllib


DEFAULT_REMOTE = "https://github.com/blisspixel/VibeSnake.git"
DEFAULT_BRANCH = "main"
PACKAGE_MARKER = "vibe-snake"


def find_checkout_root(start: Path | None = None) -> Path | None:
    """Return the repository root that owns pyproject.toml and assets/, if any."""
    current = (start or Path.cwd()).resolve()
    for candidate in (current, *current.parents):
        if _is_checkout_root(candidate):
            return candidate
    # Editable installs still resolve relative to this package file.
    package_file = Path(__file__).resolve()
    for candidate in package_file.parents:
        if _is_checkout_root(candidate):
            return candidate
    return None


def _is_checkout_root(path: Path) -> bool:
    pyproject = path / "pyproject.toml"
    assets = path / "assets"
    if not pyproject.is_file() or not assets.is_dir():
        return False
    try:
        document = tomllib.loads(pyproject.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, tomllib.TOMLDecodeError):
        return False
    project = document.get("project")
    if not isinstance(project, dict):
        return False
    return project.get("name") == PACKAGE_MARKER


def radio_track_count(root: Path) -> int:
    """Count committed radio tracks available to the offline station network."""
    radio_dir = root / "assets" / "audio" / "radio"
    if not radio_dir.is_dir():
        return 0
    return sum(1 for path in radio_dir.glob("*.mp3") if path.is_file() and path.stat().st_size > 0)
