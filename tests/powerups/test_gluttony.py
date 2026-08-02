"""
Tests for Gluttony power-up.
"""

import unittest
from unittest.mock import Mock
from vibesnake.powerups.gluttony import GluttonyPowerUp


class TestGluttonyPowerUp(unittest.TestCase):
    def setUp(self):
        """Create a fresh Gluttony power-up for each test."""
        self.position = (10, 10)
        self.gluttony = GluttonyPowerUp(self.position, duration=5.0)
        self.mock_game = Mock()
        self.mock_game.snake_gluttony_active = False

    def test_initialization(self):
        """Gluttony initializes with correct position and duration."""
        self.assertEqual(self.gluttony.position, self.position)
        self.assertEqual(self.gluttony.duration, 5.0)
        self.assertTrue(self.gluttony.active)
        self.assertFalse(self.gluttony.activated)

    def test_activate_enables_score_only_mode(self):
        """Activating Gluttony enables score-only (no growth) mode."""
        self.gluttony.activate(self.mock_game)

        self.assertTrue(self.gluttony.activated)
        self.assertTrue(self.mock_game.snake_gluttony_active)

    def test_deactivate_restores_normal_growth(self):
        """Deactivating Gluttony restores normal growth."""
        # First activate
        self.gluttony.activate(self.mock_game)
        self.assertTrue(self.mock_game.snake_gluttony_active)

        # Update past duration to trigger deactivation
        self.gluttony.update(6.0, self.mock_game)

        self.assertFalse(self.gluttony.active)
        self.assertFalse(self.mock_game.snake_gluttony_active)

    def test_update_with_time(self):
        """Gluttony duration decreases over time."""
        self.gluttony.activate(self.mock_game)

        # Update with 2 seconds
        self.gluttony.update(2.0, self.mock_game)

        self.assertTrue(self.gluttony.active)
        self.assertAlmostEqual(self.gluttony.timer, 2.0, places=1)

    def test_expires_after_duration(self):
        """Gluttony expires after full duration."""
        self.gluttony.activate(self.mock_game)

        # Update past full duration
        self.gluttony.update(6.0, self.mock_game)

        self.assertFalse(self.gluttony.active)
        self.assertFalse(self.mock_game.snake_gluttony_active)

    def test_default_duration(self):
        """Gluttony has 5 second default duration."""
        gluttony = GluttonyPowerUp(self.position)
        self.assertEqual(gluttony.duration, 5.0)

    def test_activates_only_once(self):
        """Can't reactivate already active Gluttony."""
        self.gluttony.activate(self.mock_game)

        # Try to activate again
        self.gluttony.activate(self.mock_game)

        # Should still be at original timer (0 after first activation)
        self.assertAlmostEqual(self.gluttony.timer, 0.0, places=1)


if __name__ == "__main__":
    unittest.main()
