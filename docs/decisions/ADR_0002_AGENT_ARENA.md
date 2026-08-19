# ADR 0002: Agent Arena Boundary

Status: Accepted for post-1.0 development

Decision date: 2026-08-12

Implementation note: the development tree now exercises this post-1.0 boundary through a Rules-only session assembly, local stdio MCP host and Agent Plugin at version 0.17.0, exactly eight canonical Signal School practices, explicit replay save, and read-only same-user pipe viewer. The ordered lessons are `first-turn`, `wrap-line`, `hunger-route`, `exit-route`, `power-route`, `recover-route`, `combo-route`, and `death-read`, with exactly two ordered factual requirements each. Replay-trace facts are independently verified at finalization. `first-turn` additionally uses a separate maximum-32 chain containing only first-seen valid-state opposite-reversal rejection witnesses; exact retries do not duplicate it. Successful lesson outcomes bind replay and attempt evidence hashes, use lifecycle `completed`, omit retry guidance, and live completion recommends `finish_match`; partial lessons and ordinary nonterminal early finishes use `aborted`, while only incomplete or failed-closed practice offers a fresh `start_lesson` descriptor for the same lesson and action profile. The capped `death-read` instruction names its deterministic self-collision route instead of suggesting that starvation can complete inside the cap. A call-tool request filter identifies missing, unexpected, and wrong-typed argument names for all twelve tools before method binding without changing match state, so a numeric `gameplaySeed` is named instead of collapsing into a generic bind error. Rules resource v15 publishes the `lifecycle_semantics`, `argument_binding`, and `survival_state` vocabulary that separates the agent session lifecycle from the snake's run status, plus the `receipt` and `archive` blocks. The AA-06 canonical `vibesnake-agent-exhibition-receipt-v2` hash-links both verified lane replays, a closed division identity, the passport, the replay-derived style and lesson evidence, and the ordered accepted presentation events into an instance `receipt_hash`, and publishes a separate rematch-stable `route_identity_hash`. Presentation display time stays outside both. `archive_exhibition` keeps one verified exhibition in a bounded 32-entry, four-megabyte local archive beside the saved replay file name of every lane it contains, `list_exhibitions` reads that archive without writing and can narrow to one route identity, and `forget_exhibition` removes one exhibition or clears the store. The write is atomic, the oldest exhibition is evicted first at capacity, a different exhibition is never written under an existing receipt hash, and a document that fails to recompute its own canonical hashes is quarantined rather than repaired. The archive is deliberately outside the supported player Persistence assembly and holds no human score, progression, achievement, cosmetic, or profile data. The watch overlay prints the seed, a state-hash prefix, and the verified replay-hash prefix, and frame v9 adds `vibesnake-agent-survival-state-v1` so a spectator can read observed structural exits, the closed pressure tier they cross, and held recovery resources without being given a route. Passport v4 and `symbolic-step-v4` restrict avatar, accent, and station presentation to closed catalogs while keeping caller-declared identity ephemeral and independent of human progression. Observation, result, and host response DTO v5 plus viewer frame v9 carry ordered lesson truth and reject legacy, mixed, unknown-catalog, malformed, identity-drifting, lesson-contradicting, or style-contradicting streams. Every Style Contract still exposes exactly two ordered factual criteria reconstructed from rules-advanced steps and independently bound to the verified replay at successful finalization. Style v3 calls its boolean and aggregate fields threshold crossings, which are optional measurements rather than pass/fail match grades. The packaged-host transcript opens the viewer and completes a burst, the Godot terminal-burst smoke proves the reduced-motion snap path under muted, high-contrast, and 150-percent-text settings, and the localization gate measures composed pseudo-localized overlay geometry. These contracts expose only closed rules and attempt facts. They do not prove intent, planning, mastery, personality, or spectator appeal. The preview is not part of the supported 1.0 release contract. `ExportRelease` omits its project references and compiles out the watch route, while schema-3 inspection rejects preview payloads and compiled command-line markers. The exact three-platform 1.0 candidate must retain passing evidence before promotion. Human experience acceptance remains open.

## Context

Vibe Snake already has a deterministic, clock-free rules kernel, typed public events, state hashes, verified replays, built-in AI rivals, and a spectator presentation. Those capabilities make it a strong environment for external agents, but a generic automation endpoint would not be enough. The product opportunity is an Agent Arena where an external agent develops a visible style, challenges a named rival on equal rules, produces a verified replay, and gives a human something worth watching or replaying on the same seed.

External agent play introduces different trust, pacing, privacy, and compatibility concerns from the offline desktop game. It must not destabilize the 1.0 player contract, place transport code in the rules kernel, execute third-party code in the game process, or let agent activity alter human progression.

## Decision

- Develop Agent Arena as an optional post-1.0 capability. It is excluded from the 1.0 player artifacts and release gates.
- Add a transport-neutral `VibeSnake.AgentPlay` assembly that depends only on `VibeSnake.Rules`. It owns agent-match lifecycle, closed public logical-state observations, action validation, deterministic stepping, replay capture, and verified lane results.
- Keep `SnakeRun`, replay schema 1, built-in `SpectatorMatchSession`, and human score and progression contracts unchanged for the first implementation.
- Make local, turn-based symbolic play the first profile. Rules do not advance while an agent deliberates. A valid action advances exactly one rules step, while stale or illegal actions advance none.
- Add `four-direction-burst-v1` as a separate control division. One request may apply one initial action and continue for at most 16 steps under the fixed `decision-event-stop-v1` public-event policy. It cannot accept action arrays, custom predicates, code, callbacks, or rewards, and it cannot share qualification identity silently with `four-direction-step-v1`.
- Expose canonical Signal School practices through a dedicated `start_lesson` adapter. A selected lesson owns its open seed, mode, cap, instruction, and exactly two ordered factual requirements. Bursts stop on the first all-requirements transition. Only successful replay finalization produces a lesson outcome, independently verifies replay facts and bounded attempt evidence, and binds both evidence hashes. Failed-closed practice produces no outcome and can only be retried through a fresh canonical session.
- Require every mutating action to carry an expected tick, expected state hash, and bounded idempotency key. Serialize actions per match and fail closed on replay divergence.
- Use one idempotency-key namespace across step and burst mutations. Exact retries return the cached typed response; changed payloads and cross-operation key reuse advance no additional rules state.
- Expose an allowlisted, versioned observation instead of serializing `RunSnapshot` directly. Exclude random state, future outcomes, controller internals, private user data, paths, diagnostics, prompts, credentials, and hidden reasoning.
- Support explicit open-seed and blind-seed divisions. Reveal a blind seed only in the completed verified lane result.
- Record every accepted run before presentation, finalize it into an ordinary verified single-lane replay, and treat that replay as the canonical account of what happened. Policy reproducibility is not implied by replay reproducibility.
- Add a local stdio MCP adapter after the transport-neutral core. The first host opens no network listener, accepts no arbitrary paths or rules configuration, and does not execute agent-supplied code.
- Reuse the existing Godot replay browser and provide a read-only live viewer over a local pipe rather than TCP. Version each frame with a closed operation kind, actual steps advanced, burst stop reason and event, and monotonic sequence. A latest-frame consumer reports sequence gaps as coalesced earlier updates. The viewer cannot influence rules, pacing, or replay integrity.
- Keep external-agent results separate from human scores, achievements, progression, ordinary challenges, and the built-in AI league.
- Keep the match-start Agent Passport a caller-declared claim. Persist a bounded public record and verified-result history only through the AA-07 store, assembled from receipts that recompute their own hashes. Do not persist prompts, chain of thought, credentials, provider responses, display names, or executable policy code.
- When exhibition receipts become persistent, keep transport-neutral receipt creation and verification in `VibeSnake.AgentPlay`. Add a separate optional `VibeSnake.AgentPersistence` adapter that may depend on AgentPlay, Persistence, and Rules; do not make the supported `VibeSnake.Persistence` assembly depend on post-1.0 preview code. Store both verified lanes and their canonical receipt transactionally as one bounded bundle.

## Interoperability posture

| Standard | Decision | Boundary |
| --- | --- | --- |
| MCP 2026-07-28 | Adopt for the local agent host | Official C# SDK 2.2.0, stateless requests with per-request protocol metadata and optional `server/discover`, no initialize handshake, no downlevel fallback, transport adapter only, local stdio first |
| Agent Skills | Adopt the minimal non-experimental `SKILL.md` subset | `name`, `description`, and Markdown body only; no bundled executable scripts or experimental `allowed-tools` |
| Agent Plugins 1.0.0 | Package as a developer preview | The normative versioned repository labels it Published while the public website still says Working Draft; per-platform signed host bundles are required before distribution, and the plugin is never the only connection route |
| MCP Apps 2026-01-26 | Track for an optional client-side viewer | Secondary renderer after neutral replay and frame contracts stabilize |
| Open Knowledge Format 0.2 | Generate a deterministic optional bundle from canonical sources | Discovery and provenance only, never runtime or gameplay authority |
| Gymnasium and PettingZoo | Preserve adapter compatibility | Separate research package, no Python dependency in player artifacts |

Protocol, skill, plugin, knowledge, observation, and rules versions remain independent. The baseline was last reviewed on 2026-08-13 and is reviewed at least quarterly or whenever an upstream specification, schema, SDK release, or security advisory changes. Every claimed client requires an independent cross-client smoke. Host versions change with MCP behavior or public tool and resource contracts; plugin versions change with packaged discovery, launch, or skill behavior. A standards update cannot silently change scored behavior.

## Product contract

The target first complete loop is:

1. An external agent selects an official mode, seed division, built-in rival, and Style Contract.
2. The agent observes only the closed public logical-state division and submits bounded actions.
3. The rules wait for decisions and return factual ordered event feedback.
4. Both lanes use the same gameplay seed and exact configuration while retaining independent controller state and replays.
5. The match produces verified lane results and replays. A later bounded exhibition receipt may hash-link both lanes and presentation events.
6. A human watches the recorded broadcast or accepts the exact same-seed challenge.

The project may claim meaningful agency, compounding competence, fair verification, and a legible spectator experience when those properties are proven. It must not claim that a model subjectively experiences fun.

## Security and privacy consequences

- Match handles are opaque bearer capabilities bounded at the host layer; stdio adds no separate client authentication. The host retains at most eight sessions and evicts the oldest non-live session first when capacity is needed. If all eight are live, only a match with no valid handle-bearing host operation for at least 30 minutes may be reclaimed, without a score, result, ranking, or replay. Viewer activity never refreshes or controls this lease. A ninth match is rejected when no lease expired, and every handle is invalidated when the process exits. Replacement validation and construction complete before eviction.
- Step and burst share one ledger capped at 4,096 unique mutation records per match. Known records are never evicted, preserving exact retries and preventing a formerly accepted key from advancing twice; unseen keys fail closed after exhaustion.
- Tool inputs and outputs use closed enums, explicit schema versions, size bounds, and sanitized strings.
- Replay persistence is explicit and writes only to an application-owned destination.
- Remote HTTP, OAuth, accounts, matchmaking, uploads, and hosted tournaments are separate future decisions.
- Plugin containment is not treated as process sandboxing. Manifests contain no secrets and the game does not load plugin code.
- Viewer loss, pause, speed, or dropped frames never affect canonical match state.
- Qualification-time seed decks and immutable evaluators measure generalization separately from public practice and checked-in non-practice fixtures.

## Consequences

Positive consequences:

- Slow language-model agents can play without wall-clock disadvantage.
- Existing deterministic rules and replay verification remain authoritative.
- Humans can understand agent choices through game facts, structured public intent, rivalry, and broadcast presentation.
- MCP, future fast-policy adapters, and UI clients can share one tested service boundary.

Costs and constraints:

- Agent matches need their own lifecycle, qualification, persistence, and accessibility evidence.
- Live spectating requires a separate read-only local channel because MCP stdio output must remain protocol-clean.
- Agent Plugins 1.0.0 has conflicting status surfaces: its normative versioned repository says Published while the public website says Working Draft. OKF remains at 0.2. Both integrations therefore stay generated, version-pinned, optional, and outside gameplay authority.
- Human review remains necessary to prove that broadcasts, pacing, rivalry, and rematches are entertaining.

## Reconsideration triggers

Reopen this decision if the MCP compatibility model changes materially, local stdio cannot meet supported-client needs, replay schema 1 cannot represent accepted agent traces, a read-only viewer cannot be isolated from match ownership, or human review shows that the Agent Arena loop is not understandable or compelling.
