# ADR 0001: Native Runtime and Rules Boundary

Status: Accepted for 0.3 qualification

Decision date: 2026-08-01

## Context

Vibe Snake already has a playable Python and Pygame alpha, but the 1.0 promise requires polished 2D effects, responsive and accessible UI, adaptive audio, reliable controller handling, signed native releases, and automated validation on Windows, macOS, and Linux. Continuing to grow the current coordinator would require the project to build and maintain more engine infrastructure while also trying to refine the game.

The rewrite risk is equally real. Changing engine, language, timing, random behavior, and presentation together could silently change the feel or erase working systems. The target therefore needs an isolated, measurable qualification gate before it becomes the default runtime.

## Decision

- Use Godot 4.7.1 .NET as the 1.0 presentation and platform shell.
- Use the stable .NET SDK 10.0.302 and target `net10.0`.
- Keep deterministic gameplay in `VibeSnake.Rules`, a pure C# assembly with no Godot reference.
- Keep replay files and other platform-neutral storage boundaries in dedicated service assemblies such as `VibeSnake.Persistence`, outside both rules and Godot presentation code.
- Keep the Python and Pygame game as the playable behavior reference until trace, feature, data, and native artifact parity pass.
- Treat Windows x64, macOS Universal, and Linux x64 as mandatory 1.0 platforms.
- Keep the game offline-first and exclude a browser or HTML runtime from the 1.0 target.
- Use project-owned versioned randomness, canonical state hashes, shared fixtures, and differential tests as migration controls.
- Make presentation consume snapshots and typed events. Godot code may submit commands but may not mutate rules state directly.

## Current evidence

- `global.json` selects stable .NET SDK 10.0.302 and rejects previews.
- `native/toolchain.json` pins Godot 4.7.1, its official commit, official editor archives for all three target systems, and the exact .NET export-template archive.
- The native solution builds with warnings as errors and passes 177 xUnit cases.
- `VibeSnake.Rules` measures 91.73 percent line and 87.77 percent branch coverage. `VibeSnake.Persistence` measures 90.73 percent line and 84.48 percent branch coverage. Aggregate native coverage is 91.55 percent line, 87.26 percent branch, and 97.53 percent method, above the enforced 80 percent line floor.
- One hundred Python-generated movement traces containing 25,600 step-level state and queue comparisons pass in C#.
- Thirty-five targeted Python cases covering every current score boundary, queue outcomes, normalized random respawns, growth, combo expiry, collision precedence, tail movement, wrapping, exact starvation outcomes, full-grid victory, and ordered events pass in C#.
- Eight targeted Shield cases covering collection on entry, pickup and active expiry, collision consumption and prevention, expiry precedence, starvation bypass, the simultaneous collision and starvation boundary, normalized state, and ordered power events pass in C#.
- Canonical state is strictly restored and validated, and generated operation campaigns prove restore-and-continue equivalence across active, terminal, and restarted runs.
- Parity assertions retain schema 1 first-divergence bundles with the shortest known failing step prefix, expected and actual normalized state and events, native canonical state and hash, environment identity, and a reproduction command.
- The rules contract exposes `vibesnake-core@4`, canonical state uses schema 2 and `fnv1a64-canonical-json-v3`, and schema 1 replay envelopes retain the canonical initial state, ordered logical actions, checkpoints, observed outcome, compatibility diagnostics, and SHA-256 payload integrity.
- Live replay recording preserves rejected input attempts, checks every step against a private deterministic mirror, compares the final canonical state, and refuses to finalize after divergence, lifecycle misuse, or a command, step, or serialized-size bound.
- Replay verification has typed outcomes and a deterministic 16,000,000 work-unit limit that charges body hashes and potential full-grid food and power-spawn scans before execution.
- `VibeSnake.Persistence` strictly inspects untrusted replay files, reports exact compatibility or verification failures without reflecting attacker-controlled identifiers, preserves rejected sources, serializes duplicate and quota decisions across processes, and atomically writes verified canonical files without overwrite under file-count and byte limits.
- The actual Godot scene imports, loads the rules and persistence assemblies, validates logical keyboard, controller, background latest-replay, and dropped-file actions, exercises focus-loss pause safety, bounds and sanitizes replay status, renders Shield pickup and active state, validates typed Shield feedback priority, plays finite PCM fallback cues, records and reloads an isolated terminal replay, executes seeded replay and restoration checks, and exits cleanly headlessly on Windows. Qualification fails on engine warnings, leaked objects, missing replay output, or leftover atomic temporary files.
- A packaged Windows x64 debug player exports and launches outside the checkout without Python, emits deterministic hash `643077d90db75e8c`, and passes required-payload and portability inspection.
- Its 198 distribution files total 189,615,786 bytes and have a retained SHA-256 inventory with no checkout path or exported NuGet lock file. Inspection requires and path-scans the Rules, Persistence, and Game payloads.
- Two independent Windows payloads passed the checksum-bound schema 2 inspector and produced the same artifact-manifest SHA-256, `bae7d6369d61c6a57f2fe295f0308c238acc6ccd1e057c20abffc880e8c2ae74`.
- CI definitions cover Python, pure C# rules, real Godot headless execution, checksum-verified exports, packaged-player smoke, artifact inspection, and bundle retention on Windows, macOS, and Linux.

This evidence accepts the architecture for continued qualification work. It does not pass the complete 0.3 gate. Hosted cross-platform execution, retained macOS and Linux artifacts, physical controller and audio-device evidence, content isolation, performance, accessibility, and the complete vertical slice remain open.

## Consequences

Positive consequences:

- Rules can be tested much faster than real time without opening the engine.
- Rendering, audio, input devices, and platform lifecycle gain mature engine primitives.
- Shared traces make migration differences reviewable at the first divergent step.
- Verified replay files can reproduce native failures without placing clocks or files in the rules kernel.
- Native platform artifacts no longer need Python or a source checkout.

Costs and constraints:

- The team must maintain a temporary reference and target implementation during migration.
- Every rules difference needs an explicit compatibility, defect, or target-correction decision.
- C# Godot builds cannot target the web in the current engine line, which is acceptable because web is outside 1.0.
- Native exports, signing, notarization, controller drivers, and audio devices still require platform-specific evidence.
- New broad features stay out of both runtimes until the migration gate closes.

## Reconsideration triggers

Reopen this decision only if measured qualification evidence shows an inherent blocker in Godot or C# that cannot meet the three-platform, accessibility, performance, packaging, deterministic QA, or presentation contract. A fallback must demonstrate lower total risk against the same gates, not merely lower initial port cost.
