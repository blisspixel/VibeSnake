"""Module entry point for ``python -m vibesnake.qa``."""

from __future__ import annotations

import os
import warnings


os.environ.setdefault("PYGAME_HIDE_SUPPORT_PROMPT", "1")
os.environ.setdefault("SDL_VIDEODRIVER", "dummy")
os.environ.setdefault("SDL_AUDIODRIVER", "dummy")
warnings.filterwarnings(
    "ignore",
    message="pkg_resources is deprecated as an API.*",
    category=UserWarning,
)

from vibesnake.qa.cli import main  # noqa: E402


raise SystemExit(main())
