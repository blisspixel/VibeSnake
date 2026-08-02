import unittest
from unittest.mock import patch

from vibesnake.core.food import Food
from vibesnake.core.exceptions import GridFullException
from typing import Set, Tuple

from vibesnake.data import settings


class TestFood(unittest.TestCase):
    def setUp(self):
        # Simulate a small grid of occupied cells
        self.occupied: Set[Tuple[int, int]] = {(5, 5), (5, 6), (5, 7), (5, 8)}

    def test_spawn_not_on_snake(self):
        food = Food(self.occupied)
        self.assertNotIn(food.position, self.occupied)
        self.assertTrue(0 <= food.position[0] < settings.GRID_WIDTH)
        self.assertTrue(0 <= food.position[1] < settings.GRID_HEIGHT)

    def test_respawn_stays_off_occupied_cells(self):
        food = Food(self.occupied)
        food.respawn(self.occupied)
        self.assertNotIn(food.position, self.occupied)

    def test_preferred_respawn_weights_cells_near_bait_more_heavily(self):
        food = Food(set())
        captured = {}

        def choose_first(population, weights, k):
            captured.update(zip(population, weights))
            return [population[0]]

        with patch("vibesnake.core.food.random.choices", side_effect=choose_first):
            food.respawn({(0, 0)}, preferred_position=(0, 0))

        self.assertGreater(captured[(1, 0)], captured[(settings.GRID_WIDTH - 1, settings.GRID_HEIGHT - 1)])

    def test_spawn_on_full_grid_raises_domain_exception(self):
        # All cells occupied
        full = {(x, y) for x in range(settings.GRID_WIDTH) for y in range(settings.GRID_HEIGHT)}
        with self.assertRaises(GridFullException) as raised:
            Food(full)

        self.assertEqual(raised.exception.occupied_count, len(full))
        self.assertEqual(raised.exception.grid_size, len(full))
        self.assertEqual(raised.exception.occupancy_percent, 100.0)

    def test_respawn_on_full_grid_raises_domain_exception(self):
        food = Food(set())
        full = {(x, y) for x in range(settings.GRID_WIDTH) for y in range(settings.GRID_HEIGHT)}

        with self.assertRaises(GridFullException):
            food.respawn(full)

    def test_spawn_uses_the_only_free_cell(self):
        free_cell = (10, 10)
        occupied = {
            (x, y) for x in range(settings.GRID_WIDTH) for y in range(settings.GRID_HEIGHT) if (x, y) != free_cell
        }

        self.assertEqual(Food(occupied).position, free_cell)

    def test_spawn_position_within_bounds(self):
        food = Food(set())
        x, y = food.position
        self.assertTrue(0 <= x < settings.GRID_WIDTH)
        self.assertTrue(0 <= y < settings.GRID_HEIGHT)

    def test_unbiased_spawn_samples_exact_free_cell_set(self):
        occupied = {(0, 0), (1, 0), (2, 0)}
        captured = []

        def choose_first(free_cells):
            captured.extend(free_cells)
            return free_cells[0]

        with patch("vibesnake.core.food.random.choice", side_effect=choose_first):
            food = Food(occupied)

        self.assertEqual(len(captured), settings.GRID_WIDTH * settings.GRID_HEIGHT - len(occupied))
        self.assertTrue(occupied.isdisjoint(captured))
        self.assertEqual(food.position, captured[0])


if __name__ == "__main__":
    unittest.main()
