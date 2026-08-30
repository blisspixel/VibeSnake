# Python to C# Parity Decisions

This log prevents migration mismatches from becoming accidental game-design changes. Every divergence receives one classification: preserved compatibility, Python defect, target defect, fixture defect, or intentional target correction.

## Decision format

Each entry records the compared behavior, evidence, player consequence, decision, implementation state, and regression proof. An open item stays excluded from parity claims and appears explicitly in fixture metadata.

## PD-001: Food collection occurs on entry

Status: Resolved as a Python defect

The Python coordinator and reference adapter formerly checked whether the old head already occupied the food cell before movement. The snake therefore entered food on one tick, then grew, scored, respawned food, and emitted feedback while leaving that cell on the next tick.

The intended and now shared behavior is to predict the legal next head, collect on entry, grow on that movement, reset starvation, score, and emit feedback in the same rules step. `Snake.peek_next_head`, the game coordinator, the Python QA adapter, targeted integration tests, and the C# rules kernel now use this contract.

Player consequence: controls and feedback are more immediate, a last-moment route is easier to understand, and presentation no longer appears one cell late.

## PD-002: Exact starvation deadline ordering

Status: Accepted intentional target correction

The old Python coordinator resolved starvation before the next movement tick. At the exact deadline it could end a run even when the next legal movement entered food. The production Python coordinator, Python QA reference, and C# rules kernel now use one explicit order:

1. Consume one legal buffered direction.
2. Advance the combo clock.
3. Compute the attempted destination.
4. If the legal destination contains food, move, grow, score, reset hunger, and survive.
5. Otherwise advance hunger for the attempted rules step.
6. If the destination is illegal and no recovery applies, end with collision as the attributed cause even when hunger reached zero on the same step.
7. If Shield prevents the collision, leave the body in place, consume Shield, and then resolve starvation if hunger reached zero.
8. Otherwise complete the legal non-food movement and then resolve starvation if hunger reached zero.

Last Stand is evaluated only after a legal final-tick movement fails to collect food. It is not consumed by a visibly successful deadline eat. This makes the clutch action readable, preserves player trust in the food cue, and gives telemetry one unambiguous cause. Human playtesting still validates warning clarity and perceived fairness, but it does not reopen the deterministic order implicitly.

Regression proof includes production integration tests, Python reference tests, C# unit tests, and shared cases for deadline rescue, move-then-starve, and collision precedence.

## PD-003: Gameplay random algorithm

Status: Accepted intentional target correction

Python uses module-level Mersenne Twister calls whose ordering can be affected by unrelated systems. The target uses `pcg-xsh-rr-32-v1` with serialized state and a dedicated gameplay stream. Identical numeric seeds are not expected to choose identical food cells across the two algorithms.

Differential fixtures either inject positions, exclude the newly respawned food coordinate, or compare normalized event and legality contracts. Replays store the algorithm ID and state. Cosmetic, AI, and radio randomness will use separate streams.

The movement and targeted core fixture headers declare `vibesnake-core@4` while separately declaring `positions-injected-or-random-output-normalized-v2`. The C# consumers assert both fields before executing a case. Ruleset identity therefore means the compared behavior contract, not false equivalence between the Python source RNG and native PCG32 stream.

## PD-004: Combo clock ownership

Status: Partially resolved

Python updates combo time from gameplay delta before a movement tick. The target counts integer rules ticks and uses 60 ticks for the three-second combo window and 30 ticks for the 1.5-second speed bonus at the 0.05-second cadence.

The interpolation curve, strict expiry comparison, speed bonus, and length bonus now match at fixed cadence in targeted fixtures. Combo expiry clears the streak but preserves elapsed time in both runtimes. Resetting the shared clock formerly made a late food appear fast immediately after expiry and awarded a false speed bonus. Boundary cases now cover the last speed-eligible tick, the exact speed cutoff, the exact combo window, the first expired tick, and late food after expiry. Variable render-rate traces, pause, hitstop, temporary speed changes, and any future cadence change still need explicit tests before this decision closes.

## PD-005: Full-grid completion

Status: Accepted intentional target correction

The old Python coordinator removed food after the player filled the grid and continued a foodless survival state until an unavoidable starvation death. That ending converted the strongest possible mastery proof into an anticlimactic loss.

The shared target ends the run immediately as a win after the final legal food entry. The final step emits `moved`, `ate_food`, `score_changed`, `hunger_reset`, and `won` in that order. Food becomes absent because the terminal board has no free cell. The Python coordinator now presents a Grid Master completion state, while the C# kernel records `RunStatus.Won`.

Player consequence: the game's hardest spatial achievement receives a clear, positive resolution and a stable event contract for presentation, achievements, replays, and automated QA.

## PD-006: Portable score ceiling

Status: Resolved as an intentional target correction

Python integers do not overflow, while the native kernel and serialized release formats require a bounded score. Both runtimes now saturate at 2,000,000,000 points. A score event reports only the points actually awarded, including one point immediately below the ceiling and zero points at the ceiling. Bonus mutations in the Python reference use the same bound and reject negative values.

This ceiling is far beyond an ordinary run and does not alter normal balance. It prevents runtime-specific overflow, keeps save and replay values portable, and gives fuzzed or restored high-score states one exact outcome. Shared cases cover the cell below the ceiling and the ceiling itself; native validation rejects imported scores outside the range.

## PD-007: Corrected scored behavior receives a new rules version

Status: Resolved as a compatibility correction

Preserving elapsed time when a combo expires changes whether a later food receives the speed bonus. Reusing rules version 2 would silently reinterpret an existing replay under different scored behavior. The corrected contract is therefore `vibesnake-core@3` in Python fixtures, native state, replays, hashes, tests, and canonical documentation.

Schema 1 remains unchanged because its representation is still valid. Version 2 state and replay files remain intact but fail compatibility before execution. A future compatibility layer may execute them only through an implementation of the original rules; it may not relabel or migrate their outcome into version 3.

## PD-008: Shield collection and lifecycle receive rules version 4

Status: Resolved as a compatibility correction

The Python coordinator formerly collected a power only when a later update began with the snake already occupying its cell. Shield collection, activation, and feedback could therefore occur one frame after the movement that visually entered the pickup. Production Python now performs collection immediately after a successful movement. The Shield trace adapter uses the same production `Snake`, `PowerUpManager`, and `ShieldPowerUp` contracts.

The native kernel now owns one deterministic Shield lifecycle: a visible pickup cannot overlap the snake or food, the immediate movement destination is reserved when a pickup spawns, collection and activation occur on entry, the active duration advances in fixed rules ticks, configured duration is at least two ticks so the effect is usable on the first post-collection step, expiry precedes collision resolution, one active Shield prevents and consumes exactly one self-collision, starvation bypasses Shield, a second Shield cannot coexist with an active one, and restart clears all power state. Canonical restoration, replay verification, state-machine continuation, invalid-state rejection, and deterministic spawn tests cover the same state. A blocked collision advances hunger without moving the body. At a simultaneous deadline, Shield is consumed first and starvation then ends the run. A fatal unrecovered collision retains collision attribution even when that same step exhausts hunger.

These changes add replay-relevant state and player-visible event timing, so the current contract is `vibesnake-core@4`. Canonical state advances to schema 2 and `fnv1a64-canonical-json-v3`; the replay envelope remains schema 1 because its representation still carries the embedded state, rules identity, and hash algorithm explicitly. Version 3 states and replays remain intact and are rejected as incompatible rather than silently acquiring Shield semantics.

Random Shield positions are not compared across the Python Mersenne Twister and native PCG32 streams. The eight targeted Shield traces inject pickup and active-effect state under `positions-and-power-state-injected-v1`; separate native tests prove deterministic legal spawning and saturated-board discard behavior. Presentation feedback remains outside rules parity and is qualified through Godot smoke.

## Current shared evidence

| Fixture | Source | Consumer | Coverage |
| --- | --- | --- | --- |
| `tests/fixtures/shared/core_movement_v2.json` | Production Python `Snake` | C# xUnit parity test | 100 cases, 25,600 steps, command acceptance and rejection, bounded queue consumption, position, length, wrapping, and survival in a compact self-describing encoding |
| `tests/fixtures/shared/core_rules_v4.json` | Python `CoreSimulation` using production `Snake` and `ScoreManager` | C# xUnit parity test | 35 targeted cases covering food entry, growth, every current combo, speed, length, and score-ceiling boundary, monotonic combo expiry, queue acceptance and overflow, stable non-respawn food, normalized random-stream use and respawns, collision precedence, departing tail, wrap, exact starvation outcomes, full-grid victory, and ordered events |
| `tests/fixtures/shared/shield_rules_v1.json` | Production Python `Snake`, `PowerUpManager`, and `ShieldPowerUp` | C# xUnit parity test | 8 targeted cases covering entry collection, pickup expiry, active countdown and expiry, collision consumption and prevention, expiry-before-collision precedence, starvation bypass, the collision and starvation deadline boundary, normalized state, and ordered power events |
| `tests/fixtures/shared/phase_shift_rules_v1.json` | Reviewed Python-origin production Phase Shift corpus, canonically rendered and freshness-checked by native `RepositoryChecks` | C# xUnit parity test | 6 targeted cases covering entry collection, pickup expiry, active countdown, effect expiry before collision, body overlap, starvation bypass, normalized state, and ordered power events |
| `tests/fixtures/shared/last_stand_rules_v1.json` | Reviewed Python-origin production Last Stand corpus, canonically rendered and freshness-checked by native `RepositoryChecks` | C# xUnit parity test | 5 targeted cases covering entry collection, held collision revive, body shrink, starvation revive, recovery immunity, recovery expiry, timers, and ordered power events |
| `tests/fixtures/shared/achievement_candidates_rules_v1.json` | Reviewed Python-origin `CoreSimulation(enable_achievement_candidates=True)` corpus, canonically rendered and freshness-checked by native `RepositoryChecks` | C# xUnit parity test with `EnableAchievementCandidates: true` | 4 targeted terminal cases covering score-gated candidates, already-unlocked suppression, zero-score empty emission, and self-collision candidates with ordered catalog-index payloads |

Shared fixtures declare `vibesnake-core@4` and state their exact injected-or-normalized randomness policy. The remaining corpora are regenerated and compared byte for byte in Python CI before C# consumes them. The four achievement-candidate, five Last Stand, and six Phase Shift vectors retain their reviewed Python origins; native tooling reproduces their exact 2,682, 3,596, and 3,534 bytes without executing C# rules. Their separate parity tests remain the live behavior consumers. A semantic fixture change requires the relevant parity entry and a reviewed rules decision.

## PD-009: Achievement candidate events stay product-gated

Status: Open intentional product gate for default dual-runtime corpora; dedicated product-path fixture landed

Both runtimes can evaluate a shared rules-local achievement catalog (score, length, combo, wraps, near-miss, powers, survival) and emit ordered `achievement_candidate` events with catalog-index payloads. Default configuration leaves emission off so `core_rules_v4` and power dual-runtime fixtures keep exact event lists. The Godot product path enables emission on live runs; Python `CoreSimulation(enable_achievement_candidates=True)` mirrors that path for dual-runtime experiments.

Candidates emit at most once per terminal run in C#. Profile unlock persistence remains a shell or progression concern outside pure rules. Ordered-event parity against the reviewed Python-origin product-flag corpus is proven by `achievement_candidates_rules_v1.json` without regenerating the default-off core/power corpora. Native rendering and live C# consumption remain separate so the proof is not self-referential. Flipping the default to on remains a separate deliberate corpus regeneration.

Player consequence: live product can celebrate run-local mastery without forcing every migration fixture to absorb unlock events before the dual-runtime harness is ready.

Both runtimes can suppress already-owned IDs: C# via `SnakeRun.ApplyProfileUnlocks` and Python
`CoreSimulation(already_unlocked_achievements=...)`.

## PD-010: Session achievement counters enter canonical state schema 3

Status: Resolved as a restore-correctness correction

Session counters (`sessionFoodEaten`, `sessionWraps`, `sessionNearMisses`,
`sessionPowerupsCollected`, `sessionMaxCombo`) drive run-local achievement
candidates. Under schema 2 they lived only in memory, so mid-run restore
zeroed them and could under-award candidates after checkpoint continuation.
Continuous product runs that never mid-restore, and offline replay verification
that rebuilds from the initial state, were unaffected.

Canonical state now uses schema 3 and `fnv1a64-canonical-json-v4` so every
behavior-affecting field restores and hashes. Rules identity remains
`vibesnake-core@4` because scored step outcomes, movement, powers, and ordered
gameplay events are unchanged; only restore completeness for achievement
metrics advanced. Schema 2 states and prior hash algorithm ids remain intact
and are rejected as incompatible rather than silently acquiring zeros.

Player consequence: mid-run restore, future checkpoints, and any path that
continues from serialized state preserve unlock eligibility already earned
during the run.

## PD-011: Diversified offers stay native product-gated

Status: Resolved as an intentional native product gate

The frozen shared spawn path always offers Shield and consumes one gameplay RNG draw for its legal cell. Replacing that default would change shared fixtures, replay hashes, and Python oracle behavior under the same compatibility configuration. `RunConfig.EnablePowerDecisionOffers` therefore defaults false. Default and shared-fixture config hashes remain byte stable because the false value is omitted from canonical config and state JSON. An enabled config writes explicit `enablePowerDecisionOffers: true`, receives a distinct config hash under the existing `sha256-canonical-runconfig-v3` extension rule, restores the flag, and consumes a deterministic kind draw before the legal-cell draw.

The `vibe@1` product factory enables `power-decisions-v1`; Classic remains power-free. The policy exposes all nine native powers, suppresses redundant protection, opposing tempo, exact harvest duplicates, and active geometry duplicates, and retains declared cross-family synergies. This is a native-only post-port extension, so no shared Python trace was regenerated and `vibesnake-core@4` compatibility fixtures continue to use the default-off path.

The opted-in local summary schema 2 and Godot HUD observe the enabled product path without entering pure rules state. The Mutation Fork prototype is pure, explicit, and default off; it is not part of product spawning or score identity unless a later reviewed decision enables it.

Player consequence: Vibe runs can now receive all nine readable power offers without silently relabeling legacy, shared-fixture, Classic, or restored compatibility behavior.

## PD-012: Food-geodesic power occupancy stays native product-gated

Status: Resolved as an intentional native product gate

Power occupancy already excluded the snake, detached obstacles, food, and the immediate movement destination. Product Vibe still allowed a pickup to land on a shortest wrap-Manhattan path to food, so following the default food route could collect it by accident. `RunConfig.AvoidFoodGeodesicPowerOffers` therefore defaults false. Default and shared-fixture config hashes remain byte stable because the false value is omitted from canonical config and state JSON. An enabled config writes explicit `avoidFoodGeodesicPowerOffers: true`, receives a distinct config hash under `sha256-canonical-runconfig-v3`, restores the flag, prefers cells off the reserved-destination-to-food geodesic, and falls back to ordinary occupancy when that preferred set is empty.

The `vibe@1` product factory enables the placement rule; Classic remains power-free. This is a native-only post-port extension, so no shared Python trace was regenerated and `vibesnake-core@4` compatibility fixtures continue to use the default-off path. Replay verification charges one extra full-grid occupancy pass when the flag is on because a missed preferred set still performs the fallback scan.

Player consequence: collecting a Vibe power is a detour from the current food geodesic, not a reward for staying on the shortest route.
