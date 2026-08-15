# Agent Play Integration

[Engineering index](README.md) | [Agent Arena design](../design/AGENT_ARENA.md) | [ADR 0002](../decisions/ADR_0002_AGENT_ARENA.md)

Status: post-1.0 developer preview. The development tree contains this code, but it is not part of the supported 1.0 release contract or release gates. `ExportRelease` omits AgentPlay and AgentViewer references and compiles out the watch route; schema-3 artifact inspection rejects preview payloads and compiled command-line markers. The exact three-platform 1.0 candidate must retain that evidence before promotion.

## Product boundary

Agent Arena is a local exhibition surface around the existing deterministic rules. The host owns each match, external agents submit one bounded mutation at a time through step or burst control, and humans can watch through a read-only Godot view or a verified replay. Agent activity does not update human scores, achievements, progression, household comparisons, or the built-in spectator league.

The implementation has four independent layers:

```text
agent client
    | MCP over local stdio
VibeSnake.AgentHost
    | transport-neutral calls
VibeSnake.AgentPlay -> VibeSnake.Rules
    | full public frames              | verified replay
VibeSnake.AgentViewer                 VibeSnake.Persistence
    | same-user named pipe
Godot read-only watch screen
```

`VibeSnake.AgentPlay` depends only on `VibeSnake.Rules`. MCP, named pipes, storage, process launch, profiles, and Godot types remain outside that boundary. The viewer cannot send actions. A viewer failure or disconnect cannot advance, stop, or otherwise change a match.

## Interoperability versions

| Surface | Current pin | Vibe Snake boundary |
| --- | --- | --- |
| Model Context Protocol | Stable `2026-07-28` through the locked official C# SDK package `ModelContextProtocol` 2.2.0 | Local stdio transport and lifecycle only; never rules authority |
| Agent Plugins | 1.0.0. The normative repository at commit `1fc1b6270e3cc492ec2d24ad7a34277c6d53b9c1` labels it Published (`published`), while the public specification website still labels it Working Draft (`working-draft`). Reviewed SHA-256 values are `97a658b7dca3ce1b4c2266b95da300fa51d9dc4ade59d73168e5f9104272da18` for the normative specification, `0a4aad95ce337878ad38802ebf0daa3fde76abe3f65400c86bcbb1ec0b3ab883` for the official `plugin.schema.json`, and `6539175bfcdf43085855183e86da40ea94b166547a72b47ae9a0a390516d3acb` for the official `mcp.schema.json` | Portable discovery and launch configuration; retained as a developer preview while upstream status surfaces disagree, and never required for direct host use |
| Agent Skill | `minimal-non-experimental` `SKILL.md` subset: `name`, `description`, and Markdown body | Advisory play instructions; never executable policy or hidden gameplay state |
| Open Knowledge Format | 0.2 | Generated discovery, provenance, trust, and lifecycle metadata; never a runtime schema or second rules source |
| MCP Apps | 2026-01-26 tracked only | Optional client-side viewer after neutral replay and frame contracts stabilize; not a host dependency |

Rules, observation, action, replay, viewer-frame, MCP, plugin, skill, and knowledge identities remain independent. The interoperability baseline was last reviewed on 2026-08-14. Review it at least quarterly and whenever an upstream specification, schema, SDK release, or security advisory changes. A standards update is handled as an isolated compatibility change with locked dependency restore, source and assembled-package validation, generated-knowledge drift checks, exact-protocol transcripts, and full CI. Every client named as supported also requires an independent cross-client smoke. Vibe Snake does not fetch a remote schema while loading a plugin or silently reinterpret scored behavior when an ecosystem format changes. Bump the host version when MCP behavior or its public tool and resource contract changes. Bump the plugin version when packaged discovery, launch, or skill behavior changes.

The current host and Agent Plugin package versions are both `0.7.1`. The `0.7.1` patch preserves `vibesnake-agent-passport-v4` with `symbolic-step-v4`, observation and result v5, host response DTO v5, viewer frame v7, Signal School definition, progress, delta, and outcome v2, Signal School resource v3, rules resource v7, identity resource v3, style catalog v2, both action profiles, replay schema 1, and `vibesnake-core@4`. It corrects completed-lesson lifecycle reporting and clarifies tool invocation and style-result interpretation without changing those schemas. The machine-readable [interoperability baseline](../../integrations/agent-interop-baseline.json) owns these pins, the Agent Plugins status discrepancy, immutable normative-source commit, official specification and schema digests, reviewed date, next review date, and versioned public-contract digests. Normal CI validates internal alignment, canonical timestamps, freshness, and host/plugin contract history without network access. A scheduled read-only job checks byte integrity for the pinned normative specification and two schema URLs. It does not discover newly released ecosystem versions or re-evaluate the mutable website status; those remain quarterly and release-triggered review work. A public contract change fails until its SemVer and digest-history entry advance together.

### Agentic playtester feedback

The `0.7.1` patch resolves three protocol-clarity findings from an agentic playtest of the `0.7.0` host:

1. A Signal School session with every requirement satisfied returned a verified `target_reached` outcome but used lifecycle `aborted`. It now uses `completed`. A partial lesson or ordinary nonterminal early finish remains `aborted`, and the viewer rejects any contradictory combination.
2. A `play_burst` request using `action` instead of the discovered `initialAction` field failed during SDK parameter binding before application code ran. The public description and skill now name the exact camelCase fields and direct callers to reread the discovered schema after a generic pre-invocation error. The host does not claim to intercept an error outside its tool boundary.
3. A Style Contract criterion marked unsatisfied read like a failed match grade. Tool, resource, and skill copy now states that criteria are factual measurements against optional style targets. Lifecycle, rules outcome, score, and replay verification remain the match result.

These observations are agentic interface evidence. They do not satisfy the roadmap's structured human-playtest, learnability, legibility, or spectator-appeal gates.

The v5 host response identities are `vibesnake-agent-match-start-v5`, `vibesnake-agent-match-summary-v5`, `vibesnake-agent-match-result-status-v5`, `vibesnake-agent-action-response-v5`, and `vibesnake-agent-burst-response-v5`. Lesson wire values use `vibesnake-agent-lesson-progress-v2`, `vibesnake-agent-lesson-progress-delta-v2`, `vibesnake-agent-lesson-outcome-v2`, and `vibesnake-agent-lesson-retry-v1`. Style wire values remain `vibesnake-agent-style-progress-v2` and `vibesnake-agent-style-outcome-v2`.

## MCP host

Run the source host with the .NET 10 SDK:

```powershell
dotnet run --project native/tools/VibeSnake.AgentHost/VibeSnake.AgentHost.csproj
```

The process uses the stateless MCP 2026-07-28 era over stdio and opens no network listener. Every request carries protocol metadata, optional capability discovery uses `server/discover`, and there is no protocol session or `initialize` exchange. Legacy initialize-era clients are rejected and the preview provides no downlevel fallback. Protocol output stays on stdout and diagnostics stay on stderr. A client should normally launch it through its MCP configuration instead of an interactive terminal.

The eight tools are:

| Tool | Effect |
| --- | --- |
| `start_match` | Creates an isolated Classic or Vibe match and returns an opaque handle plus its initial public observation. |
| `start_lesson` | Creates one canonical open-seed Signal School practice from a published lesson ID, action profile, and optional public passport. |
| `observe_match` | Reads current public state without advancing rules. |
| `play_move` | Accepts `up`, `right`, `down`, `left`, or `continue`; one accepted request advances exactly one rules step. |
| `play_burst` | In the separate burst profile, applies one initial action and then continues for at most 16 steps, stopping at the first fixed public decision event, selected lesson all-requirements transition, terminal state, match cap, replay failure, or requested bound. |
| `finish_match` | Finalizes a completed Signal School lesson, or ends another running exhibition early, and returns its verified nonterminal replay. |
| `get_match_result` | Reads a completed result without advancing or finishing a match. |
| `save_verified_replay` | Explicitly saves verified lane replays to the bounded application-owned replay store. It accepts no path. |

The host also publishes seven resources: `vibesnake://agent/rules`, `modes`, `identity`, `playbook`, `styles`, `signal-school`, and `rivals`. The current rules, identity, and Signal School resource contracts are v7, v3, and v3 respectively. Tool schemas and returned observations are authoritative. The bundled skill is advisory.

## Match contract

Start accepts only official `classic` or `vibe` mode configurations, `open` or `blind` seed visibility, a maximum of 2,000 steps, and either the default `four-direction-step-v1` action profile or the separate `four-direction-burst-v1` profile. It may also select one closed Style Contract, one named built-in rival, a bounded public Agent Passport v4, and a live watch capability. A supplied passport must select `avatar_id`, `accent_id`, and `station_id` from the identity resource, declare `symbolic-step-v4`, and declare the same action profile as the match. Unknown, legacy, or mixed identity payloads reject before session creation. The selected profile remains visible in every observation and result so later qualification cannot mix divisions silently.

Every intended `play_move` supplies the exact current tick, state hash, and a new bounded idempotency key. An uncertain transport retry reuses that key only with the identical request. A move may also supply `declaredIntent` as `undeclared`, `seek_food`, `seek_power`, `preserve_space`, `take_risk`, or `recover`. These rules apply:

- An accepted action advances exactly one clock-free step.
- A stale action, illegal reversal, or conflicting duplicate advances zero steps.
- Repeating the identical request with the same key returns the original response.
- Reusing a key with a different action or public intent is an idempotency conflict and advances zero steps.
- Public intent is self-declared presentation data. It appears in the next observation and live viewer but never changes rules, score, rewards, replay verification, or qualification.
- Response time never affects score.
- A rival uses the same gameplay seed and exact configuration but independent controller and replay state.
- Successfully finalized terminal, capped, and explicitly finished runs produce ordinary verified replays. Failed-closed finalization produces no verified result.
- A replay proves the recorded action trace and final state. It does not prove that an external policy is deterministic.
- A burst request supplies the same tick, state hash, and shared idempotency-key namespace plus one initial action and a bound from 1 through 16. The initial action applies once and later accepted steps continue the resulting direction.
- Burst execution uses the fixed `decision-event-stop-v1` catalog. It stops on wrap, food, terminal, power lifecycle, collision prevention, near miss, starvation warning, combo expiry, or achievement-candidate events. In Signal School it also stops when every requirement first transitions to satisfied. Routine movement, direction, score, and hunger-reset events do not stop it independently.
- A burst returns actual steps advanced, a closed stop reason, the first final-step stop event when present, final-step ordered events, the refreshed observation, and any finalized result. Terminal state and the match step cap take precedence over the requested remainder.
- A mutation key can name either a step or a burst, never both. An exact retry returns the cached typed response; any changed payload or cross-operation reuse advances zero additional steps. Each match retains at most 4,096 unique mutation records. Known keys are never evicted, while every unseen key fails closed with `mutation_capacity_exceeded` after exhaustion.
- The viewer receives the final frame of a burst rather than an artificial frame for every internal step. Replay and metrics still record every accepted rules step, the equal-seed rival advances exactly once per accepted step, and viewer loss remains irrelevant to rules.

Observations are a closed public logical-state division. They include exact pending directions and public rules timers needed for deterministic symbolic control, so equal rules do not imply identical observations for a human player and a symbolic agent. They exclude random state, future spawns, controller internals, human profile data, progression, paths, prompts, credentials, diagnostics, and hidden reasoning. A rejected mutation response contains no prior accepted-step events; consume final-step events from each accepted step or burst response immediately. Blind seeds remain hidden until the result. The host retains at most eight sessions and uses cryptographically random handles when not supplied by a test owner. An opaque match handle is a bearer capability; the stdio host has no separate client-authentication layer. When capacity is needed, it evicts the oldest completed, aborted, or failed-closed session first. If all eight are live, it may reclaim the oldest match with no valid handle-bearing host operation for at least 30 minutes, invalidating the handle without a result, score, ranking, or replay. Replacement validation and construction complete before eviction. Viewer activity never refreshes, finishes, or expires a match. If no live lease expired, a ninth match is rejected. Every handle becomes invalid when its host process exits.

## Watch an agent live

1. The agent calls `start_match` with `watchEnabled: true`.
2. The response includes a one-time `viewer` capability with `pipe_name` and `access_token`.
3. On the same user account and machine, launch:

   ```powershell
   ./play.ps1 --agent-watch-pipe=<pipe_name> --agent-watch-token=<access_token>
   ```

4. The agent continues with `play_move` or `play_burst` according to the selected action profile. Godot renders the latest full public frame and never controls the lane. It labels initial, step, burst, and finish operations; exact steps advanced; burst stop reason and event; every current closed action acceptance or rejection reason; exact end reason; failed-closed state; and verified-result availability. Sequence gaps are shown as coalesced earlier updates.
5. Save the verified replay explicitly if the human should be able to watch it again through the ordinary replay browser.

The pipe and token are ephemeral capabilities. Do not place them in logs, screenshots, reports, or shared command history. The server accepts one same-user client, consumes the token once, keeps only the latest pending frame, and never listens on TCP. Process arguments may still be visible to other software running as the same user, so this preview is a local trust boundary rather than a security boundary against a compromised account.

The live screen consumes `vibesnake-agent-viewer-frame-v7` and reuses the normal run renderer. Its agent-specific overlay displays only public identity, the resolved Passport v4 avatar, accent, and station, exactly two catalog-defined style criteria or exactly two ordered lesson requirements, stable factual labels, latest closed public intent, every current closed action acceptance or rejection reason, rival score, exact match end reason, and verified-result availability. The agent avatar and accent are resolved from closed catalogs and passed explicitly to the renderer without reading or mutating the local human cosmetic selection. Station identity is a catalog label, not approval for station audio or host content. Style copy distinguishes live rules-advanced-step observations, a replay-verified terminal outcome, and failed-closed evidence unavailability. Lesson copy distinguishes live, verified combined evidence, and failed-closed evidence, and never presents practice completion as mastery. Each frame identifies its initial, step, burst, or finish origin and binds its actual advancement to the pre-mutation tick and state hash. Step frames publish zero or one actual advancement; burst frames publish zero through sixteen actual steps plus the closed stop reason and first final-step stop event when present. Preflight burst rejections remain zero-step burst operations. The client cross-checks tick deltas, contiguous state anchors, action acceptance, final-step stop events, lifecycle, result availability, catalog membership, immutable match identity, ordered criterion and lesson-requirement definitions, evidence state, attempt-evidence bounds and hashes, rate arithmetic, satisfied counts, and optional verified outcomes before presentation. Malformed, oversized, contradictory, unknown-catalog, identity-drifting, criterion-drifting, or requirement-drifting input rejects the stream and clears any pending frame. The client reports every source-sequence gap as a count of coalesced earlier updates, so a nonadjacent board update is never presented as one ordinary step. The awaiting state explicitly says that rules are paused, and reduced-motion presentation snaps to the latest body instead of interpolating a multi-step jump. The real Godot pipe smoke proves that snap branch while running muted, high-contrast, and at 150 percent text; the localization qualification separately measures the composed pseudo-localized overlay rows in their shared geometry, including grapheme-safe middle elision for oversized rows. A burst still publishes one final public frame, so live viewing remains bounded and does not pretend to be canonical step history. AA-09b owns paced intermediate replay presentation. Ordinary human cosmetic presentation and local radio remain player-side state rather than pipe data. The screen does not show chain of thought or private provider output. The verified replay produced by successful finalization remains the canonical accepted-step record if frames are dropped; first-turn rejection evidence remains a distinct bounded witness sequence. A disconnect says only that match control remains with the host; it does not claim that a replay already exists.

Wire deserialization rejects unknown fields and missing required constructor fields. Before accepting a frame, the viewer also cross-checks the bounded match cap, episode metrics against the current tick, lesson identity and canonical practice configuration against the catalog, style facts against public episode counters, and any terminal style outcome against the exact live terminal values. This validation protects presentation truth; it does not turn the viewer stream into canonical replay history.

A step that advances Rules updates live public metrics and style facts even if replay recording then fails. The response remains a typed `replay_failure`, the lifecycle becomes failed closed, and no verified lesson outcome or replay-verified style outcome is created.

## Agent experience surfaces

Signal School publishes exactly eight ordered lesson IDs: `first-turn`, `wrap-line`, `hunger-route`, `exit-route`, `power-route`, `recover-route`, `combo-route`, and `death-read`. Every v2 definition has exactly two ordered factual requirements under `ordered-replay-attempt-evidence-v2`. `first-turn` requires one valid-state opposite reversal rejected without advancement and a later replay `DirectionChanged` step. `wrap-line` requires a `Wrapped` event whose step ends Running. `hunger-route` requires `AteFood` before starvation death. `exit-route` requires food growth whose same Running post-step snapshot retains at least two structural non-reversing exits. `power-route` requires a collected power kind and activation of that same kind at or after collection in event order. `recover-route` requires `CollisionPrevented` with a non-none cause and known power whose step ends Running. `combo-route` requires at least three food and peak combo at least three. `death-read` requires terminal Dead with a non-none cause and a terminal `Died` event with the same cause.

Replay-trace requirements are independently reconstructed during successful finalization. A separate maximum-32 witness chain records only the first-seen opposite-reversal rejection at a valid tick and state hash, with step or burst origin and a SHA-256 idempotency-key hash. Exact retries, conflicts, stale anchors, wrong profiles, and a 33rd relevant rejection add no evidence. The successful v2 outcome carries the actual end reason, a closed review code, replay payload hash, attempt evidence hash, and combined evidence hash. An unmet finalized lesson still returns factual review and a fresh `start_lesson` retry descriptor. Replay failure is failed closed: live progress reflects any rules step that actually advanced, no lesson outcome exists, and the descriptor permits only the same lesson and action profile in a fresh session. Bursts stop on the first all-requirements transition. Sixteen exact route/profile records measure action calls, actual camelCase MCP tool arguments JSON, and snake_case structured responses without JSON-RPC framing. Observation-derived bounded bursts never use more calls than the paired step route and reduce calls for at least six of eight lessons. These public fixtures prove deterministic practice mechanics and a bounded efficiency regression, not mastery, provider token cost, or qualification. No practice history is persisted.

Five closed Style Contracts each expose exactly two ordered facts. Stillwater requires at least 200 rules-advanced steps and a structural-open-exit rate of at least 9,900 basis points. Crownchaser requires peak combo 4 and 10,000-basis-point uninterrupted food continuity through the first combo of 4. Edge Prophet requires three rewarded body-proximity near misses including one on a wrapping step under the evaluator pinned to `vibesnake-core@4`. Mutagenist requires two distinct activated power kinds and two concurrent active kinds. Redline requires six food and a safe pre-step-food progress rate of at least 6,500 basis points. Structural geometry ignores temporary collision immunity, rate values use integer floor and expose exact numerators and denominators, and Redline binds each sample to the exact food visible before that step. Live values are rules-advanced-step observations and may rise or fall. Only a successfully finalized style outcome is independently reconstructed from and bound to the verified replay payload hash. These facts do not prove intent, planning, mastery, personality, or spectator appeal. Episode summaries retain the separate typed public metric vector.

Agent Passport v4 contains only a caller-declared bounded ID, policy version, display name, closed avatar, accent, and station IDs, the fixed `symbolic-step-v4` observation profile, and either the step or burst action profile. The `vibesnake://agent/identity` resource publishes the exact catalogs. The current host does not authenticate a global identity or persist long-term identity state. Passport presentation is public data for the current exhibition, not semantic memory, and it remains independent of human progression and cosmetics. Vibe Snake never stores prompts, reasoning, credentials, provider responses, or agent-authored executable code.

Qualification leagues, qualification-time seed decks, persisted passports, visual-input divisions, remote transport, hosted tournaments, and human same-seed handoff remain future work. Results from open and blind seeds, Classic and Vibe, or different observation and action profiles must not share one ranking.

## Portable plugin and knowledge

The checked-in source bundle under `integrations/vibesnake-agent-plugin/` pins Agent Plugins 1.0.0 and contains a minimal Agent Skill. The normative versioned repository labels 1.0.0 Published while the public website still says Working Draft, so packaging remains preview-only. The discrepancy is machine-recorded and re-reviewed quarterly; weekly automation verifies the pinned normative specification and schema bytes rather than treating the mutable website as a conformance oracle. Validate its source form with:

```powershell
python scripts/validate_agent_plugin.py integrations/vibesnake-agent-plugin
```

This validator enforces Vibe Snake's intentionally narrow stdio producer profile, local containment rules, and assembled-package invariants. It is not a general Agent Plugins client conformance suite or a complete Agent Skills validator.

Create the framework-dependent preview package with:

```powershell
./scripts/package_agent_plugin.ps1
```

The output is `dist/agent-plugins/portable/vibesnake-agent/`. It contains the published host, root `plugin.json` and `mcp.json`, the skill, license files, and a complete `SHA256SUMS`. Packaged validation requires every component, exact checksum coverage, one executable command token, the declared contained host argument, and the package-root working directory. It requires a compatible .NET 10 runtime. Distribution signing, per-platform self-contained packages, SBOMs, artifact qualification, and installation UX remain release responsibilities because the format does not define them.

The floating `player-latest` release is a source and reference channel. Its source ZIP contains the checked-in plugin manifest and skill, MCP host source, packaging script, and generated knowledge bundle so a developer can reproduce this assembly. It does not contain the generated `mcp.json` or claim to be a standalone supported Agent Plugin. CI assembles that generated form into an isolated output, validates it with `--require-mcp`, and discards it after qualification until AA-10 defines supported cross-platform plugin artifacts.

The generated `integrations/vibesnake-agent-knowledge/` bundle uses Open Knowledge Format 0.2 for discoverable rules and protocol concepts. It is generated from canonical source and is never a runtime schema or a second rules authority. Its `generated.at` value changes only when concept meaning changes, `verified.at` changes when the canonical sources and pinned specifications are reviewed, and `stale_after` requires a new quarterly review. CI proves deterministic derivation, but it does not replace that upstream review.

```powershell
python scripts/generate_agent_knowledge.py --check
python scripts/generate_agent_knowledge.py --write
```

Use `--check` in normal validation. Use `--write` only after intentionally changing a canonical source.

## Verification

Focused tests cover deterministic sessions, step-equivalent bursts, fixed event and all-requirements lesson stops, lesson progress, replay-bound style outcomes, combined-evidence lesson outcomes, shared cross-operation idempotency and exhaustion, concurrent retries, transactional finalization failure, profile separation, bounded idle reclamation with an injected monotonic clock, privacy projection, style metrics, passports, rivals, a stateless official C# SDK burst transcript, legacy-protocol rejection, protocol-clean subprocess behavior, replay save and exact playback, named-pipe authentication, zero-step preflight burst origin, pre-mutation advancement anchors, immutable stream identity, malformed and contradictory wire rejection, pending-frame clearing, server-side newest-unsent retention, monotonic sequence and coalescing truth, viewer disconnects, and a real Godot terminal-burst accessibility smoke. Hosted Windows, macOS, and Linux each assemble the framework-dependent Agent Plugin, verify its complete `SHA256SUMS`, parse and safely expand the generated `mcp.json` command, argument, and working directory, run the transcript through that exact declared package launch, open its viewer capability, and receive the terminal burst frame. This is not broad client compatibility certification. The repository coverage gate requires at least 90 percent line and 85 percent branch coverage for every measured agent module.

```powershell
dotnet test native/tests/VibeSnake.Rules.Tests/VibeSnake.Rules.Tests.csproj --filter "FullyQualifiedName~Agent"
./scripts/test_native_coverage.ps1
python -m pytest tests/qa/test_agent_plugin.py tests/qa/test_agent_knowledge.py
python scripts/generate_agent_knowledge.py --check
```

The normal repository lint, locked restore, dependency audit, formatting, Godot smoke, documentation, privacy, and artifact gates remain required. Passing automation proves implementation contracts, not that watching an agent is fun. Structured human review must still establish clarity, pacing, personality, accessibility, and rematch desire.
