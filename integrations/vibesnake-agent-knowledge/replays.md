---
type: "Replay Contract"
title: "Verified agent replay handoff"
description: "How completed agent play becomes a deterministic receipt and a human-watchable replay."
tags: [vibesnake, replay, verification, spectator]
generated: { by: process:vibesnake-okf-generator, at: 2026-08-13T00:00:00Z }
verified: { by: process:vibesnake-ci, at: 2026-08-13T00:00:00Z }
status: draft
sources:
  - id: agent-session
    resource: ../../native/src/VibeSnake.AgentPlay/AgentMatchSession.cs
    title: "Agent match owner"
    author: process:vibesnake-ci
  - id: replay-store
    resource: ../../native/src/VibeSnake.Persistence/ReplayStore.cs
    title: "Bounded replay store"
    author: process:vibesnake-ci
  - id: replay-doc
    resource: ../../docs/engineering/REPLAYS.md
    title: "Replay engineering contract"
    author: process:vibesnake-ci
---
# Match receipt

A completed, capped, or explicitly finished match returns `vibesnake-agent-match-result-v1` with final state hash, replay payload hash, rules and mode identity, outcome, metrics, and verification code.

# Persistence

Replay saving is an explicit call into the bounded application-owned replay store. The agent supplies no path. The saved file is reloaded and verified before the existing replay presentation consumes it.

# Human viewing

The same replay browser and clock-free playback used for human runs can play the agent action trace at a human-selected pace. Playback presentation cannot alter the canonical final hash.
