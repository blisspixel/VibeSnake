import unittest
from collections import deque

from vibesnake.core.snake import Snake
from vibesnake.core.enums import Direction
from vibesnake.data import settings


class TestSnake(unittest.TestCase):
    def setUp(self):
        self.snake = Snake()

    def test_initial_position(self):
        expected = (settings.GRID_WIDTH // 2, settings.GRID_HEIGHT // 2)
        self.assertEqual(self.snake.get_head(), expected)
        self.assertEqual(len(self.snake.body), 1)

    def test_move_forward(self):
        head_before = self.snake.get_head()
        self.assertTrue(self.snake.move())
        head_after = self.snake.get_head()
        dx, dy = self.snake.direction.vector()
        expected = ((head_before[0] + dx) % settings.GRID_WIDTH, (head_before[1] + dy) % settings.GRID_HEIGHT)
        self.assertEqual(head_after, expected)

    def test_grow(self):
        original_length = len(self.snake.body)
        self.assertTrue(self.snake.move(grow=True))
        self.assertEqual(len(self.snake.body), original_length + 1)

    def test_peek_next_head_accounts_for_buffered_turn_without_mutating(self):
        original_head = self.snake.get_head()
        self.snake.queue_direction(Direction.UP)

        predicted = self.snake.peek_next_head()

        self.assertEqual(predicted, (original_head[0], (original_head[1] - 1) % settings.GRID_HEIGHT))
        self.assertEqual(self.snake.get_head(), original_head)
        self.assertEqual(list(self.snake.next_directions), [Direction.UP])

    def test_phase_shift_can_cross_body_without_corrupting_occupancy(self):
        self.snake.body = deque([(1, 1), (1, 2), (2, 2), (2, 1), (3, 1)])
        self.snake.positions_set = set(self.snake.body)
        self.snake.direction = Direction.LEFT

        alive, _ = self.snake.move(ignore_self_collision=True)

        self.assertTrue(alive)
        self.assertEqual(self.snake.get_head(), (2, 1))
        self.assertEqual(self.snake.positions_set, set(self.snake.body))

    def test_growing_into_tail_is_a_collision(self):
        self.snake.body = deque([(2, 1), (2, 2), (3, 2), (3, 1)])
        self.snake.positions_set = set(self.snake.body)
        self.snake.direction = Direction.LEFT

        alive, _ = self.snake.move(grow=True)

        self.assertFalse(alive)

    def test_departing_tail_duplicate_remains_a_collision(self):
        self.snake.body = deque([(1, 1), (1, 2), (2, 2), (1, 1), (2, 1)])
        self.snake.positions_set = set(self.snake.body)
        self.snake.direction = Direction.LEFT

        alive, _ = self.snake.move()

        self.assertFalse(alive)
        self.assertEqual(self.snake.get_head(), (2, 1))
        self.assertEqual(self.snake.positions_set, set(self.snake.body))

    # NOTE: Self-collision and direction blocking are tested more thoroughly
    # in tests/test_gameplay_simulation.py with real gameplay scenarios


if __name__ == "__main__":
    unittest.main()
