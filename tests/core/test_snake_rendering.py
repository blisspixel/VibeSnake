"""Headless coverage for snake animation and cosmetic rendering."""

from collections import deque

import pygame
import pytest

from vibesnake.core.customization import SnakeCustomization
from vibesnake.core.enums import Direction
from vibesnake.core.snake import Snake
from vibesnake.data import settings


def _snake_with(customization):
    snake = Snake(customization)
    snake.body = deque([(10, 10), (11, 10), (12, 10)])
    snake.positions_set = set(snake.body)
    return snake


def test_cosmetic_layers_render_and_animate():
    pygame.init()
    surface = pygame.Surface((settings.WIDTH, settings.HEIGHT))
    variants = [
        SnakeCustomization(pattern="stripes", eye_style="cute", accessory="hat", trail="sparkle"),
        SnakeCustomization(pattern="dots", eye_style="angry", accessory="crown", trail="smoke"),
        SnakeCustomization(pattern="scales", eye_style="sleepy", accessory="sunglasses", trail="rainbow"),
        SnakeCustomization(pattern="checker", eye_style="derp", accessory="headphones", trail="fire"),
        SnakeCustomization(
            base_color=(255, 0, 0),
            secondary_color=(0, 0, 255),
            pattern="zigzag",
            eye_style="laser",
            accessory="bowtie",
        ),
    ]
    effects = ["shield", "boost", "phase", "gluttony", "magnet"]

    for direction, customization, effect in zip(Direction, variants[:4], effects[:4]):
        snake = _snake_with(customization)
        snake.direction = direction
        snake.add_power_up_visual(effect)
        snake.add_power_up_visual(effect)
        snake.set_starvation_warning(0.5)
        snake.update_animation(0.1)
        snake.draw(surface)
        snake.remove_power_up_visual(effect)
        snake.remove_power_up_visual(effect)

    snake = _snake_with(variants[4])
    snake.add_power_up_visual(effects[4])
    snake.draw(surface)

    default_snake = _snake_with(None)
    default_snake.draw(surface)
    default_snake.body.clear()
    default_snake.draw(surface)


def test_trail_fire_color_stages_and_particle_expiry():
    pygame.init()
    surface = pygame.Surface((settings.WIDTH, settings.HEIGHT))
    snake = _snake_with(SnakeCustomization(trail="fire"))
    snake.trail_particles = [
        {"x": 20, "y": 80, "age": age, "max_age": 1.0, "velocity_x": 0, "velocity_y": 0} for age in (0.1, 0.3, 0.6, 0.8)
    ]
    snake.draw_trail(surface)
    snake.update_animation(2.0)
    snake.update_animation(0.1)
    assert snake.trail_particles

    no_trail = Snake()
    no_trail.draw_trail(surface)


@pytest.mark.parametrize(
    ("rgb", "expected_hue"),
    [
        ((128, 128, 128), 0),
        ((255, 0, 0), 0),
        ((0, 255, 0), 120),
        ((0, 0, 255), 240),
        ((0, 0, 0), 0),
    ],
)
def test_rgb_to_hsv_color_sectors(rgb, expected_hue):
    hue, saturation, value = Snake()._rgb_to_hsv(*rgb)
    assert hue == expected_hue
    assert 0 <= saturation <= 1
    assert 0 <= value <= 1


@pytest.mark.parametrize("hue", [0, 60, 120, 180, 240, 300])
def test_hsv_to_rgb_color_sectors(hue):
    assert all(0 <= channel <= 255 for channel in Snake()._hsv_to_rgb(hue, 1, 1))
