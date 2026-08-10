# Documentation Hub

This directory is the indexed source of truth for Vibe Snake. The root [README](../README.md) is the concise product entry point. The root [roadmap](../ROADMAP.md) owns ordered work through 1.0, and the root [changelog](../CHANGELOG.md) records shipped behavior.

## Start by goal

| Goal | Start here | Continue with |
| --- | --- | --- |
| Play the current alpha | [Player guide](guides/PLAYER_GUIDE.md) | [Accessibility features](guides/ACCESSIBILITY.md), [save and recovery](guides/RECOVERY.md), [privacy](../PRIVACY.md), [input and lifecycle](design/INPUT.md), [configuration](guides/CONFIGURATION.md) |
| Understand the game vision | [Game design](design/GAME_DESIGN.md) | [Fun strategy](design/FUN_DESIGN.md), [observed balance baselines](design/BALANCE_BASELINES.md), [balance experiments](design/BALANCE_EXPERIMENTS.md), [local playtest summaries](design/PLAYTEST_SUMMARIES.md), [human playtesting](design/HUMAN_PLAYTESTING.md), [power-ups](design/POWERUPS.md), [progression](design/PROGRESSION.md) |
| See what exists and what is next | [Current status](release/STATUS.md) | [Roadmap](../ROADMAP.md), [release checklist](release/RELEASE_CHECKLIST.md), [manual product matrix](release/MANUAL_PRODUCT_MATRIX.md), [external validation](release/EXTERNAL_VALIDATION.md), [release rehearsal](release/REHEARSAL.md), [stable promotion](release/STABLE_PROMOTION.md), [changelog](../CHANGELOG.md) |
| Change the Python reference | [Architecture](engineering/ARCHITECTURE.md) | [Development](guides/DEVELOPMENT.md), [quality standards](engineering/CODE_QUALITY_STANDARDS.md), [testing](engineering/TESTING.md) |
| Build the native successor | [Technology strategy](decisions/TECHNOLOGY_STRATEGY.md) | [ADR 0001](decisions/ADR_0001_NATIVE_RUNTIME.md), [replay contract](engineering/REPLAYS.md), [parity decisions](engineering/PARITY_DECISIONS.md), [native foundation](../native/README.md) |
| Extend automatic game testing | [Automated QA laboratory](engineering/AUTOMATED_QA.md) | [Testing](engineering/TESTING.md), [parity decisions](engineering/PARITY_DECISIONS.md) |
| Work on music or assets | [Audio system](content/AUDIO.md) | [Content pipeline](content/CONTENT_PIPELINE.md), [content packs](content/CONTENT_PACKS.md) |
| Find a file or owner | [Repository map](engineering/REPOSITORY_MAP.md) | [Architecture](engineering/ARCHITECTURE.md), [contributing](../CONTRIBUTING.md) |

## Information architecture

```text
docs/
|-- README.md             This index and documentation policy
|-- guides/               Player and contributor procedures
|-- design/               Rules, experience, progression, input, and AI intent
|-- engineering/          Architecture, quality, testing, QA, parity, and ownership
|-- content/              Audio, asset rights, inventories, and pack contracts
|-- decisions/            Accepted architecture decisions and target stack
|-- release/              Evidence-backed status and release gates
`-- research/             Durable source pointers that do not prove implementation
```

The root keeps the four project-wide entry artifacts: `README.md`, `ROADMAP.md`, `CHANGELOG.md`, and `CONTRIBUTING.md`. This makes the project state visible without requiring readers to know the documentation tree first.

## Guides

- [Player guide](guides/PLAYER_GUIDE.md): installation, controls, scoring, survival, radio, and troubleshooting.
- [Accessibility feature guide](guides/ACCESSIBILITY.md): exact native feature support, settings, automated evidence, and human-review boundaries.
- [Save and recovery guide](guides/RECOVERY.md): separated reset, verified backup, conflict-safe restore, diagnostics, and local removal.
- [Development guide](guides/DEVELOPMENT.md): supported toolchains, setup, commands, and contribution workflow.
- [Configuration](guides/CONFIGURATION.md): runtime configuration, environment variables, and save locations.

## Game and experience design

- [Game design](design/GAME_DESIGN.md): experience pillars, core loop, systems, and balance principles.
- [Fun and player experience strategy](design/FUN_DESIGN.md): run escalation, choice depth, progression, radio, spectator play, lore, and human validation.
- [Observed balance baselines](design/BALANCE_BASELINES.md): fixed-seed AI distributions, reproducibility contract, and human-target boundary.
- [Balance experiment discipline](design/BALANCE_EXPERIMENTS.md): target-first, one-family tuning registry and keep/revert evidence contract.
- [Local playtest summaries](design/PLAYTEST_SUMMARIES.md): exact opt-in balance facts, privacy exclusions, export, retention, and deletion contract.
- [Structured human playtesting](design/HUMAN_PLAYTESTING.md): cohorts, stages, shared scenarios, recovery matrix, privacy, findings, and exit gates.
- [World and broadcast bible](design/WORLD_BIBLE.md): foundation canon, station institutions, rival identities, Broadcast Tour, and no-waste narrative rules.
- [Power-ups](design/POWERUPS.md): exact effects, collision precedence, lifecycle, and verification contracts.
- [Progression and save data](design/PROGRESSION.md): achievements, cosmetics, leaderboards, persistence, and recovery.
- [Score identity and achievement audit](design/SCORE_IDENTITY.md): run-purpose and seed taxonomy, schema-2 score metadata, legacy categories, and all 25 mode decisions.
- [Input and lifecycle](design/INPUT.md): logical actions, controller defaults, focus behavior, remapping, and proof boundaries.
- [AI players](design/AI_PLAYERS.md): personality schema, loading, behavior, and extension points.

## Engineering

- [Architecture](engineering/ARCHITECTURE.md): runtime components, state flow, data flow, and technical debt.
- [Repository map](engineering/REPOSITORY_MAP.md): directory ownership and important files.
- [Code quality standards](engineering/CODE_QUALITY_STANDARDS.md): implementation, security, performance, evidence, and review requirements.
- [Testing](engineering/TESTING.md): deterministic suites, coverage policy, manual checks, and CI.
- [Automated QA laboratory](engineering/AUTOMATED_QA.md): seeded simulation, policies, invariants, reports, balance campaigns, and human handoff.
- [Replay recording and storage](engineering/REPLAYS.md): native replay capture, compatibility, deterministic verification, bounded atomic persistence, and import behavior.
- [User-data directories](engineering/USER_DATA.md): platform roots, Python and native layouts, recovery, and separation rules.
- [Migration ownership map](engineering/MIGRATION_MAP.md): Python-to-native owners, port order, data-migration procedures, dual-runtime freeze.
- [Parity decisions](engineering/PARITY_DECISIONS.md): reviewed Python-to-C# mismatches, target corrections, and open differences.

## Content and production

- [Audio system](content/AUDIO.md): station inventory, file mapping, SFX integration, and production tools.
- [Assets and rights pipeline](content/CONTENT_PIPELINE.md): deterministic inventory, hashes, integrity, provenance, and export eligibility.
- [Release signing and provenance](release/SIGNING.md): unsigned qualification, protected platform signing, post-sign verification, and attestation boundaries.
- [Native release outputs](release/PACKAGING.md): deterministic qualification archives, store-depot shapes, checksums, and publication boundaries.
- [Content pack contract](content/CONTENT_PACKS.md): manifests, allowlists, compatibility, credits, and failure isolation.

## Decisions and releases

- [Technology strategy](decisions/TECHNOLOGY_STRATEGY.md): Godot and C# target architecture, cross-platform contract, qualification gates, and migration sequence.
- [ADR 0001](decisions/ADR_0001_NATIVE_RUNTIME.md): accepted native runtime and rules boundary.
- [Current status](release/STATUS.md): verified implementation snapshot and release blockers.
- [Release checklist](release/RELEASE_CHECKLIST.md): final 1.0 go or no-go gates.
- [Manual product matrix](release/MANUAL_PRODUCT_MATRIX.md): exact physical platform, flow, input, settings-profile, session, and evidence contract.
- [Controlled external validation](release/EXTERNAL_VALIDATION.md): exact clean-candidate, fresh-participant, comprehension, finding, repair, and privacy contract.
- [Known issues](release/KNOWN_ISSUES.md): current player-facing limitations and evidence still required before release.
- [Release and rollback rehearsal](release/REHEARSAL.md): staged candidate acquisition, install, update, rollback, removal, withdrawal, and authority procedure.
- [Stable 1.0 promotion](release/STABLE_PROMOTION.md): protected tag rebuild, ten upstream decisions, public artifacts, preservation, and compatibility guard.
- [Privacy statement](../PRIVACY.md), [support](../SUPPORT.md), [credits](../CREDITS.md), and [third-party notices](../THIRD_PARTY_NOTICES.md): public release-material foundations.

## Supporting material

- [Research index](research/README.md): durable primary-source pointers and research-handling policy. These are inputs, not implementation evidence.
- [Project tools](../scripts/README.md): quality gates, production tools, manual checks, and retired-tool boundaries.
- [Python tests](../tests/README.md): deterministic suite ownership by subsystem.
- [Source assets](../assets/README.md): source asset categories and release-approval boundary.
- [Project configuration](../config/README.md): content policy, inventory, and production plans.
- [Legacy and production data](../data/README.md): migration inputs and historical generation outputs.

## Documentation contract

Documentation has three trust levels:

1. Canonical: root project artifacts and Markdown under `docs/`, excluding `research/`.
2. Supporting: indexed material under `docs/research/` and targeted READMEs next to subsystems or assets.
3. Historical: ignored local records under `archive/source-assets/docs-history/`, absent from a clean clone.

Only canonical documents may claim current feature status, supported versions, test counts, coverage, or release readiness. Every material implementation or quality-gate change updates [status](release/STATUS.md) and [changelog](../CHANGELOG.md). Priority changes update the [roadmap](../ROADMAP.md) without erasing acceptance evidence.

Canonical links must be relative, resolvable, and checked by `python scripts/check_docs.py`. New documents belong in the narrowest existing category. Add a new category only when the document cannot be owned clearly by an existing one. Do not add loose planning or status files at project root.
