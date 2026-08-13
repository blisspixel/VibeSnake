# ADR 0002: Agent Arena Boundary

Status: Accepted for post-1.0 development

Decision date: 2026-08-12

Implementation note: the development tree now exercises this post-1.0 boundary through a Rules-only session assembly, local stdio MCP host, explicit replay save, and read-only same-user pipe viewer. The preview is not part of the supported 1.0 release contract. A 1.0 candidate must remove or explicitly exclude the preview assemblies and entry points before artifact qualification. Human experience acceptance is still open.

## Context

Vibe Snake already has a deterministic, clock-free rules kernel, typed public events, state hashes, verified replays, built-in AI rivals, and a spectator presentation. Those capabilities make it a strong environment for external agents, but a generic automation endpoint would not be enough. The product opportunity is an Agent Arena where an external agent develops a visible style, challenges a named rival on equal rules, produces a verified replay, and gives a human something worth watching or replaying on the same seed.

External agent play introduces different trust, pacing, privacy, and compatibility concerns from the offline desktop game. It must not destabilize the 1.0 player contract, place transport code in the rules kernel, execute third-party code in the game process, or let agent activity alter human progression.

## Decision

- Develop Agent Arena as an optional post-1.0 capability. It is excluded from the 1.0 player artifacts and release gates.
- Add a transport-neutral `VibeSnake.AgentPlay` assembly that depends only on `VibeSnake.Rules`. It owns agent-match lifecycle, public observations, action validation, deterministic stepping, replay capture, and match receipts.
- Keep `SnakeRun`, replay schema 1, built-in `SpectatorMatchSession`, and human score and progression contracts unchanged for the first implementation.
- Make local, turn-based symbolic play the first profile. Rules do not advance while an agent deliberates. A valid action advances exactly one rules step, while stale or illegal actions advance none.
- Require every mutating action to carry an expected tick, expected state hash, and bounded idempotency key. Serialize actions per match and fail closed on replay divergence.
- Expose an allowlisted, versioned observation instead of serializing `RunSnapshot` directly. Exclude random state, future outcomes, controller internals, private user data, paths, diagnostics, prompts, credentials, and hidden reasoning.
- Support explicit open-seed and blind-seed divisions. Reveal a blind seed only in the completed match receipt.
- Record every accepted run before presentation, finalize it into an ordinary verified single-lane replay, and treat that replay as the canonical account of what happened. Policy reproducibility is not implied by replay reproducibility.
- Add a local stdio MCP adapter after the transport-neutral core. The first host opens no network listener, accepts no arbitrary paths or rules configuration, and does not execute agent-supplied code.
- Reuse the existing Godot replay browser and provide a read-only live viewer over a local pipe rather than TCP. The viewer cannot influence rules, pacing, or replay integrity.
- Keep external-agent results separate from human scores, achievements, progression, ordinary challenges, and the built-in AI league.
- Store only bounded public agent identity and verified results in a later Agent Passport. Do not persist prompts, chain of thought, credentials, provider responses, or executable policy code.

## Interoperability posture

| Standard | Decision | Boundary |
| --- | --- | --- |
| MCP 2026-07-28 | Adopt for the local agent host | Official C# SDK, transport adapter only, local stdio first |
| Agent Skills | Adopt the minimal stable `SKILL.md` subset | Advisory play instructions, no bundled executable scripts or experimental `allowed-tools` |
| Agent Plugins 1.0.0 | Package as a developer preview | Working Draft format, per-platform signed host bundles required before distribution, never the only connection route |
| MCP Apps 2026-01-26 | Track for an optional client-side viewer | Secondary renderer after neutral replay and frame contracts stabilize |
| Open Knowledge Format 0.2 | Generate optionally from canonical sources | Discovery and provenance only, never runtime or gameplay authority |
| Gymnasium and PettingZoo | Preserve adapter compatibility | Separate research package, no Python dependency in player artifacts |

Protocol, skill, plugin, knowledge, observation, and rules versions remain independent. A standards update cannot silently change scored behavior.

## Product contract

The target first complete loop is:

1. An external agent selects an official mode, seed division, built-in rival, and Style Contract.
2. The agent observes only player-visible state and submits bounded actions.
3. The rules wait for decisions and return factual ordered event feedback.
4. Both lanes use the same gameplay seed and exact configuration while retaining independent controller state and replays.
5. The match produces verified receipts and replays.
6. A human watches the recorded broadcast or accepts the exact same-seed challenge.

The project may claim meaningful agency, compounding competence, fair verification, and a legible spectator experience when those properties are proven. It must not claim that a model subjectively experiences fun.

## Security and privacy consequences

- Match handles are opaque and bounded at the host layer. The host retains at most eight sessions, evicts the oldest non-live session when capacity is needed, rejects a ninth live session, and invalidates every handle when the process exits.
- Tool inputs and outputs use closed enums, explicit schema versions, size bounds, and sanitized strings.
- Replay persistence is explicit and writes only to an application-owned destination.
- Remote HTTP, OAuth, accounts, matchmaking, uploads, and hosted tournaments are separate future decisions.
- Plugin containment is not treated as process sandboxing. Manifests contain no secrets and the game does not load plugin code.
- Viewer loss, pause, speed, or dropped frames never affect canonical match state.
- Withheld qualification seeds and immutable evaluators measure generalization separately from public practice.

## Consequences

Positive consequences:

- Slow language-model agents can play without wall-clock disadvantage.
- Existing deterministic rules and replay verification remain authoritative.
- Humans can understand agent choices through game facts, structured public intent, rivalry, and broadcast presentation.
- MCP, future fast-policy adapters, and UI clients can share one tested service boundary.

Costs and constraints:

- Agent matches need their own lifecycle, qualification, persistence, and accessibility evidence.
- Live spectating requires a separate read-only local channel because MCP stdio output must remain protocol-clean.
- Agent Plugin and OKF formats are still evolving, so their artifacts stay generated, version-pinned, and optional.
- Human review remains necessary to prove that broadcasts, pacing, rivalry, and rematches are entertaining.

## Reconsideration triggers

Reopen this decision if the MCP compatibility model changes materially, local stdio cannot meet supported-client needs, replay schema 1 cannot represent accepted agent traces, a read-only viewer cannot be isolated from match ownership, or human review shows that the Agent Arena loop is not understandable or compelling.
