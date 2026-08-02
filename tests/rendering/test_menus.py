"""Headless smoke tests for every menu and overlay."""

import pygame
import pytest

from vibesnake.ai.player import get_all_ai_personalities
from vibesnake.core.achievements import AchievementManager
from vibesnake.core.customization import (
    ACCESSORIES,
    COLOR_PRESETS,
    EYE_STYLES,
    PATTERN_OPTIONS,
    TRAILS,
    SnakeCustomization,
)
from vibesnake.core.high_scores import HighScoreTable
from vibesnake.core.player_profile import PlayerProfile
from vibesnake.data import settings
from vibesnake.rendering.menus import Menu


@pytest.fixture(scope="module")
def menu():
    """Create a menu backed by the SDL dummy display."""
    pygame.init()
    screen = pygame.display.set_mode((settings.WIDTH, settings.HEIGHT))
    yield Menu(screen)


def test_primary_screens_render(menu, tmp_path):
    """Every top-level menu should render without raising an exception."""
    menu.draw_title_screen()
    menu.draw_pause_overlay()
    menu.draw_help_overlay()
    menu.draw_lets_play_overlay("Test Snake", "Moves with purpose", 123, 7)

    personalities = list(get_all_ai_personalities().items())
    menu.draw_channel_browser(personalities, 0)
    menu.draw_channel_browser(personalities, len(personalities) - 1)

    menu.draw_name_entry_screen("PLAYER", 123, True)
    menu.draw_name_entry_screen("", 0, False)

    for selected in range(3):
        menu.draw_settings_menu(selected, selected != 1, selected / 2)

    high_scores = HighScoreTable(tmp_path)
    menu.draw_high_scores(high_scores)
    high_scores.add_score("ADA", 300)
    high_scores.add_score("BOB", 100)
    menu.draw_high_scores(high_scores)


def test_game_over_variants_and_messages_render(menu):
    """Both celebration and regular game-over variants should render."""
    assert menu.choose_game_over_message(500, 400, True)
    for score in (1, 25, 75, 120, 195):
        assert menu.choose_game_over_message(score, 200, False)

    menu.draw_game_over_overlay(500, 500, True)
    menu.draw_game_over_overlay(10, 100, False)
    menu.draw_game_over_overlay(10, 100, False, "Try again", True, 1.5)
    menu.draw_game_over_overlay(100, 100, False, "Try again", True, 0)


def test_customization_variants_render(menu, tmp_path):
    """All preview layers and the locked-item treatment should be drawable."""
    profile = PlayerProfile(tmp_path)
    color_options = list(COLOR_PRESETS.items())
    option_sets = [color_options, PATTERN_OPTIONS, EYE_STYLES, ACCESSORIES, TRAILS]
    previews = [
        SnakeCustomization(pattern="stripes", eye_style="cute", accessory="hat", trail="sparkle"),
        SnakeCustomization(pattern="dots", eye_style="angry", accessory="crown", trail="smoke"),
        SnakeCustomization(pattern="scales", eye_style="sleepy", accessory="sunglasses", trail="rainbow"),
        SnakeCustomization(eye_style="derp", accessory="headphones", trail="fire"),
        SnakeCustomization(
            base_color=(0, 200, 255),
            secondary_color=(255, 0, 200),
            eye_style="laser",
            accessory="bowtie",
        ),
    ]

    for category, (preview, options) in enumerate(zip(previews, option_sets)):
        selected = 9 if category == 0 else min(category, len(options) - 1)
        menu.draw_customization_menu(
            preview,
            category,
            selected,
            options,
            player_profile=profile,
            notification="Saved",
        )

    menu.draw_customization_menu(previews[0], 0, 0, color_options)


def test_achievement_views_render(menu):
    """Achievement gallery and toast animation states should be drawable."""
    manager = AchievementManager()
    manager.check_achievement("first_bite", score=1)
    manager.check_achievement("century", score=100)
    achievement = manager.achievements["first_bite"]

    menu.draw_achievements_menu(manager.achievements, 0)
    menu.draw_achievements_menu(manager.achievements, 4)
    for timer in (4.0, 2.0, 0.25):
        menu.draw_achievement_notification(achievement, timer)


@pytest.mark.parametrize(
    ("hue", "expected"),
    [
        (0, (255, 0, 0)),
        (60, (255, 255, 0)),
        (120, (0, 255, 0)),
        (180, (0, 255, 255)),
        (240, (0, 0, 255)),
        (300, (255, 0, 255)),
    ],
)
def test_hsv_conversion(menu, hue, expected):
    assert menu._hsv_to_rgb(hue, 1.0, 1.0) == expected


def test_renderers_reacquire_fonts_after_pygame_reinitialization(tmp_path):
    """New renderers never retain font objects from a closed Pygame lifetime."""
    from vibesnake.rendering.hud import HUD

    pygame.quit()
    pygame.init()
    screen = pygame.display.set_mode((settings.WIDTH, settings.HEIGHT))
    fresh_menu = Menu(screen)
    fresh_hud = HUD(HighScoreTable(tmp_path))

    fresh_menu.draw_pause_overlay()
    fresh_hud.draw_score(screen, score=10)
