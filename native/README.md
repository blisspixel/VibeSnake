# Native Foundation

This directory contains the engine-independent C# foundation for the Godot migration. The current Python and Pygame game remains the behavior reference until the parity and artifact gates in the main roadmap pass.

## Layout

```text
native/
|-- src/VibeSnake.Rules/          Pure deterministic rules with no Godot reference
|-- src/VibeSnake.Persistence/    Bounded replay files, import, and atomic storage
|-- tests/VibeSnake.Rules.Tests/  xUnit rules, replay, parity, and storage contracts
|-- toolchain.json                Exact qualified tool versions and local evidence
`-- VibeSnake.slnx                Native solution

game/
|-- project.godot                 Godot application shell
|-- export_presets.cfg            Windows, Linux, and macOS export definitions
|-- VibeSnake.Game.sln            Solution required by Godot .NET export
|-- VibeSnake.Game.csproj         Godot C# project
|-- scenes/                       Scene resources
`-- scripts/                      Presentation and input adapters
```

## Commands

From the repository root:

```powershell
./scripts/test_native.ps1
./scripts/test_native_export.ps1 -GodotExecutable "C:\path\to\Godot_console.exe"
```

The first command qualifies the rules, formatting, coverage, editor import, and scene smoke. The second installs checksum-verified templates when needed, exports outside the checkout, launches the packaged player, and writes a validated SHA-256 artifact manifest. The repository-local SDK and editor are developer caches and are ignored. See [the development guide](../docs/guides/DEVELOPMENT.md) for setup and exact contracts.

## Current scope

The rules kernel currently proves seeded PCG32 randomness, bounded direction input, fixed-step wraparound movement, food placement and growth, combo interpolation, speed and length scoring, exact starvation, collision precedence, grid completion, pure C# contracts for all nine powers (Shield, Phase Shift, Last Stand, Slow-Mo, Boost, Magnet, Bait, Gluttony, Segment Detach), `RulesCadenceClock` wall-clock tempo intervals, immutable ordered events, explicit restart, detached snapshots, canonical JSON schema 2 serialization and strict restoration, explicit `vibesnake-core@4` identity, schema 1 replay compatibility, resource-bounded verification, and `fnv1a64-canonical-json-v3` state hashes. Shared Python-to-C# fixtures cover movement, core rules, Shield, Phase Shift, Last Stand, and remaining powers, with automated delta reduction on failing movement prefixes. Generated operation campaigns repeatedly restore and continue runs across terminal and restart boundaries. The live recorder preserves rejected input attempts, checks each Godot step against a private rules mirror, and fails closed on divergence or bounds. The persistence assembly verifies, serializes saves across processes, atomically stores, strictly reloads, and read-only inspects replays without adding files or clocks to the rules kernel. The Godot shell proves logical keyboard and any-controller defaults, focus-loss pause safety, full nine-power markers, composite HUD, head outlines, hazards, multi-power captions, Slow-Mo/Boost cadence drain, fallback audio, replay recording, bounded background latest-file verification, lossless terminal-save queuing, run-start gating, save-aware quit, bounded drop import, and that the .NET 10 assemblies can build, run, export, and launch inside the pinned engine line. Host-dependent pure rules throughput evidence is written to `TestResults/native/rules_throughput.json`. The current Windows x64 debug bundle also passes isolated user-data, required Rules, Persistence, and Game payload, portability, no-Python, no-export-lock, engine-warning, leaked-object, and complete hash-inventory checks.

This is qualification infrastructure, not final product polish. Near misses, replay browsing and playback, AI, profile and progression persistence, remapping UI, authored audio, radio, accessibility settings, pack export eligibility, physical-controller evidence, presentation frame-time measurement on declared hardware, and production presentation remain in later roadmap work packages. macOS and Linux artifact smokes exist in hosted CI; store-ready packaging and signing do not. See [the replay contract](../docs/engineering/REPLAYS.md) and [user-data directories](../docs/engineering/USER_DATA.md) for exact boundaries.
