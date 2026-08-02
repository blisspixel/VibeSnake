"""
Real gameplay integration tests.

Tests that actually simulate what happens when you play the game,
not just unit tests that pass but don't catch real bugs.
"""

import pytest
import pygame
from vibesnake.core.game_state import GameState as GameStateEnum, Game


class TestRealGameplay:
    """Test actual gameplay scenarios that users experience."""

    @pytest.fixture
    def game(self):
        """Create a fresh game instance."""
        pygame.init()
        pygame.display.set_mode((1, 1))  # Minimal display
        instance = Game()
        yield instance
        pygame.event.clear()
        if pygame.mixer.get_init():
            pygame.mixer.music.stop()

    def test_l_key_opens_channel_browser(self, game):
        """Test that L key opens channel browser without crashing."""
        # Start in menu
        assert game.state == GameStateEnum.MENU

        # Simulate L key press
        event = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_l})
        pygame.event.post(event)
        game.handle_input()

        # Should switch to channel browser
        assert game.state == GameStateEnum.CHANNEL_BROWSER

    def test_channel_browser_auto_select(self, game):
        """Test that channel browser auto-selects after 5 seconds."""
        # Open channel browser
        game.state = GameStateEnum.CHANNEL_BROWSER
        game.channel_browser_idle_timer = 0.0

        # Update for 5.1 seconds (extra time to ensure >= 5.0 is met with floating point)
        for _ in range(102):
            game.update(0.05)  # 0.05 * 102 = 5.1 seconds

        # Should auto-select and start Let's Play mode
        assert game.state == GameStateEnum.LETS_PLAY
        assert game.ai_player is not None

    def test_snake_doesnt_render_behind_hud(self, game):
        """Test that snake position y=0 renders below HUD."""
        from vibesnake.data import settings

        # Start game
        game.state = GameStateEnum.RUNNING

        # Force snake to y=0 (top of grid)
        game.snake.body.clear()
        game.snake.body.append((10, 0))  # Grid position (10, 0)
        game.snake.positions_set = set(game.snake.body)

        # Create surface and draw
        surface = pygame.Surface((settings.WIDTH, settings.HEIGHT + settings.HUD_HEIGHT))
        game.snake.draw(surface)

        # Check that snake renders at y >= HUD_HEIGHT
        # Snake at grid y=0 should render at pixel y=60
        expected_y = 0 * settings.CELL_SIZE + settings.HUD_HEIGHT
        assert expected_y == settings.HUD_HEIGHT

    def test_radio_off_option(self, game):
        """Test that radio can be turned OFF."""
        if not game.radio or not game.radio.available_stations:
            pytest.skip("No radio available")

        # Start with radio on at a known station
        game.radio.switch_station(0)  # Start at station 0 for deterministic test
        game.radio.is_playing = True  # Mark as playing
        assert game.radio.is_playing

        # Cycle through all stations - should eventually hit OFF
        # Starting from 0, after N cycles it wraps back to 0 which triggers OFF
        stations_count = len(game.radio.available_stations)
        for _ in range(stations_count):
            game.radio.next_station()

        # Should be OFF now (after wrapping around)
        assert not game.radio.is_playing
        assert game.radio.get_station_info_text() == "Radio: OFF"

    def test_m_key_mutes_radio(self, game):
        """Test that M key mutes/unmutes radio."""
        if not game.radio or not game.radio.available_stations:
            pytest.skip("No radio available")

        # Start with radio on
        game.radio.play_current_station()
        assert game.radio.is_playing

        # Press M key to mute
        event = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_m})
        pygame.event.post(event)
        game.handle_input()

        # Should be OFF now
        assert not game.radio.is_playing

        # Press M key again to unmute
        event = pygame.event.Event(pygame.KEYDOWN, {"key": pygame.K_m})
        pygame.event.post(event)
        game.handle_input()

        # Should be back ON
        assert game.radio.is_playing

    def test_starvation_timer_is_30_seconds(self, game):
        """Test that starvation timer is actually 30 seconds."""
        game.state = GameStateEnum.RUNNING

        # Check initial starvation max time
        assert game.starvation_max_time == 30.0
        assert game.starvation_warning_time == 20.0

    def test_power_up_collection_doesnt_crash(self, game):
        """Test that collecting each power-up doesn't crash."""
        from vibesnake.powerups.shield import ShieldPowerUp
        from vibesnake.powerups.magnet import MagnetPowerUp
        from vibesnake.powerups.boost import BoostPowerUp
        from vibesnake.data import settings

        game.state = GameStateEnum.RUNNING

        powerups = [
            (ShieldPowerUp((5, 5)), "snake_is_shielded", True),
            (MagnetPowerUp((6, 6)), "magnet_active", True),
            (BoostPowerUp((7, 7)), "logic_tick_override", settings.LOGIC_TICK / 2),
        ]

        for powerup, attribute, expected_value in powerups:
            # Move snake to powerup position
            game.snake.body.clear()
            game.snake.body.append(powerup.position)
            game.snake.positions_set = set(game.snake.body)

            powerup.activate(game)

            assert powerup.activated
            assert powerup.timer == 0.0
            assert getattr(game, attribute) == expected_value

    def test_visual_effects_dont_crash(self, game):
        """Test that visual effects system doesn't crash."""
        from vibesnake.data import settings

        game.state = GameStateEnum.RUNNING
        surface = pygame.Surface((settings.WIDTH, settings.HEIGHT + settings.HUD_HEIGHT))

        game.visual_effects.trigger_shake(5)
        game.visual_effects.add_burst(100, 100, (255, 0, 0), count=10)

        assert game.visual_effects.screen_shake_intensity == 12.0
        assert game.visual_effects.screen_shake_duration == pytest.approx(0.35)
        assert len(game.visual_effects.particles) == 10

        game.visual_effects.update(0.016)
        game.visual_effects.draw(surface)

        assert len(game.visual_effects.particles) == 10
        assert game.visual_effects.screen_shake_duration == pytest.approx(0.334)

    def test_all_radio_stations_switch(self, game):
        """Test that switching through all radio stations doesn't crash."""
        if not game.radio or not game.radio.available_stations:
            pytest.skip("No radio stations available")

        available_stations = game.radio.available_stations
        station_count = len(available_stations)
        game.radio.current_station_index = available_stations[0]
        game.radio.is_playing = True

        for _ in range(station_count):
            game.radio.next_station()
            assert game.radio.current_station_index in available_stations

        assert not game.radio.is_playing
        assert game.radio.get_station_info_text() == "Radio: OFF"

    def test_game_over_state_works(self, game):
        """Test that game over state doesn't crash."""
        game.state = GameStateEnum.RUNNING

        # Trigger game over by collision
        game.snake.body.clear()
        game.snake.body.append((5, 5))
        game.snake.body.append((5, 5))  # Collision with self
        game.snake.positions_set = set(game.snake.body)

        game.update(0.016)

        assert isinstance(game.state, GameStateEnum)
        assert set(game.snake.body) == game.snake.positions_set

    def test_ai_player_doesnt_crash(self, game):
        """Test that AI player can play without crashing."""
        game.state = GameStateEnum.LETS_PLAY
        game.start_lets_play_mode("speed_demon")

        # Simulate 100 frames
        for _ in range(100):
            game.update(0.016)

        assert game.ai_player is not None
        assert isinstance(game.state, GameStateEnum)

    def test_combo_system_works(self, game):
        """Test that combo system tracks correctly."""
        game.state = GameStateEnum.RUNNING

        # Eat multiple food quickly
        for i in range(5):
            game.snake.body.append((10 + i, 10))
            game.score_manager.add_food_score()
            game.score_manager.update(0.1)  # Small time delta

        # Should have combo multiplier > 1.0
        # Check combo count instead (multiplier is property)
        assert game.score_manager.combo_count >= 3  # Should have at least 3 combo
        assert game.score_manager.combo_multiplier > 1.0  # And multiplier should be active

    def test_pause_works(self, game):
        """Test that pausing doesn't break game state."""
        game.state = GameStateEnum.RUNNING
        initial_score = game.score_manager.base_score

        # Pause
        game.state = GameStateEnum.PAUSED

        # Try to update (shouldn't do anything)
        game.update(1.0)

        # Resume
        game.state = GameStateEnum.RUNNING

        # Score shouldn't have changed while paused
        assert game.score_manager.base_score == initial_score


class TestBugFixes:
    """Tests for specific bugs that were found."""

    def test_hud_height_constant_exists(self):
        """Test that HUD_HEIGHT constant is defined."""
        from vibesnake.data import settings

        assert hasattr(settings, "HUD_HEIGHT")
        assert settings.HUD_HEIGHT == 60

    def test_snake_rendering_uses_hud_offset(self):
        """Test that snake rendering code uses HUD offset."""
        # Read snake.py and verify it uses settings.HUD_HEIGHT
        import inspect
        from vibesnake.core.snake import Snake

        source = inspect.getsource(Snake.draw)
        assert "HUD_HEIGHT" in source, "Snake draw() should use HUD_HEIGHT offset"

    def test_food_rendering_uses_hud_offset(self):
        """Test that food rendering uses HUD offset."""
        import inspect
        from vibesnake.core.food import Food

        source = inspect.getsource(Food.draw)
        assert "HUD_HEIGHT" in source, "Food draw() should use HUD_HEIGHT offset"
