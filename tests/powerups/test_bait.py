"""
Tests for Bait power-up.
"""

import unittest
from unittest.mock import Mock
from vibesnake.powerups.bait import BaitPowerUp


class TestBaitPowerUp(unittest.TestCase):
    def setUp(self):
        """Create a fresh Bait power-up for each test."""
        self.position = (10, 10)
        self.bait = BaitPowerUp(self.position)
        self.mock_game = Mock()
        self.mock_game.bait_position = None
        self.mock_game.snake = Mock()
        self.mock_game.snake.get_head = Mock(return_value=(15, 15))

    def test_initialization(self):
        """Bait initializes with correct position and single-use duration."""
        self.assertEqual(self.bait.position, self.position)
        self.assertEqual(self.bait.duration, 0.0)  # Single-use, no duration
        self.assertTrue(self.bait.active)
        self.assertFalse(self.bait.activated)
        self.assertIsNone(self.bait.bait_position)

    def test_activate_places_bait_at_snake_head(self):
        """Activating Bait places it at snake's current head position."""
        self.bait.activate(self.mock_game)

        self.assertTrue(self.bait.activated)
        self.assertEqual(self.bait.bait_position, (15, 15))
        self.assertEqual(self.mock_game.bait_position, (15, 15))
        # Should be immediately inactive (single-use)
        self.assertFalse(self.bait.active)

    def test_bait_is_single_use(self):
        """Bait is single-use and immediately inactive after activation."""
        self.bait.activate(self.mock_game)

        # Should be inactive after activation
        self.assertFalse(self.bait.active)
        # Bait position should be stored
        self.assertIsNotNone(self.bait.bait_position)

    def test_activates_only_once(self):
        """Can't reactivate already used Bait."""
        self.bait.activate(self.mock_game)
        original_pos = self.bait.bait_position

        # Try to activate again (shouldn't work because already activated)
        self.mock_game.snake.get_head = Mock(return_value=(20, 20))
        self.bait.activate(self.mock_game)

        # Should still be at original position
        self.assertEqual(self.bait.bait_position, original_pos)

    def test_stores_snake_head_position(self):
        """Bait correctly stores the snake's head position."""
        # Change snake head position
        self.mock_game.snake.get_head = Mock(return_value=(25, 30))

        self.bait.activate(self.mock_game)

        self.assertEqual(self.bait.bait_position, (25, 30))
        self.assertEqual(self.mock_game.bait_position, (25, 30))


if __name__ == "__main__":
    unittest.main()
