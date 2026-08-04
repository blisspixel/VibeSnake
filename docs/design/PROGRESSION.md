# Progression and Save Data

## Player progression

Human runs track:

- Total games.
- Highest score and cumulative score.
- Highest combo multiplier.
- Apples eaten.
- Edge wraps.
- Achievement unlock state and timestamps.

AI spectator runs are intentionally excluded from human progression.

## Achievements

The game defines 25 achievements:

- 13 common.
- 7 rare.
- 4 epic.
- 1 legendary.

Conditions cover first runs, food, wraps, power-ups, survival time, scores, combo multipliers, length, total games, near misses, and play time of day. The canonical definitions and condition evaluator are in [achievements.py](../../src/vibesnake/core/achievements.py).

Achievement checks occur when a human run ends. Newly unlocked entries are queued for display and stored inside `player_profile.json`, so achievement state has the same lifecycle and owner as the statistics it depends on.

## Cosmetics

The cosmetic system exposes five independent axes:

| Axis | Choices | Notes |
| --- | ---: | --- |
| Base color | 12 | Nine free and three progression-gated metallic colors |
| Pattern | 6 | Free |
| Eye style | 5 | Free |
| Accessory | 6 | Free, including none |
| Trail | 5 | Four non-default trails are progression-gated |

That yields 12 x 6 x 5 x 6 x 5, or 10,800 combinations. Current choices and up to five loadouts are stored in `customization.json`.

Unlock thresholds are defined in [customization.py](../../src/vibesnake/core/customization.py). The UI should use the same map rather than duplicating requirement copy.

The raw 10,800 count is an implementation fact, not a quality target. The 1.0 progression pass curates smaller authored sets and removes combinations that are interchangeable, thematically incoherent, clipped, or less readable than the default appearance.

## Target progression contract

Progression exists to direct attention toward interesting play and deepen attachment. It does not compensate for a weak core loop and never grants permanent survival or scoring power.

Goals are organized into three visible lanes:

| Lane | What it celebrates | Example evidence | Reward types |
| --- | --- | --- | --- |
| Mastery | Better control and deliberate risk | Clean recoveries, wrap techniques, sustained resonance, exact seed challenges | Authored sheds, replay frames, rival rematches |
| Discovery | Trying systems and understanding the world | Power synergies, station listening, rival encounters, unusual run conditions | Station IDs, track notes, dossiers, archive fragments |
| Identity | Choosing and refining a personal presentation | Completed themed sets, selected affiliations, saved loadouts | Cosmetic sets, trails, run-card treatments, broadcast themes |

Every goal shows its exact rule, current progress, eligible modes, rules identity, and reward. The player can highlight one next goal without losing progress on the others. Surprise presentation may celebrate completion, but the requirement itself is never hidden.

## Broadcast Tour

The target progression wrapper is a finite offline circuit of authored event cards. It captures classic arcade road-tour energy through named rivals, visible standings, strong post-run commentary, themed stops, and immediate rematches. It does not copy another game's characters, economy, music, or upgrade system.

Tour tiers are Local Frequency, District Relay, Regional Coil, and Crown Broadcast. Each event card contains:

- Stable event and campaign schema versions.
- Mode, rules identity, seed policy, and eligible score category.
- Featured rival and station context.
- One primary mastery goal plus an optional style goal.
- Exact completion state and exact authored reward.
- Intro, post-run, retry, replay, and accessibility-copy IDs.
- Practice behavior that never contaminates competitive scores.

Progress is branchable but finite. A player can choose between available event cards, replay any unlocked event, practice without submitting a score, and challenge the same seed again. Failure never removes earned access. A completed goal never requires waiting for a schedule to return.

Tour rewards are expression, context, and new authored challenges. They never change base speed, collision, starting resources, score multipliers, spawn probability, or input behavior. There are no premium currencies, randomized paid rewards, wait timers, daily chores, expiring streaks, rotating scarcity, or paid skips.

Automatic qualification checks schema validity, reachability, dependency cycles, impossible goals, duplicate rewards, grind outliers, rules-category contamination, save migration, deterministic replay, rival equality, and complete localization and caption IDs. Human review later judges motivation, attachment, rivalry appeal, and whether the next event is inviting. Missing human evidence remains visible but does not pause reversible implementation.

## Leaderboard

[HighScoreTable](../../src/vibesnake/core/high_scores.py) is the sole leaderboard repository. It stores up to ten validated entries with name, score, and timestamp in descending order in `high_scores.json`. The HUD reads from that repository and does not write a separate score file.

Older checkouts may contain `highscore.json`, which held one HUD score. On first eligible load, `HighScoreTable` imports that entry, merges it with the canonical top ten, and records the completed import in `high_scores.json`. The legacy file is never modified and is not imported again.

## Save files and ownership

Every active save document uses schema version 1:

| File | Owner | Purpose |
| --- | --- | --- |
| `player_profile.json` | `PlayerProfile` | Identity, lifetime statistics, and achievements |
| `customization.json` | `CustomizationManager` | Current appearance and up to five loadouts |
| `high_scores.json` | `HighScoreTable` | Canonical top-ten leaderboard and migration state |
| `preferences.json` | `UserSettings` | Sound enabled state, master volume, and fullscreen preference |

`highscore.json` is a read-only legacy import source, not an active save document.

## Save locations

Normal runs use the operating system's per-user data directory. The full Python and native layouts, recovery rules, and dual-runtime boundaries are published in [USER_DATA.md](../engineering/USER_DATA.md).

| Platform | Default directory |
| --- | --- |
| Windows | `%LOCALAPPDATA%\VibeSnake` |
| macOS | `~/Library/Application Support/VibeSnake` |
| Linux | `$XDG_DATA_HOME/vibesnake`, or `~/.local/share/vibesnake` |

`VIBESNAKE_DATA_DIR` overrides this location for tests, portable builds, and development. An explicit path passed to a repository has the same effect for that repository.

On first normal launch, known save files in the former project `data/` directory are copied to the new location only when the destination does not already exist. The migration never deletes or overwrites the originals. A versioned marker prevents reset or deleted files from being resurrected on later launches.

## Durability and compatibility

All four repositories share the primitives in [json_store.py](../../src/vibesnake/data/json_store.py):

- Writes go to a temporary file in the destination directory, are flushed to disk, and replace the previous document atomically.
- Unreadable or structurally invalid JSON is copied to a non-overwriting `.corrupt.bak` file before defaults are used.
- Unversioned schema 0 documents are migrated to schema 1 when loaded.
- A file written by a newer schema is not overwritten by this version of the game.
- Numeric counters, leaderboard entries, loadout counts, preferences, and required structures are validated or bounded during load.

Migration, corruption, future-version, isolation, and failed-write behavior are covered in [test_persistence.py](../../tests/core/test_persistence.py) and [test_paths.py](../../tests/core/test_paths.py).

## Remaining player-facing work

The data layer is durable, but recovery is still technical rather than friendly. Before a public release, add an in-game reset confirmation and a recovery screen that explains when a `.corrupt.bak` file was created. Those tasks are tracked in [ROADMAP.md](../../ROADMAP.md).

Progression also needs its target experience implemented. Build the three goal lanes and Broadcast Tour, replace empty repetition with authored challenges, curate cosmetic sets, and validate every retained set at quiet and maximum effect intensity. No unlock may add survival power, daily obligation, or paid randomness. See [FUN_DESIGN.md](FUN_DESIGN.md#progression-without-grind) and the [world and broadcast bible](WORLD_BIBLE.md#broadcast-tour-progression).
