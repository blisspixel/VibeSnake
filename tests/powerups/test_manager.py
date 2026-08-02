"""Lifecycle tests for the power-up coordinator."""

from types import SimpleNamespace
from unittest.mock import Mock, patch

from vibesnake.data import settings
from vibesnake.powerups.magnet import MagnetPowerUp
from vibesnake.powerups.laststand import LastStandPowerUp
from vibesnake.powerups.manager import (
    POWERUP_SPAWN_INTERVAL,
    POWERUP_VISIBLE_DURATION,
    PowerUpManager,
)
from vibesnake.powerups.shield import ShieldPowerUp


def make_game(head=(3, 3)):
    snake = SimpleNamespace(positions_set={head}, get_head=lambda: head)
    return SimpleNamespace(
        snake=snake,
        food=SimpleNamespace(position=(4, 4)),
        detached_segments=[(5, 5)],
        session_powerups_collected=0,
        snake_is_shielded=False,
        visual_effects=Mock(),
    )


def test_expired_uncollected_powerup_is_removed():
    manager = PowerUpManager()
    powerup = ShieldPowerUp((8, 8), duration=5.0)
    powerup.visible_duration = 0.01
    manager.active_powerups.append(powerup)

    manager.update(0.02, make_game())

    assert manager.active_powerups == []


def test_spawn_avoids_food_and_detached_obstacles():
    manager = PowerUpManager()
    manager.spawn = Mock()
    manager.spawn_timer = POWERUP_SPAWN_INTERVAL
    game = make_game()

    manager.update(0.01, game)

    occupied = manager.spawn.call_args.args[0]
    assert game.snake.positions_set <= occupied
    assert game.food.position in occupied
    assert set(game.detached_segments) <= occupied


def test_consume_deactivates_and_removes_matching_effect():
    manager = PowerUpManager()
    game = make_game()
    shield = ShieldPowerUp((8, 8))
    shield.activate(game)
    manager.active_powerups.append(shield)

    assert manager.consume(ShieldPowerUp, game)
    assert not game.snake_is_shielded
    assert manager.active_powerups == []
    assert not manager.consume(ShieldPowerUp, game)


def test_spawn_does_not_duplicate_an_active_effect():
    manager = PowerUpManager()
    game = make_game()
    held = LastStandPowerUp((8, 8))
    held.activate(game)
    manager.active_powerups.append(held)

    with patch("vibesnake.powerups.manager.random.choice", side_effect=lambda options: options[0]):
        manager.spawn(game.snake.positions_set)

    spawned = manager.active_powerups[-1]
    assert isinstance(spawned, ShieldPowerUp)
    assert spawned.visible_duration == POWERUP_VISIBLE_DURATION
    assert sum(isinstance(powerup, LastStandPowerUp) for powerup in manager.active_powerups) == 1


def test_collectible_powerups_exclude_held_and_inactive_effects():
    manager = PowerUpManager()
    game = make_game()
    visible = ShieldPowerUp((7, 7))
    held = LastStandPowerUp((8, 8))
    held.activate(game)
    inactive = ShieldPowerUp((9, 9))
    inactive.active = False
    manager.active_powerups.extend((visible, held, inactive))

    assert manager.collectible_powerups() == (visible,)
    assert manager.collectible_positions() == {visible.position}
    assert manager.has_active_effect(LastStandPowerUp)
    assert not manager.has_active_effect(ShieldPowerUp)

    assert manager.discard_collectibles() == 1
    assert manager.active_powerups == [held, inactive]
    assert held.active


def test_magnet_collectible_draws_below_the_hud():
    magnet = MagnetPowerUp((2, 3))

    with patch("vibesnake.powerups.magnet.pygame.draw.rect") as draw_rect:
        magnet.draw(Mock())

    rendered_rect = draw_rect.call_args_list[0].args[2]
    assert rendered_rect.topleft == (
        2 * settings.CELL_SIZE,
        3 * settings.CELL_SIZE + settings.HUD_HEIGHT,
    )
