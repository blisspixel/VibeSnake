"""Personality-weighted AI movement and JSON personality loading.

The reference planner combines safety, target distance, and configurable behavior
weights. Entertainment and distinctness remain outcomes for tournament analysis
and structured observation, not properties established by these parameters.
"""

import random
import json
from pathlib import Path
from typing import Tuple, Optional, List, Dict
from dataclasses import dataclass, asdict
from vibesnake.core.enums import Direction


@dataclass
class AIPersonality:
    """
    Defines an AI player's personality and behavior patterns.

    Attributes:
        name: Display name for this AI
        description: Funny description of play style
        aggression: 0-1, how much they chase power-ups over safety
        risk_tolerance: 0-1, how close to walls/self they're willing to go
        patience: 0-1, how much they plan ahead vs react
        greed: 0-1, prioritize score over survival
        chaos: 0-1, random unpredictable moves
        power_up_priority: 0-1, how much they hunt power-ups
    """

    name: str
    description: str
    aggression: float = 0.5
    risk_tolerance: float = 0.5
    patience: float = 0.5
    greed: float = 0.5
    chaos: float = 0.0
    power_up_priority: float = 0.5
    color: Tuple[int, int, int] = (100, 255, 100)  # Display color

    def to_dict(self) -> Dict:
        """Convert to dictionary for JSON export."""
        return asdict(self)

    @classmethod
    def from_dict(cls, data: Dict) -> "AIPersonality":
        """Create personality from dictionary (JSON load)."""
        # Handle color as list from JSON
        if "color" in data and isinstance(data["color"], list):
            data["color"] = tuple(data["color"])
        return cls(**data)

    def to_json_file(self, filepath: Path):
        """Save personality to JSON file."""
        with open(filepath, "w") as f:
            json.dump(self.to_dict(), f, indent=2)

    @classmethod
    def from_json_file(cls, filepath: Path) -> "AIPersonality":
        """Load personality from JSON file."""
        with open(filepath, "r") as f:
            data = json.load(f)
        return cls.from_dict(data)


# Pre-defined entertaining AI personalities
AI_PERSONALITIES = {
    "speed_demon": AIPersonality(
        name="Speed Demon",
        description="GOTTA GO FAST! Chases food aggressively, no time for safety.",
        aggression=0.95,
        risk_tolerance=0.8,
        patience=0.2,
        greed=0.9,
        chaos=0.3,
        power_up_priority=0.4,
        color=(255, 50, 50),
    ),
    "coward": AIPersonality(
        name="The Coward",
        description="Plays it safe. Very safe. Too safe. Probably won't even move.",
        aggression=0.1,
        risk_tolerance=0.1,
        patience=0.95,
        greed=0.2,
        chaos=0.05,
        power_up_priority=0.1,
        color=(150, 150, 255),
    ),
    "greedy": AIPersonality(
        name="Mr. Greedy",
        description="MOAR POINTS! Will risk everything for combo multipliers.",
        aggression=0.7,
        risk_tolerance=0.6,
        patience=0.4,
        greed=1.0,
        chaos=0.2,
        power_up_priority=0.3,
        color=(255, 215, 0),
    ),
    "power_hunter": AIPersonality(
        name="Power Hunter",
        description="Obsessed with power-ups. Food is secondary.",
        aggression=0.8,
        risk_tolerance=0.7,
        patience=0.5,
        greed=0.4,
        power_up_priority=1.0,
        chaos=0.1,
        color=(255, 0, 255),
    ),
    "drunk": AIPersonality(
        name="The Drunk Snake",
        description="What's even happening right now? Random chaos energy.",
        aggression=0.5,
        risk_tolerance=0.5,
        patience=0.1,
        greed=0.5,
        chaos=0.9,
        power_up_priority=0.5,
        color=(255, 100, 200),
    ),
    "optimal": AIPersonality(
        name="The Optimal",
        description="Calculated. Precise. Boring but effective.",
        aggression=0.6,
        risk_tolerance=0.4,
        patience=0.9,
        greed=0.6,
        chaos=0.0,
        power_up_priority=0.7,
        color=(100, 255, 255),
    ),
    "yolo": AIPersonality(
        name="YOLO Mode",
        description="Lives on the edge. If it's not risky, it's not worth it.",
        aggression=1.0,
        risk_tolerance=1.0,
        patience=0.0,
        greed=0.8,
        chaos=0.5,
        power_up_priority=0.9,
        color=(255, 140, 0),
    ),
    "balanced": AIPersonality(
        name="The Balanced",
        description="Perfectly balanced, as all things should be.",
        aggression=0.5,
        risk_tolerance=0.5,
        patience=0.5,
        greed=0.5,
        chaos=0.1,
        power_up_priority=0.5,
        color=(100, 255, 100),
    ),
    "wall_hugger": AIPersonality(
        name="Wall Hugger",
        description="Loves the edges. Scared of the center. It's cozy here.",
        aggression=0.3,
        risk_tolerance=0.2,
        patience=0.7,
        greed=0.3,
        chaos=0.2,
        power_up_priority=0.2,
        color=(139, 69, 19),
    ),
    "zen_master": AIPersonality(
        name="Zen Master",
        description="Patient, calculated, flows like water. Very chill.",
        aggression=0.3,
        risk_tolerance=0.3,
        patience=1.0,
        greed=0.3,
        chaos=0.0,
        power_up_priority=0.6,
        color=(200, 255, 200),
    ),
}


def load_custom_ai_personalities(directory: Path = None) -> Dict[str, AIPersonality]:
    """Load JSON personalities for repeatable tournaments and behavior analysis.

    Args:
        directory: Path to directory containing .json personality files
                   Defaults to assets/ai/custom/

    Returns:
        Dict mapping personality keys to AIPersonality objects
    """
    if directory is None:
        directory = Path("assets/ai/custom")

    personalities = {}

    if not directory.exists():
        return personalities

    for json_file in directory.glob("*.json"):
        try:
            personality = AIPersonality.from_json_file(json_file)
            # Use filename (without .json) as key
            key = json_file.stem
            personalities[key] = personality
            print(f"[AI] Loaded custom personality: {personality.name} ({key})")
        except Exception as e:
            print(f"[AI] Failed to load {json_file}: {e}")

    return personalities


def get_all_ai_personalities() -> Dict[str, AIPersonality]:
    """
    Get all available AI personalities (built-in + custom).

    Returns:
        Dict mapping personality keys to AIPersonality objects
    """
    all_personalities = AI_PERSONALITIES.copy()

    # Load and merge custom personalities
    custom = load_custom_ai_personalities()
    all_personalities.update(custom)

    return all_personalities


class AIPlayer:
    """
    AI player that controls snake movement based on personality.

    This is the "brain" that decides where the snake should go based on
    game state and personality traits.
    """

    def __init__(self, personality_key: str = "balanced"):
        """
        Initialize AI player with a personality.

        Args:
            personality_key: Key from AI_PERSONALITIES dict
        """
        self.personality = AI_PERSONALITIES.get(personality_key, AI_PERSONALITIES["balanced"])
        self.decision_timer = 0.0
        self.decision_cooldown = 0.1  # How often AI makes decisions (seconds)
        self.last_direction: Optional[Direction] = None

    def get_direction(
        self,
        dt: float,
        snake_head: Tuple[int, int],
        current_direction: Direction,
        food_position: Tuple[int, int],
        powerup_positions: List[Tuple[int, int]],
        snake_body: List[Tuple[int, int]],
        grid_width: int,
        grid_height: int,
    ) -> Optional[Direction]:
        """
        Decide next move based on personality and game state.

        Args:
            dt: Delta time since last update
            snake_head: Current head position
            current_direction: Current movement direction
            food_position: Food location
            powerup_positions: List of power-up locations
            snake_body: All snake body positions
            grid_width: Grid width
            grid_height: Grid height

        Returns:
            Direction to move, or None to continue current direction
        """
        # Update decision timer
        self.decision_timer += dt
        if self.decision_timer < self.decision_cooldown:
            return None  # Don't make decision yet

        self.decision_timer = 0.0

        # Chaos mode - random moves sometimes
        if random.random() < self.personality.chaos:
            return self._random_safe_direction(snake_head, current_direction, snake_body, grid_width, grid_height)

        # Calculate target based on priorities
        target = self._choose_target(snake_head, food_position, powerup_positions)

        # Get valid directions (no reversing, no immediate collision)
        valid_directions = self._get_valid_directions(
            snake_head, current_direction, snake_body, grid_width, grid_height
        )

        if not valid_directions:
            return None  # No valid moves

        # Calculate danger score for each direction
        direction_scores = {}
        for direction in valid_directions:
            score = self._score_direction(snake_head, direction, target, snake_body, grid_width, grid_height)
            direction_scores[direction] = score

        # Choose best direction based on scores
        best_direction = max(direction_scores, key=direction_scores.get)
        self.last_direction = best_direction
        return best_direction

    def _choose_target(
        self, snake_head: Tuple[int, int], food_position: Tuple[int, int], powerup_positions: List[Tuple[int, int]]
    ) -> Tuple[int, int]:
        """Choose where to go based on personality."""
        # Check if we should prioritize power-ups
        if powerup_positions and random.random() < self.personality.power_up_priority:
            # Find closest power-up
            closest_powerup = min(
                powerup_positions, key=lambda p: abs(p[0] - snake_head[0]) + abs(p[1] - snake_head[1])
            )
            return closest_powerup

        # Default to food
        return food_position

    def _get_valid_directions(
        self,
        snake_head: Tuple[int, int],
        current_direction: Direction,
        snake_body: List[Tuple[int, int]],
        grid_width: int,
        grid_height: int,
    ) -> List[Direction]:
        """Get list of directions that won't immediately kill us."""
        valid = []

        for direction in Direction:
            # Can't reverse
            if direction == Direction.opposite(current_direction):
                continue

            # Check if this move is safe
            dx, dy = direction.vector()
            new_x = (snake_head[0] + dx) % grid_width
            new_y = (snake_head[1] + dy) % grid_height
            new_pos = (new_x, new_y)

            # Check collision with body (excluding tail since it will move)
            if new_pos in snake_body[:-1]:
                continue

            valid.append(direction)

        return valid

    def _score_direction(
        self,
        snake_head: Tuple[int, int],
        direction: Direction,
        target: Tuple[int, int],
        snake_body: List[Tuple[int, int]],
        grid_width: int,
        grid_height: int,
    ) -> float:
        """
        Score a direction based on personality traits.

        Higher score = better move.
        """
        dx, dy = direction.vector()
        new_x = (snake_head[0] + dx) % grid_width
        new_y = (snake_head[1] + dy) % grid_height
        new_pos = (new_x, new_y)

        score = 0.0

        # Distance to target (closer = better)
        target_dist = abs(new_pos[0] - target[0]) + abs(new_pos[1] - target[1])
        score += (grid_width + grid_height - target_dist) * 10 * self.personality.aggression

        # Safety score (avoid being near body)
        danger_cells = 0
        for check_dx in [-1, 0, 1]:
            for check_dy in [-1, 0, 1]:
                check_x = (new_pos[0] + check_dx) % grid_width
                check_y = (new_pos[1] + check_dy) % grid_height
                if (check_x, check_y) in snake_body:
                    danger_cells += 1

        safety_score = (9 - danger_cells) * 5
        score += safety_score * (1.0 - self.personality.risk_tolerance)

        # Patience - prefer continuing in same direction
        if direction == self.last_direction:
            score += 3 * self.personality.patience

        return score

    def _random_safe_direction(
        self,
        snake_head: Tuple[int, int],
        current_direction: Direction,
        snake_body: List[Tuple[int, int]],
        grid_width: int,
        grid_height: int,
    ) -> Optional[Direction]:
        """Pick a random safe direction for chaos mode."""
        valid = self._get_valid_directions(snake_head, current_direction, snake_body, grid_width, grid_height)
        return random.choice(valid) if valid else None
