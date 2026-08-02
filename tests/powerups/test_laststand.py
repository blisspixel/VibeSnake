"""
Tests for Last Stand power-up.
"""

import unittest
from unittest.mock import Mock
from vibesnake.powerups.laststand import LastStandPowerUp


class TestLastStandPowerUp(unittest.TestCase):
    def setUp(self):
        """Create a fresh Last Stand power-up for each test."""
        self.position = (10, 10)
        self.laststand = LastStandPowerUp(self.position)
        self.mock_game = Mock()
        self.mock_game.last_stand_held = False

    def test_initialization(self):
        """Last Stand initializes with correct position and passive duration."""
        self.assertEqual(self.laststand.position, self.position)
        self.assertEqual(self.laststand.duration, 0.0)  # Passive, no duration
        self.assertTrue(self.laststand.active)
        self.assertFalse(self.laststand.activated)
        self.assertFalse(self.laststand.is_held)

    def test_activate_marks_as_held(self):
        """Activating Last Stand marks it as held (passive)."""
        self.laststand.activate(self.mock_game)

        self.assertTrue(self.laststand.activated)
        self.assertTrue(self.laststand.is_held)
        self.assertTrue(self.mock_game.last_stand_held)

    def test_last_stand_is_passive(self):
        """Last Stand is passive and stays active after activation."""
        self.laststand.activate(self.mock_game)

        # Should still be active (held, not consumed yet)
        self.assertTrue(self.laststand.active)
        self.assertTrue(self.laststand.is_held)

    def test_deactivate_consumes_last_stand(self):
        """Deactivating Last Stand (after use) clears held status."""
        self.laststand.activate(self.mock_game)
        self.assertTrue(self.mock_game.last_stand_held)

        # Simulate consumption (called by game when death is prevented)
        self.laststand.deactivate(self.mock_game)

        self.assertFalse(self.laststand.active)
        self.assertFalse(self.laststand.is_held)
        self.assertFalse(self.mock_game.last_stand_held)

    def test_activates_only_once(self):
        """Can't reactivate already active Last Stand."""
        self.laststand.activate(self.mock_game)

        # Try to activate again
        self.laststand.activate(self.mock_game)

        # Should still be held only once
        self.assertTrue(self.laststand.is_held)

    def test_held_status_persists(self):
        """Last Stand held status persists until consumed."""
        self.laststand.activate(self.mock_game)

        self.laststand.update(60.0, self.mock_game)

        # Check it's still held
        self.assertTrue(self.laststand.is_held)
        self.assertTrue(self.mock_game.last_stand_held)

        # Should remain active (passive power-up)
        self.assertTrue(self.laststand.active)


if __name__ == "__main__":
    unittest.main()
