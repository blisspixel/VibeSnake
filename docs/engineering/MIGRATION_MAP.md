# Python-to-Native Migration Ownership Map

Status: Native source default; frozen-oracle procedures retained (2026-09-30).

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
| Content inventory / packs | Native `RepositoryChecks` inventory + Persistence pack allowlists | Done for automated foundation | Native generation, freshness, integrity, strict UTF-8 manifests, immutable parsed collections, compatibility, and optional isolation own the complete contract; test-only Python content library retired; exportEligible=0 |
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

The first 35 bounded retirement slices are complete. Native `RepositoryChecks` owns documentation, product-version, source-policy, candidate-freeze, dependency-lock, project-logo, Agent Plugin, Agent Knowledge, offline agent-interoperability baseline and digest validation, station-badge, content-inventory, README screenshot, release-material, release-rehearsal, stable-promotion, achievement-candidate fixture, Last Stand fixture, Phase Shift fixture, Shield fixture, Remaining Powers fixture, Core Rules fixture, Movement fixture qualification, manual-product plus external-validation handoff qualification, release-matrix qualification, unsigned-preview assembly, content-pack qualification, approved-radio assembly, the scheduled upstream integrity probe, and pending manual-review workspace preparation. Release-material, rehearsal, and stable-promotion outputs remain canonical LF JSON written through bounded atomic replacement, with external operations and approvals explicitly outside the validator. The seven frozen-vector renderers preserve 167 reviewed Python-origin vectors and exact 2,682-byte, 3,596-byte, 3,534-byte, 4,489-byte, 9,548-byte, 57,031-byte, and 999,087-byte canonical LF identities without executing native rules, while their separate C# parity tests remain the live behavior consumers. Six small fixtures share the 65,536-byte fixed-path lifecycle. Movement uses a separate 1,000,000-byte lifecycle with the same stable reads, symbolic/reparse-point and portable-alias rejection, sibling reservation, destination revalidation, flushed same-directory replacement, primary-failure-preserving cleanup, and exact self-verification guarantees. Agent Knowledge uses that hardened fixed-path lifecycle independently for each of its five concepts. Agent interoperability applies the same strict bounded source and atomic replacement contracts to its offline baseline and versioned digests. Agent Host package validation uses the separate `host-package` route, outside `all`, because it qualifies an assembled package directory. CI runs the combined check on Windows, macOS, and Linux; tagged-alpha assembly uses its version, inventory-release, and unsigned-preview routes after locked restore. Superseded Python entry points and duplicate Python CI routes are removed. Ruff remains the complete Python syntax parser; the unused Python version and checkout-source helpers and their helper-only tests are removed. Preview close-out runs through PowerShell without Python, preserves user configuration, and commits only already-staged changes when explicitly requested. The test-only content inventory and pack library is retired after its complete native contract-family audit; only the frozen gameplay oracle and its distribution edges remain.

Movement fixture generation and freshness is complete. The native renderer ports only the frozen Python movement coordinator and integer-seeded MT19937 command stream, not `SnakeRun`, and reproduces all 100 cases, 25,600 steps, 999,087 bytes, and SHA-256 `43f3861f6a20c39ae5d2d439d0071b855f751478d34c30eafa4c7ae968f060d4`. Its 1,000,000-byte lifecycle is separate from the 65,536-byte small-fixture boundary and has exact-cap, oversized generation/read/write, stable-path, replacement, cleanup, and self-verification contracts.

Agent Knowledge generation and freshness is also complete. Native `knowledge` and `knowledge-write` derive the five Open Knowledge Format 0.2 concepts from strict, bounded canonical JSON and source inputs, enforce the lifecycle date, reject duplicate declarations and catalog drift, close output names and bytes, and atomically self-verify every write. This migration corrected a real stale claim from viewer frame v7 to the live `vibesnake-agent-viewer-frame-v9` plus `vibesnake-agent-survival-state-v1` contract. The superseded Python generator and its four tests are removed.

Offline agent-interoperability qualification is complete. Native `interop` and `interop-write` strictly parse the bounded baseline, enforce closed keys, canonical review and lifecycle dates, immutable upstream pins, source and documentation alignment, SemVer-ordered contract history, and exact host and plugin digests. Digest updates use atomic LF replacement and exact self-verification. The superseded local Python checker, digest printer, and six local tests are removed. The scheduled three-URL read-only integrity probe now runs through native `interop-upstream` after local `interop` qualification. It stays outside `all` and ordinary CI. The fetcher enforces a 30-second timeout, a 1,048,576-byte body cap, a 32 KB response-header cap, and at most two absolute HTTPS redirects. It is not a local policy authority. `check_agent_interop_upstream.py` and its test are removed.

The remaining Python CI and package-tool graph was audited on 2026-09-30. Its boundaries and ordered disposition are:

| Remaining surface | Dependency boundary | Retirement disposition |
| --- | --- | --- |
| `check_agent_interop_upstream.py` | Scheduled read-only integrity probe for three reviewed HTTPS resources after native local baseline qualification | Complete. Native `interop-upstream` owns the probe outside `all` and ordinary CI, with an explicit 30-second timeout, 1,048,576-byte response cap, 32 KB header cap, and a limit of two absolute HTTPS redirects. It is not a local policy authority. The Python script and its test are removed. |
| Release matrix and unsigned preview | Three-platform package, manifest, provenance, and unsigned-preview assembly | Complete. Native `release-matrix` and `unsigned-preview` own qualification and assembly outside `all`. Publication eligibility stays false, signing stays unsigned, and assembly does not approve radio content. The Python scripts and their tests are removed. Pending workspace preparation is native `product-review-prepare` outside `all`. |
| `content_packs.py` and `assemble_radio_pack.py` | Called the frozen `vibesnake.content` manifest and inventory library | Complete. Native `content-packs` and `radio-pack` own qualification and assembly outside `all`. They call the existing persistence pack APIs, do not approve radio content, and do not change export eligibility. The Python scripts and the radio assembly test are removed. The test-only `inventory.py`, `packs.py`, package reexports, and their 84 Python contracts are removed after native parity qualification. |
| `scripts/manual/*` | Native interactive sample playback | Hash-bound listening records are native `radio-listening` outside `all`. `review_radio_copies.py` and its test are removed. The command rehashes exact review copies and cannot change release approval, export eligibility, curation, or source bytes. The full-decode qualification campaign is native `radio-audio` outside `all` and ordinary CI. It does not modify sources, curation, inventory, or export eligibility, and `releaseApproved` stays false. The thirtieth slice moves station review-copy preparation to `radio-review`, outside `all` and ordinary CI. It does not modify sources, curation, inventory, or export eligibility. `releaseApproved`, `sourceReplacementApproved`, and `exportEligibilityChanged` stay false, and `humanListeningStatus` stays pending. `analyze_radio_audio.py` and its parser tests are removed. Native `radio-audio` and `radio-review` own probe, loudness, and silence parsing. Native `radio-preview list` lists the fixed eight-station catalog without opening an audio device. Explicit `radio-preview play` uses a supplied FFplay executable with Enter/q/EOF controls, a 30-minute playback bound, and bounded process cleanup. Both routes stay outside `all` and ordinary CI, and neither writes a listening record or approves a track. The superseded Python sample-preview script is removed. |
| `vibesnake.qa`, the Python test tree, and the frozen player modules | Behavior oracle and remaining cross-runtime parity | Remove last, only after every validator and pack tool above has native ownership and the replacement CI matrix passes from a clean checkout. |
| Native preview close-out | `close-agent-preview.cmd` forwards to `scripts/close_agent_preview.ps1`, which invokes native qualification | Complete. Python is not required. User configuration is preserved, downstream failure codes propagate, and optional `--commit` uses only already-staged changes under the canonical repository identity. The obsolete Python wrapper is removed. |
| `product_version.py` and `_checkout.py` | No remaining production caller; only their own helper tests remained | Complete. The dead helpers and helper-only tests are removed. Native version contracts retain canonical SemVer, package spelling, strict UTF-8, and LF validation. |
| Python packaging metadata, locks, wheel/sdist assembly, and source-reference release | Distribution edges still coupled to the frozen oracle | Remove with their last dependents in the final audited cleanup, then rerun source, artifact, documentation, license, and dependency inventories. |

Agent Host package validation is complete. Native `host-package` enforces the closed `vibesnake-agent-host-package-v1` manifest, lock-derived inventory, unsigned provenance, checksum, symbol, and player-data exclusion contracts. It reads the MCP protocol constant from `VibeSnake.AgentHost` and stays outside `all`, because it qualifies an assembled package directory rather than the repository root. `package_agent_host.ps1` still publishes the package. `publication_eligible` stays false.

The thirty-fifth slice retires the test-only Python content package and its 84 contracts. A caller audit found only package-internal imports and the two duplicate test modules. All 16 inventory test families and 17 pack test families have native replacements:

| Retired guarantee | Native evidence |
| --- | --- |
| Sorted hashes, duplicates, exact freshness, policy bounds, rule ambiguity, rights and release blockers | `ContentInventoryCheckTests.cs` |
| PNG/C2PA, indexed palettes, bounded decompression, WAV, MPEG, UTF-8 and unsafe repository paths | `ContentInventoryCheckTests.cs` |
| Failed atomic replacement preserves existing bytes and removes its temporary file | Injected portable replacement failure in `ContentInventoryCheckTests.cs` |
| Detached validation, canonical files, encoding and collection bounds, exact allowlists, metadata, rights, credits and radio tracks | `ContentPackManifestTests.cs` |
| Compatibility codes, dependency ranges, missing/invalid/duplicate optional packs and fatal core failures | `ContentPackManifestTests.cs` and `ContentPackToolTests.cs` |
| Installed archive paths, hashes, capacities, tamper isolation, removal and recovery | `OptionalPackStoreTests.cs` |

Runtime inventory tests additionally bound allocation before trusting declared counts, reject contradictory export evidence and malformed fields, normalize errors for optional-radio recovery, and prevent mutation of loaded assets. Native manifest file qualification reads one strict UTF-8 snapshot and keeps parsed collections read-only. The frozen gameplay oracle and reviewed shared fixture bytes remain available for their separate migration contracts.

Manual-product and external-validation qualification is complete. Native `manual-matrix`, `manual-matrix-write`, `manual-matrix-record`, `external-validation`, `external-validation-write`, and `external-validation-record` own the closed handoffs inside `all`. Zero-session evidence stays pending, and neither route can set human review, release acceptance, or publication eligibility by itself. Native `product-review-prepare` prepares the pending physical workspace outside `all` and ordinary CI, reads platform rows and required flows from the manual contract, and cannot set human review, release acceptance, or publication eligibility. The Python preparer and its test are removed.

Release-matrix qualification and unsigned-preview assembly are complete. Native `release-matrix` and `unsigned-preview` own them outside `all`. Publication eligibility stays false, signing stays unsigned, and assembly does not approve radio content. Content-pack qualification and approved-radio assembly are complete. Native `content-packs` and `radio-pack` own them outside `all`. Radio assembly records an already-approved curation decision and does not write curation, inventory, or approval flags. Export eligibility stays zero until human review. Scheduled upstream integrity uses native `interop-upstream` outside `all` and ordinary CI. Exact-candidate workspace preparation is native `product-review-prepare` outside `all`. Hash-bound radio listening records are native `radio-listening` outside `all`. The full-decode qualification campaign is native `radio-audio`, outside `all` and ordinary CI, and does not modify sources, curation, inventory, or export eligibility. The thirtieth slice moves station review-copy preparation to `radio-review`, outside `all` and ordinary CI. It does not modify sources, curation, inventory, or export eligibility. `releaseApproved`, `sourceReplacementApproved`, and `exportEligibilityChanged` stay false, and `humanListeningStatus` stays pending. `analyze_radio_audio.py` and its parser tests are removed. Native `radio-audio` and `radio-review` own probe, loudness, and silence parsing. Native `radio-preview list` lists the fixed eight-station catalog without opening an audio device. Explicit `radio-preview play` uses a supplied FFplay executable with Enter/q/EOF controls, a 30-minute playback bound, and bounded process cleanup. Both routes stay outside `all` and ordinary CI, and neither writes a listening record or approves a track. The superseded Python sample-preview script is removed. The frozen oracle and `vibesnake.qa` stay last.

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
