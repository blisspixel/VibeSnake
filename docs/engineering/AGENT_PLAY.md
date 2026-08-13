# Agent Play Integration

[Engineering index](README.md) | [Agent Arena design](../design/AGENT_ARENA.md) | [ADR 0002](../decisions/ADR_0002_AGENT_ARENA.md)

Status: post-1.0 developer preview. The development tree contains this code, but it is not part of the supported 1.0 release contract or release gates. A 1.0 candidate must exclude every preview assembly and entry point and pass the dedicated artifact assertion before artifact qualification.

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
| Agent Plugins | 1.0.0 Working Draft (`working-draft`); reviewed schema SHA-256 values `0a4aad95ce337878ad38802ebf0daa3fde76abe3f65400c86bcbb1ec0b3ab883` for `plugin.json` and `6539175bfcdf43085855183e86da40ea94b166547a72b47ae9a0a390516d3acb` for assembled `mcp.json` | Portable discovery and launch configuration; never required for direct host use |
| Agent Skill | `minimal-non-experimental` `SKILL.md` subset: `name`, `description`, and Markdown body | Advisory play instructions; never executable policy or hidden gameplay state |
| Open Knowledge Format | 0.2 | Generated discovery, provenance, trust, and lifecycle metadata; never a runtime schema or second rules source |
| MCP Apps | 2026-01-26 tracked only | Optional client-side viewer after neutral replay and frame contracts stabilize; not a host dependency |

Rules, observation, action, replay, viewer-frame, MCP, plugin, skill, and knowledge identities remain independent. The interoperability baseline was last reviewed on 2026-08-13. Review it at least quarterly and whenever an upstream specification, schema, SDK release, or security advisory changes. A standards update is handled as an isolated compatibility change with locked dependency restore, source and assembled-package validation, generated-knowledge drift checks, exact-protocol transcripts, and full CI. Every client named as supported also requires an independent cross-client smoke. Vibe Snake does not fetch a remote schema while loading a plugin or silently reinterpret scored behavior when an ecosystem format changes. Bump the host version when MCP behavior or its public tool and resource contract changes. Bump the plugin version when packaged discovery, launch, or skill behavior changes.

The current host version and Agent Plugin package version are both `0.2.0`. The machine-readable [interoperability baseline](../../integrations/agent-interop-baseline.json) owns these pins, official schema digests, the reviewed date, the next review date, and versioned public-contract digests. Normal CI validates internal alignment, canonical timestamps, freshness, and host/plugin contract history without network access; a scheduled read-only job compares the two pinned Agent Plugins schema URLs with their reviewed SHA-256 digests. A public contract change fails until its SemVer and digest-history entry advance together.

## MCP host

Run the source host with the .NET 10 SDK:

```powershell
dotnet run --project native/tools/VibeSnake.AgentHost/VibeSnake.AgentHost.csproj
```

The process uses the stateless MCP 2026-07-28 era over stdio and opens no network listener. Every request carries protocol metadata, optional capability discovery uses `server/discover`, and there is no protocol session or `initialize` exchange. Legacy initialize-era clients are rejected and the preview provides no downlevel fallback. Protocol output stays on stdout and diagnostics stay on stderr. A client should normally launch it through its MCP configuration instead of an interactive terminal.

The seven tools are:

| Tool | Effect |
| --- | --- |
| `start_match` | Creates an isolated Classic or Vibe match and returns an opaque handle plus its initial public observation. |
| `observe_match` | Reads current public state without advancing rules. |
| `play_move` | Accepts `up`, `right`, `down`, `left`, or `continue`; one accepted request advances exactly one rules step. |
| `play_burst` | In the separate burst profile, applies one initial action and then continues for at most 16 steps, stopping at the first fixed public decision event, terminal state, match cap, replay failure, or requested bound. |
| `finish_match` | Ends a running exhibition early and finalizes its verified nonterminal replay. |
| `get_match_result` | Reads a completed result without advancing or finishing a match. |
| `save_verified_replay` | Explicitly saves verified lane replays to the bounded application-owned replay store. It accepts no path. |

The host also publishes `vibesnake://agent/rules`, `modes`, `playbook`, `styles`, `signal-school`, and `rivals` resources. Tool schemas and returned observations are authoritative. The bundled skill is advisory.

## Match contract

Start accepts only official `classic` or `vibe` mode configurations, `open` or `blind` seed visibility, a maximum of 2,000 steps, and either the default `four-direction-step-v1` action profile or the separate `four-direction-burst-v1` profile. It may also select one closed Style Contract, one named built-in rival, a bounded public Agent Passport, and a live watch capability. A supplied passport must declare the same action profile as the match, and the selected profile remains visible in every observation and result so later qualification cannot mix divisions silently.

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
- Burst execution uses the fixed `decision-event-stop-v1` catalog. It stops on wrap, food, terminal, power lifecycle, collision prevention, near miss, starvation warning, combo expiry, or achievement-candidate events. Routine movement, direction, score, and hunger-reset events do not stop it independently.
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

4. The agent continues with `play_move` or `play_burst` according to the selected action profile. Godot renders the latest full public frame and never controls the lane. It labels action acceptance or rejection, exact end reason, failed-closed state, and verified-result availability.
5. Save the verified replay explicitly if the human should be able to watch it again through the ordinary replay browser.

The pipe and token are ephemeral capabilities. Do not place them in logs, screenshots, reports, or shared command history. The server accepts one same-user client, consumes the token once, keeps only the latest pending frame, and never listens on TCP. Process arguments may still be visible to other software running as the same user, so this preview is a local trust boundary rather than a security boundary against a compromised account.

The live screen consumes `vibesnake-agent-viewer-frame-v2` and reuses the normal run renderer. Its agent-specific overlay displays only public identity, passport color, shed, station affinity, style progress, latest closed public intent, action acceptance or rejection reason, rival outcome, exact match end reason, and verified-result availability. A burst publishes one final public frame, so live viewing remains bounded and does not pretend to be canonical step history. The current frame contract does not yet identify the mutation as a burst or display its actual step count, stop reason, or stop event, so a nonadjacent board update can still look like an ordinary accepted step. AA-09a owns that live-legibility correction; AA-09b owns paced intermediate replay presentation. Ordinary board rendering, local cosmetic presentation, and local radio remain player-side presentation rather than pipe data. The screen does not show chain of thought or private provider output. The verified replay produced by successful finalization remains the canonical complete record if frames are dropped. A disconnect says only that match control remains with the host; it does not claim that a replay already exists.

## Agent experience surfaces

Signal School currently publishes the exact lesson IDs `first-turn`, `wrap-line`, `hunger-route`, `power-route`, `combo-route`, and `recover-route`, each with a fixed seed, step cap, target metric, and threshold evaluator. The host does not yet accept a lesson selection in `start_match` or return lesson completion in a match result. AA-05 owns lesson-selectable sessions and the planned eight-behavior curriculum. AA-04's bounded symbolic burst is now implemented so that curriculum work does not require hundreds of routine one-step tool calls. Five closed Style Contracts report survival steps, peak combo, near misses, powers activated, or food collected without reducing the game to one scalar reward. The current Edge Prophet evaluator counts typed `NearMiss` events; it does not infer whether the route was intentional. Expressive multi-metric objectives remain design targets. Episode summaries retain typed public metrics.

An Agent Passport contains only a stable bounded ID, policy version, display name, color, shed, station affinity, a fixed symbolic observation profile, and either the step or burst action profile. It is public presentation data for the current exhibition, not semantic memory. Vibe Snake never stores prompts, reasoning, credentials, provider responses, or agent-authored executable code.

Qualification leagues, withheld seed decks, persisted passports, visual-input divisions, remote transport, hosted tournaments, and human same-seed handoff remain future work. Results from open and blind seeds, Classic and Vibe, or different observation and action profiles must not share one ranking.

## Portable plugin and knowledge

The checked-in source bundle under `integrations/vibesnake-agent-plugin/` pins the Agent Plugins 1.0.0 Working Draft and contains a minimal Agent Skill. Validate its source form with:

```powershell
python scripts/validate_agent_plugin.py integrations/vibesnake-agent-plugin
```

This validator enforces Vibe Snake's intentionally narrow stdio producer profile, local containment rules, and assembled-package invariants. It is not a general Agent Plugins client conformance suite or a complete Agent Skills validator.

Create the framework-dependent preview package with:

```powershell
./scripts/package_agent_plugin.ps1
```

The output is `dist/agent-plugins/portable/vibesnake-agent/`. It contains the published host, root `plugin.json` and `mcp.json`, the skill, license files, and `SHA256SUMS`. It requires a compatible .NET 10 runtime. Distribution signing, per-platform self-contained packages, SBOMs, artifact qualification, and installation UX remain release responsibilities because the format does not define them.

The floating `player-latest` release is a source and reference channel. Its source ZIP contains the checked-in plugin manifest and skill, MCP host source, packaging script, and generated knowledge bundle so a developer can reproduce this assembly. It does not contain the generated `mcp.json` or claim to be a standalone supported Agent Plugin. CI assembles that generated form into an isolated output, validates it with `--require-mcp`, and discards it after qualification until AA-10 defines supported cross-platform plugin artifacts.

The generated `integrations/vibesnake-agent-knowledge/` bundle uses Open Knowledge Format 0.2 for discoverable rules and protocol concepts. It is generated from canonical source and is never a runtime schema or a second rules authority. Its `generated.at` value changes only when concept meaning changes, `verified.at` changes when the canonical sources and pinned specifications are reviewed, and `stale_after` requires a new quarterly review. CI proves deterministic derivation, but it does not replace that upstream review.

```powershell
python scripts/generate_agent_knowledge.py --check
python scripts/generate_agent_knowledge.py --write
```

Use `--check` in normal validation. Use `--write` only after intentionally changing a canonical source.

## Verification

Focused tests cover deterministic sessions, step-equivalent bursts, fixed event stops, shared cross-operation idempotency and exhaustion, concurrent retries, transactional finalization failure, profile separation, bounded idle reclamation with an injected monotonic clock, privacy projection, style metrics, passports, rivals, a stateless official C# SDK burst transcript, legacy-protocol rejection, protocol-clean subprocess behavior, replay save and exact playback, named-pipe authentication, malformed frames, viewer disconnects, and Godot projection. Hosted Windows, macOS, and Linux each assemble the framework-dependent Agent Plugin, verify its complete `SHA256SUMS`, expand the declared package host path, and run that transcript through the packaged assembly. This is not broad client compatibility certification. The repository coverage gate requires at least 90 percent line and 85 percent branch coverage for every measured agent module.

```powershell
dotnet test native/tests/VibeSnake.Rules.Tests/VibeSnake.Rules.Tests.csproj --filter "FullyQualifiedName~Agent"
./scripts/test_native_coverage.ps1
python -m pytest tests/qa/test_agent_plugin.py tests/qa/test_agent_knowledge.py
python scripts/generate_agent_knowledge.py --check
```

The normal repository lint, locked restore, dependency audit, formatting, Godot smoke, documentation, privacy, and artifact gates remain required. Passing automation proves implementation contracts, not that watching an agent is fun. Structured human review must still establish clarity, pacing, personality, accessibility, and rematch desire.
