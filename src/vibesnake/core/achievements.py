"""Achievement definitions, unlock evaluation, and notification state.

Achievements expose explicit, inspectable goals without granting mechanical
power. Their thresholds and rarity labels are design choices that still require
progression telemetry and player review. Notifications are queued so callers can
present them outside time-critical gameplay.
"""

from dataclasses import dataclass
from typing import Dict, List
import time


@dataclass
class Achievement:
    """One persisted accomplishment and its player-facing metadata."""

    id: str
    name: str
    description: str
    icon: str  # Short text badge
    unlock_condition: str  # Human-readable condition
    rarity: str  # "common", "rare", "epic", "legendary"
    unlocked: bool = False
    unlock_time: float = 0.0


# All achievements in the game (25 achievements)
# Rebalanced distribution: 13 common (52%), 7 rare (28%), 4 epic (16%), 1 legendary (4%)
# Introductory goals outnumber long-horizon challenges by design.
ACHIEVEMENTS = {
    # Tutorial achievements (new - teach core mechanics)
    "baby_steps": Achievement(
        id="baby_steps",
        name="Baby Steps",
        description="Complete your first game",
        icon="BS",
        unlock_condition="games_played >= 1",
        rarity="common",
    ),
    "just_a_taste": Achievement(
        id="just_a_taste",
        name="Just a Taste",
        description="Eat 5 food items",
        icon="F5",
        unlock_condition="food_eaten >= 5",
        rarity="common",
    ),
    "wrap_around": Achievement(
        id="wrap_around",
        name="Wrap Around",
        description="Use screen wrapping 3 times",
        icon="W3",
        unlock_condition="wraps >= 3",
        rarity="common",
    ),
    "powered_up": Achievement(
        id="powered_up",
        name="Powered Up",
        description="Collect your first power-up",
        icon="PU",
        unlock_condition="powerups_collected >= 1",
        rarity="common",
    ),
    "quick_reflexes": Achievement(
        id="quick_reflexes",
        name="Quick Reflexes",
        description="Survive for 30 seconds",
        icon="30S",
        unlock_condition="time >= 30",
        rarity="common",
    ),
    "getting_longer": Achievement(
        id="getting_longer",
        name="Getting Longer",
        description="Reach length 5",
        icon="L5",
        unlock_condition="length >= 5",
        rarity="common",
    ),
    # Score-based achievements
    "first_bite": Achievement(
        id="first_bite",
        name="First Bite",
        description="Score your first point",
        icon="P1",
        unlock_condition="score >= 1",
        rarity="common",
    ),
    "century": Achievement(
        id="century",
        name="Century",
        description="Reach 100 points",
        icon="100",
        unlock_condition="score >= 100",
        rarity="common",
    ),
    "high_roller": Achievement(
        id="high_roller",
        name="High Roller",
        description="Reach 500 points in a single game",
        icon="500",
        unlock_condition="score >= 500",
        rarity="rare",
    ),
    "legend": Achievement(
        id="legend",
        name="Legend",
        description="Reach 1000 points in a single game",
        icon="1K",
        unlock_condition="score >= 1000",
        rarity="legendary",
    ),
    # Combo achievements
    "combo_starter": Achievement(
        id="combo_starter",
        name="Combo Starter",
        description="Get a 5x combo multiplier",
        icon="C5",
        unlock_condition="combo >= 5",
        rarity="common",
    ),
    "combo_king": Achievement(
        id="combo_king",
        name="Combo King",
        description="Get a 10x combo multiplier",
        icon="C10",
        unlock_condition="combo >= 10",
        rarity="rare",
    ),
    # Length achievements
    "growing_up": Achievement(
        id="growing_up",
        name="Growing Up",
        description="Reach length 10",
        icon="L10",
        unlock_condition="length >= 10",
        rarity="common",
    ),
    "long_boi": Achievement(
        id="long_boi",
        name="Long Boi",
        description="Reach length 25",
        icon="L25",
        unlock_condition="length >= 25",
        rarity="rare",
    ),
    # Games played achievements
    "newcomer": Achievement(
        id="newcomer",
        name="Newcomer",
        description="Play 5 games",
        icon="G5",
        unlock_condition="games_played >= 5",
        rarity="common",
    ),
    "regular": Achievement(
        id="regular",
        name="Regular",
        description="Play 25 games",
        icon="G25",
        unlock_condition="games_played >= 25",
        rarity="rare",
    ),
    "veteran": Achievement(
        id="veteran",
        name="Veteran",
        description="Play 100 games",
        icon="G+",
        unlock_condition="games_played >= 100",
        rarity="epic",
    ),
    # Special playstyle achievements
    "close_call": Achievement(
        id="close_call",
        name="Close Call",
        description="Get 10 near-misses in one game",
        icon="NM",
        unlock_condition="near_misses >= 10",
        rarity="rare",
    ),
    "power_hungry": Achievement(
        id="power_hungry",
        name="Power Hungry",
        description="Collect 5 power-ups in one game",
        icon="P5",
        unlock_condition="powerups_collected >= 5",
        rarity="rare",
    ),
    # Survival achievements
    "survivor": Achievement(
        id="survivor",
        name="Survivor",
        description="Survive for 3 minutes",
        icon="3M",
        unlock_condition="time >= 180",
        rarity="rare",
    ),
    "marathon_runner": Achievement(
        id="marathon_runner",
        name="Marathon Runner",
        description="Survive for 5 minutes",
        icon="5M",
        unlock_condition="time >= 300",
        rarity="epic",
    ),
    "iron_will": Achievement(
        id="iron_will",
        name="Iron Will",
        description="Reach 200 points in a single game",
        icon="200",
        unlock_condition="score >= 200",
        rarity="epic",
    ),
    "snake_charmer": Achievement(
        id="snake_charmer",
        name="Snake Charmer",
        description="Reach length 35",
        icon="L35",
        unlock_condition="length >= 35",
        rarity="epic",
    ),
    # Misc achievements
    "night_owl": Achievement(
        id="night_owl",
        name="Night Owl",
        description="Play a game between midnight and 3 AM",
        icon="AM",
        unlock_condition="hour >= 0 and hour < 3",
        rarity="common",
    ),
    "early_bird": Achievement(
        id="early_bird",
        name="Early Bird",
        description="Play a game between 3 AM and 6 AM",
        icon="DAY",
        unlock_condition="hour >= 3 and hour < 6",
        rarity="common",
    ),
}


class AchievementManager:
    """Evaluate explicit unlock rules and queue each new notification once.

    Conditions are ordinary code rather than evaluated strings. This keeps the
    persistence boundary auditable and prevents achievement data from becoming
    executable input. Bulk operations are bounded by the fixed definition set.
    """

    def __init__(self):
        """
        Initialize achievement manager with fresh state.

        **Postconditions:**
            - self.achievements populated with deep copy of ACHIEVEMENTS dict
            - self.pending_notifications empty list
            - All achievements marked unlocked=False (fresh profile)

        **Complexity:** O(n) where n = 25 achievements (deep copy cost)
        """
        self.achievements: Dict[str, Achievement] = {}
        self.pending_notifications: List[Achievement] = []
        self._load_achievements()

    def _load_achievements(self):
        """
        Populate manager with fresh achievement instances via deep copy.

        **Deep Copy Rationale:**
        ACHIEVEMENTS dict is global template (singleton pattern):
            Problem: Direct assignment would share references across players
            Solution: Deep copy creates independent instances per profile
            Benefit: Multiple player profiles can have different completion states

        Without deep copy: All players would share same Achievement objects
        (unlock for player1 → affects player2's achievements).

        **Complexity:** O(n) where n = 25 achievements, each with 8 fields
        """
        import copy

        self.achievements = copy.deepcopy(ACHIEVEMENTS)

    def check_achievement(self, achievement_id: str, **kwargs) -> bool:
        """
        Evaluate single achievement against current game state, unlock if satisfied.

        **Unlock Algorithm:**
            1. Guard: Check achievement exists (early exit if invalid ID)
            2. Guard: Check already unlocked (idempotency - no double-unlock)
            3. Evaluate: Match achievement_id to condition logic (if/elif chain)
            4. Mutate: Set unlocked=True, unlock_time=now if condition met
            5. Notify: Append to pending_notifications for display
            6. Return: True if newly unlocked, False otherwise

        **Idempotency:**
        Once unlocked, achievement remains permanently unlocked:
            First call: condition met → unlock (return True)
            Subsequent calls: already unlocked → skip (return False)

        This prevents duplicate notifications and maintains unlock timestamp integrity.

        **Condition Evaluation - Hardcoded Strategy:**
        Uses explicit if/elif chain instead of dynamic evaluation:
            Pro: Type-safe (no eval() injection risk)
            Pro: Debuggable (clear control flow)
            Pro: Readable (explicit conditions vs cryptic callbacks)
            Con: Not data-driven (adding achievement requires code change)

        Trade-off accepted: Security + maintainability > flexibility.

        Args:
            achievement_id: Unique achievement identifier (str from ACHIEVEMENTS keys)
            **kwargs: Game state dictionary with condition variables:
                score: int - current score
                combo: int - combo multiplier
                length: int - snake length
                time: float - elapsed game time (seconds)
                games_played: int - lifetime games count
                near_misses: int - near-miss events this game
                powerups_collected: int - powerups collected this game
                unlocked_items: int - customization items unlocked
                unlocked_all_items: bool - all customizations unlocked flag
                deaths: int - death count this game

        Returns:
            True if achievement was newly unlocked this call, False otherwise
            (False includes: already unlocked, condition not met, invalid ID)

        **Side Effects:**
            - Sets achievement.unlocked = True (permanent mutation)
            - Sets achievement.unlock_time = time.time() (timestamp recording)
            - Appends achievement to pending_notifications queue
            - Prints console log "[Achievement] Unlocked: {name}"

        **Complexity:** O(1) - dict lookup + single condition check
        """
        if achievement_id not in self.achievements:
            return False

        achievement = self.achievements[achievement_id]

        # Already unlocked
        if achievement.unlocked:
            return False

        import datetime

        hour = kwargs.get("hour", datetime.datetime.now().hour)
        conditions = {
            "baby_steps": kwargs.get("games_played", 0) >= 1,
            "just_a_taste": kwargs.get("food_eaten", 0) >= 5,
            "wrap_around": kwargs.get("wraps", 0) >= 3,
            "powered_up": kwargs.get("powerups_collected", 0) >= 1,
            "quick_reflexes": kwargs.get("time", 0) >= 30,
            "getting_longer": kwargs.get("length", 0) >= 5,
            "first_bite": kwargs.get("score", 0) >= 1,
            "century": kwargs.get("score", 0) >= 100,
            "high_roller": kwargs.get("score", 0) >= 500,
            "legend": kwargs.get("score", 0) >= 1000,
            "combo_starter": kwargs.get("combo", 0) >= 5,
            "combo_king": kwargs.get("combo", 0) >= 10,
            "growing_up": kwargs.get("length", 0) >= 10,
            "long_boi": kwargs.get("length", 0) >= 25,
            "newcomer": kwargs.get("games_played", 0) >= 5,
            "regular": kwargs.get("games_played", 0) >= 25,
            "veteran": kwargs.get("games_played", 0) >= 100,
            "close_call": kwargs.get("near_misses", 0) >= 10,
            "power_hungry": kwargs.get("powerups_collected", 0) >= 5,
            "survivor": kwargs.get("time", 0) >= 180,
            "marathon_runner": kwargs.get("time", 0) >= 300,
            "iron_will": kwargs.get("score", 0) >= 200,
            "snake_charmer": kwargs.get("length", 0) >= 35,
            "night_owl": 0 <= hour < 3,
            "early_bird": 3 <= hour < 6,
        }
        unlocked = conditions.get(achievement_id, False)

        if unlocked:
            achievement.unlocked = True
            achievement.unlock_time = time.time()
            self.pending_notifications.append(achievement)
            print(f"[Achievement] Unlocked: {achievement.name} - {achievement.description}")
            return True

        return False

    def check_all_achievements(self, **kwargs):
        """
        Batch evaluate all achievements against current game state.

        **Polling Pattern:**
        Called each frame (or on significant events) with full game state:
            Problem: Many condition checks per frame (potentially expensive)
            Mitigation: check_achievement() has early-exit guards (idempotent)
            Result: Only locked achievements evaluate conditions (unlocked skip)

        As achievements unlock, check count decreases (self-pruning workload).

        **Design Alternative - Event-Driven:**
        Alternative approach: Achievements subscribe to specific events:
            Pro: Fewer checks (only on relevant events)
            Con: Complex event routing (observer pattern overhead)
            Con: Tight coupling (achievements depend on event infrastructure)

        Polling chosen for simplicity: 25 dict lookups negligible on modern hardware.

        Args:
            **kwargs: Complete game state snapshot (see check_achievement() docs)

        **Side Effects:**
            - May unlock multiple achievements (batch mutation)
            - May queue multiple notifications (pending_notifications grows)
            - Console logs for each unlock

        **Complexity:** O(n) where n = 25 achievements
            Worst case: All locked → 25 condition evaluations
            Best case: All unlocked → 25 guard checks only (early exit)
            Typical case: Mix of locked/unlocked → partial evaluation
        """
        for achievement_id in self.achievements.keys():
            self.check_achievement(achievement_id, **kwargs)

    def get_pending_notifications(self) -> List[Achievement]:
        """Return queued unlocks once and clear the internal queue."""
        notifications = self.pending_notifications.copy()
        self.pending_notifications.clear()
        return notifications

    def get_progress(self) -> dict:
        """Return completion counts and percentages for the current profile.

        Results include overall totals plus total and unlocked counts for each
        configured rarity. The method reports persistence state only; it does not
        infer motivation or player experience.
        """
        total = len(self.achievements)
        unlocked = sum(1 for a in self.achievements.values() if a.unlocked)

        by_rarity = {"common": 0, "rare": 0, "epic": 0, "legendary": 0}
        unlocked_by_rarity = {"common": 0, "rare": 0, "epic": 0, "legendary": 0}

        for achievement in self.achievements.values():
            by_rarity[achievement.rarity] += 1
            if achievement.unlocked:
                unlocked_by_rarity[achievement.rarity] += 1

        return {
            "total": total,
            "unlocked": unlocked,
            "percentage": (unlocked / total * 100) if total > 0 else 0,
            "by_rarity": by_rarity,
            "unlocked_by_rarity": unlocked_by_rarity,
        }

    def get_achievement_list(self, filter_unlocked: bool = None) -> List[Achievement]:
        """
        Retrieve achievement list with optional filtering and sorting.

        **Filtering Options:**
        - filter_unlocked=None: Return all achievements (default)
        - filter_unlocked=True: Return only unlocked achievements
        - filter_unlocked=False: Return only locked achievements

        **Sort Order - Rarity-First Display:**
        Two-level sort key:
            1. Primary: Rarity (legendary → epic → rare → common)
            2. Secondary: Status (unlocked before locked within same rarity)

        This creates **aspirational hierarchy display**:
            Top: Legendary achievements (most prestigious)
            Bottom: Common achievements (entry-level)

        Within each tier, unlocked appear first (show accomplishments).

        **UI Use Case:**
        Achievement gallery/menu rendering:
            Display order emphasizes prestige (legendary at top)
            Unlocked first within tier (show progress)
            Locked visible (explicit goals via Zeigarnik effect)

        Args:
            filter_unlocked: Optional bool filter
                None: All achievements (default)
                True: Unlocked only (trophy room)
                False: Locked only (goal list)

        Returns:
            List[Achievement] sorted by rarity (descending) then status

        **Complexity:** O(n log n) where n = 25 achievements (sorting dominates)
        """
        achievements = list(self.achievements.values())

        if filter_unlocked is not None:
            achievements = [a for a in achievements if a.unlocked == filter_unlocked]

        # Sort by rarity (legendary first), then by unlock status
        rarity_order = {"legendary": 0, "epic": 1, "rare": 2, "common": 3}
        achievements.sort(key=lambda a: (rarity_order.get(a.rarity, 999), not a.unlocked))

        return achievements

    def save_state(self) -> dict:
        """
        Serialize achievement state for cross-session persistence.

        **Persistence Strategy - Partial Serialization:**
        Only saves mutable state (unlocked, unlock_time):
            Static data (name, description, icon): Stored in ACHIEVEMENTS template
            Dynamic data (unlocked, unlock_time): Stored in player profile

        This reduces save file size (8 bytes per achievement vs 200+ for full object).

        **Dict Structure:**
        ```python
        {
            "first_bite": {"unlocked": True, "unlock_time": 1709234567.89},
            "century": {"unlocked": False, "unlock_time": 0.0},
            ...
        }
        ```

        **Use Case - Player Profile Persistence:**
        Called on game exit or profile save:
            1. Serialize achievement state (this method)
            2. Store in player profile JSON
            3. On load: Restore via load_state()

        This maintains achievement progress across sessions (permanent unlocks).

        Returns:
            Dict[str, Dict[str, Union[bool, float]]] - achievement state mapping
                Keys: Achievement IDs (str)
                Values: {"unlocked": bool, "unlock_time": float}

        **Complexity:** O(n) where n = 25 achievements (dict comprehension)
        """
        return {
            aid: {"unlocked": ach.unlocked, "unlock_time": ach.unlock_time} for aid, ach in self.achievements.items()
        }

    def load_state(self, state: dict):
        """
        Deserialize achievement state from persisted player profile.

        **Restoration Process:**
        For each saved achievement:
            1. Validate: Check achievement exists in current ACHIEVEMENTS template
            2. Mutate: Restore unlocked status (bool)
            3. Mutate: Restore unlock_time timestamp (float)

        **Version Compatibility:**
        Gracefully handles schema changes:
            Problem: Old save has removed achievement ID
            Solution: Skip unknown IDs (no error thrown)
            Result: Forward compatibility (old saves work with new code)

        This prevents save corruption when achievements are added/removed in updates.

        **State Merging:**
        Loaded state overwrites template defaults:
            Template: All achievements locked (unlocked=False)
            Load: Restores specific unlocked achievements from save
            Result: Player progress preserved

        Args:
            state: Dict[str, Dict[str, Union[bool, float]]] from save_state()
                Keys: Achievement IDs
                Values: {"unlocked": bool, "unlock_time": float}

        **Side Effects:**
            - Mutates self.achievements (restores completion state)
            - Silent skip for unknown achievement IDs (forward compatibility)

        **Complexity:** O(k) where k = number of achievements in save file
            (Typically k = 25, but may be less for old saves)
        """
        for aid, data in state.items():
            if aid in self.achievements:
                self.achievements[aid].unlocked = data.get("unlocked", False)
                self.achievements[aid].unlock_time = data.get("unlock_time", 0.0)
