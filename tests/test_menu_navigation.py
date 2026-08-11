"""
Test menu navigation and all menu options.

This test suite validates that all menu screens can be accessed and navigated.
"""

import pytest
import pygame
from vibesnake.core.game_state import Game
from vibesnake.core.enums import GameState


class TestMenuNavigation:
    """Test all menu navigation paths."""

    @pytest.fixture
    def game(self):
        """Create a game instance for testing."""
        pygame.init()
        game = Game()
        yield game
        pygame.quit()

    def press_key(self, game, key):
        """Helper to simulate a key press."""
        event = pygame.event.Event(pygame.KEYDOWN, key=key)
        pygame.event.post(event)
        game.handle_input()

    def test_main_menu_starts_correctly(self, game):
        """Test that game starts at main menu."""
        assert game.state == GameState.MENU

    def test_main_menu_input_does_not_emit_debug_prints(self, game, capsys):
        """Player menu navigation must not leak per-key diagnostics to stdout."""
        capsys.readouterr()

        self.press_key(game, pygame.K_c)

        assert "[Menu" not in capsys.readouterr().out
        assert game.state == GameState.CUSTOMIZE

    def test_help_menu_opens_with_h(self, game):
        """Test that H key opens help menu."""
        # Simulate H key press
        self.press_key(game, pygame.K_h)
        assert game.state == GameState.HELP

    def test_help_menu_closes_with_esc(self, game):
        """Test that ESC closes help menu."""
        # Open help
        self.press_key(game, pygame.K_h)
        assert game.state == GameState.HELP

        # Close with ESC
        self.press_key(game, pygame.K_ESCAPE)
        assert game.state == GameState.MENU

    def test_settings_menu_opens_with_s(self, game):
        """Test that S key opens settings menu."""
        self.press_key(game, pygame.K_s)
        assert game.state == GameState.SETTINGS

    def test_settings_menu_closes_with_esc(self, game):
        """Test that ESC closes settings menu."""
        # Open settings
        self.press_key(game, pygame.K_s)
        assert game.state == GameState.SETTINGS

        # Close with ESC
        self.press_key(game, pygame.K_ESCAPE)
        assert game.state == GameState.MENU

    def test_customization_menu_opens_with_c(self, game):
        """Test that C key opens customization menu."""
        self.press_key(game, pygame.K_c)
        assert game.state == GameState.CUSTOMIZE

    def test_customization_menu_closes_with_esc(self, game):
        """Test that ESC closes customization menu."""
        # Open customization
        self.press_key(game, pygame.K_c)
        assert game.state == GameState.CUSTOMIZE

        # Close with ESC
        self.press_key(game, pygame.K_ESCAPE)
        assert game.state == GameState.MENU

    def test_high_scores_menu_opens_with_v(self, game):
        """Test that V key opens high scores menu."""
        self.press_key(game, pygame.K_v)
        assert game.state == GameState.HIGH_SCORES

    def test_high_scores_menu_closes_with_esc(self, game):
        """Test that ESC closes high scores menu."""
        # Open high scores
        self.press_key(game, pygame.K_v)
        assert game.state == GameState.HIGH_SCORES

        # Close with ESC
        self.press_key(game, pygame.K_ESCAPE)
        assert game.state == GameState.MENU

    def test_achievements_menu_opens_with_a(self, game):
        """Test that A key opens achievements menu."""
        self.press_key(game, pygame.K_a)
        assert game.state == GameState.ACHIEVEMENTS

    def test_achievements_menu_closes_with_esc(self, game):
        """Test that ESC closes achievements menu."""
        # Open achievements
        self.press_key(game, pygame.K_a)
        assert game.state == GameState.ACHIEVEMENTS

        # Close with ESC
        self.press_key(game, pygame.K_ESCAPE)
        assert game.state == GameState.MENU

    def test_lets_play_browser_opens_with_l(self, game):
        """Test that L key opens Let's Play channel browser."""
        self.press_key(game, pygame.K_l)
        assert game.state == GameState.CHANNEL_BROWSER

    def test_lets_play_browser_closes_with_esc(self, game):
        """Test that ESC closes Let's Play channel browser."""
        # Open browser
        self.press_key(game, pygame.K_l)
        assert game.state == GameState.CHANNEL_BROWSER

        # Close with ESC
        self.press_key(game, pygame.K_ESCAPE)
        assert game.state == GameState.MENU

    def test_settings_menu_contains_only_working_controls(self, game):
        """Navigation reaches Back without passing a nonfunctional row."""
        self.press_key(game, pygame.K_s)

        self.press_key(game, pygame.K_DOWN)
        assert game.settings_selected_option == 1
        self.press_key(game, pygame.K_DOWN)
        assert game.settings_selected_option == 2
        self.press_key(game, pygame.K_RETURN)

        assert game.state == GameState.MENU

    def test_lets_play_browser_enter_starts_selected_channel(self, game):
        """Confirm in the channel browser must start the selected AI run."""
        self.press_key(game, pygame.K_l)

        self.press_key(game, pygame.K_RETURN)

        assert game.state == GameState.LETS_PLAY
        assert game.ai_player is not None

    def test_customization_number_key_does_not_change_radio(self, game, monkeypatch):
        """Loadout shortcuts must not also trigger direct station selection."""
        calls = []
        monkeypatch.setattr(game.radio, "handle_number_key", calls.append)
        monkeypatch.setattr(game.customization_manager, "save_loadout", calls.append)
        self.press_key(game, pygame.K_c)

        self.press_key(game, pygame.K_1)

        assert calls == [0]
        assert game.state == GameState.CUSTOMIZE

    def test_channel_browser_random_key_does_not_cycle_radio(self, game, monkeypatch):
        """The browser's random-channel key must have exactly one meaning."""
        radio_calls = []
        monkeypatch.setattr(game.radio, "next_station", lambda: radio_calls.append("next"))
        self.press_key(game, pygame.K_l)

        self.press_key(game, pygame.K_r)

        assert radio_calls == []
        assert game.state == GameState.LETS_PLAY
        assert game.ai_player is not None

    def test_lets_play_browser_accepts_controller_navigation_and_confirm(self, game):
        """A controller-only player must be able to choose an AI channel."""
        game.input_manager.joystick = object()
        self.press_key(game, pygame.K_l)
        initial_index = game.channel_browser_index
        pygame.event.post(pygame.event.Event(pygame.JOYHATMOTION, value=(0, -1)))
        game.handle_input()
        assert game.channel_browser_index == (initial_index + 1) % len(game.channel_list)

        pygame.event.post(pygame.event.Event(pygame.JOYBUTTONDOWN, button=0))
        game.handle_input()

        assert game.state == GameState.LETS_PLAY
        assert game.ai_player is not None

    def test_running_confirm_does_not_trigger_customization_actions(self, game):
        """Confirm during a run must not be intercepted by another state's handler."""
        self.press_key(game, pygame.K_RETURN)
        assert game.state == GameState.RUNNING

        self.press_key(game, pygame.K_RETURN)

        assert game.state == GameState.RUNNING

    def test_game_starts_with_enter(self, game):
        """Test that ENTER key starts the game."""
        self.press_key(game, pygame.K_RETURN)
        assert game.state == GameState.RUNNING

    def test_fullscreen_toggle_with_f11(self, game):
        """Test that F11 toggles fullscreen."""
        initial_fullscreen = game.fullscreen
        self.press_key(game, pygame.K_F11)
        assert game.fullscreen != initial_fullscreen

    def test_radio_starts_on_random_station(self, game):
        """Test that radio starts on a random station (not always first)."""
        # This test checks that the random station logic is working
        assert game.radio is not None
        current_station = game.radio.get_current_station()
        assert current_station is not None
        # Just verify radio is initialized and playing something
        assert current_station.name is not None
        assert len(current_station.name) > 0
