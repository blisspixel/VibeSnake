# Technology and Cross-Platform Strategy

## Decision

The target 1.0 architecture is a native Godot 4 .NET desktop game with gameplay rules in a pure C# assembly. Windows, macOS, and Linux are first-class release platforms. No browser or HTML runtime is in the 1.0 plan.

The Godot vertical slice, deterministic trace parity, and three-platform export-smoke foundations now pass, so Godot is the default source player. Migration and release acceptance are not complete: Python remains a frozen fixture oracle, and approved content, signing, physical devices, named-hardware performance, and human experience gates remain open. Reversing the target decision requires a written architecture record with measured evidence.

The accepted qualification decision is recorded in [ADR 0001](ADR_0001_NATIVE_RUNTIME.md). Cross-language behavior corrections and unresolved differences are recorded in [PARITY_DECISIONS.md](../engineering/PARITY_DECISIONS.md).

As of 2026-08-22, Godot 4.7.1 is the pinned stable maintenance release and Godot 4 C# supports Windows, Linux, and macOS. The repository pins Godot 4.7.1 Mono commit `a13da4feb`, official editor and .NET export-template hashes, and the exact .NET SDK 10.0.303 security-servicing release. .NET 10 is an active LTS release. The project targets `net10.0` with **1,511** native contract tests and current 90 percent line and 85 percent branch floors per measured module under Coverlet 10. Native evidence covers repository policy, dependency and release qualification, exact achievement-candidate and Last Stand frozen-vector rendering, stable Classic/Vibe rules, persistence, content isolation, accessibility, performance, replay, deterministic simulation, and post-1.0 agent-play preview contracts. Hosted packaged-player export and smoke pass on **Windows, macOS, and Linux** outside the checkout without Python. Python remains temporary test-only migration scaffolding; product work continues in pure C# and Godot, and V030-13 removes the oracle after equivalent .NET gates exist.

## Why change from the incumbent

Pygame and SDL can run a 2D Snake game quickly on all three desktop operating systems. The problem is not raw grid simulation cost. The problem is the amount of release-quality engine infrastructure this project is rebuilding by hand.

The Python reference has direct source-tree asset paths, a large coordinator,
software-surface rendering, many direct font constructions, static controller
assumptions, limited display scaling, a hand-built audio layer, and global random
streams. The native artifact matrix addresses packaging proof for the Godot
product on all three platforms, but it does not repair those reference-runtime
boundaries. PyInstaller would also require a separate build on each
target operating system. These issues can be fixed in Python, but doing so spends
substantial design and maintenance effort recreating systems already present in
a mature game engine.

Godot provides native desktop exports, a scene and resource pipeline, input actions, controller events, audio buses, 2D rendering and shaders, localization, UI layout, profiling, command-line export, headless execution, and a supported C# integration. C# supplies a strong type system and mature test ecosystem for a pure deterministic rules library. That combination fits the game's presentation ambition and QA requirements better than a larger Pygame coordinator.

## Qualification matrix

| Requirement | Python plus Pygame today | Godot 4 .NET target | Decision consequence |
| --- | --- | --- | --- |
| Native Windows, macOS, Linux | SDL supports them, but no self-contained reference artifact is qualified | First-class editor and export targets; hosted exports launch outside the checkout on all three platforms | Godot owns the product shell; protected signing and retained physical-platform review remain |
| Deterministic rules | Possible after major extraction | Pure C# assembly independent of Godot | Both can work; C# boundary is the target |
| Polished 2D effects | Possible through custom surfaces and shaders with added tooling | Built-in 2D renderers, shaders, animation, particles, profiling | Godot has lower presentation risk |
| Adaptive audio and mix | Current custom mixer is partial | Audio buses, effects, streams, and routing | Godot better fits radio and cue hierarchy |
| Input and remapping | Must build device lifecycle and prompt system | Input Map plus platform events, still requiring product logic | Godot supplies the lower layer |
| Responsive UI | Hand-built menus and font/layout paths | Control nodes, themes, focus, containers, localization | Godot lowers layout and accessibility risk |
| Packaging | PyInstaller per OS plus custom assets | Export presets and templates per target | Both need native release jobs; Godot owns more of the bundle |
| Automated headless QA | SDL dummy plus custom harness | Headless engine plus pure rules CLI | Both are viable; pure rules remains primary |
| Migration cost | No port | Port and parity work | Reference traces and vertical slices control this risk |
| Web export | Possible with a separate approach | C# web export is unsupported | Irrelevant because web is outside 1.0 scope |

## Target architecture

```mermaid
flowchart TD
    I[Godot input actions] --> A[Application coordinator]
    U[Godot UI and scenes] --> A
    A --> R[VibeSnake.Rules pure C#]
    A --> F[VibeSnake.Persistence]
    F --> R
    F --> UD[OS user-data replays]
    R --> S[RunState and stable hash]
    R --> E[Typed RunEvents]
    E --> P[Godot presentation adapters]
    P --> G[2D rendering and effects]
    P --> M[Audio buses and radio]
    D[Versioned content definitions] --> R
    Q[QA command-line runner] --> R
    T[xUnit, property, scenario, replay tests] --> R
    PS[Future platform save and content services] --> A
```

### Rules assembly

`VibeSnake.Rules` owns grid movement, command buffering, starvation, food, scoring, combo, powers, death resolution, ruleset definitions, seeded randomness, AI decision inputs, replays, and stable state hashes. It references only the .NET base class library and deliberately small audited packages, if any.

It must not reference Godot objects, nodes, vectors, resources, audio, files, clocks, environment variables, or platform APIs. Coordinates and colors at this boundary are project-owned value types.

### Godot application shell

Godot owns windows, display scaling, rendering, animation, shaders, particles, fonts, localization, input devices, action remapping, audio devices, buses, scene transitions, and platform lifecycle. Adapters convert logical commands into the rules engine and typed events into presentation. Presentation cannot mutate rules state directly.

### Platform services

`VibeSnake.Persistence` is the first implemented platform-neutral service assembly. It owns bounded replay file inspection, compatibility results, injected timestamps, and atomic storage beneath an absolute user-data root supplied by Godot. Future interfaces own profile saves, user preferences, content packs, logs, clipboard, screenshots, and other platform-specific paths. JSON schemas remain versioned and validated. The game uses Godot's user-data path only through these services, not from domain code.

## Project shape

```text
game/
  project.godot
  export_presets.cfg
  VibeSnake.Game.sln
  scenes/
  scripts/
  VibeSnake.Game.csproj
native/
  VibeSnake.slnx
  toolchain.json
  src/VibeSnake.Persistence/
  src/VibeSnake.Rules/
  tests/VibeSnake.Rules.Tests/
src/vibesnake/
tests/
```

The listed directories now exist. `game/` is the Godot presentation shell (product surface), `native/` owns engine-independent C# rules/persistence and tests (product kernel), and the existing Python paths remain the **behavior oracle** for dual-runtime fixtures - not a parallel product. Export presets and a Godot-required application solution define all three desktop targets. Deterministic direct-download and store-depot qualification shapes now exist. Remaining 0.3 work retains and physically reviews one exact three-platform Release build, approves the first export packs, and publishes the first native alpha. Protected signing and selected-store integration follow against an accepted candidate. None of this calls for new Python gameplay systems.

## Current qualification evidence

| Capability | Evidence now | Still required |
| --- | --- | --- |
| Stable tools | `global.json` pins exact .NET 10.0.303; `native/toolchain.json` pins Godot 4.7.1 plus official editor and .NET export-template hashes; bootstrap scripts verify both before installation | Verify upgrades only through dedicated qualification changes and the complete matrix |
| Pure rules | `VibeSnake.Rules` has no Godot reference and implements PCG32, bounded input, movement, food, growth, combo scoring, starvation, collision precedence, win state, all nine powers, immutable typed events, explicit restart, snapshots, strict canonical JSON schema 3 restoration with session achievement counters, explicit `vibesnake-core@4` identity, replay and ghost sessions, native AI personalities, progression/Tour contracts, and `fnv1a64-canonical-json-v4` hashes | Raise the measured rules branches to the roadmap's 90 percent 0.4 target and change product rules only through reviewed identities and fixtures |
| Replay persistence | `VibeSnake.Persistence` has no Godot dependency and implements bounded strict UTF-8 import, precise compatibility and resource-limited verification results, traversal-safe stored names, stable bounded browser summaries, cross-process transaction locking, idempotent hash matching, file-count and byte limits, same-directory atomic writes without overwrite, verified replay export, exact consent-bound deletion, four fixed household ghost slots, and closed privacy-safe run-summary and run-card export | Retain live replay and ghost review on every target platform and publish player-facing platform paths |
| Automated proof | 1,511 xUnit tests pass. The current local Windows Coverlet 10 report measures RepositoryChecks at 93.89/85.69 percent line/branch; Rules at 95.91/88.45 percent; Persistence at 93.27/87.10 percent; creator validation at 95.98/94.00 percent; AgentHost at 95.09/87.43 percent; AgentPlay at 92.92/85.27 percent; AgentViewer at 96.41/87.52 percent; and aggregate native coverage at 94.06/86.62 percent. The current 90 percent line and 85 percent branch module floors and missing-module rejection remain enforced; the roadmap retains 90 percent branch coverage as the 0.4 acceptance target. Repository policy, dependency-lock, station-badge, content-inventory, README screenshot, release-material structural qualification, release-rehearsal retained-record qualification, stable-promotion protected-record qualification, exact achievement-candidate and Last Stand fixture rendering/freshness, Agent Plugin, state-machine, replay, compatibility, integrity, resource, storage, audio/radio/broadcast, parity, onboarding, run-end, player-data, input-cadence, schema-7 settings, schema-2 local-playtest-summary, human-handoff, balance-experiment-guard, score-identity, score-browser, power-decision, replay-browser, offline-comparison, capture-sharing, candidate-reliability, candidate-fault, candidate-accessibility, visual-hierarchy, performance, Vibe Level, Classic/Vibe mode, adaptive-fairness, balance-laboratory, observed-baseline, native AI league, AI-personality, progression, content-curation, creator-content, localization, and post-1.0 agent-play qualifications pass | Execute V080-04 human progression and visual review plus V080-05 listening, loudness, and final content selection; human V070, localization, replay accessibility/platform, household comparison, clean-capture/trailer, agent spectator experience, and V090-06 accessibility-user review remain scheduled |
| Engine bridge | Godot imports the C# project, loads the rules and persistence assemblies, validates logical keyboard and controller defaults/remaps/cadence, exercises raw keyboard/controller completion through a six-section settings screen, handles focus-loss and last-controller-disconnect pause, records and atomically verifies a terminal replay in isolated user data, browses bounded replay summaries, loads verified deterministic playback in the background, exercises pause/step/seek/reset/return and clean capture, writes privacy-safe run summaries, retains terminal saves behind inspection, gates new runs during replay work, defers quit through save completion, bounds displayed diagnostics, runs deterministic restoration, renders all nine powers under one visual capacity and event-priority policy, resolves typed captions and cues, writes deterministic quiet/busy/warning/recovery/game-over review PNGs, plays finite PCM fallback cues, rejects warnings and leaks, and exits cleanly headlessly | Add physical-device evidence, retained live platform screenshots and clean captures, authored audio, and reviewed feel on all three systems |
| Localization | `ShellLocalization` owns 734 stable English IDs with 114 exact named-parameter templates across thirteen supported shell flows plus preview-only Agent Arena watch copy, runtime statuses, typed feedback, stable broadcast captions, optional lore, and offline comparison copy. The real Godot smoke deterministically exercises `qps-ploc`, at least 1.3125 expansion, accented and fallback glyph coverage, glyph-parameter preservation, logical-canvas fit at 150 percent text, seven composed Agent Arena overlay rows under worst-case pseudo-localized content, exact onboarding and broadcast ID resolution, and zero direct draw, prompt, static status, composed status, or audited domain-presentation expressions. | Retain keyboard and all controller-family visible review on Windows, macOS, and Linux. Additional release locales are optional and require native-speaker review. |
| CI definition | Hosted Python-oracle, native rules, Godot smoke, native export, packaged-player smoke, inspection, release-matrix, and artifact-upload jobs pass on Windows, macOS, and Linux from the canonical remote | Manually dispatch and retain the first complete three-platform Release/provenance run, then retain the exact tagged run |
| Player artifacts | Hosted Windows x64, macOS Universal, and Linux x64 Debug bundles launch outside the checkout without Python and pass read-only install, fresh external profile/log, non-ASCII path, lifecycle, deterministic-state, payload, prohibited-content, and per-file hash checks. A Windows x64 Release bundle also passes locally. The schema 3 inspector requires and path-scans the Rules, Persistence, and Game payloads, binds the editor executable to the pinned archive, and requires Release artifacts to prove Agent Arena preview exclusion. The output gate proves deterministic qualification packaging. Exact byte and checksum identities stay in each build's artifact manifest instead of being copied into this strategy document. | Retain one exact three-platform Release build, physically review its input, audio, display, content, and user-data behavior, approve content, then review the published downloads |
| Content boundary | Schema 1 policy and generated inventory account for 114 public assets and 342,510,815 bytes (including 95 radio MP3s) with exact hashes, bounded integrity checks, duplicates, roles, pack intent, rights status, and zero current export approvals | Complete loudness and listening review for radio, production credits, first approved core and radio manifests, and allow exports to read only those approved allowlists |

## Rules and rendering cadence

- Keep a fixed rules cadence, initially matching the current configured 0.05-second grid tick.
- Sample logical commands into a bounded queue and consume them only at rules steps.
- Render independently at the display cadence and interpolate presentation where useful.
- Keep animation, camera, particles, and audio on presentation clocks.
- Store integer rules steps in replays rather than floating-point wall time.
- Never change rules speed because render frames were missed.

The target is a stable 60 frames per second presentation on the published minimum hardware, with correct rules still maintained when rendering is slower or faster. Performance gates use frame-time percentiles and fixed-step throughput on controlled hardware.

## Rendering strategy

- Begin with Godot's Compatibility renderer to cover a wider range of desktop hardware for a 2D game.
- Use a logical 1280 by 720 viewport, aspect-preserving scaling, and tested letterbox or pillarbox behavior.
- Keep gameplay coordinates independent of window and monitor coordinates.
- Put world, gameplay indicators, effects, HUD, modal UI, and accessibility overlays in explicit layers.
- Centralize fonts in a theme and define fallback coverage before localization.
- Implement Vibe Level effects through one presentation state, with reduced-motion and flash-free variants from the start.
- Profile batches, draw calls, shader compilation, particles, allocations, and frame percentiles before adding more effects.

Forward+ is not required for this game. A renderer change needs a visible benefit and full platform evidence.

## Audio strategy

Use explicit buses for Master, Music, SFX, UI, Voice, and Accessibility. The native slice currently registers Music, SFX, and UI under Master, persists independent gain/mute controls, and synthesizes 31 finite essential offline cues. A playback-free allocator enforces 8 SFX and 4 UI voices, per-cue cooldown/polyphony, priority, stable interruption, expiry, and strongest-active music ducking. The fallback catalog gives navigation, combo tiers/break, restart, achievement, both death causes, and every power activation unique measured PCM identities under an executable provenance, license, and peak policy. A production-used multimodal policy pairs those cues with exact hunger time and segmented geometry, readable combo count/multiplier, stable power identities and states, explicit protection telegraphs, and distinct collision/starvation text and symbols. Optional playback fails closed through a bounded retry policy with persistent visual status, sparse local diagnostics, automatic graph repair, one-second output-topology polling, and recovery on a later cue. Headless evidence covers policy decisions without playback plus real bus routing, duck/restore, immediate isolated saved volumes, 992 rapid retriggers, full-catalog mute suppression, device-change repair, injected missing-bus failure, retry backoff, recovery, cache bounds, cleanup, rules isolation, and five multimodal mute/accessibility profiles. This is control-flow evidence through Godot's Dummy backend, not a physical-device or listening claim. Voice, authored assets, caption feel, haptic execution, and physical readability remain qualification work. Radio tracks use streamed formats and do not load the entire library into memory.

The core download includes a small curated audio set. The full radio is one or more optional, manifest-validated packs. Content tools verify decode, duration, loudness, clipping, metadata, rights, and hashes before export. Archived tracks, prompts, lyrics working files, and production reports never enter export inputs.

Adaptive music begins with authored station material and event-driven layers or stingers. Runtime music generation is outside 1.0.

## Input strategy

- Define logical actions for movement, confirm, back, pause, radio, inspect, screenshot, and accessibility shortcuts.
- Support keyboard-only and common controller-only completion of every required flow.
- Track devices by stable runtime identity and handle add, removal, and remap events.
- Use action glyph families, not hard-coded button numbers.
- Persist remaps by logical action with schema migration and conflict resolution.
- Retain the bounded shared deadzone and digital fallback; define repeat delay, repeat rate, simultaneous-device policy, and any evidence-driven per-device calibration.
- Test Xbox-layout and PlayStation-layout devices on all three platforms where drivers permit.

The native slice centralizes movement, confirm, back, pause, help, settings, and quit actions; registers arrows, WASD, D-pad, sticks, face buttons, shoulders, triggers, Start, and platform quit shortcuts; persists keyboard/controller remaps; switches Xbox, PlayStation, Nintendo, and generic prompt families after deliberate input; rejects drift; resolves conflicts explicitly; handles hot-plug notices; and pauses without accepting hidden movement on focus loss. Preferences schema 7 persists a 10 to 90 percent shared stick deadzone, preserves D-pad digital fallback, owns Master-bus mono downmix, and exposes the Vibe adaptation opt-out plus default-off local playtest consent. Vector prompt badges retain text labels and render through one qualified palette/font owner on every required shell flow including onboarding. Per-device calibration and physical controller evidence remain open.

## Determinism and replay

Implement a project-owned, versioned pseudo-random algorithm such as PCG32 for gameplay and AI streams. Do not use `System.Random` as a replay contract. Store algorithm ID, stream state, ruleset, rules version, content version, seed, and commands in each replay.

The state hash uses a canonical field order and excludes presentation, logs, platform paths, and wall-clock timestamps. The Python reference and C# engine must normalize known legacy differences and compare after every step during migration.

## Test stack

- xUnit 2.9.3 for pure C# unit and integration tests.
- A property-based C# library selected and pinned during qualification.
- Shared JSON scenario and replay fixtures.
- A custom headless campaign runner that emits the same report concepts as `python -m vibesnake.qa`.
- Godot headless scene, input, resource, and render smoke tests.
- Native exported-artifact tests on Windows, macOS, and Linux runners.
- Content validators outside the runtime.

No test framework plugin may become the rules architecture. The pure C# suite must run with `dotnet test` without opening Godot.

## Supported 1.0 artifacts

| Platform | 1.0 artifact | Required architecture |
| --- | --- | --- |
| Windows | Signed x64 executable bundle plus installer or archive | x86-64 |
| macOS | Developer ID-signed and notarized application bundle or disk image | Universal binary for Apple Silicon and Intel, if the pinned export templates continue to support both |
| Linux | x86-64 application bundle or archive with desktop integration guidance | x86-64 |

Minimum operating-system and driver versions are not claimed until tested against the pinned Godot and .NET support floors. Every platform is built and smoke-tested on a native runner. Platform-specific signing credentials remain outside the repository.

## Technology qualification gate

The source-product stack is accepted because the automated vertical slice proves the structural items below. Items that require named hardware, physical devices, approved content, or signing remain release-acceptance gates rather than reasons to send new work back to Python.

1. A pure C# core runs without Godot and produces stable hashes.
2. At least 100 shared movement, food, growth, starvation, score, and collision traces match the Python reference or have reviewed corrections.
3. A Godot scene completes menu, run, death, and restart using only the C# rules boundary.
4. Keyboard and controller actions remain correct through pause, focus loss, and hot-plug.
5. The 1280 by 720 logical viewport scales correctly at minimum, 16:9, 4:3, 16:10, ultrawide, square, and high-density displays.
6. Radio streaming, bus ducking, missing audio, and device loss recover cleanly.
7. Windows x64, macOS Universal, and Linux x86-64 exports launch outside the checkout and write to the correct user location.
8. Controlled performance scenes meet the 60 frames per second presentation target without rules drift and report p50, p95, and p99 frame times.
9. The headless rules campaign runs substantially faster than real time and retains a failing seed and trace.
10. Exported content contains only the allowlisted core pack and no development or archived material.

If a gate fails, first fix the vertical slice. A fallback to Pygame requires evidence that the failure is inherent to the target stack and that the incumbent can meet the same three-platform, presentation, accessibility, packaging, and QA contracts with lower total risk.

## Migration sequence

1. Freeze Python rules behavior with scenarios, replay-like action traces, and documented intentional defects.
2. Scaffold the pinned Godot .NET project, pure rules solution, tests, export presets, and empty platform artifacts.
3. Port movement, command buffering, food, starvation, scoring, collision, and stable random streams.
4. Pass shared trace parity and decide each mismatch as a Python defect, compatibility behavior, or migration defect.
5. Build one complete vertical slice with accessible input, viewport, basic audio, death, and restart.
6. Port each power as one contract with differential fixtures and cleanup tests.
7. Port progression, saves, radio manifests, AI, menus, cosmetics, and presentation behind services.
8. Run cross-platform artifact, performance, input, audio, accessibility, and reliability gates.
9. Make Godot the default after the automated feature, data, and artifact foundation passes: complete for source launch. Preserve the Python reference for fixture reproduction until the first stable release no longer needs it.

This sequence avoids a blind rewrite. Every ported slice is runnable, testable, and comparable.

## Dependency and upgrade policy

- Pin the Godot editor, export templates, .NET SDK feature band, NuGet lock file, and content-tool versions.
- Prefer LTS .NET and a supported Godot minor line.
- Review generally available releases before a public build. The 2026-08-13 security review keeps Godot 4.7.1 stable and advances the exact .NET 10 LTS SDK to 10.0.303; Godot 4.8 development snapshots and .NET 11 previews are not release inputs.
- Pin GitHub Actions to immutable commits after checking the corresponding stable release tag. The current baseline is checkout 7.0.1, setup-python 7.0.0, setup-dotnet 6.0.0, cache 6.1.0, upload-artifact 7.0.1, download-artifact 8.0.1, and attest 4.2.2.
- Record checksums for downloaded build tools and templates.
- Use minimal third-party runtime packages and maintain license and vulnerability inventories.
- Upgrade engine or SDK in a dedicated change with the full scenario, artifact, render, and platform matrix.
- Keep secrets, certificates, notarization credentials, and store credentials in protected release environments only.

## Primary references

- [Godot 4.7.1 maintenance release](https://godotengine.org/article/maintenance-release-godot-4-7-1/): current stable patch, commit identity, and upgrade guidance.
- [Godot 4.7 C# platform support](https://docs.godotengine.org/en/4.7/tutorials/scripting/c_sharp/index.html): C# projects support Windows, Linux, and macOS desktop exports.
- [Godot project export documentation](https://docs.godotengine.org/en/4.7/tutorials/export/exporting_projects.html): export presets, templates, resource filters, and command-line artifact generation.
- [Godot headless command-line documentation](https://docs.godotengine.org/en/latest/tutorials/editor/command_line_tutorial.html): headless display and audio drivers, automated export, benchmarks, and fixed movie output.
- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy): .NET 10 is the active LTS line in the current review and receives a longer support window than .NET 8.
- [.NET `global.json` policy](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json): SDK version, stable-only selection, search paths, and patch roll-forward behavior.
- [SDL platform support](https://wiki.libsdl.org/SDL2/Introduction) and [PyInstaller multi-OS guidance](https://pyinstaller.org/en/stable/usage.html#supporting-multiple-operating-systems): the incumbent's lower layer is portable, but executable bundles still need a separate build on each operating system.
- [Apple notarization requirements](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution): directly distributed macOS software needs signed, hardened, notarized, and tested release handling.
- [Microsoft SignTool](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool): Windows signing and verification belong in the native release pipeline.
