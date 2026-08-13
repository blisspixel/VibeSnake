# Game Design

## High concept

Vibe Snake starts with the clarity of classic Snake, then layers temporal pressure, risk-reward scoring, expressive presentation, and spectator AI around the same one-input-at-a-time movement language.

The design succeeds only if the first layer remains legible. Music, particles, progression, and novelty should sharpen decisions, not conceal them.

The working player-experience thesis is: plan the route, build the vibe, flirt with disaster, and recover with style. [FUN_DESIGN.md](FUN_DESIGN.md) defines how that thesis governs escalation, powers, progression, customization, radio, spectator play, lore, and playtesting.

## Experience pillars

### Flow under pressure

The player balances spatial safety against a 30-second starvation clock. Edge wrapping creates escape routes, while the growing body gradually converts open space into planning pressure.

### Every food matters

Food grows the snake, resets starvation, advances combos, earns points, and feeds progression. Smooth multipliers prevent arbitrary score jumps and make each successful link visible.

### Risk should be readable

Power-up detours, near misses, fast routes, and long-body bonuses should offer a clear reason to take danger. Rewards must be announced through position, color, sound, and score feedback.

### Identity without mechanical ambiguity

Cosmetics, radio stations, environment palettes, and AI personas create expression. Competitive rules should stay obvious and should not change silently with a cosmetic or station choice.

## Core run loop

```text
Read board -> choose route -> move -> collect or evade -> receive feedback
     ^                                                    |
     |                                                    v
Adapt to growth, starvation, combo, and temporary effects
```

The loop operates on two time scales:

- Immediate: the next safe turn and the next food route.
- Run-level: body growth, combo pressure, starvation, score, and power-up state.

Progression outside the run should invite another attempt without making a fresh profile mechanically uncompetitive.

## Systems

### Movement and board

- Four cardinal directions on a discrete grid.
- Direct reversal is rejected.
- Valid rapid turns are buffered.
- All screen edges wrap.
- The snake's body is the primary collision threat.

### Time pressure

- Starvation warning begins at 20 seconds.
- Starvation occurs at 30 seconds.
- Food resets the timer.
- No human score, survival, food-rate, death-rate, combo, or power target is accepted yet. [Observed AI baselines](BALANCE_BASELINES.md) remain descriptive, and privacy-bounded [local playtest summaries](PLAYTEST_SUMMARIES.md) require structured human review before they can support a target.

### Scoring and mastery

- Food begins at 10 points before bonuses.
- Combo links expire after three seconds.
- Multipliers interpolate between 1x, 2x, 3x, 5x, and 10x milestones.
- Speed, length, near-miss, clutch, and style systems add optional mastery rewards.
- The score model needs seeded balance benchmarks before competitive claims are appropriate.

### Dynamic difficulty

Vibe mode uses the deterministic, versioned, and disclosed `vibe-bounded-hunger-v1` policy described below. Classic disables adaptation, and a player can opt out in Vibe through a separate unranked score category. The removed Python scaffolding remains historical only: it calculated an unvalidated aggregate but never applied its cadence or spawn-weight outputs. Any future policy change must remain inside the deterministic rules engine, be independently versioned and disclosed, preserve an opt-out where applicable, and use an appropriate separate score category. Fixed-seed sweeps must prove bounds and stability before structured observation evaluates whether the policy is legible or worthwhile.

### Progression

Cosmetic unlocks and achievements provide self-chosen mastery, discovery, and identity goals. They should celebrate skill and experimentation without increasing survival power, creating daily obligation, or relying on empty repetition. See [PROGRESSION.md](PROGRESSION.md).

### Spectator AI

AI channels provide entertainment, simulation, strategy learning, and offline rivalry. AI personalities alter target preference, safety tolerance, patience, and randomness. The native automated foundation includes a same-seed league, deterministic replay capture, seed choice, speed and explanation controls, measured behavior claims, local rival records, and immediate equal-rules human challenges. These contracts support regression and comparison, not a claim that the channels are entertaining or a general-purpose external-agent benchmark. Human pacing, comprehension, editorial, and entertainment review remain open.

## Content principles

- Add depth before breadth. A fully legible power-up is more valuable than several disconnected ones.
- Give each event one dominant feedback cue, then support it with secondary cues.
- Preserve control response during visual effects.
- Keep jokes and lore out of critical instructions.
- Make failure attributable: the player should know whether collision, starvation, or a temporary rule ended the run.
- Offer accessibility controls before increasing effect intensity.
- Make combo milestones the single presentation escalation language instead of letting every subsystem compete for intensity.
- Add depth and authored interactions to the existing nine powers before considering another type.
- Deliver lore through optional broadcast, character, collection, and codex layers, never through critical instructions.

## Modes

The native product has two human modes over one versioned pure C# rules engine:

1. Classic: movement, wrapping, food, growth, fixed cadence, self-collision, pause, and fixed 10-point food scoring. It has no starvation, combo or bonus scoring, near misses, powers, progression candidates, or DDA.
2. Vibe: starvation, combos, near misses, power-ups, progression, full feedback, and disclosed adaptive policy `vibe-bounded-hunger-v1`.

Classic scores use `classic-standard-v1`. Vibe DDA-on scores use `vibe-standard-v1-dda-on`; the player can disable adaptation in Gameplay settings for an unranked Vibe run, which uses `vibe-standard-v1-dda-off`. These categories are never compared.

### Vibe adaptive policy

The policy is deterministic and stateless. Its only inputs are the effective run config, rules tick, combo, and remaining hunger. It never reads profile history, wall-clock time, presentation state, input device, or hidden telemetry. Hunger drain is its only output:

- Support: while hunger is in the warning band and combo is 0 through 2, hunger drains by one on every second rules step.
- Standard: hunger drains by one on every rules step.
- Pressure: at combo 10 or higher outside the warning band, hunger drains by one normally and by two on every fourth rules step.

The exact drain bound is zero through two ticks per rules step. The HUD shows `DDA [+] SUPPORT`, `DDA [=] STANDARD`, `DDA [!] PRESSURE`, or `DDA [=] OFF`; score metadata records mode, difficulty policy, score category, enabled state, policy, and captured adaptive state. Config hashes and replay restoration bind the policy. Current native run-local achievements explicitly require Vibe; V070-08 will audit every achievement against both modes rather than silently changing eligibility.

A daily challenge should wait until seeded randomness and versioned rule sets are implemented.

## Resolved power-up decisions

- Shield absorbs one collision and otherwise expires after five seconds.
- Gluttony preserves every food reward except body growth.
- Phase Shift crosses both the snake body and detached-segment obstacles.
- Last Stand covers collision and starvation, halves length, and grants a three-second recovery window.

## Open design questions

- Which rights-cleared, curated subset of the local 338,592,122-byte review library should form the small core pack, with any larger approved catalog delivered only through optional packs?
- Which run metrics are useful enough to collect locally, and what consent model would apply to any future upload?

These questions are tracked as implementation work in the [roadmap](../../ROADMAP.md).
