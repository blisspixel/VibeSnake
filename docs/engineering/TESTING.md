# Testing and Quality Gates

## Required local checks

```powershell
python scripts/lock_python_dependencies.py
python scripts/lock_python_dependencies.py --profile runtime
python -m pip_audit --strict --disable-pip --require-hashes --requirement requirements-ci.lock
python -m pip_audit --strict --disable-pip --require-hashes --requirement requirements-runtime.lock
python -m ruff format --check src tests scripts
python -m ruff check src tests scripts
python scripts/check_source_policy.py
python scripts/check_docs.py
python scripts/check_product_version.py
python scripts/capture_readme_screenshots.py --check
python scripts/visual_generate_badges.py --check
python scripts/visual_generate_logo.py --check
python scripts/content_inventory.py --check
python -m vibesnake.qa.shared_traces --check
python -m vibesnake.qa.shared_rule_traces --check
python -m vibesnake.qa.shared_power_traces --check
python -m vibesnake.qa.shared_phase_shift_traces --check
python -m vibesnake.qa.shared_last_stand_traces --check
python -m vibesnake.qa.shared_remaining_power_traces --check
python -m vibesnake.qa.shared_achievement_candidate_traces --check
python -m vibesnake.qa --seeds 0 1 2 3 4 --steps 500 --output qa_reports/core.json
python -m pytest --cov=vibesnake --cov-report=term-missing --cov-report=xml
```

Native qualification checks:

```powershell
./scripts/test_native.ps1
```

This command verifies the editor bytes against the executable inside the pinned SHA-512 archive, then verifies its exact version, flavor, and official commit identity. It uses locked dependencies, builds with warnings as errors, verifies formatting and analyzers, enforces the C# line and branch coverage floors, imports the Godot project, and runs the real seeded scene smoke.

Packaged-player qualification for the current operating system:

```powershell
./scripts/test_native_export.ps1
```

This gate verifies the checksum-bound editor and official export template, rejects export warnings, launches outside the checkout, requires a deterministic smoke hash, inspects required platform payloads, rejects Python runtimes, environment files, and development content, scans project payloads for source-machine paths, and writes a schema 2 per-file manifest containing the archive and executable checksums. Release-mode CI also requests 100 consecutive clean launch probes from the read-only install, each with a distinct fresh external profile and a 30-second timeout. The aggregate matrix requires all 300 platform launches before provenance.

The configured coverage floor is 80 percent line coverage across `vibesnake`. A run below the floor fails even if every assertion passes.

The staged quality expansion for artifact smoke tests, branch coverage, deterministic replay, accessibility, simulation, content validation, and release candidates is defined in the [roadmap quality ladder](../../ROADMAP.md#quality-ladder).

The automatic testing architecture, campaign policies, invariant catalog, balance reports, presentation checks, platform matrix, and boundary with human playtesting are in [AUTOMATED_QA.md](AUTOMATED_QA.md).

## Current baseline

As of 2026-08-10:

- 578 deterministic tests plus 14 subtests pass locally on Python 3.14; hosted CI runs the same suite across Python 3.11, 3.12, 3.13, and 3.14. Optional-content absence is an explicit tested state rather than a skipped release assumption.
- Python line coverage is 87.15 percent on Python 3.14, above the enforced 80 percent gate that CI applies to every supported interpreter.
- 899 native C# contract tests pass on .NET 10 with 90 percent line and 85 percent branch floors per measured module under Coverlet 10. Rules measures 95.77/88.69 percent line/branch, Persistence measures 93.86/87.12 percent, creator validation measures 95.98/94.00 percent, and aggregate native coverage is 94.61/87.76 percent. The gate rejects a coverage report that omits Rules or Persistence, and Coverlet's minimum-module threshold also gates the creator validator; `scripts/test_native.ps1` refreshes the exact report after each run. The 0.4 acceptance target remains 90 percent branch coverage.
- Native progression tests cover canonical human-run identity, exact goal progress, Tour dependency closure, fixed-seed and effective-rules identity, every supported single-run Tour metric, style goals, expression-only rewards, unearned-reward rejection, cosmetic unlock/selection/loadout bounds, strict JSON, atomic persistence, and recovery ownership. Godot smoke separately exercises raw keyboard and controller goal, Tour, and cosmetic flows.
- The AI league adds 120 complete runs across ten built-ins and twelve reviewed seeds, 98,984 mirrored deterministic steps, seven required metric families, 60 material trait-sensitivity rows, ten truthful behavior claims, strict custom-schema probes, overlay/status facts, and canonical noncompetitive AI score identity. The wrapper validates `TestResults/native/ai_league.json` and `TestResults/native/ai_personalities.json` before Godot smoke.
- Required `replay-browser-qualification-v2` evidence covers exact date/mode/rules/score/seed/step/status metadata, explicit verified/incompatible/modified/unreadable badges, 0.5x/1x/2x/4x speed, HUD toggle, pause/step/restart/return, verified atomic export, exact two-step stale-safe deletion, lossless cancel, preserved exports, progression isolation, and raw keyboard/controller paths.
- Required `capture-sharing-qualification-v1` evidence covers default-off clean capture, six hidden overlay families, raw keyboard/controller routes, four replay speeds, deterministic replay seek/reset, unchanged rules state, explicit verified summary export, atomicity/idempotence, exactly 24 closed schema fields, complete version/rules/integrity metadata, and player-identity/private-path exclusions.
- One hundred shared movement traces compare 25,600 Python and C# steps; 35 targeted core fixtures cover command acceptance, queue overflow, food, growth, every current combo, speed, length, score ceiling, monotonic combo expiry, stable off-path food, normalized random-stream use and respawns, collision precedence, tail movement, wrapping, exact starvation, full-grid victory, and ordered events; 8 targeted Shield fixtures cover entry collection, pickup and active expiry, collision consumption and prevention, expiry precedence, starvation bypass, the simultaneous collision and starvation boundary, normalized state, and ordered power events.
- The Godot 4.7.1 project imports and completes seeded rules, canonical restoration, logical input, focus and controller-disconnect safety, audio buses, all finite PCM fallback cues, typed power feedback, live terminal replay recording, isolated atomic storage, exact reload, read-only import, bounded future-schema feedback, replay browsing/playback, menu-run-death-restart with mirror-completed terminal replay save, structured log event assertions (`run_dead`, `replay_finalized`, `run_start`), and clean-shutdown smoke paths on Windows. Any engine warning, leaked object, missing replay (expects 1-4 isolated envelopes), or leftover temporary file fails qualification.
- Required `input-cadence-qualification-v1` evidence covers keyboard, D-pad, and stick through the real Godot InputMap direction mapper under low, normal, and stressed render schedules. Each case accepts and consumes five rapid alternating turns exactly once, leaves no queued direction, rejects passive stick drift, and reaches hash `b38d3b7b837c7c72`.
- Required `settings-screen-qualification-v1` evidence covers 6 nonempty sections, 34 described rows, schema-7 persistence with schema-1/2/3/4/5/6 migration, raw keyboard/controller completion, Vibe adaptation opt-out and category isolation, default-off local playtest consent, a uniformly applied bounded stick deadzone, D-pad digital fallback at the maximum threshold, single-instance Master mono downmix, section restore, separate tutorial reset, lossless reset cancellation, confirmed reset, save/reload, and visible recoverable write failure.
- Required `mode-contract-qualification-v2` and `adaptive-fairness-qualification-v1` evidence locks Classic/Vibe boundaries, default-on Vibe policy, zero-to-two-tick hunger-drain bounds, Support/Standard/Pressure states, closed deterministic inputs, replay safety, explicit score metadata, schema-7 opt-out round-trip, achievement mode eligibility, and distinct Classic, Vibe DDA-on, and Vibe DDA-off categories.
- Required `local-playtest-summary-qualification-v1` evidence covers default-off consent, schema-7 preference round-trip, one terminal seeded human capture, the exact 26-field schema-2 privacy allowlist, exact nine-row aggregate power summaries, identity-verified schema-1 migration, ten forbidden field families, 200-summary, 512 KiB, and newest-20-export bounds, local export, lossless deletion cancellation, confirmed permanent source/export deletion, and absence of an uploader through raw keyboard/controller routes.
- Required `power-decision-qualification-v1` evidence covers all nine reachable product powers, four tactical families, family anti-redundancy, pre-collection offers, active and held-state readability, eight aggregate lifecycle stages, schema-2 local-only summaries, config identity separation, six seeded synergy scenarios, and the default-off Mutation Fork prototype. Human route quality and the prototype keep/remove decision remain pending.
- Required `candidate-reliability-qualification-v1` evidence mirrors exactly 100,000 balanced-AI steps in each of Classic and Vibe, including decisions, direction-queue outcomes, ordered events, and state hashes. It also advances 100 fresh spectator sessions for eight steps each, verifies reset state and managed collection, and samples Godot node, object, resource, and orphan counts at baseline and every ten restarts.
- Required `candidate-fault-campaign-v1` evidence injects interrupted atomic replacement, corrupt JSON, disk-full HRESULT, read-only player data, missing optional resources, invalid packs, and unavailable audio through production boundaries. Each row preserves committed data, verifies recovery, and leaves rules unchanged. Local diagnostics also retain privacy-safe crash and deterministic-divergence reports with exact reproduction fields.
- Required `balance-laboratory-v1` evidence locks nine deterministic policies, ten hostile scenarios, 324 paired runs, 124,242 step comparisons, 27 distributions, 324 final hashes, reviewed fixed/exploratory/failure corpora, seven verified outlier replays, and a null first divergence. Required `observed-balance-baseline-evidence-v1` separately runs 100 reviewed seeds per policy and variant, records 2,700 complete metric summaries, matches a reviewed distribution hash, classifies six reference AI policies, and requires human target ranges to remain empty.
- Required `run-end-qualification-v1` evidence covers ordered collision/starvation attribution, recovery guidance, persistent fair-category personal bests, new unlock summary, same-input restart rejection, later deliberate keyboard/controller restart, confirm-only restart, and retained menu/settings/replay access.
- Required `score-browser-qualification-v1` evidence covers keyboard/controller entry and navigation, explicit import confirmation, lossless cancel, exact-once source-preserving Python top-ten import, visible noncompetitive legacy classification, top-ten and field bounds, existing-personal-best visibility, and local-score reset ownership.
- Required `player-data-recovery-qualification-v1` evidence covers five separate reset categories, exact target confirmation, cancel-without-write, backup-before-removal, copy/hash integrity, corrupt restore rejection, no-overwrite conflicts, successful restore, visible backup location, and raw keyboard/controller routes.
- Required `bare-arcade-loop-qualification-v1` evidence covers one-step input response, three-turn ordering and overflow, 3:1 production-token visibility, wrap continuity, host-smoke frame budgets, same-step death attribution, deliberate restart, zero transient reset residue, and six semantic frame descriptors spanning aspect and accessibility profiles. Its linked experience handoff keeps physical pixels and subjective feel explicitly pending.
- Required `feedback-matrix-qualification-v1` evidence covers every ordered rules event and shell action, dominant cues, accessibility alternatives, complete fallback-cue accounting, stack/interruption policy, safe bounds, explicit authored absence, haptic metadata, and zero approved-but-unused shipped feedback assets.
- Required `onboarding-qualification-v2` evidence covers missing-profile title-first startup, explicit optional Help access, tutorial/direct-play choice, the eight action lessons, active keyboard/controller prompts, skip, completion, replay, reset, and strict competitive-score, achievement, and replay isolation.
- Required `accessibility-presentation-v1` evidence covers default, reduced-motion, flash-free, and combined profiles, with no full-screen flash, zero effective shake in protective profiles, non-shortened captions, full cue/text retention, and rules-state isolation.
- Required `candidate-accessibility-audit-v1` evidence binds the seven underlying accessibility records by SHA-256, locks all twelve V090-06 areas and P1 required-flow severity, and crosses 150 percent text with the eight supported display classes. Export qualification validates the same summary, and the release aggregate requires three passing platform audits and 24 display rows without claiming retained pixels, physical-device usability, or accessibility-user review.
- Required `mouse-input-qualification-v1` evidence covers scaled menu hit testing, left-confirm settings and start routes, vertical and horizontal wheel navigation, right-Back, head-relative gameplay direction, letterbox rejection, and keyboard/controller binding isolation through real Godot input dispatch. Export qualification requires the same record.
- Required `manual-product-matrix-handoff-v1` evidence validates the exact 4-row, 36-flow, 144-cell V090-07 protocol and keeps execution and release acceptance false at zero retained sessions. Four validator contracts prove exact repository shape, drift rejection, complete-session acceptance, and failure/incomplete-session rejection.
- Required `external-validation-handoff-v1` evidence validates the exact 4-cohort, 3-artifact-platform V090-08 protocol and keeps external validation and release acceptance false at zero candidates and sessions. Six validator contracts prove exact repository shape, drift rejection, complete-session acceptance, clean replacement plus affected-gate reruns, retained-evidence or comprehension failure rejection, and malformed-record fail-closed behavior.
- Required `release-materials-handoff-v1` evidence validates ten nonempty release documents, three artifact platforms, four input classes, six screenshot roles, two video roles, and eight permitted claims while keeping candidate materials and release acceptance false without an exact candidate record. Four validator contracts prove exact contract shape, foundation qualification, full candidate acceptance, and media presence, format, and hash rejection.
- Required `release-rehearsal-handoff-v1` evidence validates a 3-platform, 11-operation, 33-cell V090-10 protocol while keeping rehearsal and release acceptance false without a staged record. Four validator contracts prove exact contract shape, handoff qualification, complete rehearsal acceptance, and failed rollback, changed protected data, or tampered retained-evidence rejection.
- Required `stable-promotion-handoff-v1` evidence validates the fixed `1.0.0` version/tag, ten upstream decisions, three public artifact rows, seven preserved evidence categories, and six compatibility acknowledgements while keeping promotion and release acceptance false without a protected-workflow record. Four validator contracts prove exact contract shape, guard qualification, complete promotion acceptance, and failed upstream decision or tampered public artifact rejection.
- A packaged Windows x64 Debug qualification player launches outside the checkout and reports state hash `600f29e8919a9400`.
- The qualified Windows Release distribution currently contains 199 files before its manifest and passes isolated replay storage, complete SHA-256 inventory, required Rules, Persistence, and Game payload, project-payload path, no-Python, no-export-lock, no-checkout-path, no-engine-warning, and no-leaked-object checks. Per-build byte counts and checksums live in the generated artifact manifest so this procedure does not preserve stale artifact identities.
- The Debug payload passed the checksum-bound schema 2 inspector with manifest SHA-256 `da98f60875d33ac110c35e411f63353addf956880e9be023e72e897b763a31d2`, then produced a 71,907,333-byte qualification ZIP with SHA-256 `5b656bf8f82fbdfbc0f4aede557bc73437c4b4279d0b7f772ba25d177670b843`. Signing readiness correctly marks this Debug artifact as non-promotable.
- Ruff and the anti-slop source policy pass across all active source, tests, scripts, native code, workflows, and canonical documentation.
- Every game-state renderer and menu has a headless smoke test.
- Keyboard, mouse, and simulated gamepad paths are covered.
- Save migration, corruption recovery, future-schema protection, and atomic-write failure are covered.
- The reference gameplay QA runner passes seeded policy campaigns, immediate trace replay, and property-generated input sequences.
- A parity mismatch writes a schema 1 JSON bundle with fixture, case, seed, shortest failing step prefix, normalized states and events, actual canonical state and hash, rules and runtime identity, and a one-command reproduction. CI uploads the bundle even though the test job failed.
- The deterministic content gate classifies and hashes the public inventory (114 files including 95 radio MP3s), performs bounded structural checks including decoded PNG scanlines and MPEG structure, reports one duplicate copy in one group, excludes development-only material, and keeps export eligibility at zero until pack quality gates pass.
- Schema 1 content-pack tests reject unknown or duplicate fields, oversized manifests, text, arrays, and numeric versions, unsafe or colliding paths, stale bytes or hashes, incomplete approved allowlists, uncleared rights, mismatched credits, invalid semantic-version or ruleset ranges, bad station track lists, dependency errors, malformed optional packs, and optional failures that incorrectly block a valid offline core. Python qualification and native loading share the same published bounds.
- Station badge checks render all eight icons with the pinned Pillow graph and project-owned pixel glyphs, then compare exact PNG bytes with the checked-in set.

The CI workflow declares Python 3.11, 3.12, 3.13, and 3.14. The complete suite passes locally on all four versions, but a hosted run cannot be confirmed until this workspace is connected to a Git repository and remote.

## Test layers

| Layer | Purpose | Location |
| --- | --- | --- |
| Unit | Pure scoring, movement, death telemetry, persistence, achievements, and individual power-up rules | `tests/core/`, `tests/powerups/` |
| Rendering | Headless menu, HUD, snake cosmetic, particle, and background execution | `tests/rendering/`, `tests/integration/` |
| Integration | Game construction, state dispatch, gameplay sequences, persistence boundaries | `tests/integration/`, selected root tests |
| Property and stateful | Generated values and action sequences with minimized reproductions | `tests/qa/`, expanding with the deterministic engine |
| Gameplay QA | Seeded automated policies, per-step invariants, trace replay, and machine-readable campaign reports | `src/vibesnake/qa/` |
| Native rules and replay | Engine-free movement, randomness, canonical serialization and restoration, generated action sequences, state hash, input, food, collision, starvation, Shield lifecycle and recovery, live replay mirroring, compatibility, deterministic verification-work accounting, and determinism contracts | `native/tests/VibeSnake.Rules.Tests/` |
| Native persistence | Strict encoding, bounded reads, source-preserving import, traversal rejection, sequential and concurrent idempotence, cross-process lock contention, atomic conflict behavior, concurrent file and byte capacity, I/O results, post-load verification, complete replay metadata/status projection, verified replay and closed privacy-safe run-summary export, exact stale-safe deletion consent, spaces and non-ASCII paths, plus optional-pack allowlist/hash validation, bounded path-free asset reads, restart-safe quarantine discovery, tamper isolation, and recoverable lifecycle | `native/tests/VibeSnake.Rules.Tests/ReplayStoreTests.cs`, `OptionalPackStoreTests.cs` |
| Godot integration | Real engine import, C# assembly loading, scene startup, logical input and explicit conflict resolution, centralized theme/font ownership, contrast-qualified vector prompt badges, focus lifecycle, 31-cue fallback audio with rapid-retrigger/mute/failure/backoff/recovery/cache/cleanup evidence, typed power feedback, deterministic continuation, isolated replay recording and storage, bounded background metadata/status verification, speed/clean-capture/playback controls, atomic replay and summary export, exact deletion cancel/confirm, lossless terminal-save queuing, replay-work run gating, save-aware quit, bounded import feedback, and clean process exit | `game/` and CI `godot-smoke` jobs |
| Native artifact | Checksum-verified export, outside-checkout packaged launch, bundle inventory, prohibited-content checks, and per-file hashes | `scripts/test_native_export.ps1`, `scripts/inspect_native_artifact.ps1`, and CI `godot-smoke` jobs |
| Source content | Exact classification, media integrity, SHA-256 inventory, duplicate detection, rights status, and export eligibility | `config/content_policy.json`, `config/content_inventory.json`, and `scripts/content_inventory.py` |
| Content curation | Exact per-station candidate accounting, balanced inventories, station identity, suspicious-name and duplicate rejection, pending human/media gates, and deterministic manifest-bound notices | `config/content_curation_v1.json`, `ContentCreditsDocument.cs`, and `ContentCurationQualificationTests.cs` |
| Creator content | Data-only personality and canonical pack-set commands, closed schemas/examples, stable error codes, compatibility, collision rejection, resolution order, and no-code/no-network boundary | `native/tools/ValidateCreatorContent/`, `docs/content/CREATOR_CONTENT.md`, and `CreatorContentQualificationTests.cs` |
| Localization | Stable copy IDs, exact format parameters, pseudo-locale determinism and expansion, fallback-font glyph coverage, input-glyph preservation, maximum text-scale fit, and explicit remaining migration count. Checkout source migration is audited during editor qualification; packaged runtime smoke independently exercises the closed catalog and layout without requiring source files. | `game/scripts/ShellLocalization.cs`, `docs/design/LOCALIZATION.md`, `TestResults/native/localization.json`, and `TestResults/native/localization_runtime.json` |
| Content packs | Exact approved allowlists, canonical manifests, compatibility, dependencies, rights-derived credits, station tracks, and isolated optional rejection | `src/vibesnake/content/packs.py`, `scripts/content_packs.py`, and `tests/qa/test_content_packs.py` |
| Manual | Visible gameplay, audio judgment, and other irreducibly perceptual checks | `scripts/manual/` |

## Deterministic collection boundary

`tests/` contains only deterministic automated tests. Broken legacy validators that duplicated current coverage or imported removed APIs have been deleted. Genuinely perceptual tools live under `scripts/manual/` with explicit entry points and no import-time side effects.

Pytest uses `testpaths = ["tests"]`, so manual tools and retired scripts cannot be collected accidentally.

## Isolation

The test session sets:

- `SDL_VIDEODRIVER=dummy`
- `SDL_AUDIODRIVER=dummy`
- `PYGAME_HIDE_SUPPORT_PROMPT=1`
- `VIBESNAKE_DATA_DIR` to a temporary directory

JSON save files are cleared before each test, and the temporary directory is removed after the session. Tests must not write to the player's `data/` directory.

## Coverage policy

Coverage is a floor, not the objective. Prefer tests that verify player-visible contracts and cross-module behavior. In particular:

- A power-up needs a `Game.update` integration test.
- Persistence changes need restart, migration, corruption, and failed-write tests.
- Rendering needs representative branch coverage plus visible review.
- Audio control-flow tests retain `audio-mixing-policy-v2` evidence for 31 cues, playback-free allocation decisions, bounded production voices, bus routing, music duck/restore, saved-volume isolation, output-topology repair, 992 rapid retriggers, mute, failure/backoff/recovery, cleanup, and rules isolation through the Dummy backend. `sfx-catalog-qualification-v1` requires unique identities, connections, provenance/license declarations, measured procedural peak bounds, no clipping, candidate exclusion, and one-to-one power cues. Physical-device and listening review remain separate human/platform gates.
- Multimodal presentation tests retain `multimodal-feedback-v1` evidence for four starvation phases, four combo milestones, all nine stable power identities, both death causes, pre-consumption protection language, and five accessibility/audio profiles. Muted and combined minimum-effects cases must retain exact text plus distinct stable geometry for collision and starvation; physical-pressure readability and recovery anticipation remain human gates.
- Radio tests retain 12 playback-free policy contracts plus `radio-behavior-qualification-v1` for validated-manifest metadata, shuffle and repeat rules, resume and station switching, end-of-track behavior, missing-track/pack recovery, bounded status/help presentation, packaged inventory, decoder presence, keyboard/controller cycling, and gameplay-RNG isolation. Physical MP3 decode, output resume, loudness, and listening await an approved pack and human review.
- Visual hierarchy smoke retains `visual-hierarchy-qualification-v1` plus five generated PNGs. CI rejects missing or altered files, incorrect PNG signatures, hash or dimension mismatches, over-budget particle/shake/flash/popup/overlay counts, unapproved peak event classes, foreground contrast below 3:1, a renderer disconnected from the policy, or any rules-state change. Retained live platform pixels and subjective review remain separate acceptance evidence.
- Performance smoke retains `performance-qualification-v1` for minimum, default, and maximum-safe profiles with 40 frames each. It rejects missing statistics, incorrect percentile ordering, gross shared-host regressions, incomplete full-board stress geometry, over-budget particles/audio/logical draw submissions, full-screen flashes, or rules hash drift. A tail-only failure with every average under budget permits one complete resample, and the unchanged ceilings must pass on that final attempt. The aggregate matrix requires 360 accepted samples, exact profile shapes and budgets, and one rules hash across Windows, macOS, and Linux. Dummy-backend driver draw-call unavailability is recorded explicitly. The 60 FPS and 16.67 ms target is accepted only from named minimum hardware, not ordinary CI.
- Vibe Level smoke retains `vibe-level-qualification-v1` for the five exact levels, once-only transitions, 13 fixed presentation scenes, seven accessibility profiles, 4.56:1 minimum contrast, fatal-gameplay priority, single-authority routing, and rules/score-category isolation. CI also proves a combo-20 event without a director transition receives ordinary food feedback, so subscribers cannot silently recreate escalation thresholds.
- Shell presentation tests retain `shell-presentation-v1` evidence for central font/palette ownership, contrast, prompt families, vector badge shapes, text fallback, distinct non-color state markers, keyboard/controller long-catalog pagination, maximum-text fallback-font layout bounds, and required-flow coverage; screenshots and visible review remain separate gates.
- Localization smoke retains `localization-qualification-v1` evidence for 516 stable IDs, 73 exact named-parameter templates, thirteen migrated shell flows, 18 onboarding IDs, 24 feedback IDs, and 24 broadcast caption IDs. It also requires deterministic `qps-ploc` expansion, unchanged input-glyph parameters, zero missing fallback-font glyphs, maximum-text-scale logical-canvas fit, and zero direct draw, prompt, static status, composed status, or audited domain-presentation expressions.
- Spectator smoke retains `spectator-experience-qualification-v1` evidence for ten measured rivals, fifty authored event lines, exact closed seed/speed/explanation/prediction choices, raw keyboard/controller completion, deterministic equal-rules rivalry outcomes, typed overlays, stall and presentation recovery, repeated switching without rules mutation, AI-state-free human seed challenges, atomic local league persistence, privacy, and strict wagering, currency, and human-progression isolation.
- Optional lore smoke retains `optional-lore-qualification-v1` evidence for 41 entries across exact 19/14/8 depth counts, all eight stations, ten rivals, and nine mutations, six discoverable content kinds, four archive content kinds, initial and full unlock completeness, copy resolution, continuity, safety, raw keyboard/controller completion, offline availability, namespace separation, rules isolation, and zero progression awards.
- Offline comparison smoke retains `offline-comparison-qualification-v1` evidence for stable tamper-evident seed codes, exact rules/content/configuration identity, three allowed options, four fixed household slots, a 16 MiB source-preserving import boundary, modified/incompatible rejection, raw keyboard/controller routes, a live equal-rules and state-isolated ghost, exact 26-field private run cards, atomic idempotent export, fresh deletion consent, lossless cancel, exact delete, progression isolation, and core-offline operation.
- Capture smoke retains `capture-sharing-qualification-v1` evidence for default-off clean capture, six hidden presentation-only overlay families, raw keyboard/controller routes, four replay speeds, deterministic seek/reset, rules isolation, verified atomic/idempotent summary export, an exact closed 24-field schema, complete version/rules/integrity metadata, and identity/path privacy exclusions. Retained platform captures and trailer composition stay outside automated acceptance.
- Each hosted Windows, macOS, and Linux Godot job uploads `TestResults/native/*.json` as a 14-day platform-specific qualification artifact.
- `dependency-inventory-v1` is regenerated from six NuGet and two Python lock files during native qualification. It retains unique packages, source-lock hashes, a combined lock-set digest, pinned tool versions, runtime ID, full Git revision, and worktree state; qualification rejects missing, duplicate, unpinned, or unreferenced entries.
- Export qualification retains `artifact-read-only-install-v1` after staging install, fresh user-data, and log paths containing spaces and non-ASCII characters and applying a real read-only install boundary. It requires an adjacent write probe to fail, launches the packaged player with isolated user data and logs outside the install, and requires the complete installed-file digest to remain unchanged.
- Godot retains `core-only-offline-v1` only after strict native manifests accept a qualified core and optional fixture, then prove optional absence, removal, tamper, incompatibility, duplicate IDs, explicit targeted removal consent, cancel preservation, validated installed payloads, bounded asset reads, recoverable quarantine, restart-safe receipt rediscovery, revalidated restore, and player-data preservation cannot block the complete automated offline flow including the keyboard/controller content screen.
- AI needs fixed-scenario behavior tests plus seeded tournament metrics.

Do not exclude difficult runtime modules merely to raise the percentage.

Automatic gameplay QA must not be described as proof of fun. It finds correctness defects, divergence, balance outliers, and reproducible stress cases. Human tests still own comprehension, feel, tension, delight, fatigue, aesthetics, and replay desire.

## CI

[.github/workflows/ci.yml](../../.github/workflows/ci.yml) runs the Python quality matrix, retains seeded QA evidence, builds and tests the pure C# rules on Windows, macOS, and Linux, and runs the real Godot headlessly on all three systems. Each native runner also installs a checksum-verified platform template, exports its packaged player, launches it outside the checkout, inspects and hashes the artifact, and uploads its manifest. Full qualified player bundles are uploaded for tagged and manually dispatched runs. The native jobs use locked NuGet dependencies, formatting checks, warnings as errors, 90 percent C# line and 85 percent branch floors per Rules/Persistence module, and checksum-verified engine archives.

Hosted execution is active on the canonical public repository: native player export and smoke jobs run on Windows, macOS, and Linux. The next CI expansion is broader Python-to-C# differential evidence, retained scaling screenshots, content allowlists, and signed provenance/attestations. Lock-derived dependency inventories already retain per-platform evidence. Physical-device and visible-presentation claims still require retained human evidence beyond headless hosted execution.

## Manual release checks

Automated SDL dummy rendering cannot verify appearance, font fallback, music balance, controller feel, or fullscreen behavior. Use [RELEASE_CHECKLIST.md](../release/RELEASE_CHECKLIST.md) for the human pass.
