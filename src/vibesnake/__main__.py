"""
Vibe Snake - package entry point.

Play:
  vibesnake
  python -m vibesnake

Update from GitHub main:
  vibesnake update
  python -m vibesnake update
"""

from __future__ import annotations

import os

os.environ.setdefault("PYGAME_HIDE_SUPPORT_PROMPT", "1")


def main(argv: list[str] | None = None) -> int:
    """CLI entry point used by `vibesnake` and `python -m vibesnake`."""
    from vibesnake.cli import main as cli_main

    return cli_main(argv)


if __name__ == "__main__":
    raise SystemExit(main())
