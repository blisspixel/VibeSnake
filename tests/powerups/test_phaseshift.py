"""
Tests for Phase Shift power-up.
"""

import unittest
from unittest.mock import Mock
from vibesnake.powerups.phaseshift import PhaseShiftPowerUp


class TestPhaseShiftPowerUp(unittest.TestCase):
    def setUp(self):
        """Create a fresh Phase Shift power-up for each test."""
        self.position = (10, 10)
        self.phaseshift = PhaseShiftPowerUp(self.position, duration=5.0)
        self.mock_game = Mock()
        self.mock_game.snake_phase_shift_active = False

    def test_initialization(self):
        """Phase Shift initializes with correct position and duration."""
        self.assertEqual(self.phaseshift.position, self.position)
        self.assertEqual(self.phaseshift.duration, 5.0)
        self.assertTrue(self.phaseshift.active)
        self.assertFalse(self.phaseshift.activated)

    def test_activate_enables_phase_shift(self):
        """Activating Phase Shift enables phase through own body."""
        self.phaseshift.activate(self.mock_game)

        self.assertTrue(self.phaseshift.activated)
        self.assertTrue(self.mock_game.snake_phase_shift_active)

    def test_deactivate_disables_phase_shift(self):
        """Deactivating Phase Shift restores normal collision."""
        # First activate
        self.phaseshift.activate(self.mock_game)
        self.assertTrue(self.mock_game.snake_phase_shift_active)

        # Update past duration to trigger deactivation
        self.phaseshift.update(6.0, self.mock_game)

        self.assertFalse(self.phaseshift.active)
        self.assertFalse(self.mock_game.snake_phase_shift_active)

    def test_update_with_time(self):
        """Phase Shift duration decreases over time."""
        self.phaseshift.activate(self.mock_game)

        # Update with 2 seconds
        self.phaseshift.update(2.0, self.mock_game)

        self.assertTrue(self.phaseshift.active)
        self.assertAlmostEqual(self.phaseshift.timer, 2.0, places=1)

    def test_expires_after_duration(self):
        """Phase Shift expires after full duration."""
        self.phaseshift.activate(self.mock_game)

        # Update past full duration
        self.phaseshift.update(6.0, self.mock_game)

        self.assertFalse(self.phaseshift.active)
        self.assertFalse(self.mock_game.snake_phase_shift_active)

    def test_default_duration(self):
        """Phase Shift has 5 second default duration."""
        phaseshift = PhaseShiftPowerUp(self.position)
        self.assertEqual(phaseshift.duration, 5.0)

    def test_activates_only_once(self):
        """Can't reactivate already active Phase Shift."""
        self.phaseshift.activate(self.mock_game)

        # Try to activate again
        self.phaseshift.activate(self.mock_game)

        # Should still be at original timer (0 after first activation)
        self.assertAlmostEqual(self.phaseshift.timer, 0.0, places=1)


if __name__ == "__main__":
    unittest.main()
