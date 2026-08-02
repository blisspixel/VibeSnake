"""
Vibe Snake - Main entry point

Run with: python -m vibesnake
"""

import os

os.environ["PYGAME_HIDE_SUPPORT_PROMPT"] = "1"

from vibesnake.core.game_state import Game
import pygame
import traceback


def main() -> int:
    """Run Vibe Snake and return a process-compatible status code."""
    try:
        Game().run()
    except Exception:
        print("[Main] Game crashed with error:")
        traceback.print_exc()
        return 1
    finally:
        pygame.quit()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
