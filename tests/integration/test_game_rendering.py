"""Integration coverage for the game state's rendering dispatcher."""

from types import SimpleNamespace

import pygame

from vibesnake.core.enums import GameState
from vibesnake.core.game_state import Game
from vibesnake.core.player_profile import PlayerProfile


def test_every_game_state_renders_headlessly():
    pygame.init()
    game = Game()
    game.current_danger_warning = SimpleNamespace(color=(255, 0, 0))
    game.current_achievement_display = game.achievement_manager.achievements["first_bite"]
    game.achievement_display_timer = 2.0
    game.score_manager.base_score = 10

    simple_states = [
        GameState.MENU,
        GameState.HELP,
        GameState.CHANNEL_BROWSER,
        GameState.CUSTOMIZE,
        GameState.SETTINGS,
        GameState.HIGH_SCORES,
        GameState.ACHIEVEMENTS,
        GameState.PAUSED,
        GameState.GAME_OVER,
        GameState.RUNNING,
    ]
    for state in simple_states:
        game.state = state
        game.draw()

    game.ai_personality_key = "balanced"
    game.state = GameState.LETS_PLAY
    game.draw()

    game.state = GameState.NAME_ENTRY
    game.draw()
    snake = game.snake
    del game.snake
    game.draw()
    game.snake = snake


def test_game_loads_achievement_progress_and_ignores_ai_runs():
    profile = PlayerProfile()
    profile.create_profile("PLAYER")
    profile.update_achievement_state({"first_bite": {"unlocked": True, "unlock_time": 1.0}})

    game = Game()
    assert game.achievement_manager.achievements["first_bite"].unlocked

    starting_games = game.player_profile.total_games
    game.state = GameState.LETS_PLAY
    game._finalize_player_run()
    assert game.player_profile.total_games == starting_games

    game.state = GameState.RUNNING
    game._finalize_player_run()
    assert game.player_profile.total_games == starting_games + 1
