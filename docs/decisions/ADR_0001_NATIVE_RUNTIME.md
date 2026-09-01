# ADR 0001: Native Runtime and Rules Boundary

Status: Accepted; native source default

Decision date: 2026-08-01

## Context

Vibe Snake began with a playable Python and Pygame alpha, but the 1.0 promise requires polished 2D effects, responsive and accessible UI, adaptive audio, reliable controller handling, signed native releases, and automated validation on Windows, macOS, and Linux. Continuing to grow that coordinator would require the project to build and maintain more engine infrastructure while also trying to refine the game.

The rewrite risk is equally real. Changing engine, language, timing, random behavior, and presentation together could silently change the feel or erase working systems. The target therefore needs an isolated, measurable qualification gate before it becomes the default runtime.

## Decision

- Use Godot 4.7.1 .NET as the 1.0 presentation and platform shell.
- Use the exact stable .NET SDK 10.0.303 and target `net10.0`.
- Keep deterministic gameplay in `VibeSnake.Rules`, a pure C# assembly with no Godot reference.
- Keep replay files and other platform-neutral storage boundaries in dedicated service assemblies such as `VibeSnake.Persistence`, outside both rules and Godot presentation code.
- Keep Python and Pygame as a frozen behavior oracle and fixture producer. Make Godot the default source player after automated trace, feature, data, and native artifact foundations pass.
- Treat Windows x64, macOS Universal, and Linux x64 as mandatory 1.0 platforms.
- Keep the game offline-first and exclude a browser or HTML runtime from the 1.0 target.
- Use project-owned versioned randomness, canonical state hashes, shared fixtures, and differential tests as migration controls.
- Make presentation consume snapshots and typed events. Godot code may submit commands but may not mutate rules state directly.

## Current evidence

- `global.json` selects exact stable .NET SDK 10.0.303 and rejects previews and other patches. `native/toolchain.json` pins Godot 4.7.1, its official commit, official editor archives for all three targets, and the exact export-template archive.
- The native solution builds with warnings as errors and passes 1,547 tests with zero build warnings. Under Coverlet 10, the current local Windows aggregate is 94.30 percent line and 86.78 percent branch coverage, with RepositoryChecks at 94.61 percent line and 86.19 percent branch. Current 90 percent line and 85 percent branch floors apply per measured module, with a retained 90 percent branch target for 0.4 acceptance.
- Shared Python-origin fixtures cover 100 movement traces with 25,600 steps, core rules, all nine powers, and the achievement-candidate product path. Achievement-candidate, Last Stand, Phase Shift, Shield, Remaining Powers, Core Rules, and Movement corpora retain 167 reviewed vectors under native exact-byte freshness ownership, while separate live C# consumers prove behavior. The seven closed renderers preserve exact 2,682-byte, 3,596-byte, 3,534-byte, 4,489-byte, 9,548-byte, 57,031-byte, and 999,087-byte canonical LF identities. Reviewed parity decisions own intentional differences.
- `VibeSnake.Rules` owns Classic and Vibe, all nine powers, adaptive hunger, canonical schema 3 state, stable hashes, replay recording and verification, AI rivals, challenges, ghosts, progression catalogs, and deterministic restore.
- `VibeSnake.Persistence` owns bounded atomic settings, bindings, achievements, progression, scores, replays, comparisons, recovery, content packs, radio policy, and local privacy-safe evidence.
- The Godot shell owns the title menu, optional Help, gameplay, detailed cosmetics, settings, remapping, scores, achievements, Tour, replays, AI channels, lore, offline comparisons, procedural cues, radio adaptation, 4:3 and widescreen presentation, fullscreen modes, focus safety, and idle pointer hiding.
- Hosted CI builds and launches player exports outside the checkout on Windows, macOS, and Linux without Python, under read-only install and isolated user-data conditions. The aggregate matrix binds source, state, dependencies, manifests, install lifecycle, reliability, performance, accessibility, and fault evidence.
- Root `play.ps1`, `play.sh`, and `play.bat` now verify the pinned editor, build the native project, and launch Godot. Python remains available only as an optional oracle path.
- Native `RepositoryChecks` owns the strict V090-09 release-material foundation and exact-candidate structure, writes canonical atomic `release-materials-handoff-v2` evidence, and intentionally allows `candidateMaterialComplete: true` only while `releaseAcceptance` remains false until manifest inspection, size reconciliation, claim approval, visible review, and video playback close separately.
- Native `RepositoryChecks` owns the V090-10 release-rehearsal foundation and retained-record validator, binds a separate material-acceptance authority and exact three-platform evidence into same-revision `release-rehearsal-handoff-v2` output, and does not claim that automated inspection performed external operations or approvals.

This evidence accepts Godot and C# as the default source product. It does not accept a store release. Approved packs, protected signing, retained physical-platform sessions, physical controllers and audio devices, named-hardware performance, accessibility review, and structured human experience validation remain open.

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
