# Python-to-Native Migration Ownership Map

Status: Native source default; frozen-oracle procedures retained (2026-08-10).

This map assigns every Python reference subsystem to its target C# or Godot owner. **Product work lands in the target owner only.** Python is a temporary frozen oracle, not a permanent second architecture: do not add player-facing features there, never implement the same feature twice, and remove it after the native replacement gates below pass.

## Ownership matrix

| Python owner | Target owner | Port state | Notes |
| --- | --- | --- | --- |
| `core/snake.py` movement, wrap, body | `VibeSnake.Rules` | Done | Shared movement and core-rule fixtures |
| `core/scoring.py` combo, bonuses | `VibeSnake.Rules` | Done | Shared core-rule fixtures |
| `core/near_miss.py` proximity and style | `VibeSnake.Rules` `NearMissDetector` + `SnakeRun` | Done for product contract | Body proximity and clutch events are wired and measured; intentionally absent edge-ride behavior is not a migration blocker |
| Starvation timer / deadline | `VibeSnake.Rules` | Done | Exact order with collision; one-shot `StarvationWarning` at default 200 remaining ticks |
| Food spawn | `VibeSnake.Rules` | Done | PCG32 free-cell selection |
| Power manager spawn cadence | `VibeSnake.Rules` | Done | Product Vibe uses deterministic nine-power decision offers; Classic remains power-free and frozen parity configs retain their compatibility path |
| Shield | `VibeSnake.Rules` + Godot | Done | Parity `shield_rules_v1`; shell markers and cues |
| Phase Shift | `VibeSnake.Rules` + Godot | Done | Parity `phase_shift_rules_v1`; shell markers and body tint |
| Last Stand | `VibeSnake.Rules` + Godot | Done | Parity `last_stand_rules_v1`; recovery captions |
| Slow-Mo / Boost tempo | `VibeSnake.Rules` + Godot | Done | Parity in `remaining_powers_rules_v1`; `RulesCadenceClock` shell drain |
| Magnet | `VibeSnake.Rules` + Godot | Done | Parity remaining-powers; shell markers |
| Bait | `VibeSnake.Rules` + Godot | Done | Parity remaining-powers; bait mark draw |
| Gluttony | `VibeSnake.Rules` + Godot | Done | Parity remaining-powers; body tint |
| Segment Detach | `VibeSnake.Rules` + Godot | Done | Parity remaining-powers; hazard draw; collect-after-move |
| Input devices | Godot `GameActions` + Persistence bindings | Done for native shell | Logical actions, schema-1 store, keyboard/controller remapping, conflict swap/cancel, family-aware vector prompts, deadzone, D-pad fallback, lifecycle safety, and render-cadence evidence |
| Menus / HUD / cosmetics | Godot presentation | Done for automated foundation | Title-first shell, complete current screen flow, detailed gameplay, eight curated sets, live preview, adaptive viewports, and accessibility evidence are live |
| Audio buses / SFX | Godot `AudioFallback` plus pure C# `AudioMixAllocator` | Partial | Four buses, mono downmix, 31 distinct licensed/provenance-declared fallback cues, bounded SFX/UI voices, cooldown, priority, interruption, music ducking, saved volumes, peak policy, and output repair qualified; authored packs and physical listening remain open |
| Radio playback | Godot content service | Done for automated foundation | Native manifest policy, one-track decoder adapter, source-checkout discovery, Music-bus routing, recovery, and isolated RNG are live; approved export packs and listening review remain |
| Persistence (profile, scores) | `VibeSnake.Persistence` + Godot | Done for current native scope | Achievements, onboarding, progression, cosmetics, fair-category personal bests, top-ten history and optional Python import, preferences, bindings, reset, backup, and recovery are live |
| Replays | `VibeSnake.Persistence` + Rules + Godot | Done for automated foundation | Recording, bounded storage/browser, verification, deterministic playback, reset, seek, export, exact deletion, stable seed codes, four household ghost slots, equal-rules ghost racing, private run cards, and recovery are live; retained platform and accessibility review remains |
| AI personalities | Pure C# AI and spectator sessions | Done for automated foundation | Ten measured personalities, local equal-rules matches, standings, commentary, explanations, recovery, and exact-seed human challenges are live |
| Content inventory / packs | Shared policy + native allowlists | Partial | Schema 1 validators exist; exportEligible=0 |
| Config | Rules config + Godot settings UI | Done for current schema | Rules identity plus schema-7 gameplay, control, audio, display, accessibility, and data settings are live; future additions require versioned migration |

## Port order (locked)

1. Shield, Phase Shift, Last Stand (collision recovery matrix): done
2. Slow-Mo and Boost (tempo modifiers): done (rules + shell cadence)
3. Magnet, Bait, Gluttony, Segment Detach: done (rules + shell + shared fixtures)
4. Presentation, radio adaptation, progression UI, remapping, glyphs, replays, and AI channels on Godot: automated foundation complete
5. Installer/archive shapes and cross-platform packaged-player smoke: automated foundation complete
6. First export-eligible packs, protected signing, physical-platform review, and human acceptance: current release gates

## Data migration procedures

These procedures apply when a versioned player-data contract changes while Python and native still coexist.

### Save repositories (profiles, scores, cosmetics, preferences)

1. **Inventory.** List every repository schema version currently accepted by Python and every fixture under `tests/` and `tests/fixtures/`.
2. **Additive first.** Prefer new optional fields with defaults over renames or removals.
3. **Migration function.** Implement a pure, tested migrator that maps version N to N+1 without reading environment clocks or absolute paths.
4. **Atomic write.** Write to a temporary sibling file, fsync if available, then replace. On failure leave the original intact and write a `.corrupt` backup only when the original cannot be parsed.
5. **Downgrade protection.** Refuse to overwrite a document whose `schema_version` is newer than the running app understands.
6. **Dual-runtime freeze.** While both runtimes can write the same user-data directory, do not ship a schema that only one runtime can read. Either implement the migrator in both, or gate the native write path until Python is retired for that repository.
7. **Evidence.** Add fixtures for oldest supported, current, corrupt, empty, and future-schema documents. Run them in CI for both runtimes that still touch the format.

### Replays

1. Replays use an independent `replay_schema_version` from save repositories.
2. Unsupported or future envelopes remain on disk; loaders return an actionable compatibility code without mutation.
3. Native `ReplayStore` is the only writer for Godot-recorded runs. Python does not rewrite native envelopes.
4. Divergence or integrity failures never replace the source file.

### Content packs and inventory

1. Pack manifests are validated against the content inventory allowlist before any native export consumes them.
2. Rights-derived credits and file hashes must match inventory rows; mismatches fail closed.
3. Until `exportEligible` is non-zero for a row, that asset must not appear in native player payloads.
4. Optional radio packs fail in isolation; core play continues with fallback audio.

### Ruleset and score identity

1. Every scored run records `ruleset_id` and `rules_version`.
2. Leaderboard categories never mix entries with different rules identity.
3. Intentional rules corrections require a `PARITY_DECISIONS.md` entry and fixture regeneration, not silent expectation edits.

## Rollback

- Keep Python runnable through `vibesnake` for oracle reproduction and migration work, but do not present it as the default player.
- Shared fixtures are the contract: a native regression must not silently change fixture expectations without a `PARITY_DECISIONS.md` entry.
- Replay schema rejections leave files intact.
- Do not delete Python power modules until every power has native parity fixtures and Godot presentation coverage (currently satisfied for all nine; retain modules until the dual-runtime freeze ends).
- If a native schema write is discovered unsafe, revert the writer first, then the migrator, then the schema bump. Never leave player files half-migrated.

## Dual-runtime freeze checklist

Before ending dual-runtime for a subsystem:

1. Shared fixtures or native unit contracts cover the subsystem contract.
2. Only one runtime writes the user-data path for that subsystem in shipping builds.
3. Migration fixtures for the last two schema versions pass.
4. Rollback steps above remain operable from a clean checkout.
5. STATUS and ROADMAP stop claiming Python ownership for that subsystem.

## Repository-wide Python retirement

The end state is one product and one implementation stack: Godot plus .NET. Shell launchers may remain for platform bootstrap, but neither gameplay, release qualification, fixture generation, nor CI should require a Python environment.

The first bounded validator slice is complete. Native `RepositoryChecks` owns documentation discovery and links, changelog contract-release uniqueness, canonical product-version parsing and package mapping, and cross-file alignment. Its 35 focused contracts cover malformed inputs and deterministic results, CI runs it on Windows, macOS, and Linux, tagged-alpha assembly uses its version route after locked restore, and the two superseded Python command files are gone. The shared Python version helper remains temporarily because later release-assembly scripts still import it.

Retirement proceeds in this order:

1. Keep the existing Python behavior and checked-in parity fixtures frozen while native replacement work lands. Defect corrections are allowed only when they protect migration or release evidence.
2. Move every authoritative content, version, source-policy, documentation, screenshot, dependency, and release validator to .NET tools with equivalent malformed-input and deterministic-output coverage.
3. Move shared fixture generation and delta reduction to the pure C# QA surface. Preserve the reviewed JSON fixtures as historical contracts until the native generators reproduce them exactly.
4. Replace the Python-version CI matrix with native tests, Godot import and packaged-player smoke on Windows, macOS, and Linux. No native artifact may acquire a Python runtime dependency during the transition.
5. Remove the Python player, its tests, package metadata, dependency locks, and source-snapshot release path only after steps 2 through 4 pass from a clean checkout.
6. Run source, artifact, documentation, license, and dependency inventories after removal. The repository is not Python-free until those gates find no Python runtime, package, launcher, or hidden release dependency.

Until these exit gates pass, Python remains test-only scaffolding. It is never a reason to duplicate or delay native product work.

## Feature freeze rule

No new scored mode, power type, or ruleset identity change lands in both runtimes in the same change. Prefer native-only after the rules port for that subsystem is complete.
