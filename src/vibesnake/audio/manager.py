"""Load authored audio when present and provide small deterministic cue fallbacks."""

from __future__ import annotations

from array import array
import math
import os
import sys

import pygame

from vibesnake.data import settings


try:
    pygame.mixer.init()
    mixer_ready = True
except pygame.error as error:
    print(f"[Assets] Audio mixer unavailable: {error}")
    mixer_ready = False


def _synthesize_tone(
    start_frequency: float,
    end_frequency: float,
    duration_seconds: float,
) -> pygame.mixer.Sound | None:
    """Create a quiet stereo-safe 16-bit chirp using the active mixer format."""
    mixer_format = pygame.mixer.get_init()
    if not mixer_ready or mixer_format is None:
        return None
    sample_rate, sample_format, channels = mixer_format
    if sample_format != -16 or channels <= 0:
        return None

    frame_count = max(1, round(sample_rate * duration_seconds))
    frequency_delta = end_frequency - start_frequency
    samples = array("h")
    for frame in range(frame_count):
        elapsed = frame / sample_rate
        progress = frame / frame_count
        envelope = math.sin(math.pi * progress) ** 2
        phase = 2 * math.pi * (start_frequency * elapsed + 0.5 * frequency_delta * elapsed * progress)
        value = round(32767 * 0.16 * envelope * math.sin(phase))
        samples.extend([value] * channels)
    if sys.byteorder != "little":
        samples.byteswap()
    return pygame.mixer.Sound(buffer=samples.tobytes())


def load_sound(
    path: str,
    fallback: tuple[float, float, float] | None = None,
) -> pygame.mixer.Sound | None:
    """Load a cue, falling back to a deterministic tone when configured."""
    if mixer_ready and os.path.isfile(path):
        try:
            return pygame.mixer.Sound(path)
        except (OSError, pygame.error) as error:
            print(f"[Assets] Audio cue is unreadable: {os.path.basename(path)}: {error}")
    if fallback is not None:
        return _synthesize_tone(*fallback)
    return None


EAT_SOUND = load_sound(settings.EAT_SOUND_PATH, (660.0, 990.0, 0.09))
LOST_SOUND = load_sound(settings.LOST_SOUND_PATH, (220.0, 90.0, 0.32))
MAGNET_SOUND = load_sound(settings.MAGNET_SOUND_PATH, (330.0, 660.0, 0.16))


def play_music() -> None:
    """Loop the optional local core track when it exists and decodes."""
    if not mixer_ready or not os.path.isfile(settings.MUSIC_PATH):
        return
    try:
        pygame.mixer.music.load(settings.MUSIC_PATH)
        pygame.mixer.music.play(-1)
    except (OSError, pygame.error) as error:
        print(f"[Assets] Core music is unavailable: {error}")
