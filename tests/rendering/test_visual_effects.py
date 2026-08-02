"""Tests for particles, feedback effects, and procedural backgrounds."""

import pygame

from vibesnake.rendering.visual_effects import (
    BackgroundElement,
    BackgroundRenderer,
    Particle,
    VisualEffectsManager,
)


def test_effect_lifecycle_and_drawing():
    pygame.init()
    surface = pygame.Surface((320, 240))
    effects = VisualEffectsManager()

    effects.add_burst(100, 100, (255, 0, 0), count=3)
    effects.add_powerup_activation_effect(320, 240, (0, 255, 255))
    for multiplier in (1.0, 2.0, 3.0, 5.0):
        effects.add_food_collection_sparkle(120, 80, multiplier)
    effects.particles.append(Particle(10, 10, 0, 0, 1, 1, (255, 255, 255), 2, fade=False))

    effects.trigger_shake(0)
    effects.trigger_shake(20)
    effects.add_power_up_aura((50, 200, 255), 1.0)
    effects.add_stacked_powerup("Shield", (0, 255, 255), 2.0, "S")
    effects.add_stacked_powerup("Magnet", (255, 0, 255), 0.5, "M")
    effects.add_score_popup(80, 80, "+100")

    assert effects.is_hitstop_active()
    assert effects.get_hitstop_time_scale() == 0.0
    effects.draw(surface)
    assert isinstance(effects.get_shake_offset(), tuple)

    effects.update(0.1)
    effects.remove_stacked_powerup("Shield")
    effects.update(3.0)
    assert not effects.is_hitstop_active()
    assert effects.get_hitstop_time_scale() == 1.0
    assert not effects.particles
    assert not effects.text_popups
    assert not effects.active_auras
    assert not effects.stacked_powerups

    effects.clear()
    assert effects.screen_flash_color is None


def test_background_progression_creation_and_drawing(monkeypatch):
    pygame.init()
    surface = pygame.Surface((320, 240))
    background = BackgroundRenderer(320, 240)

    thresholds = [
        (0, "garden"),
        (100, "cliffs"),
        (300, "rainforest"),
        (600, "geothermal"),
        (1000, "temple"),
    ]
    choices = {
        "garden": ["grass", "flower", "rock"],
        "cliffs": ["stone", "boulder", "rock_formation"],
        "rainforest": ["leaf", "vine", "flower"],
        "geothermal": ["glow", "crystal", "stone"],
        "temple": ["tile", "rune", "pillar"],
    }

    for score, environment in thresholds:
        background.set_score(score)
        assert background.current_environment == environment
        for element_type in choices[environment]:
            monkeypatch.setattr(
                "vibesnake.rendering.visual_effects.random.choice",
                lambda values, selected=element_type: selected if selected in values else values[0],
            )
            element = background._create_element_for_environment()
            assert element.element_type == element_type

    background.elements = [
        BackgroundElement(10, 80, "grass", 5, (0, 255, 0), 0, 0.5),
        BackgroundElement(30, 80, "flower", 5, (255, 0, 0), 1, 1.0),
        BackgroundElement(50, 80, "stone", 8, (100, 100, 100), 2, 1.5),
    ]
    background.update(0.5)
    background.draw(surface)
    background.grid_enabled = False
    background.scanlines_enabled = False
    background.draw(surface)
