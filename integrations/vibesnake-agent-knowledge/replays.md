---
type: "Replay Contract"
title: "Verified agent replay handoff"
description: "How successfully finalized agent play becomes a verified result and human-watchable replay."
tags: [vibesnake, replay, verification, spectator]
generated: { by: process:vibesnake-okf-generator, at: 2026-08-15T11:31:47Z }
verified: { by: process:vibesnake-quality-gate, at: 2026-08-15T11:31:47Z }
stale_after: "2026-11-14"
status: draft
sources:
  - id: agent-session
    resource: ../../native/src/VibeSnake.AgentPlay/AgentMatchSession.cs
    title: "Agent match owner"
  - id: replay-store
    resource: ../../native/src/VibeSnake.Persistence/ReplayStore.cs
    title: "Bounded replay store"
  - id: replay-doc
    resource: ../../docs/engineering/REPLAYS.md
    title: "Replay engineering contract"
---
# Verified result

A successfully finalized completed, capped, or explicitly finished match returns `vibesnake-agent-match-result-v5` with final state hash, replay payload hash, rules and mode identity, outcome, metrics, and verification code. A styled result carries exactly two criterion outcomes independently reconstructed from and bound to that verified replay. A Signal School result carries ordered requirement outcomes, a factual review, the replay payload hash, a distinct bounded attempt-evidence hash, and their aggregate evidence hash. Failed-closed finalization returns neither a verified result, a style or lesson outcome, nor a verified replay.

# Persistence

Replay saving is an explicit call into the bounded application-owned replay store. The agent supplies no path. The saved file is reloaded and verified before the existing replay presentation consumes it. Replay schema 1 stores accepted rules steps only; the bounded Signal School attempt witnesses remain ephemeral host result evidence until a future exhibition receipt explicitly persists both evidence domains.

# Human viewing

The same replay browser and clock-free playback used for human runs can play the agent action trace at a human-selected pace. Playback presentation cannot alter the canonical final hash.
