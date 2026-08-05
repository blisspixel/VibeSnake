# Changelog

Notable player-facing and engineering changes are recorded here. The project is pre-release and does not yet promise semantic-version stability.

## Unreleased

### Added

- Multi-stream `RandomStreamBank` for independent gameplay, AI, cosmetic, radio, and copy PCG32 streams derived from one master seed.
- Fail-closed native custom AI `PersonalityDocument` validation with schema, trait range, boolean rejection, RGB checks, and filename-scoped errors.
- Architecture boundary tests that forbid Godot/presentation references from `VibeSnake.Rules` and `VibeSnake.Persistence`.
- Declared `ContentPackBudgets` ceilings for core and radio pack size and timing gates.
- Godot automated menu to run to death to restart smoke on a forced self-collision terminal path with replay save drain.
- Offline `LocalDiagnostics` crash reports with path sanitization, retention limits, and no network submission.
- In-memory `ShellSettings` multi-bus volumes and reduced-motion / high-contrast / flash-free placeholders for the 0.5 accessible shell.
- Explicit `ShellTransitions` table for legal menu/run/pause/ended presentation transitions.
- Preferences schema 2 with multi-bus audio and accessibility fields, schema 1 migration, and atomic `PreferencesStore`.
- `SnakeRun.Create` records master seed via `RandomStreamBank.Gameplay` without changing scored RNG outcomes.
- Headless smoke writes host-dependent `presentation_frames.json` p50/p95/p99 evidence under TestResults/native.
- Schema 1 logical `InputBindingsDocument` with conflict detection, required escape-hatch actions, and atomic per-device-class storage.
- Pure `InputBindingToken` parser for `key:`, `button:`, and `axis:` tokens with unit coverage.
- Godot `GameActions.ApplyKeyboardBindings` / `ApplyControllerBindings` apply stored documents to the InputMap while preserving the opposite device class, secondary convenience keys, and stick axes; headless smoke remaps pause and restores defaults.
- Logical `VirtualViewport` 1280x720 letterbox and pointer transform contract for shell scaling.
- Godot shell draws through `VirtualViewport` letterbox transforms (engine stretch disabled), tracks window resize, and maps window pointers into logical canvas space.
- Pure `NearMissDetector` for body-proximity tiers, edge ride, clutch eat, style points, and bounded combo windows using fixed rules ticks.
- `ContentBudgetReport` inventory totals vs declared pack ceilings (including core working-set membership), plus ContentService packaging resolve codes and media-type listing without loading media.
- `ContentInventory.CountByMediaTypePrefix` (and ContentService delegation) for pack composition queries.
- Complete `ContentPackBudgets` predicate helpers for core working-set, radio station compressed/installed ceilings, inventory-scan timing, and cold-start timing.
- `ContentTimingReport` for measured inventory-scan and cold-start timings against declared ceilings (no declared-hardware claim).
- Shell settings apply multi-bus volume and mute to the Godot Master/Music/SFX/UI buses; high-contrast canvas colors, text scale, and shortened reduced-motion feedback captions.
- `SnakeRun` can award near-miss score events for body proximity, clutch eats, and boost style points via pure `NearMissDetector` when `RunConfig.EnableNearMiss` is true (default false until shared fixtures regenerate); shell feedback recognizes `RunEventKind.NearMiss`.
- Interactive Godot sessions apply preferred fullscreen mode from shell settings (headless smoke stays windowed).
- Escape-hatch restore-defaults input action (F8 / controller select) rewrites keyboard and controller binding documents and re-applies the InputMap.
- Closed `RulesEventCatalog` of ordered `RunEventKind` wire names for deterministic event publication (includes `near_miss` and `starvation_warning`).
- `RunEventKind.StarvationWarning` / `RunEvent.StarvationWarning` emit once when hunger crosses the configured warning band (default 200 ticks remaining).
- Godot shell captions and orange HUD tint for the starvation warning band, with headless smoke coverage.
- Reserved `RunEventKind.ComboExpired` / `combo_expired` catalog entry for upcoming dual-runtime fixture regeneration (state reset remains silent for parity).
- Reserved `RunEventKind.AchievementCandidate` / `achievement_candidate` catalog entry for progression wiring without emission yet.
- `LocalDiagnostics.EnsureDiagnosticsDirectory` for in-game open-folder support without network paths.
- `RulesEventCatalog.PresentationPriority` and `SelectPrimaryKind` for caption selection when multiple events share a step.
- Native pure C# Phase Shift power contract with collection, timed expiry, body-overlap movement, Shield precedence, canonical restore, and replay participation.
- Six shared Python-to-C# Phase Shift parity fixtures (`phase_shift_rules_v1.json`) and CI check via `python -m vibesnake.qa.shared_phase_shift_traces --check`.
- Native pure C# Last Stand power contract: held revive, half-body shrink, hunger reset, recovery immunity, collision precedence with Phase Shift and Shield.
- Five shared Python-to-C# Last Stand parity fixtures (`last_stand_rules_v1.json`) and CI check via `python -m vibesnake.qa.shared_last_stand_traces --check`.
- [Migration ownership map](docs/engineering/MIGRATION_MAP.md) for V030-12.
- Native Slow-Mo and Boost cadence modifiers with composable snapshot scale and fixed-step movement invariance.
- Native Magnet food attraction (one-cell pull toward the head each rules step, blocked by body and pickups).
- Native Bait (weighted next-food respawn), Gluttony (eat without growth), and Segment Detach (timed tail obstacles with Phase Shift bypass).
- Pure `RulesCadenceClock` for Slow-Mo/Boost wall-clock step intervals with re-evaluated tempo during multi-step drains.
- Godot presentation for the full nine-power portfolio: letter markers, composite HUD, head outlines, body tints, bait marks, detached hazards, multi-power captions, and generic power fallback cues alongside Shield-specific tones.
- Nine shared Python-to-C# remaining-power parity fixtures (`remaining_powers_rules_v1.json`) covering Slow-Mo, Boost, Magnet, Bait, Gluttony, and Segment Detach, checked via `python -m vibesnake.qa.shared_remaining_power_traces --check`.
- Automated parity delta reduction (`ParityDeltaReducer`) that minimizes failing command prefixes, proves clean re-execution, and records minimized reproducers on movement divergence bundles.
- Host-dependent pure rules throughput evidence (`rules_throughput.json`) with a conservative CI floor for the technology decision gate (presentation frame times remain unmeasured).
- Expanded [migration map](docs/engineering/MIGRATION_MAP.md) with save, replay, pack, and ruleset data-migration procedures plus a dual-runtime freeze checklist.
- Published [user-data directory contracts](docs/engineering/USER_DATA.md) for Python and native platform roots, layouts, recovery, and separation rules.
- Python QA contracts for the remaining-power shared fixture (`test_shared_remaining_power_traces.py`).
- Native content-inventory gate that keeps `exportEligible` at zero until pack approval, and rejects rooted or traversing inventory paths.
- Pure `ContentInventory` reader in Persistence for allowlist queries, with path-traversal rejection and public inventory parse coverage.
- Native artifact inspection refuses inventory path traversal and blocks packaging of assets that are not `exportEligible`, with shared PowerShell policy coverage.
- Godot `ContentService` smoke validates allowlist denial with an embedded fixture so packaged-player smoke outside the checkout does not require development inventory files.

### Changed

- Native `test_native.ps1` retries Coverlet once after a rebuild when hit-file truncation fails on Windows after a green test run.
- Documentation snapshot refreshed for public radio inventory, hosted multi-platform CI, and single-`main` repository hygiene.
- Canonical run state serializes phase-shift and last-stand fields.
- Godot shell advances rules through cadence-aware accumulation instead of one physics frame per rules step.
- Native power collection now runs after movement settles so Segment Detach matches the Python coordinator body state.

## 0.2.1 - 2026-08-02

### Added

- Player CLI commands `vibesnake play`, `update`, `status`, `doctor`, and `version`, plus `play.ps1` / `play.sh` / `play.bat` launch helpers.
- Continuous [player-latest](https://github.com/blisspixel/VibeSnake/releases/tag/player-latest) GitHub release rebuilt from every `main` push with playable source zip, wheels, and checksums.
- Player-facing install scripts (`install_player.ps1` / `install_player.sh`) and player-build workflow.
- Adaptive window presentation with preferred 4:3 framing, integer pixel scaling, and retro-modern title/settings/pause chrome.
- Preferred Snakev2 brand logo hash gate and README captures for main menu, customization, and powers-active play.
- GTA-style offline radio library (95 tracks, eight stations) shipping in public source under `assets/audio/radio/`.

### Changed

- Default player documentation centers on `vibesnake` commands and GitHub `main` updates.
- Dependabot version-update PRs disabled (`open-pull-requests-limit: 0`) so the public repository stays on a single clean `main` branch.

## 0.2.0

### Added

- Canonical documentation hub, status report, roadmap, player guide, architecture guide, subsystem references, and release checklist.
- Detailed capability-gated release plan from 0.3.0 through 1.0.0, including product scope, compatibility policy, acceptance gates, quality growth, risk controls, and primary research references.
- GitHub Actions quality workflow for Python 3.11 through 3.14.
- Pytest, coverage, and Ruff configuration in `pyproject.toml`.
- Isolated test save directories through `VIBESNAKE_DATA_DIR`.
- Headless rendering, input, persistence, achievement, and entry-point tests.
- Main-loop integration tests for all nine power-up contracts.
- Versioned user preferences for sound, volume, and fullscreen mode.
- Configuration, save-path, migration, corruption, and atomic-write tests.
- Apache License 2.0 terms, NOTICE boundaries, contribution, conduct, security,
  support, pull-request, and dependency-update policy, with public intake closed
  until confidential reporting routes are tested.
- Seeded reference-core QA runner with food-seeking, survival, and abusive-input policies.
- Per-step gameplay invariants, action traces, immediate replay hashes, JSON campaign reports, and a CI-friendly QA command.
- Property-based generated command-sequence tests through Hypothesis.
- Canonical fun and player-experience, automated QA, and native technology strategy documents.
- Godot 4.7.1 C# qualification project with menu, logical keyboard and controller actions, focus-loss pause safety, quit and back flows, run, death, restart, drawing, and a deterministic headless smoke mode.
- Pure `VibeSnake.Rules` assembly with PCG32 randomness, bounded commands, wraparound movement, food, growth, combo scoring, exact starvation, collision, grid completion, immutable ordered events, explicit restart, snapshots, strict canonical JSON restoration, and versioned state hashes.
- One hundred seventy-seven xUnit native contracts. Rules coverage is 91.73 percent line and 87.77 percent branch; persistence coverage is 90.73 percent line and 84.48 percent branch; aggregate native coverage is 91.55 percent line, 87.26 percent branch, and 97.53 percent method, with an enforced 80 percent per-module line floor.
- Generated native state-machine campaigns that repeatedly serialize, restore, continue, terminate, and restart deterministic runs under valid and abusive command sequences.
- Native Music, SFX, and UI buses with finite cached 16-bit stereo PCM fallback cues for confirm, back, pause, food, Shield spawn, Shield activation, Shield expiry, Shield break, death, and victory.
- Schema 1 first-divergence bundles that retain the shortest executed failing prefix, expected and actual normalized state and events, native canonical state and hash, fixture identity, environment identity, and exact filtered reproduction.
- Explicit `vibesnake-core@4` ruleset identity, canonical state schema 2, `fnv1a64-canonical-json-v3` hashes, and a schema 1 replay envelope with canonical serialization, logical action attempts, checkpoints, final outcome, compatibility diagnostics, deterministic verification, and SHA-256 payload integrity.
- Live native replay recording that preserves rejected logical attempts, compares every completed step with a private deterministic mirror, compares final canonical state, and fails closed on divergence, lifecycle misuse, command, step, or serialized-size limits.
- Platform-neutral replay persistence with strict UTF-8 and size validation, source-preserving external inspection, precise compatibility and verification results, idempotent payload matching, cross-process transaction locking, no-overwrite same-directory atomic writes, and explicit 256-file and 256-MiB storage limits.
- Legacy-save migration failures now remain on the operating-system user-data path and emit a warning instead of silently reactivating checkout-local or install-local storage.
- Relative `XDG_DATA_HOME` and `LOCALAPPDATA` values are ignored so normal runs cannot redirect player data into the launch directory or source checkout.
- Relative `XDG_DATA_HOME` values are also ignored by the export-template installer, preventing implicit writes beneath its launch directory.
- Deterministic replay-verification work accounting for body hashes and potential full-grid food and power-spawn scans, early adversarial workload rejection, fixed untrusted compatibility diagnostics, and bounded sanitized player captions.
- Godot terminal-run replay capture, background post-write reload and verification, lossless save queuing behind inspection, run-start gating, save-aware quit and window-close handling with a monotonic five-second deadline, single-flight latest-replay input, read-only dropped-file inspection, actionable compatibility captions, and isolated user-data smoke.
- Matching `vibesnake-core@4` identity and explicit injected-or-normalized randomness-policy declarations in all three generated Python-to-C# parity corpora.
- Strict source-content policy plus a deterministic 18-file clean-clone inventory with logical IDs, media types, exact sizes, SHA-256 hashes, bounded structural integrity, duplicate links, pack intent, rights status, and export eligibility. Rights-unverified audio, historical production records, and copied research are isolated in the ignored local archive.
- Content inventory tests and a CI gate that reject unclassified, ambiguous, stale, unsafe, malformed, or silently changed assets.
- Schema 1 core and radio pack validation with canonical manifests, exact approved-inventory allowlists, rights-derived credits, compatibility and dependency ranges, station track contracts, a qualification command, and optional failure isolation.
- One hundred compact Python-generated movement fixtures with 25,600 matching C# steps and per-command queue outcomes, 35 targeted cross-language core-rule cases spanning every current score boundary, queue overflow, normalized random respawns, and terminal rules, plus 8 targeted Shield lifecycle cases including collision recovery at the starvation deadline.
- A complete native Shield contract with deterministic legal spawn, collection on entry, visible and active expiry, anti-stacking, one-use self-collision recovery, starvation bypass, saturated-board discard, restart cleanup, strict restoration, replay verification, and typed ordered events.
- Grid-safe Shield pickup and active-state presentation with visible countdowns, persistent text feedback, active head outline, and prioritized procedural cues.
- A native-runtime architecture decision and an explicit Python-to-C# parity decision log.
- Exact .NET 10.0.302 and Godot 4.7.1 toolchain manifests, stable-only SDK resolution, locked NuGet dependencies, and checksum-verified cross-platform Godot bootstrap.
- Exact Godot 4.7.1 .NET export-template checksum, three desktop export presets, selective template installer, and Godot-required application solution.
- Outside-checkout native player qualification with deterministic smoke logging, export-warning rejection, required Rules, Persistence, and Game payload checks, prohibited-content and project-payload path checks, macOS ZIP inspection, per-file SHA-256 inventory, and a machine-readable schema 2 artifact manifest that records its checksum-bound editor provenance.
- Two clean Shield-enabled Windows x64 debug exports with identical state hash `643077d90db75e8c`, 196-file and 189,537,416-byte inventories, and manifest SHA-256 `309bc5e0c37dd8adf0c24097542aca59b1699c1f5c3930117557875175ab47be`.
- Two clean replay-enabled Windows x64 debug exports with identical state hash `643077d90db75e8c`, 198-file and 189,615,786-byte inventories, isolated verified replay output, and checksum-bound schema 2 manifest SHA-256 `bae7d6369d61c6a57f2fe295f0308c238acc6ccd1e057c20abffc880e8c2ae74`.
- PowerShell qualification regressions that reject editor binaries which only spoof the pinned version text, versioned CPython runtimes, Python frameworks, shared Python libraries, and `.env` variants in player artifacts.
- Windows, macOS, and Linux CI matrices for native rules, real Godot headless smoke, checksum-verified exports, packaged-player smoke, artifact inspection, and retained native bundles.
- A universal 51-package Python 3.11 through 3.14 dependency lock with exact SHA-256 hashes, ordered input digest, atomic regeneration, local verification, and CI enforcement.
- Repository-owned local pre-commit hooks plus executable gates for unfinished-work markers, Python placeholders, bare exception clauses, forbidden emoji, and forbidden em dash across active source and canonical documentation.
- Native import limits for grid dimensions, total cells, canonical state, body and queue counts, replay size, step count, commands, and checkpoints.
- Deterministic README captures for the main menu, Vibe run, and AI spectator
  mode, plus exact source-fingerprint and PNG verification in local hooks and CI.
- Eight deterministic station badges generated with project-owned pixel glyphs and
  checked byte for byte in CI.
- Preferred handcrafted Vibe Snake brand logo retained from the Snakev2 mark and
  hash-checked in CI.
- Strict audits for both hash-locked Python graphs, with no known vulnerabilities
  in the final CI or player-runtime dependency sets.

### Changed

- Combo expiry now clears the streak without erasing elapsed time, preventing a late food from receiving a false speed bonus immediately after expiry.
- Python and native scores now share a 2,000,000,000-point saturation contract, and score events report only points actually awarded at the ceiling.
- The scored behavior correction introduced `vibesnake-core@3`; rules version 2 replays remain intact and are rejected as incompatible instead of being silently interpreted with corrected timing semantics.
- Immediate Shield collection and replay-relevant power state advance the current contract to `vibesnake-core@4`; version 3 state and replay data remain intact and incompatible rather than being silently reinterpreted.
- The source reference now supports Python 3.11 through 3.14 and uses Pygame Community Edition 2.5.7 or newer within major version 2, replacing the nearly end-of-life Python 3.10 floor and legacy Pygame distribution.
- The Python quality matrix now passes 466 tests and reports 3 expected optional-radio skips across all four supported versions, with 87.16 percent line coverage measured on Python 3.14.
- Starvation now advances on every non-food rules step, including a blocked collision. An unrecovered collision remains the attributed cause at a simultaneous deadline, while a Shield recovery consumes the Shield and then resolves starvation without deferring it.
- Native Shield duration configuration now requires at least two ticks, guaranteeing that a collected Shield can protect the first post-collection step under the documented expiry ordering.
- Content-pack test manifests now derive their current half-open rules range from the production rules identity, preventing a rules bump from making the valid offline core stale.
- Shared targeted rules now record whether the gameplay random stream advanced and whether off-path food remained byte-for-byte stable, while allowing each runtime to choose a different legal free respawn cell.

- Radio discovery now recognizes current and legacy filename prefixes and assigns all 95 remaining candidates exactly once.
- Achievement evaluation now matches all 25 current definitions.
- Achievement unlocks now persist with the player profile.
- Human progression is no longer advanced by AI spectator runs.
- Starvation and collision deaths use the same player-run finalization path.
- Superseded reports and plans were moved into the ignored, recoverable local archive so stale and unreviewed records are absent from public documentation.
- Shield now absorbs exactly one collision within its active window.
- Phase Shift, Gluttony, Bait, Last Stand, and Segment Detach now alter normal runs as documented.
- Achievement icons now use portable text badges.
- One `HighScoreTable` now owns the top-ten leaderboard, and the HUD reads that source.
- Legacy single-score data is imported exactly once into the canonical leaderboard.
- Player data now uses operating-system user-data directories with non-destructive checkout migration.
- Profile, cosmetic, leaderboard, and preference documents now use schema version 1, atomic replacement, corrupt-file backups, and future-schema write guards.
- Runtime configuration now validates supported types and ranges, applies resolution presets, and controls power-up visibility duration.
- The 1.0 roadmap now targets Godot 4 .NET with a pure C# rules assembly, step-level parity against the Python reference, and first-class Windows, macOS, and Linux artifacts.
- The roadmap now ties powers, effects, progression, radio, AI spectators, customization, lore, and offline comparison to one explicit fun thesis and human validation gate.
- The active technology pin moved from the provisional Godot 4.6 line to verified Godot 4.7.1 with .NET SDK 10.0.302.
- The QA campaign report moved to schema version 2 with ordered events, explicit win state, and outcome aggregates.
- Shared targeted rules moved to schema version 2 and now compare exact starvation, collision precedence, full-grid victory, and ordered event detail.
- CI installs the exact hash-locked Python graph, audits the resolved lock, lints the complete active Python tree, enforces source policy, and rejects stale dependency inputs.
- NuGet audit now evaluates direct and transitive dependencies during locked restore, with every vulnerability severity promoted to a build failure.
- Settings now create renderer-owned fonts for each Pygame lifecycle instead of retaining invalid import-time font objects.
- Ruff formatting is now an explicit local-hook, CI, contributor, development,
  testing, and release-checklist gate alongside linting.
- Credentialed and mutating legacy audio-production programs, their unused
  dependency profile, historical rename records, and rights-unresolved media are
  isolated in the ignored archive. Public tooling is now deterministic except for
  one explicitly manual local radio-preview command.
- Unsupported player-psychology claims in telemetry and presentation comments
  now describe observable state, timing, and rule contracts instead.
- The unwired Python adaptive-difficulty controller and its unvalidated aggregate
  were removed. Adaptive rules remain a future versioned, disclosed, separately
  scored roadmap capability.
- Test cases now begin from an isolated reference-random stream, making exercised
  branches and coverage evidence repeat exactly across consecutive runs.
- Public source content is reduced to 18 classified, rights-cleared files totaling
  95,377 bytes; none is silently export approved.
- The development graph no longer carries an unconfigured type checker whose
  findings were not enforced; the typed production target remains the warnings-as-errors
  C# rules kernel.

### Fixed

- Strict native state and replay import now reject an impossible self-collision death that retains an active Shield.

- Default pytest collection no longer scans archived or paid-API scripts.
- Stale tests now reflect current movement, scoring, and menu contracts.
- Escape closes the help screen.
- Locked customization requirements no longer compare incompatible values.
- Duplicate unlock-map keys and several lint errors were removed.
- Expired uncollected power-ups are removed, transient effects clear on reset, and duplicate active effect types no longer spawn.
- Direction buffering is bounded and rejects stale queued reversals before they can create delayed input or unbounded queue growth.
- Food collection, growth, scoring, and feedback now occur on entry into the food cell instead of one movement tick later.
- Radio playback now skips unreadable MP3s, remembers failed files for the session, and tries the remaining station tracks before turning the station off.
- Godot export no longer fails silently for lack of the required application solution.
- Native project symbols map source documents to a deterministic virtual root instead of leaking the developer checkout path.
- Native player packs exclude the NuGet lock file, and artifact qualification now rejects source-machine and development paths.
- Godot export restores use ignored export-specific lock files and fail qualification if any canonical dependency lock changes.
- Exact-deadline food now resolves before starvation, while a missed deadline completes its legal movement before death and only then evaluates Last Stand.
- Shield collection now occurs on the successful movement that enters its cell instead of waiting for a later render update.
- Filling the final grid cell now completes the run as a victory instead of entering an unwinnable foodless survival state.
- Packaged-player qualification now owns the launched process through exit, preventing Windows log cleanup races.
- Export-specific NuGet locks now live outside Godot's resource tree, and artifact inspection rejects packed `res://obj` metadata.
- Fallback cues now use finite PCM resources with explicit stop, detach, release, and process-exit phases, eliminating the native audio playback leak found by repeated smoke tests.
- Native editor and packaged-player smoke now fail on engine warnings, leaked objects, or a missing success marker.
- The dependency-lock command now receives the checkout root from its repository
  wrapper instead of deriving it from an installed package location, so the exact
  documented and CI command works after a non-editable install.
- Repository Python gates and screenshot capture now import the checkout source
  explicitly, preventing a non-editable CI install from hiding current code or
  rendering without checkout assets.
- Rewarded spatial near-miss events now advance the run-local achievement
  counter, while clutch and boosted-food style events remain separate signals.
- Magnet preserves food already on the next head cell and will not pull food
  onto the snake, detached hazards, or visible power-ups.
- Boost and Slow-Mo cadence factors now compose and expire independently, so
  one effect cannot clear the other.
- AI spectators target only visible collectible power-ups, and scored style
  events now read rules state instead of presentation timers.
- Food respawn reserves visible power-up cells and, on a saturated board,
  removes temporary pickups and detached hazards before declaring a true
  snake-filled grid victory.
- Unknown or malformed cosmetic unlock requirements now fail closed instead of
  granting content accidentally.
- Coverage-report filename variants are ignored, preventing local paths from
  entering a future initial commit.
- The terms-unresolved generated reference logo is preserved only in the
  ignored archive and replaced in public source by deterministic original art.
- Corrupt-save quarantine now uses one idempotent backup per save, atomically refreshing it only when the corrupt bytes change.
- PNG validation now walks the complete bounded chunk stream and verifies structure, CRCs, dimensions, image-data ordering, and terminal bytes; MP3 validation requires two complete compatible MPEG frames.
- Seven zero-byte media entries and three broken or side-effectful legacy test runners were removed.
- Placeholder assertions in gameplay integration tests were replaced with deterministic state and rendering postconditions.
- The Settings menu no longer advertises a difficulty control that has no effect.
- The locked build environment now uses setuptools 83, resolving `PYSEC-2026-3447`; the strict audit reports no known vulnerability in the resolved Python graph.
- The process entry point now propagates meaningful success and failure codes.
- QA campaign construction rejects empty scenario sets and invalid scenario
  parameters before simulation.
- Radio number keys and cycling controls no longer affect unrelated game states,
  and optional-pack absence produces one truthful status message.
- Food placement now samples uniformly from the complete bounded free-cell set
  and raises the documented full-grid exception instead of returning a sentinel.
- Source PNG validation now checks bounded decompression, decoded scanline length,
  legal filter bytes, and palette constraints in addition to chunk structure and CRCs.
- Native restoration rejects oversized dimensions, queues, counters, and tick
  values, while score arithmetic saturates instead of overflowing.
- Empty package shells and undocumented duplicate root launchers were removed.
- Rewarded near-miss, clutch, and style events now advance the run-local
  achievement counter instead of achievements always receiving zero.
- Ruleset identity validation now rejects missing, malformed, boolean, fractional,
  and non-positive versions, and content-pack compatibility derives from the same
  canonical identity used by states, replays, fixtures, and both rules runtimes.
- Phase Shift recovery no longer treats an occupied duplicate tail coordinate as a
  departing free cell, preventing survival through a real self-collision after
  overlapping body states.
- Targeted fixture generation and QA campaigns restore the caller's logger level
  after quiet generation, preventing process-global logging state from leaking
  into later tools or tests.

## 0.2.0 alpha

- Added progression, achievements, cosmetic loadouts, AI spectator channels, adaptive difficulty scaffolding, near-miss scoring, visual effects, and the expanded radio network.
- This release predates the current verification baseline. Historical completion claims are retained only in the archive.
