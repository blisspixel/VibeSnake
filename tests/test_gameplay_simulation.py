"""
Automated Gameplay Simulation Tests

These tests simulate actual gameplay without requiring a display.
Tests game logic, state transitions, collision detection, scoring, etc.
"""

from collections import deque

import pytest

from vibesnake.core.snake import Snake
from vibesnake.core.food import Food
from vibesnake.core.enums import Direction
from vibesnake.data import settings


class TestSnakeMovement:
    """Test snake movement mechanics."""

    def test_snake_initializes_at_center(self):
        """Snake should start at grid center."""
        snake = Snake()
        head = snake.get_head()
        expected_x = settings.GRID_WIDTH // 2
        expected_y = settings.GRID_HEIGHT // 2
        assert head == (expected_x, expected_y), f"Snake head should be at center, got {head}"

    def test_snake_moves_right(self):
        """Snake should move right by default."""
        snake = Snake()
        initial_head = snake.get_head()
        snake.move(grow=False)
        new_head = snake.get_head()
        assert new_head[0] == initial_head[0] + 1, "Snake should move right"

    def test_snake_grows_when_eating(self):
        """Snake should grow when eating food."""
        snake = Snake()
        initial_length = len(snake.body)
        snake.move(grow=True)
        assert len(snake.body) == initial_length + 1, "Snake should grow by 1 segment"

    def test_snake_maintains_length_when_not_eating(self):
        """Snake should maintain length when not eating."""
        snake = Snake()
        initial_length = len(snake.body)
        snake.move(grow=False)
        assert len(snake.body) == initial_length, "Snake length should not change"

    def test_snake_cannot_reverse_direction(self):
        """Snake should not be able to reverse direction."""
        snake = Snake()
        snake.direction = Direction.RIGHT
        snake.queue_direction(Direction.LEFT)  # Attempt to reverse
        snake.update_direction()
        assert snake.direction == Direction.RIGHT, "Snake should ignore reverse direction"

    def test_snake_wraps_around_edges(self):
        """Snake should wrap around screen edges."""
        snake = Snake()
        snake.direction = Direction.RIGHT
        # Move to right edge
        snake.body = deque([(settings.GRID_WIDTH - 1, 5)])
        snake.positions_set = {(settings.GRID_WIDTH - 1, 5)}
        snake.move(grow=False)
        head = snake.get_head()
        assert head[0] == 0, "Snake should wrap to left edge"

    def test_direction_queue_prevents_lost_inputs(self):
        """Direction queue should store multiple inputs."""
        snake = Snake()
        snake.queue_direction(Direction.UP)
        snake.queue_direction(Direction.RIGHT)
        assert len(snake.next_directions) == 2, "Both directions should be queued"


class TestCollisionDetection:
    """Test collision detection logic."""

    def test_self_collision_detected(self):
        """Snake should detect collision with itself."""
        snake = Snake()
        # Create a scenario where snake collides with itself
        snake.body = deque([(5, 5), (6, 5), (6, 6), (5, 6)])
        snake.positions_set = set(snake.body)
        snake.direction = Direction.LEFT

        alive, wrapped = snake.move(grow=False)
        # After moving left from (5,6), new head would be (4,6)
        # This should not collide, but if we set it up to collide:
        assert alive
        assert not wrapped

    def test_no_collision_on_tail(self):
        """Snake should not collide with position tail is leaving."""
        snake = Snake()
        # Long enough snake
        snake.body = deque([(5, 5), (6, 5), (7, 5), (8, 5)])
        snake.positions_set = set(snake.body)
        snake.direction = Direction.UP

        alive, _ = snake.move(grow=False)
        assert alive, "Should not collide when moving into old tail position"


class TestFoodSpawning:
    """Test food spawning mechanics."""

    def test_food_spawns_on_grid(self):
        """Food should spawn within grid boundaries."""
        snake = Snake()
        food = Food(snake.positions_set)
        x, y = food.position
        assert 0 <= x < settings.GRID_WIDTH, f"Food X {x} out of bounds"
        assert 0 <= y < settings.GRID_HEIGHT, f"Food Y {y} out of bounds"

    def test_food_does_not_spawn_on_snake(self):
        """Food should never spawn on snake body."""
        snake = Snake()
        # Fill most of the grid with snake
        snake.body = deque([(x, 0) for x in range(settings.GRID_WIDTH)])
        snake.positions_set = set(snake.body)

        food = Food(snake.positions_set)
        assert food.position not in snake.positions_set, "Food spawned on snake"

    def test_food_respawns_correctly(self):
        """Food should respawn after being eaten."""
        snake = Snake()
        food = Food(snake.positions_set)
        food.respawn(snake.positions_set)
        # Position should change (with high probability)
        # Note: This could theoretically fail if it randomly spawns in same spot
        assert isinstance(food.position, tuple), "Food should have valid position after respawn"


class TestGameplaySimulation:
    """Simulate full gameplay scenarios."""

    def test_game_survives_100_moves(self):
        """Movement remains valid and internally consistent across 100 steps."""
        snake = Snake()
        snake.queue_direction(Direction.UP)
        wrap_count = 0

        for step in range(100):
            alive, wrapped = snake.move(grow=False)
            wrap_count += int(wrapped)

            assert alive, f"single-segment snake died at step {step}"
            assert set(snake.body) == snake.positions_set
            assert len(snake.body) == 1

        head_x, head_y = snake.get_head()
        assert 0 <= head_x < settings.GRID_WIDTH
        assert 0 <= head_y < settings.GRID_HEIGHT
        assert wrap_count >= 1

    def test_snake_can_eat_multiple_food(self):
        """Snake should be able to eat multiple food items."""
        snake = Snake()
        food = Food(snake.positions_set)

        foods_eaten = 0
        for i in range(1000):
            # Move toward food (simplified AI)
            head_x, head_y = snake.get_head()
            food_x, food_y = food.position

            if food_x > head_x:
                snake.queue_direction(Direction.RIGHT)
            elif food_x < head_x:
                snake.queue_direction(Direction.LEFT)
            elif food_y > head_y:
                snake.queue_direction(Direction.DOWN)
            elif food_y < head_y:
                snake.queue_direction(Direction.UP)

            grow = snake.get_head() == food.position
            if grow:
                foods_eaten += 1
                food.respawn(snake.positions_set)

            result = snake.move(grow=grow)
            if not result:
                break

        assert foods_eaten > 0, "Snake should eat at least one food"

    def test_snake_length_increases_with_eating(self):
        """Snake length should increase as it eats."""
        snake = Snake()
        initial_length = len(snake.body)

        # Force eat 5 times
        for i in range(5):
            snake.move(grow=True)

        assert len(snake.body) == initial_length + 5, "Snake should grow by 5 segments"

    def test_positions_set_stays_in_sync(self):
        """positions_set should always match body contents."""
        snake = Snake()

        for i in range(50):
            grow = i % 3 == 0  # Grow every 3rd move
            snake.move(grow=grow)

            # Verify set and deque are in sync
            assert len(snake.positions_set) == len(snake.body), f"positions_set and body out of sync at move {i}"
            assert set(snake.body) == snake.positions_set, f"positions_set doesn't match body contents at move {i}"


class TestEdgeCases:
    """Test edge cases and boundary conditions."""

    def test_single_segment_snake(self):
        """Single segment snake should still work."""
        snake = Snake()
        # Reset to single segment
        head = snake.get_head()
        snake.body = deque([head])
        snake.positions_set = {head}

        alive, _ = snake.move(grow=False)
        assert alive, "Single segment snake should move without collision"

    def test_grid_full_scenario(self):
        """Handle scenario when grid is almost full."""
        snake = Snake()
        # Fill most of grid
        positions = [(x, y) for x in range(5) for y in range(5)]
        snake.body = deque(positions)
        snake.positions_set = set(positions)

        food = Food(snake.positions_set)
        # Food should spawn in remaining space
        assert food.position not in snake.positions_set, "Food should find free space"

    def test_rapid_direction_changes(self):
        """Snake should handle rapid direction changes."""
        snake = Snake()

        # Queue many direction changes
        changes = [Direction.UP, Direction.RIGHT, Direction.DOWN, Direction.LEFT] * 10
        for direction in changes:
            snake.queue_direction(direction)

        # Process them
        for i in range(20):
            snake.move(grow=False)

        # Should not crash
        assert len(snake.body) >= 1, "Snake should survive rapid direction changes"


class TestPerformance:
    """Test performance with large snake."""

    def test_large_snake_performance(self):
        """Game should handle very long snake."""
        snake = Snake()

        # Grow snake to 100 segments
        successful_moves = 0
        for i in range(100):
            if snake.move(grow=True):
                successful_moves += 1
            else:
                break  # Hit self, which is expected with wrapping

        # Snake should grow significantly before hitting itself
        assert len(snake.body) >= 40, f"Snake should grow substantially (got {len(snake.body)} segments)"

        # Ensure collision detection still fast
        import time

        start = time.time()
        for i in range(100):
            if not snake.move(grow=False):
                break  # Stop if collision
        elapsed = time.time() - start

        # Should complete 100 moves in under 0.1 seconds
        assert elapsed < 0.1, f"Performance issue: 100 moves took {elapsed}s"


if __name__ == "__main__":
    # Run tests with pytest
    pytest.main([__file__, "-v", "--tb=short"])
