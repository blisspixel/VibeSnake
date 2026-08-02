# AI Players

## Overview

AI spectator mode uses the same snake movement interface as a human run. The checkout includes ten built-in personalities and one custom JSON personality, producing eleven visible channels at startup when all files load.

The implementation lives in [player.py](../../src/vibesnake/ai/player.py). Cosmetic themes are mapped in [customization.py](../../src/vibesnake/core/customization.py).

## Personality fields

| Field | Meaning |
| --- | --- |
| `name` | Channel display name |
| `description` | Short player-facing style summary |
| `aggression` | Weight placed on moving toward a target |
| `risk_tolerance` | Willingness to accept nearby body danger |
| `patience` | Preference for maintaining an existing direction |
| `greed` | Reserved style trait; not all decisions currently consume it |
| `chaos` | Chance of choosing a random safe direction |
| `power_up_priority` | Chance of targeting a power-up instead of food |
| `color` | RGB display color |

Behavioral values are intended to range from 0.0 through 1.0, but custom files are not currently schema-validated or clamped.

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

At each decision interval, the AI:

1. Optionally chooses a random safe direction based on `chaos`.
2. Chooses the nearest power-up or current food as a target.
3. Removes direct reversal and immediate body collisions from valid directions.
4. Scores candidates by target distance, local body danger, risk tolerance, aggression, and direction continuity.
5. Queues the highest-scoring direction.

This is a reactive policy. It does not search full future paths, model starvation routes, or prove that a region remains escapable.

## Test and research use

Personality labels are hypotheses until measured. For repeatable comparisons, the project still needs:

- Seeded random state.
- Identical food and power-up sequences.
- Versioned rules and configuration.
- Batch tournament execution.
- Replay or decision logging.
- Metrics for score, survival, risk, target choice, and route efficiency.

AI runs do not update the human profile, achievements, apples, wraps, or total games.

The 1.0 spectator design also requires seed and rivalry choice, pause and speed controls, a concise target and risk explanation, local league records, handcrafted event commentary, and an immediate way for a human to challenge the same seed. See [FUN_DESIGN.md](FUN_DESIGN.md#lets-play-as-a-game-not-a-screensaver) and the 0.8 work in [ROADMAP.md](../../ROADMAP.md).

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

## Security and robustness

Custom JSON is local data, not executable code. Even so, a release should reject unexpected keys, wrong types, non-finite numbers, invalid RGB values, and out-of-range traits with a clear filename-specific error.
