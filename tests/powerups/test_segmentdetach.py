"""
Tests for Segment Detach power-up.
"""

import unittest
from unittest.mock import Mock
from collections import deque
from vibesnake.powerups.segmentdetach import SegmentDetachPowerUp


class TestSegmentDetachPowerUp(unittest.TestCase):
    def setUp(self):
        """Create a fresh Segment Detach power-up for each test."""
        self.position = (10, 10)
        self.detach = SegmentDetachPowerUp(self.position)
        self.mock_game = Mock()
        self.mock_game.detached_segments = []
        self.mock_game.detached_segments_timer = 0.0
        self.mock_game.snake = Mock()

    def test_initialization(self):
        """Segment Detach initializes with correct position and single-use duration."""
        self.assertEqual(self.detach.position, self.position)
        self.assertEqual(self.detach.duration, 0.0)  # Single-use, no duration
        self.assertTrue(self.detach.active)
        self.assertFalse(self.detach.activated)
        self.assertEqual(self.detach.detached_segments, [])

    def test_activate_detaches_segments(self):
        """Activating Segment Detach detaches last 5 segments."""
        # Create a snake with 10 segments
        snake_positions = deque([(i, 0) for i in range(10)])
        self.mock_game.snake.body = snake_positions
        self.mock_game.snake.positions_set = set(snake_positions)

        self.detach.activate(self.mock_game)

        self.assertTrue(self.detach.activated)
        # The deque is ordered tail to head, so the oldest five segments detach.
        self.assertEqual(len(self.detach.detached_segments), 5)
        self.assertEqual(self.detach.detached_segments, [(0, 0), (1, 0), (2, 0), (3, 0), (4, 0)])
        self.assertEqual(list(self.mock_game.snake.body), [(5, 0), (6, 0), (7, 0), (8, 0), (9, 0)])
        self.assertEqual(self.mock_game.snake.positions_set, set(self.mock_game.snake.body))
        # Should be immediately inactive (single-use)
        self.assertFalse(self.detach.active)

    def test_detach_sets_game_obstacles(self):
        """Segment Detach creates obstacles in game state."""
        snake_positions = deque([(i, 0) for i in range(10)])
        self.mock_game.snake.body = snake_positions
        self.mock_game.snake.positions_set = set(snake_positions)

        self.detach.activate(self.mock_game)

        # Game should have detached segments
        self.assertEqual(self.mock_game.detached_segments, [(0, 0), (1, 0), (2, 0), (3, 0), (4, 0)])
        # Obstacles should last 10 seconds
        self.assertEqual(self.mock_game.detached_segments_timer, 10.0)

    def test_detach_with_small_snake(self):
        """Segment Detach with snake smaller than 5 segments."""
        # Snake with only 3 segments
        snake_positions = deque([(i, 0) for i in range(3)])
        self.mock_game.snake.body = snake_positions
        self.mock_game.snake.positions_set = set(snake_positions)

        self.detach.activate(self.mock_game)

        # Should detach only 2 segments (keep at least 1 - the head)
        self.assertEqual(len(self.detach.detached_segments), 2)
        self.assertEqual(self.detach.detached_segments, [(0, 0), (1, 0)])
        self.assertEqual(list(self.mock_game.snake.body), [(2, 0)])

    def test_detach_is_single_use(self):
        """Segment Detach is single-use and immediately inactive after activation."""
        snake_positions = deque([(i, 0) for i in range(10)])
        self.mock_game.snake.body = snake_positions
        self.mock_game.snake.positions_set = set(snake_positions)

        self.detach.activate(self.mock_game)

        # Should be inactive after activation
        self.assertFalse(self.detach.active)
        # Segments should be stored
        self.assertNotEqual(len(self.detach.detached_segments), 0)

    def test_activates_only_once(self):
        """Can't reactivate already used Segment Detach."""
        snake_positions = deque([(i, 0) for i in range(10)])
        self.mock_game.snake.body = snake_positions
        self.mock_game.snake.positions_set = set(snake_positions)

        self.detach.activate(self.mock_game)
        original_segments = self.detach.detached_segments[:]

        # Try to activate again (shouldn't work because already activated)
        replacement = deque([(i, 5) for i in range(10)])
        self.mock_game.snake.body = replacement
        self.mock_game.snake.positions_set = set(replacement)
        self.detach.activate(self.mock_game)

        # Should still be at original segments
        self.assertEqual(self.detach.detached_segments, original_segments)


if __name__ == "__main__":
    unittest.main()
