"""Legacy Python reference coordinator for game state and subsystem wiring.

This module owns menu transitions, input routing, fixed-tick gameplay,
presentation, audio, persistence, and run finalization for the source-player
build. It remains the behavioral reference while scored rules move into the
pure C# kernel. Presentation timing must not reorder deterministic rule steps.
"""

import pygame
import random

from vibesnake.core.snake import Snake
from vibesnake.core.food import Food
from vibesnake.rendering.hud import HUD
from vibesnake.core.enums import GameState, DeathCause
from vibesnake.core.exceptions import GridFullException
from vibesnake.data import settings
from vibesnake.audio.manager import EAT_SOUND, LOST_SOUND, play_music
from vibesnake.audio.radio_manager import initialize_radio
from vibesnake.powerups.manager import PowerUpManager
from vibesnake.powerups.cadence import clear_cadence_factors
from vibesnake.powerups.boost import BoostPowerUp
from vibesnake.powerups.laststand import LastStandPowerUp
from vibesnake.powerups.shield import ShieldPowerUp
from vibesnake.rendering.display import AdaptiveDisplay
from vibesnake.rendering.menus import Menu
from vibesnake.rendering.visual_effects import VisualEffectsManager, BackgroundRenderer
from vibesnake.core.scoring import ScoreManager
from vibesnake.core.metrics import MetricsTracker
from vibesnake.core.near_miss import NearMissDetector
from vibesnake.core.customization import (
    CustomizationManager,
    COLOR_PRESETS,
    PATTERN_OPTIONS,
    EYE_STYLES,
    ACCESSORIES,
    TRAILS,
)
from vibesnake.core.player_profile import PlayerProfile
from vibesnake.core.achievements import AchievementManager
from vibesnake.core.high_scores import HighScoreTable
from vibesnake.core.user_settings import UserSettings
from vibesnake.input.input_manager import InputManager
from vibesnake.ai.player import AIPlayer, get_all_ai_personalities
from vibesnake.utils.logger import get_logger

logger = get_logger(__name__)

_RADIO_CONTROL_STATES = frozenset(
    {
        GameState.RUNNING,
        GameState.PAUSED,
        GameState.GAME_OVER,
        GameState.LETS_PLAY,
    }
)


class Game:
    """Coordinate the Python reference game's state machine and subsystems.

    Game owns the Pygame window, menus, input, run state, score, powers,
    progression, audio, and rendering adapters. Frame delta accumulates until a
    movement tick is due. Hitstop may pause rule advancement while rendering
    continues. Rule changes require deterministic tests because presentation
    output alone cannot establish correctness, clarity, or player experience.
    """

    def __init__(self):
        self.user_settings = UserSettings(
            default_sound_enabled=settings.SOUND_ENABLED,
            default_volume=settings.SOUND_VOLUME,
        )
        self.fullscreen = self.user_settings.fullscreen
        # Logical canvas stays rule-stable; the OS window is adaptive (default 4:3 framing).
        self.display = AdaptiveDisplay(
            settings.WIDTH,
            settings.HEIGHT,
            fullscreen=self.fullscreen,
            preferred_aspect=(4, 3),
            integer_scale=True,
            caption="Vibe Snake",
        )
        self.screen = self.display.canvas
        self.clock = pygame.time.Clock()
        self.menu = Menu(self.screen)
        self.high_score_table = HighScoreTable()
        self.hud = HUD(self.high_score_table)
        self.input_manager = InputManager()
        self.visual_effects = VisualEffectsManager()
        self.background = BackgroundRenderer(settings.WIDTH, settings.HEIGHT)
        self.sound_on = self.user_settings.sound_enabled
        self.volume = self.user_settings.volume

        # Player profile system
        self.player_profile = PlayerProfile()

        # Initialize GTA-style radio system with random station and song
        self.radio = initialize_radio()
        if self.radio and self.radio.available_stations:
            self.radio.set_volume(self.volume)
            import random

            # Pick random available station index
            random_station_index = random.choice(self.radio.available_stations)
            # Switch to that station (this will pick a random song from it)
            self.radio.switch_station(random_station_index)
            if not self.sound_on:
                self.radio.stop()
            current_station = self.radio.get_current_station()
            print(f"[Radio] Started with random station: {current_station.name if current_station else 'Unknown'}")

        # Let's Play mode (Wargames) state - load all AI personalities (built-in + custom)
        self.ai_player = None
        self.ai_personality_key = None
        self.lets_play_runs_history = []  # Track AI performance
        self.all_ai_personalities = get_all_ai_personalities()  # Includes custom JSONs

        # Channel browser state
        self.channel_browser_index = 0
        self.channel_list = list(self.all_ai_personalities.items())
        self.channel_browser_idle_timer = 0.0  # Auto-select after 5s of inactivity

        # Name entry state
        self.player_name = ""
        self.cursor_blink_timer = 0.0
        self.cursor_visible = True

        # Customization state
        self.customization_manager = CustomizationManager()
        self.customization_category = 0  # 0=Color, 1=Pattern, 2=Eyes, 3=Accessory, 4=Trail
        self.customization_option = 0
        self.customization_options = []  # Current category options
        self.customization_notification = None  # Temporary notification message
        self.customization_notification_timer = 0.0  # Timer for notification display

        # Achievement system
        self.achievement_manager = AchievementManager()
        self.achievement_manager.load_state(self.player_profile.achievement_state)
        self.achievement_notifications = []  # Queue of achievements to show
        self.current_achievement_display = None  # Currently displayed achievement
        self.achievement_display_timer = 0.0  # Time remaining for current display
        self.achievements_scroll_offset = 0  # Scroll offset for achievements menu

        # Settings menu state
        self.settings_selected_option = 0  # Currently selected setting

        # Game over message (chosen once, displayed consistently)
        self.game_over_message = ""

        # AI auto-restart timer for Let's Play mode
        self.ai_game_over_timer = 0.0
        self.ai_auto_restart_delay = 3.0  # 3 seconds before auto-restart

        # Always start at menu - no name entry on first launch
        # Create anonymous profile if none exists
        if not self.player_profile.has_profile():
            print("[Init] NO PROFILE - Creating anonymous profile and going to MENU")
            self.player_profile.create_profile("Anonymous")
        else:
            print(f"[Init] Profile found: {self.player_profile.get_name()} - Going to MENU")

        self.state = GameState.MENU
        self.reset()

        # Initialize FSM transition validation map
        self._init_fsm_transition_map()

    def _init_fsm_transition_map(self):
        """
        Initialize Finite State Machine (FSM) transition whitelist for crash prevention.

        **Purpose:**
        Prevents illegal state transitions that could cause crashes, data corruption,
        or undefined behavior through guard clause validation.

        **Problem Statement (from ENHANCEMENT_ROADMAP.md):**
        Current: 11 states, no illegal transition prevention
        Risk: Crashes from invalid state transitions (e.g., LETS_PLAY → CUSTOMIZE)

        **FSM Pattern - Transition Whitelist:**
        Implements explicit valid transitions rather than blacklist:
            Pro: Fail-safe by default (unknown transitions rejected)
            Pro: Self-documenting (map shows all valid flows)
            Con: Requires update when adding new transitions

        **Transition Map Structure:**
        Dictionary mapping: current_state → Set[allowed_next_states]

        **Design Decision - Bidirectional vs Unidirectional:**
        Some transitions bidirectional (MENU ↔ HELP), others unidirectional:
            Bidirectional: Modal overlays that preserve underlying state
            Unidirectional: State changes requiring initialization (MENU → RUNNING)

        **Validation Strategy:**
        Before every `self.state = new_state`:
            1. Check if transition in allowed set
            2. If valid: Allow transition
            3. If invalid: Log error, reject transition (maintain current state)

        **Alternative Approaches Considered:**

        1. Blacklist (rejected):
           - Problem: Fails open (unknown transitions allowed by default)
           - Problem: Hard to reason about all invalid combinations

        2. State machine library (rejected):
           - Problem: Overkill for 11 states
           - Problem: Adds dependency, learning curve

        3. Transition validation in __setattr__ (rejected):
           - Problem: Too magical (implicit validation)
           - Problem: Performance overhead on all attribute sets

        **Chosen Approach:** Explicit guard method `can_transition()`
        - Pro: Explicit validation at call sites
        - Pro: Easy to debug (clear error messages)
        - Pro: No performance impact on non-state attributes

        **Complexity:** O(1) - set membership check

        See: Gamma et al. (1994) "Design Patterns" - State Pattern
             ENHANCEMENT_ROADMAP.md Phase 1 - State Transition Validation
        """
        self._fsm_transitions = {
            # MENU: Hub state - can go to all pre-game states + start gameplay
            GameState.MENU: {
                GameState.RUNNING,  # Start game (ENTER key)
                GameState.HELP,  # Help overlay (H key)
                GameState.SETTINGS,  # Settings menu (S key)
                GameState.CUSTOMIZE,  # Customization (C key)
                GameState.ACHIEVEMENTS,  # Achievements (A key)
                GameState.HIGH_SCORES,  # High scores (V key)
                GameState.CHANNEL_BROWSER,  # Let's Play browser (L key)
            },
            # HELP: Modal overlay over MENU
            GameState.HELP: {
                GameState.MENU,  # Close help (H key or ESC)
            },
            # SETTINGS: Configuration menu
            GameState.SETTINGS: {
                GameState.MENU,  # Back to menu (ESC)
            },
            # CUSTOMIZE: Avatar customization
            GameState.CUSTOMIZE: {
                GameState.MENU,  # Back to menu (ESC)
            },
            # ACHIEVEMENTS: Progress tracking display
            GameState.ACHIEVEMENTS: {
                GameState.MENU,  # Back to menu (ESC)
            },
            # HIGH_SCORES: Leaderboard display
            GameState.HIGH_SCORES: {
                GameState.MENU,  # Back to menu (ESC)
            },
            # CHANNEL_BROWSER: AI personality selection
            GameState.CHANNEL_BROWSER: {
                GameState.LETS_PLAY,  # Start watching AI (ENTER)
                GameState.MENU,  # Cancel (ESC)
            },
            # LETS_PLAY: Watching AI gameplay
            GameState.LETS_PLAY: {
                GameState.GAME_OVER,  # AI dies
                GameState.MENU,  # Stop watching (L key or ESC)
                GameState.LETS_PLAY,  # Auto-restart AI (after game over timer)
            },
            # RUNNING: Active gameplay
            GameState.RUNNING: {
                GameState.PAUSED,  # Pause game (P key or ESC)
                GameState.GAME_OVER,  # Player dies
            },
            # PAUSED: Suspended gameplay
            GameState.PAUSED: {
                GameState.RUNNING,  # Resume (P key or ESC)
                GameState.MENU,  # Quit to menu
            },
            # GAME_OVER: Terminal gameplay state
            GameState.GAME_OVER: {
                GameState.NAME_ENTRY,  # High score achieved
                GameState.MENU,  # Restart or quit
                GameState.HIGH_SCORES,  # After name entry, show leaderboard
                GameState.RUNNING,  # Quick restart (ENTER)
            },
            # NAME_ENTRY: High score name input
            GameState.NAME_ENTRY: {
                GameState.GAME_OVER,  # After submitting name
                GameState.HIGH_SCORES,  # After submitting name (alternate flow)
            },
        }

    def can_transition(self, from_state: GameState, to_state: GameState) -> bool:
        """
        Validate whether state transition is legal according to FSM rules.

        **Guard Clause Pattern:**
        Returns bool to enable if/else validation at call sites:
            ```python
            if self.can_transition(self.state, GameState.RUNNING):
                self.state = GameState.RUNNING
            else:
                logger.error(f"Illegal transition: {self.state} → RUNNING")
            ```

        **Fail-Safe Default:**
        If from_state not in map: Return False (reject unknown transitions)
        Rationale: Conservative approach prevents crashes from undefined behavior

        **Logging:**
        Illegal transitions logged at ERROR level for debugging:
            - Shows attempted transition (from → to)
            - Enables detection of logic bugs in input handlers

        Args:
            from_state: Current game state
            to_state: Desired next state

        Returns:
            bool - True if transition valid, False if illegal

        **Side Effects:**
            - Logs error message if transition invalid
            - No state modification (pure validation)

        **Complexity:** O(1) - dict lookup + set membership check

        **Usage Example:**
        ```python
        # Before (unsafe):
        self.state = GameState.CUSTOMIZE

        # After (guarded):
        if self.can_transition(self.state, GameState.CUSTOMIZE):
            self.state = GameState.CUSTOMIZE
        else:
            print("Cannot enter customization from current state")
        ```
        """
        if from_state not in self._fsm_transitions:
            logger.error(f"[FSM] Unknown state in transition map: {from_state}")
            return False

        allowed = self._fsm_transitions[from_state]
        is_valid = to_state in allowed

        if not is_valid:
            logger.error(f"[FSM] Illegal transition attempt: {from_state} → {to_state}")
            logger.error(f"[FSM] Valid transitions from {from_state}: {[s.name for s in allowed]}")

        return is_valid

    def transition_to(self, new_state: GameState) -> bool:
        """
        Safely transition to new state with validation and logging.

        **Convenience Method:**
        Combines validation + transition + logging in single call:
            - Validates transition via can_transition()
            - Performs state change if valid
            - Logs transition for debugging

        **Benefits:**
        1. Single call site (DRY principle)
        2. Automatic logging of all transitions
        3. Fail-safe (invalid transitions rejected)

        **Usage Pattern:**
        ```python
        # Instead of:
        if self.can_transition(self.state, GameState.MENU):
            self.state = GameState.MENU
            print("Transitioned to MENU")

        # Use:
        if not self.transition_to(GameState.MENU):
            print("Failed to transition to MENU")
        ```

        Args:
            new_state: Desired state to transition to

        Returns:
            bool - True if transition succeeded, False if rejected

        **Side Effects:**
            - Updates self.state if valid
            - Logs transition at INFO level if valid
            - Logs error if invalid (via can_transition)

        **Complexity:** O(1) - validation + assignment + logging
        """
        old_state = self.state
        if self.can_transition(old_state, new_state):
            self.state = new_state
            logger.info(f"[FSM] State transition: {old_state.name} → {new_state.name}")
            return True
        return False

    def toggle_fullscreen(self):
        """
        Toggle between fullscreen and windowed display modes.

        Drawing always targets the fixed logical canvas. The OS window is
        recreated; letterboxing adapts the canvas to phone, square, wide, or
        classic 4:3 frames without changing grid rules.
        """
        self.fullscreen = not self.fullscreen
        if self.fullscreen:
            display_info = pygame.display.Info()
            print(f"[Display] Switching to fullscreen ({display_info.current_w}x{display_info.current_h})")
        else:
            print("[Display] Switching to adaptive windowed presentation (4:3 preferred)")
        self.display.set_fullscreen(self.fullscreen)
        self.user_settings.fullscreen = self.fullscreen
        self.user_settings.save()

    def reset(self):
        """
        Reset all game subsystems to initial state for new game session.

        **Purpose:**
        Reinitializes all gameplay systems while preserving persistent data:
            Reset: Snake, Food, Score, Powerups, Metrics, NearMiss, Timers
            Preserve: PlayerProfile, HighScoreTable, Achievements, Customization

        **Customization Routing:**
        Applies different appearance based on game mode:
            Normal Mode: Use player's customization from CustomizationManager
            Let's Play Mode: Use AI personality theme (color/accessory matching character)

        **Subsystem Initialization Order:**
        1. Snake: Create with current customization (AI or player)
        2. Food: Spawn in empty cell (excludes snake positions)
        3. Managers: Reset Score, Metrics, NearMiss, PowerUps
        4. Timers: Reset logic_timer (movement) and starvation_timer (urgency)
        5. Flags: Clear shielded, magnet, logic_tick_override states
        6. Audio: Restart radio if enabled (current station continues)

        **Starvation Reset:**
        Timer reset to 0.0s on game start:
            Purpose: Fresh 30-second countdown each session
            Contract: Every run receives the full initial countdown

        **Side Effects:**
            - Creates new Snake instance (replaces old entity)
            - Spawns new Food at random position
            - Resets all timers to zero
            - Restarts radio playback if sound enabled
            - Does NOT modify persistent data (profile, scores, achievements)

        **Complexity:** O(n) where n = grid size for food spawn, effectively O(1)
        """
        # Pass current customization to snake
        # Use AI personality customization if in Let's Play mode
        if hasattr(self, "ai_personality_key") and self.ai_personality_key:
            from vibesnake.core.customization import get_ai_personality_customization

            customization = get_ai_personality_customization(self.ai_personality_key)
        else:
            customization = self.customization_manager.get_customization()
        self.snake = Snake(customization=customization)

        # Attempt food spawn with exception handling for grid-full condition
        try:
            self.food = Food(self.snake.positions_set)
        except GridFullException as e:
            # Grid 100% full - victory condition (player filled entire grid!)
            logger.error(f"[Food] Grid full at game start: {e}")
            logger.info("[Food] Entering inactive food state because no cell is free")
            # Preserve the rendering interface with an explicitly absent position.
            self.food = Food.__new__(Food)
            self.food.position = None  # Explicit None to prevent rendering

        self.score_manager = ScoreManager()
        self.metrics = MetricsTracker()
        self.near_miss = NearMissDetector()
        self.logic_timer = 0
        clear_cadence_factors(self)
        self.snake_is_shielded = False
        self.magnet_active = False
        self.snake_phase_shift_active = False
        self.snake_gluttony_active = False
        self.bait_position = None
        self.last_stand_held = False
        self.revival_invulnerability_timer = 0.0
        self.detached_segments = []
        self.detached_segments_timer = 0.0
        self.powerups = PowerUpManager()
        self.visual_effects.clear()
        self.current_danger_warning = None  # Active danger warning (visual only, no score)
        self.session_elapsed_time = 0.0
        self.session_food_eaten = 0
        self.session_powerups_collected = 0
        self.session_wraps = 0
        self.session_near_misses = 0

        # Starvation adds route urgency. These initial timings remain subject to
        # fixed-seed simulation and structured player observation.
        self.starvation_timer = 0.0
        self.starvation_max_time = 30.0
        self.starvation_warning_time = 20.0  # Warning at 20 seconds

        # Start radio if sound is enabled and radio stations are available
        if self.sound_on and self.radio:
            if self.radio.available_stations:
                self.radio.play_current_station()
            else:
                # Fallback to default music if no radio stations
                play_music()

    def toggle_pause(self):
        """
        Toggle pause state during active gameplay.

        **FSM Transition:**
        RUNNING ↔ PAUSED (bidirectional state swap)

        **Pause Behavior:**
        Freezes game logic (snake movement, timers, powerups):
            Game loop: Continues rendering (display remains visible)
            Update logic: Skipped (no state changes)
            Input: Only unpause and menu navigation active

        Pausing never advances simulation or consumes buffered movement.

        **Complexity:** O(1) - state enum assignment
        """
        if self.state == GameState.RUNNING:
            self.state = GameState.PAUSED
        elif self.state == GameState.PAUSED:
            self.state = GameState.RUNNING

    def toggle_sound(self):
        """
        Toggle audio playback (music and sound effects).

        **Implementation:**
        Two code paths based on radio availability:
            Radio Available: Toggle radio playback (GTA-style station system)
            No Radio: Toggle default pygame music (fallback)

        **Side Effects:**
            - Updates self.sound_on flag (tracked for persistence)
            - Starts/stops radio or default music
            - Affects all subsequent sound effect playback

        **Complexity:** O(1) - audio system state toggle
        """
        # Toggle radio playback (M key handler)
        if self.radio:
            self.radio.toggle_playback()
            self.sound_on = self.radio.is_playing
        else:
            # Fallback for no radio - toggle default music
            self.sound_on = not self.sound_on
            if self.sound_on:
                play_music()
            else:
                pygame.mixer.music.stop()
        self.user_settings.sound_enabled = self.sound_on
        self.user_settings.save()

    def start_lets_play_mode(self, personality_key=None):
        """
        Initialize Let's Play spectator mode with AI personality streamer.

        **Let's Play Mode - Wargames Reference:**
        Players watch AI personalities play Snake with distinct playstyles:
            Spectator Experience: Idle game (watch AI succeed/fail)
            Personality Variety: Multiple AI characters with unique behaviors
            Auto-Restart: AI continuously plays (endless loop)

        AI commentary and personality are presentation layers. They must not
        alter the rules, score category, or human progression state.

        **Personality Selection:**
        Two modes:
            Specific: personality_key provided (channel browser selection)
            Random: personality_key=None (surprise streamer)

        **Customization Routing:**
        AI personality determines snake appearance:
            Each AI: Themed color + accessory (character identity)
            Example: "Risky Rita" = red snake + crown accessory

        **FSM Transition:**
        MENU/CHANNEL_BROWSER → LETS_PLAY (enter spectator mode)

        Args:
            personality_key: str - AI personality identifier, or None for random

        **Side Effects:**
            - Creates AIPlayer instance with personality config
            - Calls reset() (reinitializes game with AI customization)
            - Sets state to LETS_PLAY (activates spectator input handlers)
            - Logs AI personality name and description

        **Complexity:** O(1) - AI initialization + game reset

        See: Horton & Wohl (1956) "Mass Communication and Para-Social Interaction"
        """
        # Pick personality (specific or random)
        if personality_key:
            self.ai_personality_key = personality_key
        else:
            self.ai_personality_key = random.choice(list(self.all_ai_personalities.keys()))

        self.ai_player = AIPlayer(self.ai_personality_key)

        # Reset game for AI
        self.reset()
        self.state = GameState.LETS_PLAY

        personality = self.all_ai_personalities[self.ai_personality_key]
        print(f"[Stream] NOW STREAMING: {personality.name}")
        print(f"[Stream] Play style: {personality.description}")

    def get_customization_options(self):
        """
        Retrieve customization options for currently selected category.

        **Category Mapping:**
        Returns different option lists based on self.customization_category:
            0 (Color): List of (name, color_value) tuples from COLOR_PRESETS
            1 (Pattern): List of pattern identifiers (stripes, dots, scales, etc.)
            2 (Eyes): List of eye style identifiers (cute, angry, sleepy, etc.)
            3 (Accessory): List of accessory identifiers (hat, crown, sunglasses, etc.)
            4 (Trail): List of trail effect identifiers (sparkle, smoke, rainbow, etc.)

        **Return Format:**
        Different structure per category:
            Color: [(name, rgb_value), ...] (tuples for display + application)
            Others: [identifier, ...] (strings for lookup)

        **Complexity:** O(1) - dict lookup or list reference
        """
        if self.customization_category == 0:  # Color
            return list(COLOR_PRESETS.items())
        elif self.customization_category == 1:  # Pattern
            return PATTERN_OPTIONS
        elif self.customization_category == 2:  # Eyes
            return EYE_STYLES
        elif self.customization_category == 3:  # Accessory
            return ACCESSORIES
        elif self.customization_category == 4:  # Trail
            return TRAILS
        return []

    def _apply_customization_selection(self):
        """
        Apply selected customization option if unlocked (with progression gating).

        **Unlock Gate System:**
        Customization items may be locked behind requirements:
            Requirement Types: apples_eaten, wall_rides, games_played, highest_combo, highest_score
            Check: PlayerProfile.check_unlocked(item_name, requirement)
            Result: Apply if unlocked, block if locked (log message)

        **Category-Specific Application:**
        Different attribute updated per category:
            0 (Color): customization.base_color = rgb_value
            1 (Pattern): customization.pattern = identifier
            2 (Eyes): customization.eye_style = identifier
            3 (Accessory): customization.accessory = identifier
            4 (Trail): customization.trail = identifier

        **Immediate Preview:**
        Updates self.customization_manager.current_customization immediately:
            Effect: Preview snake appearance changes in real-time
            Persistence: NOT saved to disk yet (deferred until ENTER key)

        Preview changes remain in memory until ENTER explicitly persists them.

        **Side Effects:**
            - Modifies customization_manager.current_customization in-memory
            - Does NOT save to player profile file (deferred save)
            - Logs locked item rejection messages

        **Complexity:** O(1) - unlock check + attribute assignment
        """
        from vibesnake.core.customization import UNLOCK_REQUIREMENTS

        options = self.get_customization_options()
        if not options or self.customization_option >= len(options):
            return

        selected = options[self.customization_option]

        # Get item name for unlock checking
        if self.customization_category == 0:  # Color
            item_name = selected[0]  # Tuple: (name, color_value)
        else:
            item_name = selected  # String

        # Check if item is unlocked
        requirement = UNLOCK_REQUIREMENTS.get(item_name, ("free", 0, ""))
        is_unlocked = self.player_profile.check_unlocked(item_name, requirement)

        # Only apply if unlocked
        if not is_unlocked:
            print(f"[Customization] {item_name} is locked - cannot apply")
            return

        customization = self.customization_manager.get_customization()

        if self.customization_category == 0:  # Color
            color_name, color_value = selected
            customization.base_color = color_value
            print(f"[Customization] Applied color: {color_name}")

        elif self.customization_category == 1:  # Pattern
            customization.pattern = selected
            print(f"[Customization] Applied pattern: {selected}")

        elif self.customization_category == 2:  # Eyes
            customization.eye_style = selected
            print(f"[Customization] Applied eyes: {selected}")

        elif self.customization_category == 3:  # Accessory
            customization.accessory = selected
            print(f"[Customization] Applied accessory: {selected}")

        elif self.customization_category == 4:  # Trail
            customization.trail = selected
            print(f"[Customization] Applied trail: {selected}")

        # Update the customization manager (but don't save to disk yet - that happens on ENTER)
        self.customization_manager.current_customization = customization

    def _check_achievements(self, *, deaths: int = 1):
        """
        Evaluate all achievements against current game state and queue notifications.

        **Achievement Checking Pipeline:**
        1. Gather State: Build game_state dict with all relevant metrics
        2. Evaluate: AchievementManager checks all achievements against state
        3. Detect Unlocks: Get newly unlocked achievements (not previously earned)
        4. Queue Notifications: Add unlocks to display queue for HUD rendering

        **Tracked Metrics:**
        Compiles comprehensive game state snapshot:
            score: Current base score (before multipliers)
            combo: Combo multiplier level (2x, 3x, etc.)
            length: Snake body segment count
            time: Playtime in seconds since game start
            games_played: Lifetime game count from PlayerProfile
            near_misses: Count of rewarded spatial near-miss events this session
            deaths: Always 1 (called on game over)

        **Notification Queue:**
        Newly unlocked achievements added to self.achievement_notifications:
            Purpose: Display achievements sequentially (not all at once)
            Consumption: HUD pops from queue and shows timed notification
            Duration: Each notification shows for ~3 seconds

        **Call Context:**
        Invoked on game over (death state transition):
            Timing: After score finalized but before state transition
            Purpose: Capture final game metrics for achievement evaluation

        **Side Effects:**
            - Calls achievement_manager.check_all_achievements()
            - Extends self.achievement_notifications list
            - Logs newly unlocked achievement count

        **Complexity:** O(n) where n = number of achievements (typically ~30)
        """
        playtime = self.session_elapsed_time

        # Build game state dictionary for achievement checking
        game_state = {
            "score": self.score_manager.base_score,
            "combo": int(self.score_manager.combo_multiplier),
            "length": len(self.snake.body),
            "time": playtime,
            "games_played": self.player_profile.total_games,
            "near_misses": self.session_near_misses,
            "food_eaten": self.session_food_eaten,
            "wraps": self.session_wraps,
            "powerups_collected": self.session_powerups_collected,
            "deaths": deaths,
        }

        # Check all achievements
        self.achievement_manager.check_all_achievements(**game_state)

        # Get newly unlocked achievements and queue them for notifications
        new_achievements = self.achievement_manager.get_pending_notifications()
        if new_achievements:
            self.achievement_notifications.extend(new_achievements)
            self.player_profile.update_achievement_state(self.achievement_manager.save_state())
            print(f"[Achievements] Unlocked {len(new_achievements)} new achievements!")

    def _finalize_player_run(self, *, won: bool = False):
        """Persist human progression after a terminal run."""
        if self.state == GameState.LETS_PLAY:
            return

        self.player_profile.increment_games()
        self.player_profile.update_score(
            self.score_manager.base_score,
            int(self.score_manager.combo_multiplier),
        )
        self._check_achievements(deaths=0 if won else 1)

    def _consume_shield(self) -> bool:
        """Absorb one fatal collision and remove the active Shield effect."""
        if not self.snake_is_shielded:
            return False

        consumed = self.powerups.consume(ShieldPowerUp, self)
        if not consumed:
            self.snake_is_shielded = False
            self.visual_effects.remove_stacked_powerup("Shield")

        head_x, head_y = self.snake.get_head()
        self.visual_effects.add_score_popup(
            head_x * settings.CELL_SIZE,
            head_y * settings.CELL_SIZE + settings.HUD_HEIGHT,
            "SHIELD BLOCK",
            (0, 255, 255),
        )
        self.visual_effects.trigger_shake(4)
        return True

    def _try_revive_with_last_stand(self) -> bool:
        """Consume Last Stand, halve the snake, and grant recovery time."""
        if not self.last_stand_held:
            return False

        if not self.powerups.consume(LastStandPowerUp, self):
            self.last_stand_held = False
            self.visual_effects.remove_stacked_powerup("L.Stand")
            return False

        target_length = max(1, (len(self.snake.body) + 1) // 2)
        while len(self.snake.body) > target_length:
            self.snake.body.popleft()
        self.snake.positions_set = set(self.snake.body)

        self.starvation_timer = 0.0
        self.snake.set_starvation_warning(0.0)
        self.revival_invulnerability_timer = 3.0
        self.visual_effects.add_stacked_powerup(
            name="Revival",
            color=(255, 69, 0),
            duration=3.0,
            icon_char="R",
        )
        head_x, head_y = self.snake.get_head()
        self.visual_effects.add_score_popup(
            head_x * settings.CELL_SIZE,
            head_y * settings.CELL_SIZE + settings.HUD_HEIGHT,
            "LAST STAND",
            (255, 120, 40),
        )
        self.visual_effects.trigger_shake(8)
        logger.info("Last Stand prevented a fatal event; recovery window started")
        return True

    def _report_death_statistics(self) -> None:
        """Print descriptive session telemetry without inferring experience."""
        death_stats = self.metrics.get_death_statistics()
        print("[Telemetry] Death Statistics - Session Summary:")
        print(f"  Total Deaths: {death_stats['total_deaths']}")
        print(f"  Collision: {death_stats['collision_deaths']} ({death_stats['collision_percent']}%)")
        print(f"  Starvation: {death_stats['starvation_deaths']} ({death_stats['starvation_percent']}%)")
        print("  Interpretation: Requires cohort context and structured playtest review")

    def _handle_starvation_deadline(self) -> None:
        """Resolve a reached starvation deadline after the current rules step."""
        print(f"[Game] Starved! Timer: {self.starvation_timer:.1f}s / {self.starvation_max_time:.0f}s")

        if self._try_revive_with_last_stand():
            print("[LastStand] Starvation prevented")
            return

        self.metrics.record_death(
            self.score_manager.base_score,
            DeathCause.STARVATION,
        )

        self._report_death_statistics()

        self.play_lost_sound()
        self._finalize_player_run()
        if self.high_score_table.is_high_score(self.score_manager.base_score):
            if self.state == GameState.LETS_PLAY and self.ai_personality_key:
                ai_name = self.all_ai_personalities[self.ai_personality_key].name
                rank = self.high_score_table.add_score(
                    ai_name,
                    self.score_manager.base_score,
                )
                print(f"[HighScore] AI '{ai_name}' scored #{rank} with {self.score_manager.base_score} points")
                self.state = GameState.GAME_OVER
                self.hud.refresh_high_score()
                self.game_over_message = self.menu.choose_game_over_message(
                    self.score_manager.base_score,
                    self.hud.high_score,
                    True,
                )
                self.ai_game_over_timer = 0.0
            else:
                self.state = GameState.NAME_ENTRY
                self.player_name = self.player_profile.get_name()
                self.cursor_blink_timer = 0.0
        else:
            self.state = GameState.GAME_OVER
            self.hud.refresh_high_score()
            self.game_over_message = self.menu.choose_game_over_message(
                self.score_manager.base_score,
                self.hud.high_score,
                False,
            )

        self.visual_effects.trigger_hitstop(0.15)

    def _resolve_starvation_if_due(self) -> bool:
        """Resolve starvation once its inclusive deadline has been reached."""
        if self.starvation_timer < self.starvation_max_time:
            return False

        self._handle_starvation_deadline()
        return True

    def _update_temporary_powerup_state(self, dt: float) -> None:
        """Advance revival immunity and detached-obstacle timers."""
        if self.revival_invulnerability_timer > 0.0:
            self.revival_invulnerability_timer = max(
                0.0,
                self.revival_invulnerability_timer - dt,
            )

        if self.detached_segments_timer > 0.0:
            self.detached_segments_timer = max(0.0, self.detached_segments_timer - dt)
            if self.detached_segments_timer == 0.0:
                self.detached_segments.clear()

    def play_eat_sound(self):
        """Play the configured food-collection cue when sound is enabled.

        Playback uses the current effects volume. Audio-device or cue failures are
        reported without interrupting gameplay.
        """
        if self.sound_on and EAT_SOUND:
            try:
                EAT_SOUND.set_volume(self.volume)
                EAT_SOUND.play()
            except Exception as e:
                print(f"[Game] Sound playback failed: {e}")

    def play_lost_sound(self):
        """Fade music for 500 milliseconds and play the run-ending cue.

        The operation is skipped when sound is disabled or the cue is unavailable.
        Audio-device failures are reported without changing run state.
        """
        if self.sound_on and LOST_SOUND:
            try:
                pygame.mixer.music.fadeout(500)
                LOST_SOUND.set_volume(self.volume)
                LOST_SOUND.play()
            except Exception as e:
                print(f"[Game] Failed to play lost sound: {e}")

    def _finish_customization(self, *, save: bool) -> None:
        """Leave customization through one state-safe completion path."""
        if save:
            self.customization_manager._save_customizations()
        if not hasattr(self, "snake"):
            self.reset()
        self.state = GameState.MENU

    def _handle_customization_input(self, event: pygame.event.Event) -> None:
        """Handle only customization input without intercepting other states."""
        if event.type == pygame.KEYDOWN:
            options = self.get_customization_options()
            if event.key == pygame.K_UP:
                self.customization_option = (self.customization_option - 1) % len(options)
                self._apply_customization_selection()
            elif event.key == pygame.K_DOWN:
                self.customization_option = (self.customization_option + 1) % len(options)
                self._apply_customization_selection()
            elif event.key == pygame.K_LEFT:
                self.customization_category = (self.customization_category - 1) % 5
                self.customization_option = 0
                self.customization_options = self.get_customization_options()
                self._apply_customization_selection()
            elif event.key == pygame.K_RIGHT:
                self.customization_category = (self.customization_category + 1) % 5
                self.customization_option = 0
                self.customization_options = self.get_customization_options()
                self._apply_customization_selection()
            elif event.key == pygame.K_RETURN:
                self._finish_customization(save=True)
            elif event.key == pygame.K_ESCAPE:
                self._finish_customization(save=False)
            elif pygame.K_1 <= event.key <= pygame.K_3:
                slot = event.key - pygame.K_1
                self.customization_manager.save_loadout(slot)
                self.customization_notification = f"SAVED TO SLOT {slot + 1}"
                self.customization_notification_timer = 2.0
            elif pygame.K_4 <= event.key <= pygame.K_6:
                slot = event.key - pygame.K_4
                self.customization_manager.load_loadout(slot)
                self.customization_notification = f"LOADED SLOT {slot + 1}"
                self.customization_notification_timer = 2.0
                self._apply_customization_selection()
            return

        if event.type == pygame.JOYHATMOTION and self.input_manager.joystick:
            hat_x, hat_y = event.value
            if hat_y:
                options = self.get_customization_options()
                offset = -1 if hat_y == 1 else 1
                self.customization_option = (self.customization_option + offset) % len(options)
                self._apply_customization_selection()
            elif hat_x:
                offset = -1 if hat_x == -1 else 1
                self.customization_category = (self.customization_category + offset) % 5
                self.customization_option = 0
                self.customization_options = self.get_customization_options()
                self._apply_customization_selection()
            return

        if self.input_manager.check_action_button(event, "select"):
            self._finish_customization(save=True)
        elif self.input_manager.check_action_button(event, "back"):
            self._finish_customization(save=False)

    def handle_input(self):
        """
        Process input events and route to state-specific handlers.

        **Input Architecture - State-Based Routing:**
        Central input dispatcher implementing event-driven input model:
            Global Handlers: F11 (fullscreen), M (sound), H (help), L (Let's Play)
            State Handlers: Different key bindings per FSM state
            Radio Hotkeys: 1-9 (stations), R (next), [ ] (prev/next)

        **Event Polling:**
        Uses pygame event queue (pygame.event.get()):
            Pattern: Poll all events, dispatch by type and state
            QUIT: Immediate exit (no cleanup needed)
            KEYDOWN: Route to appropriate state handler

        **InputManager Integration:**
        Abstracts input sources (keyboard vs controller):
            check_action_button(): Maps logical actions to physical inputs
            Actions: 'select', 'back', 'toggle_sound', 'help', 'pause'
            Benefit: Controller support without duplicating logic

        **Radio Hotkeys - Context-Aware:**
        GTA-style station switching with state awareness:
            1-9: Direct station selection (all states)
            R: Next station (all states)
            [ ]: Prev/next station (all states)
            Arrow keys: Prev/next (menu/pause only - avoid snake movement conflict)

        **State Routing:**
        Dispatches to specialized handlers after global processing:
            MENU: Menu navigation (up/down/select)
            RUNNING: Snake direction + pause
            PAUSED: Unpause + menu navigation
            GAME_OVER: Restart + menu return
            CUSTOMIZE: Category + option selection
            ACHIEVEMENTS: Scroll navigation
            SETTINGS: Option selection + volume adjustment
            LETS_PLAY: Spectator controls (exit)
            CHANNEL_BROWSER: AI personality selection

        **Complexity:** O(n) where n = events in queue (typically 1-5 per frame)
        """
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                pygame.quit()
                exit()

            if event.type == pygame.VIDEORESIZE and not self.fullscreen:
                self.display.handle_resize(event.size)
                continue

            # F11 to toggle fullscreen (works in any state)
            if event.type == pygame.KEYDOWN and event.key == pygame.K_F11:
                self.toggle_fullscreen()
                continue

            # Handle state-independent actions using InputManager
            if self.input_manager.check_action_button(event, "toggle_sound"):
                self.toggle_sound()

            # Handle help toggle (H key)
            if self.input_manager.check_action_button(event, "help"):
                if self.state == GameState.MENU:
                    self.state = GameState.HELP
                elif self.state == GameState.HELP:
                    self.state = GameState.MENU

            # Handle Let's Play mode (L key opens channel browser)
            if event.type == pygame.KEYDOWN and event.key == pygame.K_l:
                if self.state == GameState.MENU:
                    # Open channel browser to pick AI streamer
                    self.state = GameState.CHANNEL_BROWSER
                    self.channel_browser_index = 0  # Reset selection
                elif self.state == GameState.LETS_PLAY:
                    self.state = GameState.MENU
                    self.ai_player = None
                    self.ai_personality_key = None  # Clear AI personality so player customization is used

            # Radio controls are available only where they cannot collide with a screen action.
            if event.type == pygame.KEYDOWN and self.radio and self.state in _RADIO_CONTROL_STATES:
                radio_key_handled = False
                # Number keys 1-9 select a station directly.
                if pygame.K_1 <= event.key <= pygame.K_9:
                    station_number = event.key - pygame.K_0  # Convert to 1-9
                    self.radio.handle_number_key(station_number)
                    radio_key_handled = True
                # R and brackets cycle stations.
                elif event.key == pygame.K_r:
                    self.radio.next_station()
                    radio_key_handled = True
                elif event.key == pygame.K_RIGHTBRACKET:
                    self.radio.next_station()
                    radio_key_handled = True
                elif event.key == pygame.K_LEFTBRACKET:
                    self.radio.previous_station()
                    radio_key_handled = True
                # Arrow cycling is limited to non-moving gameplay states.
                elif event.key == pygame.K_RIGHT and self.state in (
                    GameState.PAUSED,
                    GameState.GAME_OVER,
                ):
                    self.radio.next_station()
                    radio_key_handled = True
                elif event.key == pygame.K_LEFT and self.state in (
                    GameState.PAUSED,
                    GameState.GAME_OVER,
                ):
                    self.radio.previous_station()
                    radio_key_handled = True

                if radio_key_handled:
                    self.sound_on = self.radio.is_playing
                    self.user_settings.sound_enabled = self.sound_on
                    self.user_settings.save()

            if self.state == GameState.MENU:
                if self.input_manager.check_action_button(event, "select"):
                    # Ensure game objects are initialized before starting
                    if not hasattr(self, "snake"):
                        self.reset()
                    self.state = GameState.RUNNING
                    logger.debug("Menu started a player run")
                    # IMPORTANT: Return immediately to prevent other handlers from processing this event
                    return
                elif self.input_manager.check_action_button(event, "back"):
                    pygame.quit()
                    exit()
                elif event.type == pygame.KEYDOWN and event.key == pygame.K_c:
                    # Enter customization menu
                    self.state = GameState.CUSTOMIZE
                    self.customization_category = 0
                    self.customization_option = 0
                    self.customization_options = self.get_customization_options()
                elif event.type == pygame.KEYDOWN and event.key == pygame.K_a:
                    # Enter achievements menu
                    self.state = GameState.ACHIEVEMENTS
                    self.achievements_scroll_offset = 0
                elif event.type == pygame.KEYDOWN and event.key == pygame.K_s:
                    # Enter settings menu
                    self.state = GameState.SETTINGS
                    self.settings_selected_option = 0
                elif event.type == pygame.KEYDOWN and event.key == pygame.K_v:
                    # View high scores
                    self.state = GameState.HIGH_SCORES

            elif self.state == GameState.SETTINGS:
                # Handle settings menu input
                if event.type == pygame.KEYDOWN:
                    if event.key == pygame.K_UP:
                        self.settings_selected_option = (self.settings_selected_option - 1) % 3
                    elif event.key == pygame.K_DOWN:
                        self.settings_selected_option = (self.settings_selected_option + 1) % 3
                    elif event.key == pygame.K_LEFT or event.key == pygame.K_RIGHT:
                        # Adjust current setting
                        if self.settings_selected_option == 0:  # Sound toggle
                            self.sound_on = not self.sound_on
                            if self.sound_on and self.radio:
                                self.radio.play_current_station()
                            elif not self.sound_on and self.radio:
                                self.radio.stop()
                            self.user_settings.sound_enabled = self.sound_on
                            self.user_settings.save()
                        elif self.settings_selected_option == 1:  # Volume
                            if event.key == pygame.K_LEFT:
                                self.volume = max(0.0, self.volume - 0.1)
                            else:
                                self.volume = min(1.0, self.volume + 0.1)
                            if self.radio:
                                self.radio.set_volume(self.volume)
                            self.user_settings.volume = self.volume
                            self.user_settings.save()
                    elif event.key == pygame.K_RETURN:
                        # Select current option
                        if self.settings_selected_option == 2:  # Back to Menu
                            self.state = GameState.MENU
                    elif event.key == pygame.K_ESCAPE:
                        self.state = GameState.MENU

            elif self.state == GameState.ACHIEVEMENTS:
                # Handle achievements menu input
                if event.type == pygame.KEYDOWN:
                    if event.key == pygame.K_UP:
                        self.achievements_scroll_offset = max(0, self.achievements_scroll_offset - 80)
                    elif event.key == pygame.K_DOWN:
                        max_scroll = max(0, len(self.achievement_manager.achievements) * 80 - 400)
                        self.achievements_scroll_offset = min(max_scroll, self.achievements_scroll_offset + 80)
                    elif event.key == pygame.K_ESCAPE:
                        self.state = GameState.MENU

            elif self.state == GameState.CUSTOMIZE:
                self._handle_customization_input(event)

            elif self.state == GameState.CHANNEL_BROWSER:
                if event.type == pygame.KEYDOWN:
                    self.channel_browser_idle_timer = 0.0
                if event.type == pygame.KEYDOWN and event.key == pygame.K_UP:
                    self.channel_browser_index = (self.channel_browser_index - 1) % len(self.channel_list)
                elif event.type == pygame.KEYDOWN and event.key == pygame.K_DOWN:
                    self.channel_browser_index = (self.channel_browser_index + 1) % len(self.channel_list)
                elif event.type == pygame.JOYHATMOTION and self.input_manager.joystick:
                    _, hat_y = event.value
                    if hat_y:
                        offset = -1 if hat_y == 1 else 1
                        self.channel_browser_index = (self.channel_browser_index + offset) % len(self.channel_list)
                        self.channel_browser_idle_timer = 0.0
                elif self.input_manager.check_action_button(event, "select"):
                    selected_key = self.channel_list[self.channel_browser_index][0]
                    self.start_lets_play_mode(selected_key)
                elif event.type == pygame.KEYDOWN and event.key == pygame.K_r:
                    self.start_lets_play_mode()
                elif (
                    event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE
                ) or self.input_manager.check_action_button(event, "back"):
                    self.state = GameState.MENU

            elif self.state == GameState.LETS_PLAY:
                # Let's Play mode controls
                if self.input_manager.check_action_button(event, "back"):
                    self.state = GameState.MENU
                    self.ai_player = None
                    self.ai_personality_key = None  # Clear AI personality so player customization is used
                elif self.input_manager.check_action_button(event, "select"):
                    # Restart with new AI
                    self.start_lets_play_mode()

            elif self.state == GameState.HELP:
                # Can exit help with back button or select to start game
                if self.input_manager.check_action_button(event, "back") or (
                    event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE
                ):
                    self.state = GameState.MENU
                elif self.input_manager.check_action_button(event, "select"):
                    self.state = GameState.RUNNING

            elif self.state == GameState.RUNNING:
                # Get direction from any input source (keyboard, mouse, gamepad)
                direction = self.input_manager.get_direction(
                    event, snake_head_pos=self.snake.get_head(), cell_size=settings.CELL_SIZE
                )
                if direction:
                    self.snake.queue_direction(direction)

                # Action buttons
                if self.input_manager.check_action_button(event, "pause"):
                    self.toggle_pause()
                elif event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
                    # Return to menu
                    self.state = GameState.MENU

            elif self.state == GameState.PAUSED:
                if self.input_manager.check_action_button(event, "pause"):
                    self.toggle_pause()
                elif event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
                    # Return to menu
                    self.state = GameState.MENU
                elif self.input_manager.check_action_button(event, "back"):
                    pygame.quit()
                    exit()

            elif self.state == GameState.NAME_ENTRY:
                # NAME_ENTRY is now ONLY used for high score entry (never for first-time setup)
                # Confirm with ENTER or controller A button
                if (
                    event.type == pygame.KEYDOWN and event.key == pygame.K_RETURN
                ) or self.input_manager.check_action_button(event, "select"):
                    # Confirm name entry
                    final_name = self.player_name if self.player_name else "Anonymous"
                    # Add to high score table
                    rank = self.high_score_table.add_score(final_name, self.score_manager.base_score)
                    print(f"[NameEntry] High score added! Rank #{rank}: {final_name} ({self.score_manager.base_score})")
                    self.hud.refresh_high_score()
                    # Go to high scores screen to show the new entry
                    self.state = GameState.HIGH_SCORES

                # Skip with ESC or controller B button
                elif (
                    event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE
                ) or self.input_manager.check_action_button(event, "back"):
                    # Skip name entry - save as anonymous
                    rank = self.high_score_table.add_score("Anonymous", self.score_manager.base_score)
                    print(f"[NameEntry] High score added as Anonymous! Rank #{rank}")
                    self.hud.refresh_high_score()
                    # Go to high scores screen
                    self.state = GameState.HIGH_SCORES

                elif event.type == pygame.KEYDOWN and event.key == pygame.K_BACKSPACE:
                    # Delete last character
                    if self.player_name:
                        self.player_name = self.player_name[:-1]
                elif (
                    event.type == pygame.KEYDOWN
                    and event.unicode
                    and event.unicode.isprintable()
                    and len(self.player_name) < 12
                ):
                    # Add character (max 12 chars)
                    self.player_name += event.unicode.upper()

            elif self.state == GameState.HIGH_SCORES:
                # ESC or ENTER to return to menu
                if (
                    event.type == pygame.KEYDOWN and event.key in (pygame.K_ESCAPE, pygame.K_RETURN)
                ) or self.input_manager.check_action_button(event, "back"):
                    self.state = GameState.MENU

            elif self.state == GameState.GAME_OVER:
                # C key or ENTER to retry
                if (event.type == pygame.KEYDOWN and event.key == pygame.K_c) or self.input_manager.check_action_button(
                    event, "select"
                ):
                    self.reset()
                    # If we were watching AI, go back to Let's Play mode; otherwise player game
                    if self.ai_personality_key:
                        self.state = GameState.LETS_PLAY
                        print(f"[Game] Restarting AI stream: {self.all_ai_personalities[self.ai_personality_key].name}")
                    else:
                        self.state = GameState.RUNNING
                # Q key to quit (or ESC to return to menu if was watching AI)
                elif self.input_manager.check_action_button(event, "back") or (
                    event.type == pygame.KEYDOWN and event.key == pygame.K_q
                ):
                    if self.ai_personality_key:
                        # Was watching AI - return to menu instead of quitting
                        self.state = GameState.MENU
                        self.ai_player = None
                        self.ai_personality_key = None
                    else:
                        # Player game - quit to desktop
                        pygame.quit()
                        exit()

    def update(self, dt):
        # Update background animation and score-based environment (always)
        self.background.update(dt)
        if hasattr(self, "score_manager"):
            self.background.set_score(self.score_manager.base_score)

        # Update radio (GTA-style: auto-play next track when current ends)
        if self.radio:
            self.radio.update()

        # Handle channel browser idle timeout
        if self.state == GameState.CHANNEL_BROWSER:
            self.channel_browser_idle_timer += dt
            if self.channel_browser_idle_timer >= 5.0:
                # Auto-select random channel after 5s
                self.start_lets_play_mode()  # Random selection
            return

        # Handle name entry cursor blink
        if self.state == GameState.NAME_ENTRY:
            self.cursor_blink_timer += dt
            if self.cursor_blink_timer >= 0.5:  # Blink every 0.5 seconds
                self.cursor_visible = not self.cursor_visible
                self.cursor_blink_timer = 0.0
            return

        # Handle customization notification timer
        if self.state == GameState.CUSTOMIZE:
            if self.customization_notification_timer > 0:
                self.customization_notification_timer -= dt
                if self.customization_notification_timer <= 0:
                    self.customization_notification = None
            return

        # Handle AI auto-restart in Game Over state
        if self.state == GameState.GAME_OVER and self.ai_personality_key:
            self.ai_game_over_timer += dt
            if self.ai_game_over_timer >= self.ai_auto_restart_delay:
                # Auto-restart AI stream
                print(f"[AI] Auto-restarting stream: {self.all_ai_personalities[self.ai_personality_key].name}")
                self.reset()
                self.state = GameState.LETS_PLAY
                self.ai_game_over_timer = 0.0
            # Still allow user to control radio even during game over
            return

        # Only update game logic when actually running or in Let's Play mode
        if self.state not in (GameState.RUNNING, GameState.LETS_PLAY):
            return

        # Update visual effects (always, even during hitstop for smooth visuals)
        self.visual_effects.update(dt)

        # Update achievement notification display
        if self.current_achievement_display:
            self.achievement_display_timer -= dt
            if self.achievement_display_timer <= 0:
                self.current_achievement_display = None
        elif self.achievement_notifications:
            # Show next achievement notification
            self.current_achievement_display = self.achievement_notifications.pop(0)
            self.achievement_display_timer = 4.0  # Display for 4 seconds

        # Apply hitstop time scaling to game logic
        # During hitstop, rule advancement pauses while presentation continues.
        time_scale = self.visual_effects.get_hitstop_time_scale()
        gameplay_dt = dt * time_scale
        self.session_elapsed_time += gameplay_dt

        # If fully frozen, skip gameplay logic
        if time_scale == 0.0:
            # Still update snake animation for visual smoothness
            self.snake.update_animation(dt)
            return

        # Update snake animation (vibing visuals)
        self.snake.update_animation(gameplay_dt)
        self._update_temporary_powerup_state(gameplay_dt)

        # AI decision making in Let's Play mode
        if self.state == GameState.LETS_PLAY and self.ai_player:
            powerup_positions = [powerup.position for powerup in self.powerups.collectible_powerups()]

            # Let AI decide direction
            ai_direction = self.ai_player.get_direction(
                gameplay_dt,
                snake_head=self.snake.get_head(),
                current_direction=self.snake.direction,
                food_position=self.food.position,
                powerup_positions=powerup_positions,
                snake_body=list(self.snake.body),
                grid_width=settings.GRID_WIDTH,
                grid_height=settings.GRID_HEIGHT,
            )

            if ai_direction:
                self.snake.queue_direction(ai_direction)

        # Advance the bounded starvation deadline.
        # 30s for all players - fair and balanced
        self.starvation_timer += gameplay_dt

        # Update snake visual starvation warning
        if self.starvation_timer >= self.starvation_warning_time:
            time_remaining = self.starvation_max_time - self.starvation_timer
            warning_intensity = 1.0 - (time_remaining / (self.starvation_max_time - self.starvation_warning_time))
            warning_intensity = max(0.0, min(1.0, warning_intensity))
            self.snake.set_starvation_warning(warning_intensity)
        else:
            self.snake.set_starvation_warning(0.0)

        # Update score manager (combo timer)
        self.score_manager.update(gameplay_dt)

        # Update near-miss detector (cooldowns)
        self.near_miss.update(gameplay_dt)

        if self.magnet_active and self.food.position is not None:
            next_head = self.snake.peek_next_head()
            if self.food.position != next_head:
                fx, fy = self.food.position
                sx, sy = self.snake.get_head()
                dx = 0 if fx == sx else (1 if sx > fx else -1)
                dy = 0 if fy == sy else (1 if sy > fy else -1)
                candidate = (fx + dx, fy + dy)
                blocked_food_cells = (
                    self.snake.positions_set | set(self.detached_segments) | self.powerups.collectible_positions()
                )
                if candidate not in blocked_food_cells:
                    self.food.position = candidate

        self.powerups.update(gameplay_dt, self)

        self.logic_timer += gameplay_dt
        tick_speed = self.logic_tick_override or settings.LOGIC_TICK
        if self.logic_timer < tick_speed:
            return

        self.logic_timer = 0

        ate_food = self.food.position is not None and self.snake.peek_next_head() == self.food.position
        grow = ate_food and not self.snake_gluttony_active
        alive, wrapped = self.snake.move(
            grow=grow,
            ignore_self_collision=self.snake_phase_shift_active,
        )

        if alive and not self.snake_phase_shift_active and self.snake.get_head() in self.detached_segments:
            alive = False

        if alive:
            self.powerups.collect_at(self.snake.get_head(), self)

        # Track wall rides for unlock progression
        if wrapped:
            self.session_wraps += 1
            if self.state != GameState.LETS_PLAY:
                self.player_profile.increment_wall_rides()

        # Check for near-miss moments after moving (if alive)
        if alive and not ate_food:
            near_miss_event = self.near_miss.check_near_miss(
                self.snake.get_head(), self.snake.positions_set, len(self.snake.body)
            )
            if near_miss_event:
                # Handle warnings (visual only) vs events (score + message)
                if near_miss_event.is_warning:
                    # Pre-warning: Visual feedback only (red glow on snake head)
                    # No score bonus, no event tracking, no message popup
                    # Just store for visual rendering (will be consumed by draw code)
                    self.current_danger_warning = near_miss_event
                else:
                    # Full near-miss event: Award points with combo multiplier
                    # Combo system rewards chaining multiple near-miss events
                    combo_multiplier = self.near_miss.get_combo_multiplier()
                    base_bonus = near_miss_event.score_bonus
                    multiplied_bonus = int(base_bonus * combo_multiplier)

                    self.score_manager.add_bonus_score(multiplied_bonus)
                    self.near_miss.add_event(near_miss_event)
                    self.session_near_misses += 1

                    # Show visual feedback with combo indicator if active
                    popup_x = settings.WIDTH - 150  # Right side with margin
                    popup_y = settings.HUD_HEIGHT + 50  # Just below HUD

                    # Display message with combo multiplier if active
                    if combo_multiplier > 1.0:
                        display_message = (
                            f"+{multiplied_bonus} {near_miss_event.message} (x{combo_multiplier:.1f} COMBO!)"
                        )
                    else:
                        display_message = f"+{multiplied_bonus} {near_miss_event.message}"

                    self.visual_effects.add_score_popup(popup_x, popup_y, display_message, near_miss_event.color)
                    print(
                        f"[NearMiss] {near_miss_event.message} +{base_bonus} (x{combo_multiplier:.1f} combo) = +{multiplied_bonus}"
                    )
                    # Clear any active warning since full event triggered
                    self.current_danger_warning = None
            else:
                # No danger detected - clear any active warning
                self.current_danger_warning = None

        if not alive:
            if self.revival_invulnerability_timer > 0.0:
                print("[LastStand] Collision ignored during recovery window")
                self._resolve_starvation_if_due()
                return
            if self._consume_shield():
                print("[Shield] Collision absorbed; shield consumed")
                self._resolve_starvation_if_due()
                return
            if self._try_revive_with_last_stand():
                print("[LastStand] Collision prevented")
                return
            self.play_lost_sound()

            # Record death for metrics (collision cause)
            self.metrics.record_death(self.score_manager.base_score, DeathCause.COLLISION)

            print("[Game] Game Over! Final score:", self.score_manager.base_score)

            self._report_death_statistics()

            self._finalize_player_run()

            # Check if qualifies for high score table
            if self.high_score_table.is_high_score(self.score_manager.base_score):
                # If watching AI in Let's Play mode, auto-submit AI's name
                if self.state == GameState.LETS_PLAY and self.ai_personality_key:
                    ai_name = self.all_ai_personalities[self.ai_personality_key].name
                    rank = self.high_score_table.add_score(ai_name, self.score_manager.base_score)
                    print(f"[HighScore] AI '{ai_name}' scored #{rank} with {self.score_manager.base_score} points")
                    self.state = GameState.GAME_OVER
                    self.hud.refresh_high_score()
                    is_new_high_score = True
                    self.game_over_message = self.menu.choose_game_over_message(
                        self.score_manager.base_score, self.hud.high_score, is_new_high_score
                    )
                    # Start auto-restart timer for AI
                    self.ai_game_over_timer = 0.0
                else:
                    # Player game - ask for name
                    self.state = GameState.NAME_ENTRY
                    self.player_name = self.player_profile.get_name()  # Pre-fill with current name
                    self.cursor_blink_timer = 0.0
            else:
                self.state = GameState.GAME_OVER
                self.hud.refresh_high_score()
                # Choose game over message once (won't change every frame)
                is_new_high_score = False
                self.game_over_message = self.menu.choose_game_over_message(
                    self.score_manager.base_score, self.hud.high_score, is_new_high_score
                )
                # Start auto-restart timer if AI mode
                if self.ai_personality_key:
                    self.ai_game_over_timer = 0.0

            # Pause rule advancement briefly at the death transition.
            self.visual_effects.trigger_hitstop(0.15)
            self.visual_effects.trigger_shake(8)
            return

        if not ate_food and self._resolve_starvation_if_due():
            return

        if ate_food:
            # Check for clutch eat (eating with low starvation timer)
            clutch_event = self.near_miss.check_clutch_eat(self.starvation_timer, self.starvation_max_time)

            # Reset starvation timer when food is eaten
            print(f"[Game] Food eaten! Starvation timer reset from {self.starvation_timer:.1f}s to 0.0s")
            self.starvation_timer = 0.0

            # Track apples eaten for unlock progression
            if self.state != GameState.LETS_PLAY:
                self.player_profile.increment_apples_eaten()
            self.session_food_eaten += 1

            # Calculate bonuses
            speed_bonus = self.score_manager.time_since_last_food < 1.5
            snake_length = len(self.snake.body)

            # Check for style points (boost active)
            has_boost = self.powerups.has_active_effect(BoostPowerUp)
            style_event = self.near_miss.check_style_points(has_boost) if has_boost else None

            # Award points with bonuses
            points_earned = self.score_manager.add_food_score(speed_bonus=speed_bonus, snake_length=snake_length)

            # Add visual effect for food collection
            food_pixel_x = self.food.position[0] * settings.CELL_SIZE + settings.CELL_SIZE // 2
            food_pixel_y = self.food.position[1] * settings.CELL_SIZE + settings.CELL_SIZE // 2
            self.visual_effects.add_food_collection_sparkle(
                food_pixel_x, food_pixel_y, combo_multiplier=self.score_manager.combo_multiplier
            )

            # Award clutch/style bonuses if detected (with combo multiplier)
            if clutch_event:
                combo_multiplier = self.near_miss.get_combo_multiplier()
                base_bonus = clutch_event.score_bonus
                multiplied_bonus = int(base_bonus * combo_multiplier)

                self.score_manager.add_bonus_score(multiplied_bonus)
                self.near_miss.add_event(clutch_event)

                # Visual feedback for clutch moment (top-right corner)
                popup_x = settings.WIDTH - 150
                popup_y = settings.HUD_HEIGHT + 50

                # Display with combo indicator if active
                if combo_multiplier > 1.0:
                    display_message = f"+{multiplied_bonus} {clutch_event.message} (x{combo_multiplier:.1f} COMBO!)"
                else:
                    display_message = f"+{multiplied_bonus} {clutch_event.message}"

                self.visual_effects.add_score_popup(popup_x, popup_y, display_message, clutch_event.color)
                self.visual_effects.trigger_shake(5)
                print(f"[NearMiss] CLUTCH EAT! +{base_bonus} (x{combo_multiplier:.1f} combo) = +{multiplied_bonus}")

            if style_event:
                combo_multiplier = self.near_miss.get_combo_multiplier()
                base_bonus = style_event.score_bonus
                multiplied_bonus = int(base_bonus * combo_multiplier)

                self.score_manager.add_bonus_score(multiplied_bonus)
                self.near_miss.add_event(style_event)

                # Visual feedback (top-right, stacked if clutch also present)
                popup_x = settings.WIDTH - 150
                popup_y = settings.HUD_HEIGHT + (80 if clutch_event else 50)
                # Display with combo indicator if active
                if combo_multiplier > 1.0:
                    display_message = f"+{multiplied_bonus} {style_event.message} (x{combo_multiplier:.1f} COMBO!)"
                else:
                    display_message = f"+{multiplied_bonus} {style_event.message}"

                self.visual_effects.add_score_popup(popup_x, popup_y, display_message, style_event.color)
                print(f"[NearMiss] STYLE POINTS! +{base_bonus} (x{combo_multiplier:.1f} combo) = +{multiplied_bonus}")

            print(
                f"[Game] Apple eaten at {self.food.position} | Score: {self.score_manager.base_score} (+{points_earned}, {self.score_manager.combo_count}x combo)"
            )
            self.play_eat_sound()

            # Food takes board-space priority over uncollected power-ups.
            core_occupied = self.snake.positions_set | set(self.detached_segments)
            collectible_positions = self.powerups.collectible_positions()
            respawn_error = None
            try:
                self.food.respawn(
                    core_occupied | collectible_positions,
                    preferred_position=self.bait_position,
                )
            except GridFullException as first_error:
                respawn_error = first_error

            if respawn_error is not None and self.powerups.discard_collectibles():
                respawn_error = None
                try:
                    self.food.respawn(
                        core_occupied,
                        preferred_position=self.bait_position,
                    )
                except GridFullException as retry_error:
                    respawn_error = retry_error

            if respawn_error is not None and self.detached_segments:
                self.detached_segments = []
                self.detached_segments_timer = 0.0
                respawn_error = None
                try:
                    self.food.respawn(
                        self.snake.positions_set,
                        preferred_position=self.bait_position,
                    )
                except GridFullException as retry_error:
                    respawn_error = retry_error

            if respawn_error is not None:
                logger.warning("[Food] Cannot respawn food: %s", respawn_error)
                logger.info(
                    "[Victory] Player achieved GRID MASTER - %.1f%% occupancy!",
                    respawn_error.occupancy_percent,
                )
                self.food.position = None
                self._finalize_player_run(won=True)
                self.state = GameState.GAME_OVER
                self.hud.refresh_high_score()
                self.game_over_message = "GRID MASTER! You filled every cell and completed the run."
                self.visual_effects.trigger_hitstop(0.25)
                self.visual_effects.trigger_shake(10)
                print("[Victory] GRID MASTER ACHIEVED! Run complete.")
            self.bait_position = None

    def draw(self):
        if self.state == GameState.MENU:
            self.menu.draw_title_screen()
            self.display.present()
        elif self.state == GameState.HELP:
            self.menu.draw_help_overlay()
            self.display.present()
        elif self.state == GameState.CHANNEL_BROWSER:
            # Show channel browser for picking AI streamer
            self.menu.draw_channel_browser(self.channel_list, self.channel_browser_index)
            self.display.present()
        elif self.state == GameState.CUSTOMIZE:
            # Show customization menu with player profile for unlock checking
            self.menu.draw_customization_menu(
                customization=self.customization_manager.get_customization(),
                selected_category=self.customization_category,
                selected_option=self.customization_option,
                options_list=self.get_customization_options(),
                player_profile=self.player_profile,
                notification=self.customization_notification,
            )
            self.display.present()
        elif self.state == GameState.SETTINGS:
            # Show settings menu
            self.menu.draw_settings_menu(
                selected_option=self.settings_selected_option, sound_on=self.sound_on, volume=self.volume
            )
            self.display.present()
        elif self.state == GameState.HIGH_SCORES:
            # Show high scores screen
            self.menu.draw_high_scores(self.high_score_table)
            self.display.present()
        elif self.state == GameState.ACHIEVEMENTS:
            # Show achievements menu
            self.menu.draw_achievements_menu(
                achievements=self.achievement_manager.achievements, scroll_offset=self.achievements_scroll_offset
            )
            self.display.present()
        elif self.state == GameState.LETS_PLAY:
            self._draw_game_elements(skip_hud=True)  # Skip HUD - overlay shows score
            # Show AI personality overlay (replaces HUD at top)
            if self.ai_personality_key:
                personality = self.all_ai_personalities[self.ai_personality_key]
                self.menu.draw_lets_play_overlay(
                    personality.name,
                    personality.description,
                    self.score_manager.base_score,
                    self.score_manager.combo_count,
                )
            self.display.present()
        elif self.state == GameState.PAUSED:
            self._draw_game_elements()
            self.menu.draw_pause_overlay()
            self.display.present()
        elif self.state == GameState.NAME_ENTRY:
            # Only draw game elements if coming from gameplay (high score entry)
            # On first launch, just show the name entry screen
            if hasattr(self, "snake"):
                self._draw_game_elements()
            else:
                # First time - just draw a simple background
                self.screen.fill((20, 20, 30))

            self.menu.draw_name_entry_screen(
                current_name=self.player_name,
                score=0 if not hasattr(self, "score_manager") else self.score_manager.base_score,
                cursor_blink=self.cursor_visible,
            )
            # CRITICAL: Must call flip() to actually show the screen!
            self.display.present()
        elif self.state == GameState.GAME_OVER:
            self._draw_game_elements()
            # Pass score, high score, and whether this is a new high score for epic celebration
            is_new_high_score = (
                self.score_manager.base_score > self.hud.high_score and self.score_manager.base_score > 0
            )
            self.menu.draw_game_over_overlay(
                score=self.score_manager.base_score,
                high_score=self.hud.high_score,
                is_new_high_score=is_new_high_score,
                message=self.game_over_message,
                is_ai_mode=bool(self.ai_personality_key),
                ai_restart_time=self.ai_auto_restart_delay - self.ai_game_over_timer
                if self.ai_personality_key
                else 0.0,
            )
            self.display.present()
        else:
            # RUNNING state
            self._draw_game_elements()
            self.display.present()

    def _draw_game_elements(self, skip_hud=False):
        # Draw procedural animated background instead of solid color
        self.background.draw(self.screen)

        for x, y in self.detached_segments:
            obstacle_rect = pygame.Rect(
                x * settings.CELL_SIZE,
                y * settings.CELL_SIZE + settings.HUD_HEIGHT,
                settings.CELL_SIZE,
                settings.CELL_SIZE,
            )
            pygame.draw.rect(self.screen, (70, 105, 155), obstacle_rect)
            pygame.draw.rect(self.screen, (160, 195, 235), obstacle_rect, 2)

        self.snake.draw(self.screen)

        # Draw danger warning (red glow) if active
        if hasattr(self, "current_danger_warning") and self.current_danger_warning:
            # Draw pulsing red glow around snake head
            head_x, head_y = self.snake.get_head()
            screen_x = head_x * settings.CELL_SIZE
            screen_y = head_y * settings.CELL_SIZE + settings.HUD_HEIGHT

            # Pulsing effect (alpha oscillates between 80-180)
            import math

            pulse = (math.sin(pygame.time.get_ticks() / 200.0) + 1) / 2  # 0.0 to 1.0
            alpha = int(80 + pulse * 100)  # 80 to 180

            # Create semi-transparent red overlay surface
            glow_surface = pygame.Surface((settings.CELL_SIZE * 3, settings.CELL_SIZE * 3), pygame.SRCALPHA)
            glow_color = (*self.current_danger_warning.color, alpha)

            # Draw concentric circles for glow effect (largest to smallest)
            for radius_mult in [1.8, 1.5, 1.2]:
                radius = int(settings.CELL_SIZE * radius_mult)
                pygame.draw.circle(
                    glow_surface, glow_color, (settings.CELL_SIZE * 3 // 2, settings.CELL_SIZE * 3 // 2), radius
                )

            # Blit glow centered on snake head
            glow_x = screen_x - settings.CELL_SIZE
            glow_y = screen_y - settings.CELL_SIZE
            self.screen.blit(glow_surface, (glow_x, glow_y))

        self.food.draw(self.screen)
        self.powerups.draw(self.screen)

        # Draw visual effects (particles, flashes)
        self.visual_effects.draw(self.screen)

        # Starvation warning is now shown via snake hue shift

        # Skip HUD when in Let's Play mode (overlay shows score instead)
        if not skip_hud:
            self.hud.draw_score(
                self.screen,
                self.score_manager.base_score,
                combo_multiplier=self.score_manager.combo_multiplier,
                combo_count=self.score_manager.combo_count,
                radio_manager=self.radio,
                active_powerups=self.powerups.active_powerups,
            )

        # Draw achievement notification if active
        if self.current_achievement_display:
            self.menu.draw_achievement_notification(self.current_achievement_display, self.achievement_display_timer)

        # NOTE: flip() is called by the state handler, not here

    def run(self):
        while True:
            dt = self.clock.tick(settings.FPS) / 1000.0
            self.handle_input()
            self.update(dt)
            self.draw()
