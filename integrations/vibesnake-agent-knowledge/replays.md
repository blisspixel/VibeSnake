---
type: "Replay Contract"
title: "Verified agent replay handoff"
description: "How successfully finalized agent play becomes a verified result and human-watchable replay."
tags: [vibesnake, replay, verification, spectator]
generated: { by: process:vibesnake-okf-generator, at: 2026-08-14T00:23:09Z }
verified: { by: process:vibesnake-quality-gate, at: 2026-08-14T00:23:09Z }
stale_after: "2026-11-13"
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

A successfully finalized completed, capped, or explicitly finished match returns `vibesnake-agent-match-result-v2` with final state hash, replay payload hash, rules and mode identity, outcome, metrics, and verification code. A Signal School result also carries a primary-metric outcome reconstructed from the verified replay and bound to that replay payload hash. Failed-closed finalization returns neither a verified result nor a verified replay.

# Persistence

Replay saving is an explicit call into the bounded application-owned replay store. The agent supplies no path. The saved file is reloaded and verified before the existing replay presentation consumes it.

# Human viewing

The same replay browser and clock-free playback used for human runs can play the agent action trace at a human-selected pace. Playback presentation cannot alter the canonical final hash.
