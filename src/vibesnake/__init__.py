"""Vibe Snake package."""

from __future__ import annotations

try:
    from importlib.metadata import PackageNotFoundError, version
except ImportError:  # pragma: no cover
    from importlib_metadata import PackageNotFoundError, version  # type: ignore

try:
    __version__ = version("vibe-snake")
except PackageNotFoundError:  # pragma: no cover - editable/dev edge cases
    __version__ = "0.2.1"

__all__ = ["__version__"]
