"""Behavioral tests for scoring, combos, and bonuses."""

import math
import unittest

from vibesnake.core.scoring import MAXIMUM_SCORE, ScoreManager


class TestScoreManager(unittest.TestCase):
    def setUp(self):
        """Create a fresh score manager for each test."""
        self.score_manager = ScoreManager(base_food_points=10, combo_time_threshold=3.0)

    def test_initial_state(self):
        """Score manager starts at zero with no combo."""
        self.assertEqual(self.score_manager.base_score, 0)
        self.assertEqual(self.score_manager.combo_count, 0)
        self.assertEqual(self.score_manager.combo_multiplier, 1.0)

    def test_expired_combo_does_not_create_a_false_speed_bonus(self):
        """A late food remains late after its combo expires."""
        self.score_manager.combo_count = 4
        self.score_manager.time_since_last_food = self.score_manager.combo_time_threshold

        self.score_manager.update(0.05)
        points = self.score_manager.add_food_score(speed_bonus=self.score_manager.time_since_last_food < 1.5)

        self.assertEqual(points, 13)
        self.assertEqual(self.score_manager.combo_count, 1)

    def test_basic_food_score(self):
        """The first food begins the smoothly interpolated combo."""
        points = self.score_manager.add_food_score()
        self.assertEqual(points, 13)
        self.assertEqual(self.score_manager.base_score, 13)
        self.assertEqual(self.score_manager.combo_count, 1)

    def test_combo_multiplier_tiers(self):
        """Combo multipliers interpolate smoothly between milestones."""
        self.score_manager.add_food_score()
        self.assertAlmostEqual(self.score_manager.combo_multiplier, 4 / 3)

        self.score_manager.add_food_score()
        self.score_manager.add_food_score()
        self.assertEqual(self.score_manager.combo_multiplier, 2.0)

        self.score_manager.add_food_score()
        self.score_manager.add_food_score()
        self.assertEqual(self.score_manager.combo_multiplier, 3.0)

        for _ in range(5):
            self.score_manager.add_food_score()
        self.assertEqual(self.score_manager.combo_multiplier, 5.0)

        for _ in range(10):
            self.score_manager.add_food_score()
        self.assertEqual(self.score_manager.combo_multiplier, 10.0)

    def test_combo_multiplier_interpolates_between_milestones(self):
        expected_multipliers = {
            1: 4 / 3,
            2: 5 / 3,
            4: 2.5,
            6: 3.4,
            7: 3.8,
            8: 4.2,
            9: 4.6,
            15: 7.5,
            25: 10.0,
        }

        for combo, expected in expected_multipliers.items():
            with self.subTest(combo=combo):
                self.score_manager.combo_count = combo
                self.assertAlmostEqual(self.score_manager.combo_multiplier, expected)

    def test_combo_breaks_on_timeout(self):
        """Combo breaks if too much time passes."""
        self.score_manager.add_food_score()
        self.score_manager.add_food_score()
        self.assertEqual(self.score_manager.combo_count, 2)

        # Simulate 3.5 seconds passing (threshold is 3.0)
        self.score_manager.update(3.5)

        self.assertEqual(self.score_manager.combo_count, 0)
        self.assertEqual(self.score_manager.combo_multiplier, 1.0)
        self.assertEqual(self.score_manager.time_since_last_food, 3.5)

    def test_combo_maintained_within_threshold(self):
        """Combo stays if food eaten within threshold."""
        self.score_manager.add_food_score()
        self.score_manager.update(2.0)  # 2 seconds < 3 second threshold
        self.score_manager.add_food_score()

        self.assertEqual(self.score_manager.combo_count, 2)

    def test_speed_bonus(self):
        """Speed bonus adds 50% extra points."""
        points = self.score_manager.add_food_score(speed_bonus=True)
        self.assertEqual(points, 18)

    def test_risk_bonus(self):
        """Risk bonus adds 25% extra points."""
        points = self.score_manager.add_food_score(risk_bonus=True)
        self.assertEqual(points, 15)

    def test_length_bonus(self):
        """Length bonus scales with snake size."""
        # No bonus for length <= 10
        points = self.score_manager.add_food_score(snake_length=5)
        self.assertEqual(points, 13)

        # A fresh combo makes the logarithmic length component easy to isolate.
        points = ScoreManager().add_food_score(snake_length=20)
        self.assertEqual(points, 27)

    def test_length_bonus_follows_the_current_balance_contract(self):
        for snake_length in (11, 20, 50, 100, 200):
            with self.subTest(snake_length=snake_length):
                manager = ScoreManager()
                points = manager.add_food_score(snake_length=snake_length)
                base_points = int(manager.base_food_points * manager.combo_multiplier)
                expected_bonus = int((snake_length - 10) * math.log(snake_length) / 2)
                self.assertEqual(points - base_points, expected_bonus)

    def test_all_bonuses_stack(self):
        """All bonuses can be active simultaneously."""
        # Combo of 5 (3x multiplier) + speed + risk + length
        for _ in range(5):
            self.score_manager.add_food_score()

        points = self.score_manager.add_food_score(speed_bonus=True, risk_bonus=True, snake_length=30)

        # Combo 6 is 3.4x (34), plus 5 speed, 2 risk, and 34 length.
        self.assertEqual(points, 75)

    def test_score_saturates_and_returns_only_awarded_points(self):
        """Every score mutation respects the portable release ceiling."""
        self.score_manager.base_score = MAXIMUM_SCORE - 1

        self.assertEqual(self.score_manager.add_food_score(), 1)
        self.assertEqual(self.score_manager.base_score, MAXIMUM_SCORE)
        self.assertEqual(self.score_manager.add_food_score(), 0)
        self.assertEqual(self.score_manager.base_score, MAXIMUM_SCORE)

        self.score_manager.add_bonus_score(100)
        self.assertEqual(self.score_manager.base_score, MAXIMUM_SCORE)

        with self.assertRaisesRegex(ValueError, "bonus must not be negative"):
            self.score_manager.add_bonus_score(-1)

    def test_break_combo_on_death(self):
        """Death resets combo."""
        for _ in range(5):
            self.score_manager.add_food_score()

        self.assertEqual(self.score_manager.combo_count, 5)

        lost = self.score_manager.break_combo_on_death()
        self.assertEqual(lost, 5)
        self.assertEqual(self.score_manager.combo_count, 0)

    def test_get_display_info(self):
        """Display info returns correct data."""
        self.score_manager.add_food_score()
        self.score_manager.add_food_score()
        self.score_manager.add_food_score()  # 3 food = 2x multiplier

        info = self.score_manager.get_display_info()
        self.assertEqual(info["score"], 49)
        self.assertEqual(info["combo"], 3)
        self.assertEqual(info["multiplier"], 2.0)


if __name__ == "__main__":
    unittest.main()
