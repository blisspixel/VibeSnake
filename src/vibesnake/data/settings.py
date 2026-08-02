"""
Global game constants and settings loaded from configuration system.

**Module Purpose:**
Central registry for all game parameters - grid dimensions, timing constants,
visual settings, asset paths. Provides single point of modification for tuning.

**Design Pattern:**
Module-Level Singleton - settings imported once, shared globally as constants

**Benefits:**
1. **DRY Principle**: Single source of truth for all magic numbers
2. **Hot-Swappable**: Change config file → restart → new settings (no recompile)
3. **Type Safety**: Constants are typed and validated on module load
4. **Discoverability**: IDE autocomplete reveals all available settings

**Initialization:**
Loads immediately on first import (module-level code execution).
All constants computed once and cached for lifetime of process.

**Performance:** O(1) access - all values are module-level constants

See: "Code Complete" by McConnell - Chapter on Magic Numbers
"""

import pygame
import os
from vibesnake.data.config import load_config

# Load complete configuration tree from file system
config = load_config()

# ============================================================================
# GRID & SPATIAL DIMENSIONS
# ============================================================================

CELL_SIZE = config["cell_size"]  # Pixels per grid cell (typically 20px)
GRID_WIDTH = config["grid_width"]  # Horizontal cell count (game area width)
GRID_HEIGHT = config["grid_height"]  # Vertical cell count (game area height)
WIDTH = CELL_SIZE * GRID_WIDTH  # Total window width in pixels

# HUD (Heads-Up Display) Configuration
# Game renders on grid BELOW HUD - prevents overlap with score/stats
HUD_HEIGHT = 60  # Fixed HUD bar height in pixels
HUD_GRID_ROWS = HUD_HEIGHT // CELL_SIZE  # Equivalent rows (3 rows for 20px cells)

# Total window height = HUD bar + game grid
# This creates "letterbox" layout: [HUD | Game Area]
HEIGHT = HUD_HEIGHT + (CELL_SIZE * GRID_HEIGHT)

# ============================================================================
# TIMING & FRAME RATE
# ============================================================================

FPS = config["fps"]  # Render framerate (60 Hz standard)
LOGIC_TICK = config["logic_tick"]  # Seconds between game updates (0.1 = 10Hz)

# **Design Note - Decoupled Render & Logic:**
# FPS controls visual smoothness (60 frames/sec)
# LOGIC_TICK controls game speed (10 updates/sec)
# This separation allows smooth animation without affecting difficulty.
#
# Example: FPS=60, LOGIC_TICK=0.05 → 60fps render, 20Hz gameplay (harder)
#          FPS=30, LOGIC_TICK=0.1  → 30fps render, 10Hz gameplay (easier)

# ============================================================================
# COLOR PALETTE (RGB Tuples)
# ============================================================================

# Primary colors from config (user-customizable)
WHITE = tuple(config["colors"].get("text", [255, 255, 255]))
GREEN = tuple(config["colors"].get("snake", [0, 255, 0]))
RED = tuple(config["colors"].get("food", [213, 50, 80]))
BLUE = tuple(config["colors"].get("background", [50, 153, 213]))

# Secondary colors (hardcoded, not in config)
BLACK = (0, 0, 0)  # Used for text shadows, borders
YELLOW = (255, 255, 0)  # Power-up highlights
ORANGE = (255, 165, 0)  # Warning states

# **Rationale for tuple() conversion:**
# Config returns list [r, g, b], but pygame expects tuple (r, g, b).
# Ensures type compatibility with pygame.draw.* functions.

# ============================================================================
# FONT FACTORY
# ============================================================================


def create_font(size: int, *, bold: bool = False) -> pygame.font.Font:
    """Create a retro-readable UI font for the current Pygame font-module lifetime."""
    if size <= 0:
        raise ValueError("font size must be positive")
    from vibesnake.rendering.theme import pixel_font

    # Map legacy point sizes onto the pixel-font base so HUD and menus share one look.
    base_px = max(10, min(48, int(size * 0.75)))
    return pixel_font(base_px, bold=bold)


# ============================================================================
# AUDIO SETTINGS
# ============================================================================

# Compute project root for asset path resolution
# __file__ = .../src/vibesnake/data/settings.py
# _BASE_DIR = .../project_root (4 levels up)
_BASE_DIR = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

# An explicit local overlay permits rights review without placing candidates in Git.
_audio_override = os.environ.get("VIBESNAKE_AUDIO_DIR", "").strip()
AUDIO_DIR = os.path.abspath(
    os.path.expanduser(_audio_override) if _audio_override else os.path.join(_BASE_DIR, "assets", "audio")
)
EAT_SOUND_PATH = os.path.join(AUDIO_DIR, "eat.wav")
MUSIC_PATH = os.path.join(AUDIO_DIR, "music.mp3")
LOST_SOUND_PATH = os.path.join(AUDIO_DIR, "lost.mp3")
MAGNET_SOUND_PATH = os.path.join(AUDIO_DIR, "magnet.mp3")

# Audio configuration flags
SOUND_ENABLED = config["sound"].get("enabled", True)  # Master audio toggle
SOUND_VOLUME = config["sound"].get("volume", 1.0)  # Gain multiplier [0.0, 1.0]

# ============================================================================
# DATA & ASSET PATHS
# ============================================================================

LOGO_PATH = os.path.join(_BASE_DIR, "assets", "images", "logo.png")
CONFIG_PATH = os.path.join(_BASE_DIR, "assets", "config", "config.json")
