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

## Native V080-04 foundation

The Godot product now implements the automated progression foundation in pure C# and Godot:

- Twenty goals span mastery, discovery, and identity lanes plus early, middle, and mastery pacing. Every row shows exact current/target progress, the exact requirement, Vibe and rules identity, and its expression reward. One next goal can be highlighted without changing other progress.
- Only the exact canonical normal-human run context can merge terminal run metrics. AI, replay, tutorial, practice, seeded-challenge, modified, and forged lookalike contexts are rejected. There are no completed-run-count goals.
- Eight curated cosmetic sets replace the theoretical combination count for the native product path. Every locked set maps to one exact Tour event requirement and `0/1` or `1/1` progress. The selected set and up to five unique saved loadouts persist. Patterns, eyes, accessories, head markers, and trails are presentation-only; trail opacity is capped at 50 percent.
- The Broadcast Tour contains twelve fixed-seed event cards across Local Frequency, District Relay, Regional Coil, and Crown Broadcast. Dependency branches are finite. Every card has canonical mode/rules/score identity, rival and station references, a primary goal, optional style goal, exact expression reward, copy IDs, noncompetitive practice, deterministic replay recording, and immediate same-seed rematch.
- Tour practice cannot update personal bests, score history, ordinary achievement unlocks, local playtest summaries, or normal-human run metrics. A completed primary goal updates only dependency-closed Tour state, exact earned expression rewards, and derived cosmetic/goal progress.
- Schema-1 `progression.json` rejects unknown and duplicate fields, oversized data, impossible metric counts, out-of-order Tour completion, forged reward IDs, unearned known rewards, locked selected/saved sets, and mismatched derived counts. Writes use same-directory temporary replacement.

`progression-qualification-v1` covers raw keyboard and controller goal highlighting, Tour browsing/start/return, locked-event rejection, fixed-seed practice isolation, cosmetic selection/loadout persistence, bounded reduced-motion notifications, catalog validation, canonical context references, and rules hash isolation. It deliberately reports `pending-zero-reviewed-human-sessions`: AI and deterministic fixtures do not set human pacing targets.

## Python reference leaderboard

[HighScoreTable](../../src/vibesnake/core/high_scores.py) is the sole leaderboard repository. It stores up to ten validated entries with name, score, and timestamp in descending order in `high_scores.json`. The HUD reads from that repository and does not write a separate score file.

Older checkouts may contain `highscore.json`, which held one HUD score. On first eligible load, `HighScoreTable` imports that entry, merges it with the canonical top ten, and records the completed import in `high_scores.json`. The legacy file is never modified and is not imported again.

## Native local scores

The Godot product stores current competitive personal bests in schema-2 `personal_bests.json` and bounded top-ten history in schema-1 `score_history.json`. Every row is separated by rules version, mode, run purpose, seed category, score category, difficulty policy, DDA state and policy, and full config identity. Normal human and seeded challenge scores are competitive but never share a category. Tutorial, practice, AI, replay, modified, and legacy rows cannot update a current personal best.

Keyboard V or Down and controller Down open Local Scores. Existing personal bests seed history idempotently. Players who want their frozen Python top ten can copy `high_scores.json` to the native `user://imports/high_scores.json` inbox, choose import with R or controller North, and confirm. The importer accepts at most ten schema-1 rows within 64 KiB, records the source SHA-256, does not modify the source, and will not run twice. Because the old file lacks modern rules metadata, imported rows remain visibly noncompetitive under `Legacy 0.2`.

## Save files and ownership

The frozen Python reference save documents use schema version 1:

| File | Owner | Purpose |
| --- | --- | --- |
| `player_profile.json` | `PlayerProfile` | Identity, lifetime statistics, and achievements |
| `customization.json` | `CustomizationManager` | Current appearance and up to five loadouts |
| `high_scores.json` | `HighScoreTable` | Canonical top-ten leaderboard and migration state |
| `preferences.json` | `UserSettings` | Sound enabled state, master volume, and fullscreen preference |

`highscore.json` is a read-only legacy import source, not an active save document.

Native score ownership is separate:

| File | Owner | Purpose |
| --- | --- | --- |
| `personal_bests.json` | `PersonalBestStore` | Schema-2 exact-category personal bests; schema 1 migrates visibly to `Legacy 0.2` |
| `score_history.json` | `ScoreHistoryStore` | Schema-1 top ten per exact category and completed Python-import marker |
| `progression.json` | `ProgressionStore` | Schema-1 exact human goals, highlighted goal, selected/saved cosmetic sets, earned expression rewards, and dependency-closed Broadcast Tour completion |
| `imports/high_scores.json` | Player | Optional read-only Python import source; never reset or modified by native code |

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

All four Python repositories share the primitives in [json_store.py](../../src/vibesnake/data/json_store.py):

- Writes go to a temporary file in the destination directory, are flushed to disk, and replace the previous document atomically.
- Unreadable or structurally invalid JSON is copied to a non-overwriting `.corrupt.bak` file before defaults are used.
- Unversioned schema 0 documents are migrated to schema 1 when loaded.
- A file written by a newer schema is not overwritten by this version of the game.
- Numeric counters, leaderboard entries, loadout counts, preferences, and required structures are validated or bounded during load.

Migration, corruption, future-version, isolation, and failed-write behavior are covered in [test_persistence.py](../../tests/core/test_persistence.py) and [test_paths.py](../../tests/core/test_paths.py).

Native score writes use strict bounded parsing and atomic temporary-file replacement. The Local Scores reset category backs up, verifies, removes, and restores `personal_bests.json` and `score_history.json` together while leaving the player-supplied import source alone.

## Remaining player-facing work

The native product has separated reset confirmation and verified recovery. Human review of recovery wording and real platform file browsers remains before public release. The Python reference recovery path remains technical and frozen except for release-blocking fixes.

The automated target experience is implemented. Remaining V080-04 acceptance is human: review real run distributions and unlock order, check quiet and maximum-effect cosmetics on retained platform pixels, judge whether goals motivate interesting play, and assess attachment, rivalry appeal, post-run momentum, and rematch desire. No AI distribution may substitute for that evidence. No unlock may add survival power, daily obligation, or paid randomness. See [FUN_DESIGN.md](FUN_DESIGN.md#progression-without-grind) and the [world and broadcast bible](WORLD_BIBLE.md#broadcast-tour-progression).
