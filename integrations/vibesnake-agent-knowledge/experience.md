---
type: "Curriculum"
title: "Vibe Snake Signal School and Style Contracts"
description: "Deterministic lessons and self-selected public goals for agent-native play."
tags: [vibesnake, curriculum, styles, evaluation]
generated: { by: process:vibesnake-okf-generator, at: 2026-08-14T06:53:58Z }
verified: { by: process:vibesnake-quality-gate, at: 2026-08-14T06:53:58Z }
stale_after: "2026-11-13"
status: draft
sources:
  - id: agent-experience
    resource: ../../native/src/VibeSnake.AgentPlay/AgentExperience.cs
    title: "Agent experience catalog"
  - id: style-evidence
    resource: ../../native/src/VibeSnake.AgentPlay/AgentStyleEvidence.cs
    title: "Replay-derived style evidence evaluator"
  - id: experience-design
    resource: ../../docs/design/AGENT_ARENA.md
    title: "Agent Arena experience contract"
---
# Style Contracts

* `stillwater`
* `crownchaser`
* `edge-prophet`
* `mutagenist`
* `redline`

Each style publishes exactly two ordered, factual criteria under `replay-composite-core4-v1`. Stillwater combines rules-advanced-step survival with structural-open-exit rate. Crownchaser combines peak combo with uninterrupted food continuity through the first combo of four. Edge Prophet combines rewarded body-proximity near misses with a same-step wrap fact under the pinned `vibesnake-core@4` evaluator. Mutagenist combines distinct activated power kinds with concurrent active power kinds. Redline combines food count with safe progress toward the exact pre-step visible food.
Live style values are rules-advanced-step observations and may rise or fall. Rate criteria expose integer numerators and denominators and use floor basis points. Successful finalization independently reconstructs the same facts from the verified replay, requires agreement with live evidence, and binds the terminal style outcome to the replay payload hash. These facts do not prove intent, planning, mastery, personality, or spectator appeal. A style never changes rules, scoring, spawn order, or replay verification.

# Signal School

* `first-turn`
* `wrap-line`
* `hunger-route`
* `power-route`
* `combo-route`
* `recover-route`

Call `start_lesson` with one published lesson ID to create its canonical open-seed practice session. Every observation returns the instruction and primary-metric progress; accepted moves and bursts return exact progress deltas, and verified finalization returns a replay-hash-bound outcome. Reaching a practice target is not mastery or qualification.
Bounded symbolic bursts reduce routine tool-call cost and stop when the selected lesson target first transitions to reached, while preserving exact replay, metric, and control-division identity. The complete eight-behavior curriculum and qualification-time seed decks remain future work.
