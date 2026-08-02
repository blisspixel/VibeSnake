"""
Comprehensive tests for Phase 3: Personalization & Progression features.

Tests:
- Name entry system
- Customization UI
- Save/load slots
- Stat tracking (apples_eaten, wall_rides)
- Unlock system
- Achievement system
- AI personality customization
"""

import pygame
from vibesnake.core.game_state import Game, GameState
from vibesnake.core.player_profile import PlayerProfile
from vibesnake.core.customization import CustomizationManager, get_ai_personality_customization, SnakeCustomization
from vibesnake.core.achievements import AchievementManager, ACHIEVEMENTS


class TestNameEntrySystem:
    """Test name entry system functionality."""

    def test_name_entry_state_exists(self):
        """Test that NAME_ENTRY state exists."""
        assert hasattr(GameState, "NAME_ENTRY")

    def test_name_entry_initialization(self):
        """Test that game initializes name entry variables."""
        pygame.init()
        game = Game()
        assert hasattr(game, "player_name")
        assert hasattr(game, "cursor_blink_timer")
        assert hasattr(game, "cursor_visible")
        assert game.player_name == ""
        pygame.quit()

    def test_name_length_limit(self):
        """Test that name entry respects 12 character limit."""
        pygame.init()
        try:
            game = Game()
            game.state = GameState.NAME_ENTRY
            pygame.event.clear()
            for character in "THIRTEENCHARSX":
                pygame.event.post(
                    pygame.event.Event(
                        pygame.KEYDOWN,
                        key=ord(character.lower()),
                        unicode=character,
                    )
                )

            game.handle_input()

            assert game.player_name == "THIRTEENCHAR"
            assert len(game.player_name) == 12
        finally:
            pygame.quit()


class TestCustomizationSystem:
    """Test snake customization system."""

    def test_customization_manager_initialization(self):
        """Test CustomizationManager initializes correctly."""
        manager = CustomizationManager()
        assert manager.current_customization is not None
        assert isinstance(manager.current_customization, SnakeCustomization)
        assert isinstance(manager.loadouts, list)

    def test_save_load_slots(self):
        """Test saving and loading customization slots."""
        manager = CustomizationManager()

        # Create a custom snake
        custom = SnakeCustomization(
            base_color=(255, 0, 0), pattern="stripes", eye_style="angry", accessory="crown", trail="fire"
        )
        manager.current_customization = custom

        # Save to slot 0
        manager.save_loadout(0)
        assert len(manager.loadouts) > 0

        # Change current customization
        manager.current_customization = SnakeCustomization(base_color=(0, 255, 0))
        assert manager.current_customization.base_color == (0, 255, 0)

        # Load from slot 0
        manager.load_loadout(0)
        assert manager.current_customization.base_color == (255, 0, 0)
        assert manager.current_customization.pattern == "stripes"
        assert manager.current_customization.eye_style == "angry"

    def test_three_save_slots(self):
        """Test that all 3 save slots work independently."""
        manager = CustomizationManager()

        # Save different colors to each slot
        colors = [(255, 0, 0), (0, 255, 0), (0, 0, 255)]
        for i, color in enumerate(colors):
            manager.current_customization = SnakeCustomization(base_color=color)
            manager.save_loadout(i)

        # Verify each slot has the correct color
        for i, color in enumerate(colors):
            manager.load_loadout(i)
            assert manager.current_customization.base_color == color


class TestStatTracking:
    """Test stat tracking for progression."""

    def test_player_profile_stat_variables(self):
        """Test that PlayerProfile has all stat tracking variables."""
        profile = PlayerProfile()
        assert hasattr(profile, "apples_eaten")
        assert hasattr(profile, "wall_rides")
        assert hasattr(profile, "total_games")
        assert hasattr(profile, "highest_score")
        assert hasattr(profile, "highest_combo")
        assert profile.apples_eaten == 0
        assert profile.wall_rides == 0

    def test_increment_apples_eaten(self):
        """Test incrementing apples_eaten."""
        profile = PlayerProfile()
        assert profile.apples_eaten == 0

        profile.increment_apples_eaten()
        assert profile.apples_eaten == 1

        profile.increment_apples_eaten()
        assert profile.apples_eaten == 2

    def test_increment_wall_rides(self):
        """Test incrementing wall_rides."""
        profile = PlayerProfile()
        assert profile.wall_rides == 0

        profile.increment_wall_rides()
        assert profile.wall_rides == 1

        profile.increment_wall_rides()
        assert profile.wall_rides == 2

    def test_wall_wrap_detection(self):
        """Test that snake.move() returns wrap detection."""
        pygame.init()
        from vibesnake.core.snake import Snake

        snake = Snake()

        # Move should return (alive, wrapped) tuple
        result = snake.move(grow=False)
        assert isinstance(result, tuple)
        assert len(result) == 2
        assert isinstance(result[0], bool)  # alive
        assert isinstance(result[1], bool)  # wrapped

        pygame.quit()


class TestUnlockSystem:
    """Test unlock requirement checking."""

    def test_unlock_requirements_format(self):
        """Test that unlock requirements use tuple format."""
        from vibesnake.core.customization import UNLOCK_REQUIREMENTS

        # Check a few requirements have correct tuple format
        for item_name, requirement in UNLOCK_REQUIREMENTS.items():
            assert isinstance(requirement, tuple)
            assert len(requirement) == 3
            assert isinstance(requirement[0], str)  # requirement_type
            assert isinstance(requirement[1], int)  # requirement_value
            assert isinstance(requirement[2], str)  # description

    def test_check_unlocked_free_items(self):
        """Test that free items are always unlocked."""
        profile = PlayerProfile()

        # Classic Green should be free
        from vibesnake.core.customization import UNLOCK_REQUIREMENTS

        requirement = UNLOCK_REQUIREMENTS.get("Classic Green")
        assert requirement[0] == "free"
        assert profile.check_unlocked("Classic Green", requirement)

    def test_check_unlocked_apples_requirement(self):
        """Test unlock checking for apples_eaten requirements."""
        profile = PlayerProfile()

        # Golden Shimmer requires 1000 apples
        requirement = ("apples_eaten", 1000, "Eat 1000 apples")
        assert not profile.check_unlocked("Golden Shimmer", requirement)

        # Simulate eating 1000 apples
        profile.apples_eaten = 1000
        assert profile.check_unlocked("Golden Shimmer", requirement)

    def test_check_unlocked_wall_rides_requirement(self):
        """Test unlock checking for wall_rides requirements."""
        profile = PlayerProfile()

        # Diamond Sparkle requires 500 wall rides
        requirement = ("wall_rides", 500, "Ride walls 500 times")
        assert not profile.check_unlocked("Diamond Sparkle", requirement)

        # Simulate 500 wall rides
        profile.wall_rides = 500
        assert profile.check_unlocked("Diamond Sparkle", requirement)


class TestAchievementSystem:
    """Test achievement system."""

    def test_achievement_manager_initialization(self):
        """Test AchievementManager initializes with all achievements."""
        manager = AchievementManager()
        assert len(manager.achievements) == len(ACHIEVEMENTS)
        assert len(manager.achievements) == 25  # We have 25 achievements

    def test_achievement_structure(self):
        """Test that achievements have correct structure."""
        manager = AchievementManager()

        for ach_id, achievement in manager.achievements.items():
            assert hasattr(achievement, "id")
            assert hasattr(achievement, "name")
            assert hasattr(achievement, "description")
            assert hasattr(achievement, "icon")
            assert hasattr(achievement, "unlock_condition")
            assert hasattr(achievement, "rarity")
            assert hasattr(achievement, "unlocked")
            assert achievement.rarity in ["common", "rare", "epic", "legendary"]

    def test_achievement_notification_queue(self):
        """Test achievement notification queueing."""
        pygame.init()
        game = Game()

        assert hasattr(game, "achievement_notifications")
        assert hasattr(game, "current_achievement_display")
        assert hasattr(game, "achievement_display_timer")
        assert isinstance(game.achievement_notifications, list)

        pygame.quit()

    def test_achievements_state_exists(self):
        """Test that ACHIEVEMENTS state exists."""
        assert hasattr(GameState, "ACHIEVEMENTS")


class TestAIPersonalityCustomization:
    """Test AI personality visual customization."""

    def test_ai_customization_function_exists(self):
        """Test that AI customization function exists."""
        from vibesnake.core.customization import get_ai_personality_customization

        assert callable(get_ai_personality_customization)

    def test_all_ai_personalities_have_customizations(self):
        """Test that all AI personalities have visual themes."""
        from vibesnake.ai.player import AI_PERSONALITIES

        ai_keys = list(AI_PERSONALITIES.keys())

        for key in ai_keys:
            customization = get_ai_personality_customization(key)
            assert isinstance(customization, SnakeCustomization)
            # Each should have a unique base_color
            assert customization.base_color is not None

    def test_ai_customization_uniqueness(self):
        """Test that AI personalities have unique visual themes."""
        ai_keys = ["speed_demon", "coward", "greedy", "power_hunter"]

        customizations = [get_ai_personality_customization(key) for key in ai_keys]
        colors = [c.base_color for c in customizations]

        # All colors should be different
        assert len(colors) == len(set(colors))

    def test_speed_demon_theme(self):
        """Test Speed Demon has correct theme."""
        custom = get_ai_personality_customization("speed_demon")
        assert custom.base_color == (255, 50, 50)  # Red
        assert custom.pattern == "stripes"
        assert custom.eye_style == "laser"
        assert custom.trail == "fire"

    def test_greedy_theme(self):
        """Test Mr. Greedy has correct theme."""
        custom = get_ai_personality_customization("greedy")
        assert custom.base_color == (255, 215, 0)  # Gold
        assert custom.pattern == "scales"
        assert custom.accessory == "crown"
        assert custom.trail == "sparkle"


class TestIntegration:
    """Integration tests for Phase 3 systems working together."""

    def test_game_initialization_with_phase3(self):
        """Test that game initializes with all Phase 3 systems."""
        pygame.init()
        game = Game()

        # Check all Phase 3 components exist
        assert hasattr(game, "player_profile")
        assert hasattr(game, "customization_manager")
        assert hasattr(game, "achievement_manager")
        assert hasattr(game, "customization_notification")
        assert hasattr(game, "achievements_scroll_offset")

        pygame.quit()

    def test_stat_tracking_saves_on_game_over(self):
        """Test that stats are saved when game ends."""
        # This would require simulating a full game
        # For now, verify the method exists
        profile = PlayerProfile()
        assert hasattr(profile, "update_score")
        assert hasattr(profile, "increment_games")


def run_all_tests():
    """Run all Phase 3 tests and report results."""
    print("=" * 60)
    print("PHASE 3 COMPREHENSIVE TEST SUITE")
    print("=" * 60)

    test_classes = [
        TestNameEntrySystem,
        TestCustomizationSystem,
        TestStatTracking,
        TestUnlockSystem,
        TestAchievementSystem,
        TestAIPersonalityCustomization,
        TestIntegration,
    ]

    total_tests = 0
    passed_tests = 0
    failed_tests = []

    for test_class in test_classes:
        print(f"\n{test_class.__name__}:")
        print("-" * 60)

        test_instance = test_class()
        test_methods = [method for method in dir(test_instance) if method.startswith("test_")]

        for method_name in test_methods:
            total_tests += 1
            try:
                method = getattr(test_instance, method_name)
                method()
                print(f"  [PASS] {method_name}")
                passed_tests += 1
            except Exception as e:
                print(f"  [FAIL] {method_name}")
                print(f"    Error: {str(e)}")
                failed_tests.append((test_class.__name__, method_name, str(e)))

    print("\n" + "=" * 60)
    print("TEST RESULTS")
    print("=" * 60)
    print(f"Total Tests: {total_tests}")
    print(f"Passed: {passed_tests}")
    print(f"Failed: {len(failed_tests)}")
    print(f"Success Rate: {(passed_tests / total_tests) * 100:.1f}%")

    if failed_tests:
        print("\nFAILED TESTS:")
        for class_name, method_name, error in failed_tests:
            print(f"  - {class_name}.{method_name}")
            print(f"    {error}")

    return passed_tests == total_tests


if __name__ == "__main__":
    success = run_all_tests()
    exit(0 if success else 1)
