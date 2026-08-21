# Repository Map

## Top level

| Path | Ownership |
| --- | --- |
| [README.md](../../README.md) | Concise product entry point and document links |
| [CONTRIBUTING.md](../../CONTRIBUTING.md) | Contribution workflow and definition of done |
| [VERSION](../../VERSION) | Canonical product SemVer shared by native builds and release tags |
| [pyproject.toml](../../pyproject.toml) | Python package metadata and PEP 440 version, `vibesnake` console command, build backend, pytest, coverage, and Ruff policy |
| [requirements.txt](../../requirements.txt) | Runtime dependencies |
| [requirements-dev.txt](../../requirements-dev.txt) | Human-edited development dependency constraints |
| [requirements-ci.lock](../../requirements-ci.lock) | Universal Python 3.11 through 3.14 graph with exact versions and SHA-256 hashes |
| [requirements-runtime.lock](../../requirements-runtime.lock) | Minimal hash-locked player graph for Python 3.11 through 3.14 |
| [requirements-runtime.txt](../../requirements-runtime.txt) | Human-edited player and local build constraints used to generate the runtime lock |
| [.pre-commit-config.yaml](../../.pre-commit-config.yaml) | Repository-owned local quality hooks with no mutable remote hook dependencies |
| [global.json](../../global.json) | Stable .NET 10 SDK selection and local-tool lookup policy |
| [Directory.Build.props](../../Directory.Build.props) | Shared C# warnings, nullability, language, and deterministic-build policy |
| [LICENSE](../../LICENSE) | Apache License 2.0 terms |
| [NOTICE](../../NOTICE) | Project and original-content attribution |
| [config/content_policy.json](../../config/content_policy.json) | Human-reviewed source asset classification, rights status, and shipping policy |
| [config/content_inventory.json](../../config/content_inventory.json) | Generated deterministic file hashes, sizes, integrity results, and export eligibility |
| [config/content_curation_v1.json](../../config/content_curation_v1.json) | Exact per-station pending, approved, and rejected content decisions bound to the inventory policy |
| [docs/design/LOCALIZATION.md](../design/LOCALIZATION.md) | Stable shell-copy, pseudo-locale, glyph, layout, and translator handoff contract |

## Frozen Python oracle

```text
src/vibesnake/
|-- __main__.py             Process entry point
|-- cli.py                  play, update, status, doctor, version commands
|-- checkout.py             GitHub main checkout helpers for updates
|-- update.py               Fast-forward reinstall from GitHub main
|-- ai/player.py            Personality schema, loading, and decisions
|-- audio/                  SFX and radio management
|-- core/                   Game coordinator and core models
|-- data/                   Config, settings, and data-path resolution
|-- input/                  Keyboard, mouse, and gamepad routing
|-- powerups/               Base class, manager, and nine types
|-- content/                Asset inventory, pack validation, compatibility, and release blockers
|-- qa/                     Simulation, invariants, reports, dependency lock, shared fixtures, and CLI
|-- rendering/              HUD, menus, adaptive display, theme, backgrounds, and effects
`-- utils/                  Logging setup
```

High-change files and their reason:

- [core/game_state.py](../../src/vibesnake/core/game_state.py): composition root and main loop.
- [rendering/menus.py](../../src/vibesnake/rendering/menus.py): all non-gameplay screens.
- [core/snake.py](../../src/vibesnake/core/snake.py): movement plus cosmetic rendering.
- [rendering/visual_effects.py](../../src/vibesnake/rendering/visual_effects.py): feedback and procedural environments.

Read [ARCHITECTURE.md](ARCHITECTURE.md) before restructuring these files.

## Native product

```text
game/
|-- project.godot             Godot 4.7.1 project and fixed presentation settings
|-- export_presets.cfg        Windows x64, Linux x64, and macOS Universal presets
|-- VibeSnake.Game.sln        Application solution required by Godot .NET export
|-- VibeSnake.Game.csproj     Godot C# shell, rules, and persistence references
|-- scenes/Main.tscn          Qualification entry scene
|-- scripts/GameActions.cs    Logical keyboard, controller, and replay defaults
|-- scripts/AudioFallback.cs  Finite PCM cues, bounded multi-voice mix policy, and resource lifecycle
|-- scripts/RadioStreamPlayer.cs  Validated one-track MP3 decoder adapter and policy recovery bridge
|-- scripts/RadioQualification.cs  Manifest, behavior, input, RNG-isolation, and missing-pack evidence
|-- scripts/BroadcastQualification.cs  Station identity, boundary, ducking, fatigue, caption, and isolation evidence
|-- scripts/ModeContractQualification.cs  Classic/Vibe identity, mechanics, input-route, and category evidence
|-- scripts/AdaptiveFairnessQualification.cs  Vibe DDA bounds, opt-out, metadata, determinism, and category evidence
|-- scripts/PowerDecisionQualification.cs  Nine-power families, offer/HUD, lifecycle, synergy, and experiment-gate evidence
|-- scripts/PowerDecisionRunTrace.cs  Local-only aggregate per-power lifecycle counters
|-- scripts/ReplayBrowserQualification.cs  Metadata/status, speed/HUD, export/delete, input, and isolation evidence
|-- scripts/ProgressionQualification.cs  Goals, cosmetics, Tour, input-route, persistence, and isolation evidence
|-- scripts/ShellLocalization.cs  Stable English copy IDs, strict parameters, deterministic pseudo-locale, and evidence schema
|-- scripts/StepFeedback.cs   Typed event-to-cue and persistent-caption priority
|-- scripts/MultimodalFeedback.cs  Hunger/combo/power/death presentation contract and profile evidence
|-- scripts/VisualHierarchy.cs  Production visual budgets, priority, contrast, and review-frame evidence
|-- scripts/PerformanceQualification.cs  Effect profiles, full-board stress shape, frame statistics, and budgets
|-- scripts/VibeLevelDirector.cs  Sole combo-escalation authority, subsystem budgets, and fixed-scene evidence
`-- scripts/Main.cs           Action routing, replay/progression integration, drawing, lifecycle, and headless smoke adapter

native/
|-- VibeSnake.slnx            Native solution
|-- toolchain.json            Exact SDK, engine, editor and template hashes, renderer, and cadence pins
|-- src/VibeSnake.Rules/      Engine-independent rules, product modes, AI personalities, power decisions, progression/Tour catalogs, canonical state, and restore boundary
|-- src/VibeSnake.Persistence/  Bounded replay/storage, progression, and local summaries plus pure audio, radio, and broadcast policies
|-- src/VibeSnake.AgentPlay/  Transport-neutral step and bounded-burst external-agent sessions, public observations, experience contracts, and verified replay ownership
|-- src/VibeSnake.AgentViewer/  Read-only same-user pipe client and public snapshot projection
|-- tools/VibeSnake.AgentHost/  Local stateless-era stdio MCP adapter, capacity and idle-bounded session registry, replay save, and read-only viewer server
|-- tools/RepositoryChecks/  Native repository-policy and dependency-lock qualification command
|-- tools/ValidateCreatorContent/  Data-only personality and canonical pack-set validation command
`-- tests/VibeSnake.Rules.Tests/  xUnit parity, restore, replay, storage, and generated state-machine contracts

```

Progression-specific native ownership:

- [ProgressionCatalog.cs](../../native/src/VibeSnake.Rules/ProgressionCatalog.cs): exact goals, lanes, pacing bands, metric projection, and highlighted-goal rules.
- [CosmeticSetCatalog.cs](../../native/src/VibeSnake.Rules/CosmeticSetCatalog.cs): eight curated presentation-only sets and exact expression-reward requirements.
- [StationIdentityCatalog.cs](../../native/src/VibeSnake.Rules/StationIdentityCatalog.cs): eight stable station presentation identities without broadcast approval or scheduling policy.
- [AgentIdentity.cs](../../native/src/VibeSnake.AgentPlay/AgentIdentity.cs): Passport v4 plus the closed agent accent catalog and catalog-validation boundary.
- [AgentExperience.cs](../../native/src/VibeSnake.AgentPlay/AgentExperience.cs): episode metrics plus the versioned two-criterion Style Contract catalog and public progress and outcome records.
- [AgentLessonEvidence.cs](../../native/src/VibeSnake.AgentPlay/AgentLessonEvidence.cs): eight two-requirement Signal School practices, bounded opposite-reversal witnesses, independent replay and attempt evaluation, factual outcomes, successful-completion guidance, and fresh-session retry descriptors only for incomplete or failed-closed practice.
- [AgentStyleEvidence.cs](../../native/src/VibeSnake.AgentPlay/AgentStyleEvidence.cs): bounded rules-advanced-step style facts, structural-exit geometry, and independent replay reconstruction for factual composite outcomes.
- [BroadcastTourCatalog.cs](../../native/src/VibeSnake.Rules/BroadcastTourCatalog.cs): four tiers and twelve dependency-gated event contracts.
- [BroadcastTourSession.cs](../../native/src/VibeSnake.Rules/BroadcastTourSession.cs): fixed-seed practice construction and exact terminal primary/style evaluation.
- [ProgressionDocument.cs](../../native/src/VibeSnake.Persistence/ProgressionDocument.cs): strict atomic progression, reward, Tour, cosmetic selection, and loadout persistence.
- [ContentCreditsDocument.cs](../../native/src/VibeSnake.Persistence/ContentCreditsDocument.cs): deterministic human-readable credits and third-party notices generated only from validated manifests.

```text
scripts/
|-- content_inventory.py        Generate or verify the source asset inventory
|-- content_packs.py            Qualify canonical core and optional pack manifests
|-- assemble_radio_pack.py      Build one approved deterministic radio archive
|-- assemble_unsigned_preview.py  Join the qualified players, provenance, and radio pack
|-- install_player.ps1/.sh      Legacy frozen-Python reference bootstrap
|-- assert_godot_toolchain.ps1  Checksum-bound pinned editor-build gate
|-- native_artifact_policy.ps1  Shared prohibited native-bundle path rules
|-- platform_path_policy.ps1    Absolute environment-path policy for tooling
|-- test_powershell_gates.ps1   Toolchain and artifact-policy regressions
|-- install_godot.ps1             Checksum-verified editor bootstrap
|-- install_godot_templates.ps1   Selective checksum-verified export-template bootstrap
|-- test_native.ps1               Rules, coverage, balance/AI evidence, import, and scene smoke
|-- package_agent_plugin.ps1      Assemble and checksum the framework-dependent preview Agent Plugin
|-- package_agent_host.ps1        Assemble the current-RID unsigned self-contained Agent Host package
|-- validate_agent_host_package.py Enforce the AA-10 host-package manifest, inventory, provenance, and checksum contract
|-- validate_agent_plugin.py      Validate source and packaged Agent Plugins 1.0.0 manifests, launch containment, completeness, and checksums
|-- generate_agent_knowledge.py   Generate or drift-check the Open Knowledge Format 0.2 bundle
|-- test_native_export.ps1        Outside-checkout packaged-player smoke
`-- inspect_native_artifact.ps1   Payload, portability, and SHA-256 manifest gate

play.ps1 / play.sh / play.bat     Verify, build, and launch the native Godot game
```

The Godot and C# paths are the default source-playable product. The Python package remains a frozen behavior oracle, fixture producer, and optional migration reference. New product behavior belongs in `game/` and `native/`.

The optional post-1.0 interoperability source lives under `integrations/vibesnake-agent-plugin/` and `integrations/vibesnake-agent-knowledge/`. `integrations/agent-interop-baseline.json` is the machine-readable authority for reviewed MCP, Agent Plugins, Agent Skill, MCP Apps, and OKF pins, versions, schema digests, and review dates. The checked-in plugin directory contains the source manifest and skill; `scripts/package_agent_plugin.ps1` publishes the host and generates the root `mcp.json` only in its isolated output. The source forms are present in `player-latest`, but neither the assembled plugin nor any Agent Arena entry point is part of the supported 1.0 player artifact. See [agent play integration](AGENT_PLAY.md).

Persistence and configuration boundaries:

- [core/player_profile.py](../../src/vibesnake/core/player_profile.py): lifetime statistics and achievement state.
- [core/customization.py](../../src/vibesnake/core/customization.py): active appearance and loadouts.
- [core/high_scores.py](../../src/vibesnake/core/high_scores.py): canonical top-ten leaderboard and legacy import.
- [core/user_settings.py](../../src/vibesnake/core/user_settings.py): audio and fullscreen preferences.
- [data/json_store.py](../../src/vibesnake/data/json_store.py): atomic JSON writes and corrupt-file backups.
- [data/paths.py](../../src/vibesnake/data/paths.py): user-data location and checkout-save migration.
- [data/config.py](../../src/vibesnake/data/config.py): schema-versioned runtime configuration loading and validation.

## Assets

```text
assets/
|-- ai/                     Built-in support files, custom definitions, examples
|-- audio/                  Production metadata plus public radio MP3s under audio/radio/
|-- config/config.json      Runtime configuration overlay
`-- images/                 Logo and deterministic radio badges
```

The Python oracle directly depends on `assets/`. Native release exports admit only manifest-bound, export-eligible content, which is why the optional radio pack remains absent while `exportEligible` is zero. [CONTENT_PIPELINE.md](../content/CONTENT_PIPELINE.md) documents the generated inventory, rights gate, and measured source debt. [CONTENT_PACKS.md](../content/CONTENT_PACKS.md) defines the implemented manifest, allowlist, compatibility, and optional-failure contract for the native boundary.

## Tests

```text
tests/
|-- audio/                  Radio discovery and playback orchestration
|-- core/                   Models, scoring, persistence, achievements, snake rendering
|-- input/                  Input mapping and device routing
|-- integration/            Game initialization, rendering, HUD, and gameplay flows
|-- powerups/               Deterministic class-level power-up behavior
|-- qa/                     Property, invariant, policy, simulation, report, and CLI tests
|-- fixtures/shared/        Python-generated JSON consumed by native parity tests
|-- rendering/              Menus, particles, and backgrounds
`-- test_*.py               Cross-cutting and legacy deterministic tests
```

All files collected under `tests/` are deterministic automated checks. Manual and perceptual tools live under `scripts/manual/`; broken duplicate validators and import-time external-service runners have been removed.

## Tools and production data

- `scripts/`: deterministic quality, content, screenshot, badge, toolchain, and native-artifact utilities.
- `config/radio_network_plan.json`: production plan for radio content.
- `data/`: legacy source-checkout saves and generation history. Normal player saves now live in the operating system's user-data directory and should never be committed.
- `archive/`: ignored, recoverable local audio candidates, retired production
  tooling, raw research, superseded documentation, and private production
  history. It is not public project documentation or release tooling.
- `docs/research/`: durable source pointers and research-handling policy.

## Generated local artifacts

`.coverage*`, `coverage*.xml`, `coverage*.json`, `TestResults/`, `.pytest_cache/`, `.ruff_cache/`, `.dotnet/`, `.tools/`, `.godot/`, `bin/`, `obj/`, build outputs, logs, virtual environments, and Python bytecode are generated. They are not source documentation and should remain ignored.
