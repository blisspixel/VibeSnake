---
type: "Game Rules"
title: "Vibe Snake agent rules and observations"
description: "The public, deterministic rules boundary available to an external agent."
tags: [vibesnake, rules, observation, agents]
generated: { by: process:vibesnake-okf-generator, at: 2026-08-15T11:31:47Z }
verified: { by: process:vibesnake-quality-gate, at: 2026-08-15T11:31:47Z }
stale_after: "2026-11-14"
status: draft
sources:
  - id: rules-identity
    resource: ../../native/src/VibeSnake.Rules/RulesetIdentity.cs
    title: "Ruleset identity"
  - id: agent-contracts
    resource: ../../native/src/VibeSnake.AgentPlay/AgentContracts.cs
    title: "Agent contracts"
  - id: agent-identity
    resource: ../../native/src/VibeSnake.AgentPlay/AgentIdentity.cs
    title: "Agent identity catalogs"
  - id: station-identity
    resource: ../../native/src/VibeSnake.Rules/StationIdentityCatalog.cs
    title: "Station identity catalog"
  - id: mode-catalog
    resource: ../../native/src/VibeSnake.Rules/RunModeCatalog.cs
    title: "Official mode catalog"
---
# Authority

The rules authority is `vibesnake-core@4`. The public observation schema is `vibesnake-agent-observation-v5`.
This knowledge bundle is descriptive. The rules assembly, tool schemas, and verified replay remain authoritative.

# Actions

An agent may choose `continue`, `up`, `right`, `down`, or `left`. In `four-direction-step-v1`, one accepted action advances exactly one clock-free rules step. In the separate `four-direction-burst-v1` division, one initial action is followed by at most 15 straight continuations and stops under fixed `decision-event-stop-v1` public events, a selected lesson's transition to all requirements reached, or a closed terminal, cap, replay-failure, or requested-bound reason.
Each mutation is bound to the observed tick, state hash, and one shared idempotency-key namespace capped at 4,096 unique records per match. Exact retries return cached typed responses; known keys are never evicted, and changed, cross-operation, or post-cap unseen keys advance no additional state.

# Public observation

The observation includes the catalog-validated public Agent Passport v4, board, ordered body, direction queue, food, visible powers and obstacles, score, combo, hunger, active effects, adaptive policy, previous public events, episode metrics, optional two-criterion live style progress, and optional ordered Signal School requirement progress.
Passport identity is caller-declared and ephemeral. Avatar, accent, and station IDs must resolve through the host's closed identity resource; they affect presentation only and remain independent of human progression and cosmetics.
It excludes random state, future outcomes, controller internals, profiles, progression, paths, prompts, credentials, diagnostics, and hidden reasoning.

# Seed divisions

Open matches expose the gameplay seed. Blind matches withhold it until the verified result. Classic and Vibe results remain separate identities.
