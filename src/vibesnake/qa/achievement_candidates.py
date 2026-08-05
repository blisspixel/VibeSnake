"""Rules-local achievement candidate evaluation for dual-runtime parity.

IDs, order, and conditions match the pure C# ``AchievementCatalog`` subset.
Profile-lifetime and wall-clock achievements are intentionally excluded.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Callable, Iterable, Sequence

# Matches RunConfig.RulesTickMilliseconds on the native side.
RULES_TICK_MILLISECONDS = 50


@dataclass(frozen=True)
class AchievementDefinition:
    """One rules-local achievement candidate definition."""

    id: str
    name: str
    description: str
    rarity: str


@dataclass(frozen=True)
class RunAchievementMetrics:
    """Snapshot of run-local metrics used to decide achievement candidates."""

    score: int
    max_combo: int
    length: int
    food_eaten: int
    wrap_count: int
    near_misses: int
    powerups_collected: int
    survival_ticks: int
    is_terminal: bool


DEFINITIONS: tuple[AchievementDefinition, ...] = (
    AchievementDefinition("first_bite", "First Bite", "Score your first point", "common"),
    AchievementDefinition("century", "Century", "Reach 100 points", "common"),
    AchievementDefinition(
        "high_roller",
        "High Roller",
        "Reach 500 points in a single game",
        "rare",
    ),
    AchievementDefinition(
        "legend",
        "Legend",
        "Reach 1000 points in a single game",
        "legendary",
    ),
    AchievementDefinition("just_a_taste", "Just a Taste", "Eat 5 food items", "common"),
    AchievementDefinition("getting_longer", "Getting Longer", "Reach length 5", "common"),
    AchievementDefinition("growing_strong", "Growing Strong", "Reach length 10", "common"),
    AchievementDefinition("serpent", "Serpent", "Reach length 25", "rare"),
    AchievementDefinition("combo_starter", "Combo Starter", "Get a 5x combo", "common"),
    AchievementDefinition("combo_king", "Combo King", "Get a 10x combo", "rare"),
    AchievementDefinition("wrap_around", "Wrap Around", "Use screen wrapping 3 times", "common"),
    AchievementDefinition(
        "close_call",
        "Close Call",
        "Get 10 near-misses in one game",
        "rare",
    ),
    AchievementDefinition(
        "powered_up",
        "Powered Up",
        "Collect your first power-up",
        "common",
    ),
    AchievementDefinition(
        "power_hungry",
        "Power Hungry",
        "Collect 5 power-ups in one game",
        "rare",
    ),
    AchievementDefinition(
        "quick_reflexes",
        "Quick Reflexes",
        "Survive for 30 seconds",
        "common",
    ),
    AchievementDefinition("endurance", "Endurance", "Survive for 180 seconds", "rare"),
    AchievementDefinition("marathon", "Marathon", "Survive for 300 seconds", "epic"),
)

_CONDITIONS: dict[str, Callable[[RunAchievementMetrics], bool]] = {
    "first_bite": lambda m: m.score >= 1,
    "century": lambda m: m.score >= 100,
    "high_roller": lambda m: m.score >= 500,
    "legend": lambda m: m.score >= 1000,
    "just_a_taste": lambda m: m.food_eaten >= 5,
    "getting_longer": lambda m: m.length >= 5,
    "growing_strong": lambda m: m.length >= 10,
    "serpent": lambda m: m.length >= 25,
    "combo_starter": lambda m: m.max_combo >= 5,
    "combo_king": lambda m: m.max_combo >= 10,
    "wrap_around": lambda m: m.wrap_count >= 3,
    "close_call": lambda m: m.near_misses >= 10,
    "powered_up": lambda m: m.powerups_collected >= 1,
    "power_hungry": lambda m: m.powerups_collected >= 5,
    "quick_reflexes": lambda m: m.survival_ticks * RULES_TICK_MILLISECONDS >= 30_000,
    "endurance": lambda m: m.survival_ticks * RULES_TICK_MILLISECONDS >= 180_000,
    "marathon": lambda m: m.survival_ticks * RULES_TICK_MILLISECONDS >= 300_000,
}


def index_of(achievement_id: str) -> int:
    """Return the zero-based catalog index, or -1 when unknown."""
    if not achievement_id or not achievement_id.strip():
        raise ValueError("achievement_id must be non-empty")
    for index, definition in enumerate(DEFINITIONS):
        if definition.id == achievement_id:
            return index
    return -1


def definition_at(index: int) -> AchievementDefinition | None:
    """Return the definition at index, or None when out of range."""
    if index < 0 or index >= len(DEFINITIONS):
        return None
    return DEFINITIONS[index]


def evaluate_candidates(
    metrics: RunAchievementMetrics,
    already_unlocked: Iterable[str] | None = None,
    *,
    require_terminal: bool = True,
) -> list[str]:
    """Return newly earned rules-local achievement IDs in catalog order."""
    if require_terminal and not metrics.is_terminal:
        return []

    unlocked = set(already_unlocked or ())
    earned: list[str] = []
    for definition in DEFINITIONS:
        if definition.id in unlocked:
            continue
        condition = _CONDITIONS.get(definition.id)
        if condition is None:
            continue
        if condition(metrics):
            earned.append(definition.id)
    return earned


def candidate_event_values(
    metrics: RunAchievementMetrics,
    already_unlocked: Iterable[str] | None = None,
) -> Sequence[int]:
    """Return catalog indexes for terminal achievement_candidate events."""
    values: list[int] = []
    for achievement_id in evaluate_candidates(metrics, already_unlocked):
        index = index_of(achievement_id)
        if index >= 0:
            values.append(index)
    return values
