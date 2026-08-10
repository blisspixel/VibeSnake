# AI Players

## Overview

AI spectator mode uses the same snake movement interface as a human run. The checkout includes ten built-in compatibility personalities and one Python-oracle custom JSON example.

The ship-product policy lives in pure C# under `native/src/VibeSnake.Rules/AiPersonality*.cs`. The frozen Python behavior oracle remains in [player.py](../../src/vibesnake/ai/player.py), and its cosmetic mappings remain in [customization.py](../../src/vibesnake/core/customization.py). The native catalog preserves the ten compatibility IDs, original traits, descriptions, and colors while V080-03 performs the measured truthfulness migration.

## Personality fields

| Field | Meaning |
| --- | --- |
| `name` | Channel display name |
| `description` | Short player-facing style summary |
| `aggression` | Weight placed on moving toward a target |
| `risk_tolerance` | Willingness to accept nearby body danger |
| `patience` | Preference for maintaining an existing direction |
| `greed` | Weight placed on food progress and against power detours |
| `chaos` | Chance of choosing a random safe direction |
| `power_up_priority` | Chance of targeting a power-up instead of food |
| `color` | RGB display color |

Native built-ins use exact integers from 0 through 100 to avoid platform floating-point drift. The frozen Python custom format uses 0.0 through 1.0 and is not the shipping validation boundary. Strict native custom-file validation remains V080-03 work.

## Add a custom channel

Create a JSON file under `assets/ai/custom/`:

```json
{
  "name": "Route Planner",
  "description": "Prefers safe lines and deliberate turns.",
  "aggression": 0.45,
  "risk_tolerance": 0.2,
  "patience": 0.85,
  "greed": 0.35,
  "chaos": 0.02,
  "power_up_priority": 0.4,
  "color": [80, 180, 255]
}
```

The filename stem becomes the personality key. Files under `assets/ai/examples/` are examples and are not loaded automatically.

The asset-local [AI README](../../assets/ai/README.md) provides a short copy-ready reference.

## Decision process

At each native decision interval, the AI:

1. Draws a fixed random budget so counterfactual comparisons consume identical samples.
2. Chooses visible power or food using power priority, aggression, and greed.
3. Removes direct reversal and immediate body or detached-obstacle collisions.
4. Scores candidates using target, food, and power progress; nearby danger; onward choices; patience; risk tolerance; aggression; greed; and power priority.
5. Applies bounded `chaos`, queues one legal direction, and exposes target, distance, hazard, onward-choice, and chaos diagnostics.

This is a reactive policy. It does not search full future paths, model starvation routes, or prove that a region remains escapable.

## Test and research use

Personality labels remain hypotheses until measured. `native-ai-league-v1` now provides the repeatable comparison foundation:

- All ten built-ins run on the same twelve reviewed fixed, exploratory, and previous-failure seeds.
- Mirrored runs compare 98,984 deterministic rules steps and retain decision-trace SHA-256 plus final state hashes.
- The 120-run report groups score, survival, food efficiency, power preference, risk exposure, dead-end rate, and route efficiency by personality and rules version.
- Sixty opposite-extreme trait interventions consume identical random samples on the same observed states. Every trait/personality pair now changes at least 1 percent of observed decisions under the V080-03 controller.
- Every result uses the closed `ai`/`ai-simulation` noncompetitive score context. The harness constructs no score persistence store.

The current observed ranges demonstrate policy separation: median survival spans 647 to 900 capped steps, power preference spans 2,728 to 8,488 basis points, risk exposure spans 6 to 2,641 basis points, and route efficiency spans 6,077 to 9,579 basis points. `ai-personality-qualification-v1` binds one deliberately broad measured behavior range to each player-facing identity. These are AI regression claims, not evidence that the personalities are entertaining or human balance targets.

AI runs do not update the human profile, achievements, apples, wraps, or total games.

The native 1.0 spectator foundation now supplies seed and rivalry choice, pause, speed and step controls, concise target/risk/resource explanations, local league records, fifty handcrafted event lines, and an immediate human challenge using the exact seed and rules configuration. The two AI lanes share gameplay seed and rules but retain separate deterministic controller streams. View switching, commentary fallback, and unavailable-audio recovery are presentation-only. See [FUN_DESIGN.md](FUN_DESIGN.md#lets-play-as-a-game-not-a-screensaver) and the 0.8 work in [ROADMAP.md](../../ROADMAP.md).

The final broadcast identities and station affinities are defined in the [world and broadcast bible](WORLD_BIBLE.md#rival-signal-serpents). Legacy personality keys remain compatibility identifiers while player-facing names and copy are migrated.

## Automatic spectator qualification

Before a channel is presented as a finished personality, automation must:

- Run every rival on the same fixed, exploratory, and previous-failure seed corpora.
- Prove identical rules, content visibility, random-stream ownership, and score formulas between AI and human runs.
- Detect stalls, repeated loops, unreachable-target fixation, illegal reversal, starvation blindness, restart leaks, and hidden information use.
- Measure survival, score, route efficiency, risk exposure, wrap use, power detours, synergy choices, recovery use, and target changes.
- Demonstrate that every advertised trait changes at least one declared behavior distribution without making the channel nonfunctional.
- Cover every commentary trigger, priority, cooldown, caption, no-repeat bag, interruption rule, and missing-audio fallback.
- Generate verified replays, concise decision explanations, local standings, and an immediate equal-rules seed challenge.
- Stress pause, speed change, step, overlay toggle, replay, return, and repeated channel switching without leaked state or resource growth.

Passing this gate permits the label `automated-qualified, experience-unverified`. It does not prove that watching is entertaining. Human absence never stops additional automated refinement or implementation, but channel appeal and final experience claims remain pending.

`spectator-experience-qualification-v1` now passes the implemented automatic foundation: ten final rivals, measured policy bindings, twelve reviewed seed choices, deterministic equal-rules results, raw keyboard and controller flows, repeated view switching, bounded stalled-target recovery, invalid-channel fallback, missing-commentary and unavailable-audio recovery, typed overlays, exact AI-state-free human challenges, local standings/rivalries/milestones, atomic persistence, and progression isolation. Physical controller-family review on all three desktop platforms and human comprehension, pacing, editorial, and entertainment review remain pending.

## Security and robustness

Custom JSON is local data, not executable code. Even so, a release should reject unexpected keys, wrong types, non-finite numbers, invalid RGB values, and out-of-range traits with a clear filename-specific error.
