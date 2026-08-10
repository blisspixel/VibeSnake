# Architecture

## Current runtime overview

The playable 0.2 alpha uses a source-layout Python package with Pygame for input, audio, timing, and rendering. `Game` is the composition root and currently owns most orchestration. This section documents what exists, not the accepted 1.0 target.

```mermaid
flowchart TD
    Entry["vibesnake.__main__"] --> Game["core.game_state.Game"]
    Game --> Input["input.InputManager"]
    Game --> AI["ai.AIPlayer"]
    Game --> Models["Snake, Food, Score, Death Telemetry, Near Miss"]
    Game --> Powerups["powerups.PowerUpManager"]
    Game --> Render["HUD, Menu, Background, Visual Effects"]
    Game --> Audio["RadioManager and SFX"]
    Game --> Saves["Profile, Cosmetics, High Scores, Preferences"]
    Config["assets/config/config.json"] --> Game
    Images["assets/images"] --> Render
    AudioPack["optional VIBESNAKE_AUDIO_DIR overlay"] --> Audio
    Fallbacks["procedural event cues"] --> Audio
    CustomAI["assets/ai/custom/*.json"] --> AI
```

## Target architecture

The gated 1.0 target is Godot 4 .NET for native presentation, input, audio, UI, and exports, with deterministic gameplay in a pure C# assembly. Windows, macOS, and Linux are first-class targets. The Python runtime remains the behavior reference until step-level trace, feature, data, and artifact parity pass.

See [TECHNOLOGY_STRATEGY.md](../decisions/TECHNOLOGY_STRATEGY.md) for the decision, boundaries, platform contract, qualification gate, and migration sequence. See [AUTOMATED_QA.md](AUTOMATED_QA.md) for the shared trace and differential-test strategy.

### Current native qualification slice

The first target-architecture seam now exists:

```text
Godot input and drawing
        |
        v
Godot input and drawing
        |
        v
game/scripts/Main.cs
       / \
      v   v
VibeSnake.Rules    VibeSnake.Persistence
      ^                    |
      |____________________|
                           v
                    OS user-data replays
```

`VibeSnake.Rules` has no Godot dependency. It owns seeded PCG32 randomness, bounded commands, wraparound movement, food, growth, combo interpolation, speed and length scoring, exact starvation and collision precedence, completion, the complete Shield spawn, collection, duration, expiry, collision-recovery, starvation-bypass, restart, restore, live replay recording, snapshots, immutable ordered event detail, strict canonical JSON state schema 3 restoration (including session achievement counters), explicit `vibesnake-core@4` identity, `fnv1a64-canonical-json-v4` state hashes, schema 1 replay envelopes with canonical SHA-256 integrity and deterministic verification-work limits, closed tamper-evident seed challenges, and isolated equal-rules ghost sessions. `VibeSnake.Persistence` depends on the public replay contract and owns strict UTF-8 inspection, compatibility and verification results, bounded replay directories, four fixed household rival slots, closed privacy-safe run cards, timestamps, cross-process transaction locking, same-directory atomic writes, playback-free bounded audio voice allocation, manifest-derived radio policy, and the station/boundary broadcast policy. It has no Godot dependency. `Main.cs` translates logical keyboard and controller actions into commands, mirrors every attempt into `RunReplayRecorder`, supplies the absolute `user://` path to the persistence stores, runs one persistence or verification operation at a time away from the main thread, bounds player-facing status, translates snapshots, ghost state, and replay results into drawing, and adapts pure audio, radio, and broadcast decisions to Godot nodes. `StepFeedback.cs` maps ordered typed events to prioritized cues and persistent captions. `MultimodalFeedback.cs` owns the presentation-only hunger, combo, power, protection, and death descriptors consumed by `Main.cs`; it cannot alter rules state. `VisualHierarchy.cs` owns presentation capacity, event tiers, foreground/background contrast, popup bounds, protection-first head-outline selection, and deterministic PNG review fixtures. `VibeLevelDirector.cs` is the only owner of escalation thresholds and emits typed levels/transitions with complete subsystem and accessibility budgets. `Main.cs` consumes these policies for live caption priority, board palette, HUD treatment, bounded trails, terminal opacity, and outline limits. The shell also owns focus-loss pause and read-only dropped-file policy. A headless mode starts the real scene and proves equal seeded runs, restored continuation, live terminal replay recording, isolated atomic storage, exact reload, read-only import, bounded compatibility feedback, background latest-replay input, stable seed-code round trips, source-preserving household import, live equal-rules ghost play, private run-card export, exact ghost deletion, logical movement input, focus lifecycle, audio-bus registration, every finite PCM fallback cue, bounded allocation, music duck/restore, output repair, complete power feedback resolution, five-profile multimodal attribution, visual-hierarchy frames, three performance profiles, 13 Vibe Level scenes, eight-station broadcast policy, and clean shutdown without engine warnings or leaked objects. Native parity assertions retain schema 1 first-divergence bundles with the shortest executed failing prefix and exact filtered reproduction. The same smoke contract runs from an exported player outside the checkout, followed by isolated user-data inspection, process-exit, required Rules, Persistence, and Game payload checks, portability, prohibited-content, packed-lock, and per-file hash checks. See [REPLAYS.md](REPLAYS.md) for the complete replay boundary.

`RunModeCatalog` is the closed product-mode boundary over `vibesnake-core@4`. It defines `classic@1` and `vibe@1`, exact product configurations, player descriptions, board, pause, seed, restart, difficulty/DDA policy, and effective score categories. Pure `AdaptiveDifficultyPolicy` owns deterministic `vibe-bounded-hunger-v1`; its closed inputs are effective config, rules tick, combo, and hunger, and its sole output is a bounded zero-to-two-tick hunger drain. `sha256-canonical-runconfig-v3` binds mode, score switches, DDA enabled state, and policy into fair-score and replay identity. `RunScoreIdentity` discloses those fields, and Vibe DDA-on/off categories are distinct. Godot selects modes and the opt-out through logical remappable actions and renders the contract, but it cannot redefine mechanics.

The slice remains incomplete for approved authored audio content and final production presentation. Pure C# now owns all nine powers, near misses, profile achievements, Classic/Vibe mode contracts, the bounded Vibe adaptive policy, replay browsing/playback, stable seed challenges, isolated local ghosts, private run cards, interactive equal-rules AI spectators, a closed optional-lore catalog, and playback-free radio behavior; Godot owns their player-facing adapters. The lore adapter is an offline, presentation-only archive whose unlock checks read existing progression, spectator, and replay state without awarding progression or mutating rules. The comparison adapter uses fixed household slots and never permits ghost state to enter player collision, scoring, randomness, or progression. These systems still require their explicit content, physical-device, performance, and human review gates before promotion.

The content boundary begins in `vibesnake.content.inventory`. It scans source assets without loading them into either game runtime, requires every file to match exactly one human-reviewed policy rule, and generates deterministic hashes, integrity results, duplicate links, rights state, and export eligibility. `vibesnake.content.packs` then validates one dependency-free offline core and station-specific optional manifests against the complete approved inventory allowlist, compatibility ranges, rights-derived credits, and exact file metadata. Its resolver isolates optional failures from a valid core. This is executable admission and resolution proof, not yet a native runtime asset locator, and the native player continues to ship no legacy source assets.

## Entry and lifecycle

The console script and `python -m vibesnake` both call [__main__.py](../../src/vibesnake/__main__.py). It constructs `Game`, runs the loop, reports an unhandled exception, and always shuts down Pygame.

The loop in [game_state.py](../../src/vibesnake/core/game_state.py) is:

```text
clock tick -> input dispatch -> state update -> state-specific draw -> display flip
```

Rendering targets 60 FPS. Snake movement uses a separate logic tick, currently 0.05 seconds from configuration unless a temporary effect overrides it.

## State machine

`GameState` defines 12 states:

- Meta: Menu, Help, Settings, High Scores, Customize, Achievements.
- Gameplay: Running, Paused, Game Over, Name Entry.
- AI: Channel Browser and Let's Play.

`Game._fsm_transitions` is an explicit whitelist. `transition_to` validates supported transitions, but some older input paths still assign `self.state` directly. A future refactor should route every transition through one function and attach entry and exit actions there.

## Core model boundaries

| Component | Responsibility | Important file |
| --- | --- | --- |
| Snake | Body order, occupied-cell set, queued direction, movement, cosmetic rendering | [snake.py](../../src/vibesnake/core/snake.py) |
| Food | Legal spawning and food rendering | [food.py](../../src/vibesnake/core/food.py) |
| ScoreManager | Combo timer, multiplier, food points, and bonus points | [scoring.py](../../src/vibesnake/core/scoring.py) |
| MetricsTracker | Run-local collision and starvation death counts | [metrics.py](../../src/vibesnake/core/metrics.py) |
| NearMissDetector | Risk events, warnings, cooldowns, and near-miss combo | [near_miss.py](../../src/vibesnake/core/near_miss.py) |
| PowerUpManager | Spawn, collect, update, and draw power-up instances | [manager.py](../../src/vibesnake/powerups/manager.py) |

The snake intentionally keeps both an ordered deque and a set of positions. The deque supports movement and drawing order; the set makes occupancy checks constant time. Mutations must keep both structures synchronized.

## Rendering

- [menus.py](../../src/vibesnake/rendering/menus.py) draws all non-gameplay screens and overlays.
- [hud.py](../../src/vibesnake/rendering/hud.py) draws score, combo, radio, and active effect information.
- [visual_effects.py](../../src/vibesnake/rendering/visual_effects.py) owns particles, hitstop, shake, flashes, environment progression, grid, and scanlines.
- [snake.py](../../src/vibesnake/core/snake.py) also renders the customized snake and its trail. This is a deliberate model-view overlap that should eventually be separated if new renderers are added.

Headless rendering tests use SDL dummy drivers and exercise every menu and game-state dispatcher branch.

## Input and AI

[InputManager](../../src/vibesnake/input/input_manager.py) combines keyboard, mouse, and gamepad direction sources. It tracks the last active input mode so passive mouse movement does not override keyboard control.

[AIPlayer](../../src/vibesnake/ai/player.py) uses the same direction queue as a human. Each decision chooses food or a power-up target, rejects immediate unsafe moves and direct reversal, scores valid directions, and optionally injects chaos. It is reactive rather than a full path planner.

## Persistence

Profile, cosmetic, leaderboard, and preference repositories each own one schema-versioned JSON document. Achievement state is embedded in the profile, and the HUD reads the canonical `HighScoreTable` instead of writing its own file.

[json_store.py](../../src/vibesnake/data/json_store.py) supplies atomic replacement and non-overwriting corrupt-file backups. [paths.py](../../src/vibesnake/data/paths.py) selects the platform user-data directory, honors `VIBESNAKE_DATA_DIR`, and performs a non-destructive one-time import from the former checkout-local `data/` directory. Schema 0 documents migrate on load, while future schemas are read-protected from downgrade writes. See [PROGRESSION.md](../design/PROGRESSION.md).

## Assets and configuration

The runtime resolves config, images, sounds, and radio tracks from directories beside the source tree. That makes editable installs work but prevents a normal wheel from being self-contained. Asset lookup needs a resource abstraction before release.

Configuration is loaded once at import time by [config.py](../../src/vibesnake/data/config.py) and exposed as module constants in [settings.py](../../src/vibesnake/data/settings.py). This means most changes require restart and makes test-time reconfiguration harder.

## Main technical debt

1. `core/game_state.py` is a large coordinator containing input, state transitions, run rules, persistence calls, telemetry output, and death handling.
2. `rendering/menus.py` contains every screen in one large renderer.
3. Power-up behavior is integrated, but effect resolution still lives inside the large game coordinator.
4. Asset paths assume a repository checkout.
5. Several modules initialize configuration or Pygame resources at import time.
6. Some state changes bypass the transition validator.
7. The frozen Python oracle has no reset/recovery workflow. The native product path now owns separated, verified backup/reset/recovery and must not be backported into Python.

## Recommended refactor seams

While Python remains active, change it only through tested seams that improve the reference or unblock migration:

1. Extract `RunFinalizer` and `DeathResolver` from `Game`.
2. Introduce an event stream for food, collision, power-up, achievement, and audio reactions.
3. Give each game state a small input, update, and draw handler.
4. Formalize a small repository protocol if additional save documents are introduced.
5. Introduce content contracts that can be represented in shared fixtures and the target resource service.
6. Move snake rendering behind a renderer while retaining the existing movement model.

Do not build the same broad feature twice. Each extraction must either repair a release-blocking reference defect, create a reusable data contract, or provide trace evidence for the C# port. Every change preserves public behavior through integration tests and keeps the 80 percent project coverage floor.
