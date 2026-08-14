---
type: "Curriculum"
title: "Vibe Snake Signal School and Style Contracts"
description: "Deterministic lessons and self-selected public goals for agent-native play."
tags: [vibesnake, curriculum, styles, evaluation]
generated: { by: process:vibesnake-okf-generator, at: 2026-08-14T02:33:19Z }
verified: { by: process:vibesnake-quality-gate, at: 2026-08-14T02:33:19Z }
stale_after: "2026-11-13"
status: draft
sources:
  - id: agent-experience
    resource: ../../native/src/VibeSnake.AgentPlay/AgentExperience.cs
    title: "Agent experience catalog"
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

A style contract reports progress from public episode metrics. It does not change rules, scoring, spawn order, or replay verification.

# Signal School

* `first-turn`
* `wrap-line`
* `hunger-route`
* `power-route`
* `combo-route`
* `recover-route`

Call `start_lesson` with one published lesson ID to create its canonical open-seed practice session. Every observation returns the instruction and primary-metric progress; accepted moves and bursts return exact progress deltas, and verified finalization returns a replay-hash-bound outcome. Reaching a practice target is not mastery or qualification.
Bounded symbolic bursts reduce routine tool-call cost and stop when the selected lesson target first transitions to reached, while preserving exact replay, metric, and control-division identity. The complete eight-behavior curriculum and withheld-seed qualification remain future work.
