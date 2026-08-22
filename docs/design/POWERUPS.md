# Power-ups

All nine power-ups are connected to normal runs in both the Python reference and the native Vibe product mode. Unless stated otherwise, collecting a duplicate type is prevented while that effect remains active. Intended cross-family combinations can overlap. All nine powers are complete native C# contracts in `VibeSnake.Rules`. The Godot shell renders markers, family and visibility text, HUD status, head outlines, detached obstacles, bait marks, prioritized fallback cues, and Slow-Mo/Boost wall-clock cadence for the full portfolio.

## Gameplay contracts

| Power-up | Duration | Exact effect |
| --- | ---: | --- |
| Shield | Up to 5 seconds | Absorbs the next fatal collision, then is consumed. It expires unused after five seconds. It does not prevent starvation. |
| Slow-Mo | 6 seconds | Doubles the movement interval, giving the player more real time between grid steps. |
| Magnet | 6 seconds | Moves food one cell toward the snake head on each gameplay frame while active. |
| Boost | 4 seconds | Halves the movement interval, doubling grid-step frequency. |
| Phase Shift | 5 seconds | Allows the snake to cross its own body and active detached-segment obstacles. Screen edges already wrap and do not need phasing. |
| Gluttony | 5 seconds | Food still resets starvation, awards score, advances progression, and respawns, but the snake does not grow. |
| Bait | Next food respawn | Records the collection cell and uses inverse-square Manhattan-distance weighting to pull the next food spawn toward it. The marker is then consumed. |
| Last Stand | Held until used | Automatically triggers when a collision or starvation would end the run, keeps the score, shrinks the snake to half length rounded up, resets starvation, and grants three seconds of collision recovery the player still steers. The HUD labels the unused resource as a held coil. |
| Segment Detach | Instant, obstacles last 10 seconds | Removes up to five oldest tail cells while preserving the head. Those cells become drawn collision obstacles, block food and power-up spawns, and expire together after ten seconds. |

## Collision precedence

Fatal collision handling uses this order:

1. Phase Shift prevents self and detached-obstacle collisions before they become fatal.
2. Last Stand recovery immunity ignores collisions during its three-second window.
3. Shield absorbs one remaining fatal collision.
4. Last Stand revives the snake if held.
5. The run ends normally.

Starvation bypasses Shield but can consume Last Stand. Eating during Gluttony still resets starvation.

## Shared lifecycle

The [base class](../../src/vibesnake/powerups/base.py) has three terminal-safe states:

1. Spawned and visible until collection or visibility expiry.
2. Activated and updating until duration expiry or explicit consumption.
3. Inactive and removed by the [manager](../../src/vibesnake/powerups/manager.py).

Instant effects become inactive during activation. Last Stand overrides timed expiry and remains held until the death resolver consumes it. Reset clears every effect flag, obstacle, timer, and visual indicator.

The manager schedules spawns, excludes the snake, food, detached obstacles, and visible collectibles from candidate cells, rejects duplicate active effect types, detects collection, advances effects, and removes every inactive instance.

## Native product offer policy

`PowerDecisionCatalog` is the stable `power-decisions-v1` authority for the nine kinds, four tactical families, player question, state grammar, and offer telegraph.

| Family | Powers | Product offer rule |
| --- | --- | --- |
| Protection | Shield, Phase Shift, Last Stand | Any active or recovering protection resource suppresses all three protection offers. |
| Tempo | Slow-Mo, Boost | Either active tempo effect suppresses both tempo offers so a new offer cannot silently negate the current one. |
| Harvest | Magnet, Bait, Gluttony | Exact duplicates are suppressed; cross-kind harvest combinations remain available. |
| Geometry | Segment Detach | A new detach is suppressed while detached obstacles remain. |

Automatic selection is deterministic and enum ordered before the gameplay RNG chooses among eligible kinds. The product `vibe@1` factory enables the policy and geodesic placement and can reach all nine kinds. Classic disables power spawning. Default `RunConfig` leaves both flags off and retains the frozen Shield-only random path and legacy occupancy for shared dual-runtime fixtures and old canonical states. Enabled flags are part of config, replay, and score identity, and an enabled canonical state restores them explicitly.

## Decision readability and local evidence

A spawned pickup reserves the immediate movement destination, so its typed spawn event, stable on-board letter and outline, audio cue, family label, effect text, and remaining visibility appear at least one rules boundary before collection is possible. Product `vibe@1` also excludes every wrap-Manhattan geodesic cell from the reserved destination to food, so walking the shortest food route cannot collect the offer; collection is a detour. If that preferred set is empty, spawn falls back to ordinary occupancy so a tight board still receives an offer. On even-by-even boards a pair of antipodal cells can put every remaining cell on some shortest wrap path; the factory 64 by 33 board has an odd height, so that total covering cannot occur. Compatibility configs omit the flag and retain legacy occupancy. The HUD keeps the offer line visible beside existing active states instead of hiding it. Every timed state shows seconds, Last Stand and Bait use explicit held language, and Segment Detach shows both obstacle count and remaining time.

Opted-in local playtest summary schema 2 stores nine aggregate-only power rows. Each row counts `offered`, `detoursObserved`, `collected`, `activated`, `expired`, `consumed`, `saved`, and `deathAdjacent`. A detour is counted once when a direction change moves closer to the live offer. A save is a typed collision-prevention event. Death adjacency uses the last related power event within 20 rules ticks. No raw input event, input time, device identity, path, or free text is retained. Schema 1 summaries migrate with zeroed power rows because they never recorded this evidence.

## Required synergy scenarios

| Scenario | Automated contract | Human decision still required |
| --- | --- | --- |
| Boost plus Phase Shift | Double cadence retains an explicit body-pass collision window. | Can the player plan the high-speed line and explain the protection window? |
| Slow-Mo plus Magnet | Slow cadence composes with deterministic food pull. | Does the combination improve deliberate recovery without becoming trivial? |
| Bait plus Boost | A held spawn bias composes with the faster conversion window. | Is the preview understandable before the player commits? |
| Gluttony plus Magnet | Pulled food restores score and hunger without body growth. | Can the player explain the growth trade? |
| Segment Detach plus protection | Temporary hazards coexist with an independent protection resource and readable countdown. | Is body relief worth the obstacle field? |
| Last Stand after a long combo | The held rescue consumes once and exposes recovery immunity without losing score. | Was the save anticipated, attributable, controllable, and worth retrying? |

Reviewed deterministic seeds and observation fields for all six are in `config/power_decision_contract_v1.json` and `config/qa_human_playtest_protocol.json`. Automated seeded contracts pass. Human scenario status remains pending with zero sessions.

## Mutation Fork experiment

The pure prototype can deterministically choose two distinct eligible offers and resolves either collection by withdrawing the other. Its flag defaults off, is not wired into product spawning, and consumes no production random state. The contract remains `automated-prototype-human-unverified` and unapproved. It may be enabled in the product only after seeded and human evidence shows more planning without added confusion. Otherwise it is removed cleanly.

## Native Slow-Mo and Boost qualification

Slow-Mo and Boost are pure cadence modifiers on the rules snapshot. They do not change fixed-step movement distance, scoring, or randomness when `Step` is invoked. Presentation shells advance rules through `RulesCadenceClock`, which drains wall-clock time at `RulesTickMilliseconds * MovementCadenceNumerator / MovementCadenceDenominator`. Slow-Mo multiplies the numerator by 2, Boost multiplies the denominator by 2, and both compose (product of factors), matching the Python cadence helper. The Godot shell re-reads the live interval after every drained step so tempo expiry mid-burst is honored.

## Native Bait, Gluttony, and Segment Detach qualification

- **Bait** records the collection head and weights the next food respawn with integer inverse-square Manhattan weights (`1_000_000 / (d + 1)^2`), then clears the marker.
- **Gluttony** scores and resets hunger on food without growing the body for its duration.
- **Segment Detach** removes up to five tail cells into timed obstacles that block content and kill without Phase Shift; Phase Shift bypasses those cells; obstacles expire together. Collection runs after movement settles so detached cells match the Python post-move body.

## Native Last Stand qualification

The pure C# rules kernel implements Last Stand as a held recovery resource:

- Collection marks `LastStandHeld` without a duration timer until consumption. Presentation copy distinguishes the held coil, automatic fatal-event trigger, and player-steered three-second recovery.
- Collision precedence: Phase Shift, recovery immunity, Shield, held Last Stand revive, then death.
- Revive keeps score, shrinks body to half length rounded up (`max(1, (n + 1) / 2)`), resets hunger, and grants recovery immunity for the configured recovery ticks (default 60 ticks / 3 seconds).
- Recovery immunity blocks self-collision without moving the body and advances each rules step.
- Starvation consumes held Last Stand the same way and never leaves a starvation death while held.
- Five shared Python-to-C# cases live in `last_stand_rules_v1.json`.

## Native Phase Shift qualification

The pure C# rules kernel implements Phase Shift beside Shield:

- Collection activates a fixed-duration Phase Shift timer (default 100 ticks / 5 seconds).
- While active, self-collision is ignored and the snake may occupy duplicate body coordinates.
- Occupancy tracking keeps a cell blocked until the last body occurrence leaves it.
- Lifecycle advances before movement, so a one-tick remaining Phase Shift expires before that step's collision check.
- Phase Shift does not prevent starvation and does not stack with a second Phase Shift pickup.
- When Phase Shift and Shield are both active, Phase Shift wins on self-collision (the body phases through; Shield is not consumed).
- Six generated Python-to-C# cases compare collection, expiry, body overlap, and starvation bypass under `phase_shift_rules_v1.json`.

## Native Shield qualification

The pure C# rules kernel implements Shield without a Godot dependency:

- A dedicated PCG32 gameplay stream chooses a legal pickup cell outside the snake, food, and immediate destination.
- The reserved destination guarantees at least one full movement boundary between spawn feedback and possible collection.
- The pickup remains visible for 120 fixed ticks, collection occurs on the movement that enters its cell, and activation begins with the full 100-tick duration.
- Existing pickup and active timers advance before movement. A Shield with one tick remaining therefore expires before that step's collision resolution.
- One active Shield consumes itself to prevent one self-collision. The blocked movement does not advance the body, but the starvation clock advances for the attempted rules step. If that step reaches the starvation deadline, Shield is consumed and starvation then ends the run.
- Shield does not prevent starvation, does not stack, is removed on restart, and participates in canonical state, hashes, restoration, and replay verification.
- If food must respawn on the only free cell, the pickup is explicitly discarded rather than overlapping food or creating a false grid-completion result.

The Godot slice maps every power kind to a single-letter marker, signal color, composite HUD status, and prioritized fallback cue. Shield keeps its dedicated break and lifecycle tones; other powers share generic spawn, activate, expire, and recovery tones with power-specific captions. Active states draw head outlines (and body tint for Phase Shift and Gluttony), bait marks, and detached-segment hazards. These are accessible engineering fallbacks, not final authored art or sound.

Eight generated Python-to-C# cases compare normalized state and ordered power events for collection, both expiry paths, collision recovery, expiry precedence, starvation bypass, and collision recovery at the starvation deadline. Native tests separately prove deterministic spawn, a usable minimum duration, anti-stacking, saturated-board discard, restart, restoration, replay, and invalid-state rejection. See [the parity decision log](../engineering/PARITY_DECISIONS.md#pd-008-shield-collection-and-lifecycle-receive-rules-version-4).

## Configuration

- In the Python oracle, `powerups.enabled`, `powerups.spawn_interval`, and `powerups.visible_duration` retain the reference manager behavior.
- In native rules, `PowerSpawnIntervalTicks` and `PowerVisibleTicks` own cadence and visibility.
- Native `EnablePowerDecisionOffers` defaults false for compatibility and is true in the Vibe product factory.
- Native `AvoidFoodGeodesicPowerOffers` defaults false for compatibility and is true in the Vibe product factory. The false value is omitted from canonical config and state JSON.
- `config/power_decision_contract_v1.json` locks families, lifecycle evidence, six scenarios, and the default-off Mutation Fork gate.

See [CONFIGURATION.md](../guides/CONFIGURATION.md).

## Verification contract

A new or changed power-up is complete only when automated tests prove:

- It spawns in a legal free cell and cannot duplicate an already active type.
- Product offers cannot be redundant with an active protection, opposing tempo, or active geometry state.
- Collection triggers exactly one activation.
- Its documented rule changes a real run through `Game.update`.
- Explicit consumption and timed expiry clean up flags and visuals exactly once.
- Death, restart, pause, food spawning, obstacles, and other effects do not leak state.
- AI and human runs use the same gameplay rule unless explicitly documented.
- Product-mode changes refresh the descriptive AI baseline without creating human targets.

The current behavioral suite lives in [test_powerup_gameplay.py](../../tests/integration/test_powerup_gameplay.py), with lifecycle and class tests under [tests/powerups](../../tests/powerups/).
