---
type: "Curriculum"
title: "Vibe Snake Signal School and Style Contracts"
description: "Deterministic lessons and self-selected public goals for agent-native play."
tags: [vibesnake, curriculum, styles, evaluation]
generated: { by: process:vibesnake-okf-generator, at: 2026-08-15T11:31:47Z }
verified: { by: process:vibesnake-quality-gate, at: 2026-08-15T11:31:47Z }
stale_after: "2026-11-14"
status: draft
sources:
  - id: agent-experience
    resource: ../../native/src/VibeSnake.AgentPlay/AgentExperience.cs
    title: "Agent experience catalog"
  - id: lesson-evidence
    resource: ../../native/src/VibeSnake.AgentPlay/AgentLessonEvidence.cs
    title: "Signal School requirement and evidence evaluator"
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
* `exit-route`
* `power-route`
* `recover-route`
* `combo-route`
* `death-read`

Call `start_lesson` with one of eight published lesson IDs to create its canonical open-seed practice session. Every definition publishes ordered closed requirements under `ordered-replay-attempt-evidence-v2`; observations return live requirement progress and the first unmet requirement, accepted moves and bursts return exact progress deltas, and verified finalization returns a factual outcome. A completed practice is not mastery or qualification.
Accepted-step facts are independently reconstructed from the verified replay. The rejection-aware first-turn lesson additionally uses a maximum-32 canonical attempt-witness sequence: exact idempotent retries do not add evidence, and stale, conflicting, capacity, or wrong-profile requests cannot qualify. The outcome binds the replay payload hash and distinct attempt-evidence hash into one evidence hash. An ordinary saved replay contains only accepted-step history, so it cannot later prove the rejected reversal without a future receipt that carries the attempt evidence.
A verified miss names the first unmet requirement and a closed review code. Failed-closed evidence produces no verified lesson outcome and directs the client to a fresh same-lesson `start_lesson` session without inherited rules state, mutation keys, or practice history. The resource also publishes exact action-call and UTF-8 byte measurements from checked-in canonical routes; these are evidence, not product-wide limits. Byte accounting covers each exact camelCase MCP tool arguments object and snake_case structured response only; it excludes MCP framing, logs, viewer traffic, and token estimates. Bounded straight-line burst fixtures choose an observation-derived bound from 1 through 16, never exceed the paired step route's action-call count, and reduce calls for at least six of eight lessons. Checked-in non-practice seeds are the public qualification-time lesson deck; they are not secret and they are not mastery.
