"""End-to-end contracts for power-ups that alter the main game loop."""

from collections import deque

import pygame
import pytest

from vibesnake.core.enums import Direction, GameState
from vibesnake.core.exceptions import GridFullException
from vibesnake.core.game_state import Game
from vibesnake.core.near_miss import NearMissEvent
from vibesnake.data import settings
from vibesnake.powerups.bait import BaitPowerUp
from vibesnake.powerups.boost import BoostPowerUp
from vibesnake.powerups.gluttony import GluttonyPowerUp
from vibesnake.powerups.laststand import LastStandPowerUp
from vibesnake.powerups.phaseshift import PhaseShiftPowerUp
from vibesnake.powerups.segmentdetach import SegmentDetachPowerUp
from vibesnake.powerups.shield import ShieldPowerUp
from vibesnake.powerups.slowmo import SlowMoPowerUp


@pytest.fixture
def game():
    pygame.init()
    pygame.display.set_mode((1, 1))
    instance = Game()
    instance.state = GameState.RUNNING
    instance.sound_on = False
    instance.radio = None
    instance.visual_effects.clear()
    return instance


def set_snake(game, body, direction=Direction.RIGHT):
    game.snake.body = deque(body)
    game.snake.positions_set = set(body)
    game.snake.direction = direction
    game.snake.next_directions.clear()


def activate(game, powerup):
    game.powerups.active_powerups.append(powerup)
    powerup.activate(game)
    game.visual_effects.clear()


def advance_move(game, dt=None):
    game.logic_timer = 0.0
    game.update(dt or settings.LOGIC_TICK)


def collision_body():
    return [(1, 1), (1, 2), (2, 2), (2, 1), (3, 1)]


def test_shield_absorbs_exactly_one_collision(game):
    set_snake(game, collision_body(), Direction.LEFT)
    game.food.position = (20, 20)
    shield = ShieldPowerUp((10, 10))
    activate(game, shield)

    advance_move(game)

    assert game.state == GameState.RUNNING
    assert not game.snake_is_shielded
    assert shield not in game.powerups.active_powerups
    assert game.starvation_timer == pytest.approx(settings.LOGIC_TICK)

    game.visual_effects.clear()
    advance_move(game)

    assert game.state != GameState.RUNNING


def test_shield_collision_does_not_defer_starvation_deadline(game):
    set_snake(game, collision_body(), Direction.LEFT)
    game.food.position = (20, 20)
    game.starvation_timer = game.starvation_max_time - settings.LOGIC_TICK
    activate(game, ShieldPowerUp((10, 10)))

    advance_move(game)

    assert game.state != GameState.RUNNING
    assert not game.snake_is_shielded
    assert game.starvation_timer >= game.starvation_max_time


def test_shield_collects_on_entry_without_a_render_frame_delay(game):
    set_snake(game, [(5, 5)], Direction.RIGHT)
    game.food.position = (20, 20)
    shield = ShieldPowerUp((6, 5))
    game.powerups.active_powerups.append(shield)

    advance_move(game)

    assert game.snake.get_head() == (6, 5)
    assert game.snake_is_shielded
    assert shield.activated
    assert shield.timer == 0.0
    assert game.session_powerups_collected == 1

    game.update(settings.LOGIC_TICK / 2)

    assert game.session_powerups_collected == 1


def test_phase_shift_crosses_self_collision_and_preserves_occupancy(game):
    set_snake(game, collision_body(), Direction.LEFT)
    game.food.position = (20, 20)
    activate(game, PhaseShiftPowerUp((10, 10)))

    advance_move(game)

    assert game.state == GameState.RUNNING
    assert game.snake.get_head() == (2, 1)
    assert game.snake.positions_set == set(game.snake.body)


def test_gluttony_scores_for_food_without_growing(game):
    set_snake(game, [(5, 5)])
    game.food.position = game.snake.peek_next_head()
    starting_length = len(game.snake.body)
    activate(game, GluttonyPowerUp((10, 10)))

    advance_move(game)

    assert game.state == GameState.RUNNING
    assert len(game.snake.body) == starting_length
    assert game.score_manager.base_score > 0
    assert game.session_food_eaten == 1


def test_magnet_does_not_pull_food_away_from_the_next_head_cell(game):
    set_snake(game, [(5, 5)])
    game.food.position = game.snake.peek_next_head()
    game.magnet_active = True

    advance_move(game)

    assert game.snake.get_head() == (6, 5)
    assert game.session_food_eaten == 1


def test_magnet_does_not_pull_food_onto_a_detached_obstacle(game):
    set_snake(game, [(5, 5)])
    game.food.position = (7, 5)
    game.detached_segments = [(6, 5)]
    game.magnet_active = True

    game.update(settings.LOGIC_TICK / 2)

    assert game.food.position == (7, 5)


def test_magnet_does_not_pull_food_onto_a_collectible_powerup(game):
    set_snake(game, [(5, 5)])
    game.food.position = (7, 5)
    game.powerups.active_powerups.append(ShieldPowerUp((6, 5)))
    game.magnet_active = True

    game.update(settings.LOGIC_TICK / 2)

    assert game.food.position == (7, 5)


def test_overlapping_cadence_effects_expire_independently(game):
    slowmo = SlowMoPowerUp((10, 10))
    boost = BoostPowerUp((11, 10))
    activate(game, slowmo)
    activate(game, boost)

    assert game.logic_tick_override == pytest.approx(settings.LOGIC_TICK)

    slowmo.deactivate(game)
    assert game.logic_tick_override == pytest.approx(settings.LOGIC_TICK / 2)

    boost.deactivate(game)
    assert game.logic_tick_override is None


def test_bait_is_consumed_by_next_food_respawn(game, monkeypatch):
    set_snake(game, [(5, 5)])
    game.food.position = game.snake.peek_next_head()
    bait = BaitPowerUp((10, 10))
    bait.activate(game)
    visible_powerup = ShieldPowerUp((20, 20))
    game.powerups.active_powerups.append(visible_powerup)
    captured = {}

    def respawn(occupied, preferred_position=None):
        captured["occupied"] = occupied
        captured["preferred_position"] = preferred_position
        game.food.position = (6, 6)

    monkeypatch.setattr(game.food, "respawn", respawn)

    advance_move(game)

    assert captured["preferred_position"] == (5, 5)
    assert visible_powerup.position in captured["occupied"]
    assert game.bait_position is None


def test_last_stand_revives_collision_once_and_shrinks_snake(game):
    set_snake(game, collision_body(), Direction.LEFT)
    game.food.position = (20, 20)
    last_stand = LastStandPowerUp((10, 10))
    activate(game, last_stand)

    advance_move(game)

    assert game.state == GameState.RUNNING
    assert not game.last_stand_held
    assert last_stand not in game.powerups.active_powerups
    assert len(game.snake.body) == 3
    assert game.snake.get_head() == (3, 1)
    assert game.revival_invulnerability_timer == pytest.approx(3.0)

    advance_move(game)
    assert game.state == GameState.RUNNING


def test_last_stand_intercepts_starvation(game):
    set_snake(game, [(5, 5), (6, 5), (7, 5), (8, 5)])
    game.food.position = (20, 20)
    last_stand = LastStandPowerUp((10, 10))
    activate(game, last_stand)
    game.starvation_timer = game.starvation_max_time - settings.LOGIC_TICK

    advance_move(game)

    assert game.state == GameState.RUNNING
    assert game.starvation_timer == 0.0
    assert len(game.snake.body) == 2
    assert not game.last_stand_held


def test_food_on_starvation_deadline_rescues_without_consuming_last_stand(game):
    set_snake(game, [(5, 5), (6, 5), (7, 5), (8, 5)])
    game.food.position = game.snake.peek_next_head()
    last_stand = LastStandPowerUp((10, 10))
    activate(game, last_stand)
    game.starvation_timer = game.starvation_max_time - settings.LOGIC_TICK

    advance_move(game)

    assert game.state == GameState.RUNNING
    assert game.starvation_timer == 0.0
    assert game.last_stand_held
    assert last_stand in game.powerups.active_powerups


def test_full_grid_food_respawn_completes_the_run_as_a_victory(game, monkeypatch):
    set_snake(game, [(5, 5)])
    game.food.position = game.snake.peek_next_head()
    grid_size = settings.GRID_WIDTH * settings.GRID_HEIGHT

    def fail_respawn(occupied, preferred_position=None):
        del occupied, preferred_position
        raise GridFullException(grid_size, grid_size)

    monkeypatch.setattr(game.food, "respawn", fail_respawn)

    advance_move(game)

    assert game.state == GameState.GAME_OVER
    assert game.food.position is None
    assert game.bait_position is None
    assert game.game_over_message.startswith("GRID MASTER!")


def test_food_respawn_discards_a_pickup_before_declaring_grid_victory(game, monkeypatch):
    set_snake(game, [(5, 5)])
    game.food.position = game.snake.peek_next_head()
    pickup = ShieldPowerUp((20, 20))
    game.powerups.active_powerups.append(pickup)
    attempts = []

    def respawn(occupied, preferred_position=None):
        del preferred_position
        attempts.append(set(occupied))
        if pickup.position in occupied:
            raise GridFullException(len(occupied), settings.GRID_WIDTH * settings.GRID_HEIGHT)
        game.food.position = (21, 20)

    monkeypatch.setattr(game.food, "respawn", respawn)

    advance_move(game)

    assert game.state == GameState.RUNNING
    assert game.food.position == (21, 20)
    assert pickup not in game.powerups.active_powerups
    assert len(attempts) == 2


def test_food_respawn_clears_detached_obstacles_before_grid_victory(game, monkeypatch):
    set_snake(game, [(5, 5)])
    game.food.position = game.snake.peek_next_head()
    game.detached_segments = [(20, 20)]
    game.detached_segments_timer = 5.0
    attempts = []

    detached_position = game.detached_segments[0]

    def respawn_without_stale_state(occupied, preferred_position=None):
        del preferred_position
        attempts.append(set(occupied))
        if detached_position in occupied:
            raise GridFullException(len(occupied), settings.GRID_WIDTH * settings.GRID_HEIGHT)
        game.food.position = (21, 20)

    monkeypatch.setattr(game.food, "respawn", respawn_without_stale_state)

    advance_move(game)

    assert game.state == GameState.RUNNING
    assert game.food.position == (21, 20)
    assert game.detached_segments == []
    assert game.detached_segments_timer == 0.0
    assert len(attempts) == 2


def test_starvation_deadline_completes_legal_move_before_death(game):
    set_snake(game, [(5, 5)])
    game.food.position = (20, 20)
    starting_head = game.snake.get_head()
    game.starvation_timer = game.starvation_max_time - settings.LOGIC_TICK

    advance_move(game)

    assert game.state != GameState.RUNNING
    assert game.snake.get_head() != starting_head


def test_segment_detach_shortens_tail_and_obstacles_expire(game):
    set_snake(game, [(x, 0) for x in range(10)])
    detach = SegmentDetachPowerUp((10, 10))

    detach.activate(game)

    assert list(game.snake.body) == [(x, 0) for x in range(5, 10)]
    assert game.detached_segments == [(x, 0) for x in range(5)]
    assert game.snake.positions_set == set(game.snake.body)

    game.detached_segments_timer = 0.01
    game.update(0.02)

    assert game.detached_segments == []
    assert game.detached_segments_timer == 0.0


def test_detached_segment_is_a_collision_obstacle(game):
    game.detached_segments = [(0, 0)]
    game.detached_segments_timer = 10.0
    set_snake(game, [(settings.GRID_WIDTH - 1, 0)], Direction.RIGHT)
    game.food.position = (20, 20)

    advance_move(game)

    assert game.state != GameState.RUNNING


def test_rewarded_near_miss_increments_run_counter(game, monkeypatch):
    set_snake(game, [(x, 5) for x in range(8)])
    game.food.position = (20, 20)
    event = NearMissEvent(
        type="near_miss",
        position=(8, 5),
        score_bonus=1,
        message="CLOSE CALL!",
        color=(255, 200, 0),
    )
    monkeypatch.setattr(game.near_miss, "check_near_miss", lambda *args: event)

    advance_move(game)

    assert game.session_near_misses == 1


def test_clutch_and_style_events_do_not_count_as_spatial_near_misses(game):
    set_snake(game, [(5, 5)])
    game.food.position = game.snake.peek_next_head()
    game.starvation_timer = game.starvation_max_time - 1.0
    activate(game, BoostPowerUp((10, 10)))

    advance_move(game)

    assert game.session_near_misses == 0
    assert {event.type for event, _timer in game.near_miss.recent_events} == {
        "clutch_eat",
        "style_points",
    }


def test_reset_clears_transient_powerup_state(game):
    game.snake_is_shielded = True
    game.snake_phase_shift_active = True
    game.snake_gluttony_active = True
    game.magnet_active = True
    game.last_stand_held = True
    game.bait_position = (1, 1)
    game.revival_invulnerability_timer = 2.0
    game.detached_segments = [(2, 2)]
    game.detached_segments_timer = 5.0
    game.session_near_misses = 4
    game.visual_effects.add_stacked_powerup("Old", (255, 255, 255), 5.0)

    game.reset()

    assert not game.snake_is_shielded
    assert not game.snake_phase_shift_active
    assert not game.snake_gluttony_active
    assert not game.magnet_active
    assert not game.last_stand_held
    assert game.bait_position is None
    assert game.revival_invulnerability_timer == 0.0
    assert game.detached_segments == []
    assert game.detached_segments_timer == 0.0
    assert game.session_near_misses == 0
    assert game.visual_effects.stacked_powerups == []
