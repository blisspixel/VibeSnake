# Changelog

Notable player-facing and engineering changes are recorded here. The project is pre-release and does not yet promise semantic-version stability.

## Unreleased

### Changed

- The root launchers now start the native Godot and C# product from source. The Python implementation remains available only as the frozen behavioral oracle.
- Reworked the README into a concise project front door that routes design, engineering, player, and release detail to canonical documents. Updated those documents to match the native source default, current controls, persistence schema, validation counts, and release posture.
- Replaced the legacy README media with four deterministic 1280x720 native captures covering the main menu, Vibe gameplay, customization, and the compact AI channel. The capture verifier now fingerprints the native presentation sources and rejects stale or incomplete screenshot sets.
- Reduced the active Let's Play overlay from a 106-pixel dashboard to a translucent 44-pixel two-line broadcast ticker. Control prompts appear briefly at start or after pointer, keyboard, or controller interaction, auto-hide after three seconds, and remain while paused or complete. Measured HUD columns keep ordinary radio station and track names visible without collisions.
- Unified bare-loop and performance qualification around the same 60 ms shared-host p95 ceiling, retaining the 25 ms sustained-average ceiling, the 100 ms bare-loop hard-frame cap, and named-hardware 60 FPS acceptance. When every profile average passes and only the shared-runner p95 tail fails, performance smoke may take one fresh sample set; the second result must pass the unchanged ceilings.
- Unified local and hosted native coverage execution behind one module-validating gate with a single clean rebuild retry for truncated Coverlet hit streams.
- Roadmap, README, release status, technology strategy, and native README now state the product path explicitly: Godot + pure C# ship; Python is a frozen oracle only. Next-work table prioritizes shell depth, packaging, and pack eligibility over further Python feature work.
- Input, viewport, presentation, audio, content, replay, onboarding, run-end, player-data recovery, balance, development, testing, and release documentation now matches the verified controller-remap, vector-prompt, scaling-matrix, audio-mixing/recovery, strict-pack, signing-readiness, deterministic-package, power-decision, replay-browser, AI-personality, progression, content-curation, creator-validation, offline-comparison, and 891-test native baseline.
- Added a pure broadcast policy for eight explicitly unapproved station identities, four safe host boundaries, event-aware ducking, critical-cue priority, captions, fatigue limits, and no-repeat host selection. Radio track choice now uses per-station shuffle bags that exhaust playable tracks and prevent immediate repeats across refills.
- Added stable `classic@1` and `vibe@1` product-mode contracts with separate fair-score categories, exact mode factories, remappable keyboard/controller menu selection, and retained `mode-contract-qualification-v2` evidence. Classic disables starvation, combo/speed/length bonuses, near misses, powers, progression scoring, and adaptation in rules rather than presentation only. Config identity advances to `sha256-canonical-runconfig-v3` so mode, score-model, and DDA policy fields cannot mix categories.
- Added deterministic Vibe policy `vibe-bounded-hunger-v1`. Support drains hunger every other step below combo 3 in the warning band, Pressure adds one hunger tick every fourth step at combo 10 or higher outside that band, and Standard drains normally. The live HUD and run-end score metadata disclose state and policy. Preferences schema 5 adds a keyboard/controller-accessible opt-out and isolates `vibe-standard-v1-dda-on` from `vibe-standard-v1-dda-off`; `adaptive-fairness-qualification-v1` proves bounds, determinism, replay restoration, metadata, preference round-trip, and category isolation. Current native run-local achievements are explicitly Vibe-only pending the V070-08 per-achievement audit.
- Added `balance-laboratory-v1` with nine deterministic routing, stress, personality, and replay policies; ten hostile scenario probes; 324 paired runs; 27 descriptive distributions; 124,242 compared steps; seven verified outlier replays; state hashes; first-divergence evidence; and reviewed fixed, exploratory, and previous-failure corpora.
- Added a hash-locked observed balance baseline over 100 reviewed seeds, three fair-score variants, nine policies, and 2,700 runs. Per-run and distribution evidence records score, survival, length, food rate, death causes, combo peak, power encounters, pickups, activations, and outcomes. Six policies are classified as reference AI, while human target ranges remain explicitly empty.
- Added the pure C# `native-personality-controller-v1` catalog and AI league. All ten built-in personalities run across the same twelve reviewed seeds in 120 simulations and 99,358 paired deterministic steps. `native-ai-league-v1` records the seven roadmap metric families by personality and rules version, performs 60 same-state trait interventions, flags fifteen low-materiality personality/trait pairs for truthfulness work, and proves every AI score identity is noncompetitive without constructing human score persistence.
- Advanced the native AI policy to `native-personality-controller-v2`. Reviewed broadcast names and descriptions now match ten explicit measured behavior claims; risk can actively accept hazards or preserve exits, `greed` changes food routing and power detours, chaos remains bounded, and all 60 trait interventions exceed the 1 percent materiality floor over 98,984 paired steps. The strict shared custom schema rejects unknown, duplicate, oversized, unsafe, reserved, non-finite, out-of-range, and invalid-color content with source-specific reports. Typed spectator snapshots expose target, risk, policy, current reason, five recent decisions, and unambiguous built-in versus `CUSTOM / UNOFFICIAL` status through `ai-personality-qualification-v1`.
- Advanced opted-in local playtest summaries to schema 2. The exact 26-field balance-only record adds nine catalog-ordered per-power aggregate rows for offer, detour, collection, activation, expiry, consumption, save, and death adjacency, while excluding identity, raw input timing, device and system details, paths, and free text. Identity-verified schema-1 records migrate without weakening the newest-200, 512 KiB, newest-20 export, default-off consent, local-only, and separately confirmed permanent deletion boundaries.
- Expanded the hash-locked structured-human-playtest protocol to fifteen scenarios and nineteen observation fields. The six added power cases cover Boost plus Phase Shift, Slow-Mo plus Magnet, Bait plus Boost, Gluttony plus Magnet, Segment Detach plus protection, and Last Stand after a long combo. The automated handoff now requires eleven retained artifacts and deliberately reports zero human sessions, unverified experience, and no human target ranges until real participant evidence is reviewed.
- Added `power-decisions-v1`: all nine product powers are reachable in Vibe under protection, tempo, harvest, and geometry anti-redundancy rules. Pre-collection offers retain family, marker, effect, duration, and held-state presentation beside active powers. `power-decision-qualification-v1` locks deterministic reachability, compatibility, lifecycle aggregates, six seeded synergy scenarios, and config identity. The deterministic two-choice Mutation Fork prototype remains default-off, unwired, and human-unverified.
- Added a strict balance-experiment registry and automated guard. Seven eligible balance families require target ranges before any change, exactly one family per experiment, a declared competence/autonomy/tension/recovery effect, fixed-corpus and human evidence, exact identities, and keep/revert/blocked review. The passing initial state contains zero targets and experiments and cannot authorize tuning from average score alone.
- Added a closed eight-context score-purpose and seed-origin taxonomy. Personal-best schema 2 now persists explicit mode, run kind, seed category, score category, difficulty, DDA, config, and display identity; only normal human and separately categorized seeded challenges are competitive, while schema 1 migrates visibly to noncompetitive `Legacy 0.2`. A hash-locked audit accounts for all 25 reference achievements, retaining 17 explicit Vibe-only native candidates and documenting every Classic or reference-only exclusion.
- Added schema-1 native top-ten history per exact score category and a player-facing Local Scores browser. Keyboard V or Down and controller Down open the same screen; category navigation, confirmation, cancellation, and import work through both input families. A confirmed one-time import reads only `user://imports/high_scores.json`, records its SHA-256, preserves the source, sanitizes bounded local labels, and presents every imported row as noncompetitive `Legacy 0.2`. Local-score recovery owns both `personal_bests.json` and `score_history.json`.
- Native coverage gates now require at least 90 percent line and branch coverage independently from both Rules and Persistence in local and hosted CI runs, and reject reports that omit either module.
- Input binding validation rejects non-finite controller-axis values, and release artifact parsing rejects duplicate or unknown fields at every schema level.

### Added

- Added `candidate-reliability-qualification-v1`. Packaged smoke now mirrors 100,000 balanced-AI steps in each of Classic and Vibe and requires identical decisions, queue outcomes, ordered events, state hashes, and a null first divergence. It also advances 100 fresh spectator sessions, verifies reset state and managed collection, and retains eleven stable Godot node/object/resource/orphan samples. A failure now retains first-divergence seed, run, step, hashes, and recent-command evidence before the candidate fails.
- Added `candidate-fault-campaign-v1` through the real packaged smoke. Seven production-boundary probes cover interrupted writes, corrupt JSON, disk-full errors, read-only player data, missing resources, invalid content packs, and unavailable audio while requiring committed-data preservation, recovery, and unchanged rules. Privacy-safe synthetic crash and deterministic-divergence reports verify the local triage path. The aggregate matrix requires 21 fault rows and both triage probes across Windows, macOS, and Linux; retained Release execution on all three platforms remains open.
- Added `performance-qualification-v1` to every release-matrix row. The aggregate requires 360 ordered live-frame samples, exact minimum/default/maximum-safe stress shapes, reviewed shared-host ceilings and presentation budgets, and one rules-state hash across Windows, macOS, and Linux. Named-hardware 60 FPS acceptance, resolution captures, long-session resource review, and minimum/recommended specification publication remain open.
- Added `candidate-accessibility-audit-v1` to packaged smoke and every release-matrix row. The fail-closed aggregate SHA-256-binds seven source records, locks twelve ordered roadmap areas, requires independent keyboard/controller remapping and single-action routes, separated audio and mono output, multimodal alternatives, reduced motion, zero full-screen flashes, and 150 percent text across eight display classes. The matrix requires three platform audits and 24 display rows. The new accessibility feature guide publishes exact native support while physical-device, retained-platform, photosensitivity, and accessibility-user review remain open.
- Added the V090-07 `manual-product-matrix-handoff-v1` contract and validator. Four exact Windows/macOS-Apple-Silicon/macOS-Intel/Linux rows cross 36 required flows into 144 cells, with keyboard, mouse, Xbox-layout, PlayStation-layout, eight settings profiles, candidate/artifact identity, and safe retained evidence requirements. Zero manual sessions are recorded, so execution and release acceptance remain false. The native shell now provides nine scaled mouse menu targets, left-confirm and head-relative steering, middle-pause, right-Back, two-axis wheel navigation, and letterbox rejection with packaged `mouse-input-qualification-v1` evidence.
- Added the V090-08 `external-validation-handoff-v1` contract and validator. Four participant cohorts cross three artifact platforms, four input classes, six fresh-participant comprehension checks, four structured report families, exact clean candidate and artifact identities, retained files, finding closure, and affected-gate reruns. CI records zero external candidates, sessions, findings, and crashes, so execution and release acceptance remain false until the controlled group runs.
- Added the V090-09 `release-materials-handoff-v1` contract and validator plus privacy, recovery, known-issues, credits, and third-party-notice foundations. Final material acceptance requires ten exact document hashes, three-platform OS and size disclosures, physical evidence for keyboard, mouse, and both controller layouts, separate content sizes, exact save locations, six candidate screenshot roles, two candidate video roles, and eight evidence-linked claims. Current pending markers and absent candidate media keep release acceptance false.
- Added the V090-10 `release-rehearsal-handoff-v1` contract and validator. Three platforms each require eleven passing staged acquisition, signature, install, save, optional-content, update, rollback, and removal operations. Candidate and previous artifacts, manifests, release materials, migration fixtures, protected user data, withdrawal, operational authority, and every evidence file are hash-bound. No staged rehearsal record exists, so rehearsal and release acceptance remain false.
- Added the final `stable-promotion-handoff-v1` guard. Tag and version `1.0.0` cannot pass until ten upstream decisions accept one revision, the protected workflow rebuilds it, all public artifacts and their provenance/checksums match, the approved optional pack stays separate, public-file installs pass, the complete release record is preserved, and six compatibility promises remain exact. No promotion record exists, so stable promotion and release acceptance remain false.
- Added `candidate-install-lifecycle-preflight-v1` to Release artifact qualification. The real exported player now exercises first launch, hash-identical repair and relaunch, read-only and non-ASCII install/user paths, all seven known legacy save fixtures, future-schema rejection without rewrite, optional-pack and player-data recovery, and application removal with external data retained. The aggregate matrix requires the preflight on Windows, macOS, and Linux while selected-channel installer lifecycle and real cross-version binary rollback remain open.
- Added candidate-only exported-player launch reliability. Release matrix jobs run 100 clean, timeout-bounded, warning-free launches from a read-only install with distinct fresh profiles on each desktop platform, and the aggregate matrix requires all 300 before provenance. The Windows Release artifact and one real campaign probe pass locally; hosted three-platform evidence remains pending.
- Added `release-matrix-qualification-v1`, a post-build CI gate that cross-binds Windows x64, macOS Universal, and Linux x64 source revision, build mode, deterministic state hash, dependency lock set, artifact manifest, unsigned signing-readiness, read-only install, and deterministic package evidence. Provenance now depends on the complete matrix. Protected signing, notarization, Linux runtime and desktop integration, final provenance, and channel approval remain open.
- Prepared the inactive V090-01 `candidate-freeze-policy-v1` boundary. CI resolves 116 files across rules, save schemas, replay schema, content manifests, input defaults, accessibility defaults, and candidate fault qualification; validates prerequisite, severity, permitted-change, and required-evidence contracts; and can build a deterministic file and contract SHA-256 baseline after all 0.8 acceptance dependencies close. No candidate freeze is currently claimed.
- Completed the V080-11 offline comparison foundation with stable tamper-evident seed codes, four explicit source-preserving household slots, equal-rules isolated ghosts, atomic privacy-safe run cards, exact content-hashed deletion consent, recovery ownership, raw keyboard/controller routes, and live Godot qualification. Household handoff, platform presentation, and ghost readability remain human gates.
- Completed the V080-10 optional lore foundation with a closed 41-entry three-depth offline archive, existing-progress unlocks, raw keyboard/controller browse routes, strict copy and continuity validation, and rules/progression isolation. Canon, tone, humor, platform presentation, and curiosity pacing remain human gates.
- Completed the V080-09 interactive spectator foundation with ten measured rivals, equal-rules lanes, local standings and rivalry history, authored commentary, typed explanations, exact-seed human challenges, raw keyboard/controller routes, persistence recovery, and progression isolation. Physical-controller, platform, editorial, pacing, and entertainment review remain human gates.
- Completed the V080-08 automated capture and sharing foundation. The remappable Help action toggles clean gameplay and replay capture from keyboard or controller, hiding run HUD, replay controls, terminal, audio-status, debug, and spectator overlay families without changing rules state. Verified replay export now also writes a closed 24-field schema-1 run summary with application, rules, mode, score category, config, integrity, seed, and outcome metadata, explicit identity/path exclusions, atomic no-overwrite writes, and idempotence. `capture-sharing-qualification-v1` retains deterministic playback and privacy evidence while platform captures and final composition remain human gates.
- Completed the V080-07 automated localization migration with 503 stable shell copy IDs, 66 strict named-parameter templates, thirteen migrated shell flows, an opt-in deterministic pseudo-locale, exact input-glyph preservation, accented-glyph coverage, and 150-percent layout measurement. Retained `localization-qualification-v1` evidence resolves all 18 onboarding, 23 feedback, 24 broadcast caption, 41 optional lore, and offline comparison IDs and requires zero direct draw, prompt, static status, composed status, or audited domain-presentation expressions. Visible multi-platform review remains a human release gate.
- Added the V080-06 native `ValidateCreatorContent` command with data-only personality and canonical pack-set modes, stable schema-1 JSON reports, closed published schemas and examples, 31 documented validation/compatibility codes, hard duplicate-ID collision rejection, core-then-ordinal optional resolution, and executable no-code/no-network qualification. Arbitrary code plugins remain outside 1.0.
- Added the V080-05 automated content-curation handoff: all 95 radio assets are assigned once across eight balanced station candidate lists, duplicate and suspicious release inputs are rejected, current approvals remain zero, and deterministic manifest-bound human-readable credits and third-party notices generation is available. Full decode, loudness, listening, core-music selection, badge review, production manifests, and export approval remain explicit gates.
- Added the native V080-04 progression foundation: twenty exact three-lane goals, highlighted-goal persistence, eight curated cosmetic sets with exact locked requirements and five saved loadouts, a finite twelve-event/four-tier fixed-seed Broadcast Tour, expression-only earned rewards, safe notification queuing, strict `progression.json`, and raw keyboard/controller qualification. Tour practice records deterministic replays and same-seed rematches while remaining outside competitive scores, ordinary achievements, local playtest summaries, and human-run progression metrics.
- Replay schema 1 captures canonical shell-supplied UTC time plus explicit gameplay and AI seeds while retaining byte-compatible legacy envelopes without those optional fields.
- Pure clock-free replay playback verifies at construction, advances one deterministic rules step at a time, and supports exact reset and forward or backward seek without mutating player progression.
- The bounded replay store lists generated replay summaries newest first without reading payloads, enforces count and byte budgets, and rejects untrusted manual names from the browser.
- Godot now provides a background-verified replay browser and playback screen from `R` or Controller North. Each bounded row shows date, mode, rules version, score, seed, duration in steps, and explicit verified/incompatible/modified/unreadable state without exposing internal names or hashes. Keyboard/controller playback adds 0.5x/1x/2x/4x speed, pause, single-step, back-ten, HUD toggle, restart, and return; focus loss and last-controller disconnect pause playback. Verified export is atomic and bounded. Per-item deletion uses content-hashed two-step consent, rejects stale plans, cancels losslessly, removes exactly one stored replay, and preserves exports. `replay-browser-qualification-v2` proves both raw input families and progression isolation.
- Central shell transition methods now own every post-initialization screen and pause-state write. Structural tests and the real scene smoke lock the exhaustive nine-state transition graph and replay/settings flows.
- Required `input-cadence-qualification-v1` evidence feeds real Godot keyboard, D-pad, and stick events through the live InputMap mapper and production fixed-step drain under low, normal, and stressed render schedules. All nine cases accept and consume the same five-turn stream exactly once, end with no queued input, share one rules hash, and reject passive stick drift.
- Godot settings now opens with F1 or controller Start and organizes 33 described rows into Gameplay, Controls, Audio, Display, Accessibility, and Data. Preferences schema 6 adds default-off local playtest consent while retaining the Vibe adaptation preference, persisted Master-bus mono downmix, 10 to 90 percent shared gameplay-stick deadzone, schema-1/2/3/4/5 migration, and D-pad digital fallback. Keyboard/controller navigation, adaptation opt-out/category isolation, local summary export/deletion, independent bus values and mutes, mono apply/restore/reload/reset, section resets, lossless reset cancel, confirmed preferences/bindings reset, atomic save/reload, runtime application, and visible session-only fallback on save failure retain `settings-screen-qualification-v1` and `local-playtest-summary-qualification-v1` evidence.
- The strict VirtualViewport evidence matrix now includes an explicit classic 4:3 case and rejects missing, duplicate, or extra matrix entries.
- Typed `accessibility-presentation-v1` policy/evidence covers default, reduced-motion, flash-free, and combined profiles. Full-screen flashes are prohibited, shake is zeroed under protective profiles, caption time is never shortened, all 31 cues and critical text remain available, and presentation settings cannot change the rules hash.
- First-run onboarding detects a missing profile decision and offers direct play or eight deterministic, interactive, unscored lessons for turning, reversal rejection, wrapping, food and score, starvation, Shield, pause, and restart. H or controller left-stick replays it; Data resets only tutorial progress; skip/completion/reset persist atomically; active-device prompts and hard score/achievement/replay isolation retain `onboarding-qualification-v1` evidence.
- Run end now presents outcome, exact collision or starvation cause, relevant recovery guidance, score, fair-category personal best, new-record state, run statistics, new unlocks, replay status, and rules/config identity in a stable order. Confirm is the only restart action; the terminal input sequence cannot restart; later keyboard Enter or controller South can; menu, settings, and replay access remain available through `run-end-qualification-v1` evidence.
- Data settings now separate preferences/bindings, progression, personal bests, replays, and optional content. Confirmation lists exact `user://` targets; background reset creates and SHA-256 verifies a bounded backup before removal; cancel writes nothing; quit waits safely; restore never overwrites current data; corrupt/incomplete backups remain visible and non-restorable with their location and recovery choices. `player-data-recovery-qualification-v1` retains keyboard/controller routes and all failure contracts.
- The bare arcade loop now retains explicit one-step input, three-turn buffer, 3:1 graphical contrast, wrap, host-smoke pacing, same-step death attribution, deliberate restart, and zero-residual reset budgets. Production food color now clears 3:1 against both the head and board. Six cross-aspect/accessibility semantic frames plus linked evidence and an explicit pending-human checklist form `bare-arcade-loop-qualification-v1` without claiming physical feel or platform pixels.
- A closed typed feedback matrix now covers all 19 ordered rules events and 15 shell-action families. Every row declares one dominant channel, visual/audio/text/haptic behavior, priority, cooldown, polyphony, stacking, interruption, ducking, shake, flash, hitstop, criticality, accessibility alternatives, implementation state, and authored-asset state. Qualification accounts for all 31 fallbacks and explicitly reports zero approved-but-unused shipped feedback assets.

- Pure `ReleaseArtifactManifest` schema 2 validates native export inspection manifests (platform payload patterns, SHA fields, byte-sum integrity) and declares installer/archive shapes for Windows/Linux portable folders and macOS app-bundle zip (V030-10).
- `ValidateArtifactManifest` tool and export inspection wire pure C# validation after writing `artifact-manifest.json`.
- Godot bindings browse/remap screen (`B` / right shoulder): Left/Right selects keyboard or controller, Up/Down selects an action, Confirm captures a key, controller button, or deliberate axis, Back cancels, and F8 restores defaults. Remaps persist and reapply without dropping the opposite device.
- Xbox, PlayStation, Nintendo, and generic text prompt families switch only after deliberate input and show the active binding on menu, run-end, achievements, and bindings screens.
- Binding conflicts now identify the owning action and enter an explicit resolution state: Confirm atomically swaps both keyboard or controller actions, while Back/Escape cancels without changing or persisting either binding.
- Central `ShellTheme` palette/font ownership and asset-free vector prompt badges cover keyboard, Xbox, PlayStation, Nintendo, and generic controller labels across menu, run-end, achievements, and bindings flows. Every badge retains readable text.
- Required `shell-presentation-v1` evidence locks two palettes, minimum contrast, all prompt families, all eight badge shapes, centralized font metrics, 150-percent text-layout bounds, scale-aware long-list rows, keyboard/controller achievement pagination, non-color state/focus markers, text fallback, and required-flow coverage.
- Required `virtual-viewport-matrix-v1` evidence covers minimum clamp, 16:9, 16:10, ultrawide, square, 4K, and 5K aspect preservation, pointer round trips, and letterbox exclusion.
- Required `audio-mixing-policy-v2` evidence covers all 31 finite cues, bounded 8-voice SFX and 4-voice UI allocation, cooldown, polyphony, priority, stable interruption, strongest-active music ducking and restoration, immediate isolated saved volumes, output-topology polling and repair, 992 rapid retriggers, full-catalog mute suppression, injected missing-bus failure, bounded retry, recovery, cache bounds, cleanup, and deterministic-rules isolation. The playback-free allocator adds 25 focused contracts.
- `sfx-catalog-qualification-v1` proves distinct navigation/confirm/back, restart, achievement, four combo tiers, combo break, both death causes, and all nine power activations. All 31 procedural PCM fingerprints are unique, licensed, provenance-declared, stereo 22.05 kHz, measured inside the -24.5 to -18.0 dBFS peak window, and connected through the feedback matrix. Authored SFX remain explicitly unapproved pending rights, -18 LUFS/-1 dBTP normalization, decode, repetition, and listening review; candidate metadata and blocked inventory assets cannot enter native artifacts.
- Production `multimodal-feedback-v1` presentation now gives starvation four named color roles plus exact time and segmented shapes, moves score and combo together while retaining a reduced-motion marker and readable multiplier, assigns every power a stable icon/name/state/effect cue, telegraphs Shield, Phase Shift, Last Stand, and recovery protection, and distinguishes collision from starvation with exact text and stable `[X]`/`[0]` geometry. Default, muted, reduced-motion, flash-free, and combined minimum-effects qualification retains at least two death-attribution channels without changing rules state.
- Native `RadioCatalog`, `RadioPlaybackPolicy`, and `RadioStreamPlayer` now project only validated manifest metadata, load one hash-verified MP3 at a time, define shuffle/no-repeat/single-track-repeat/pause/resume/station-switch/end behavior, retain per-station track identity, isolate missing tracks and packs, and consume only the named radio random stream. Bounded station/track/pack/mute/help UI appears on menu, run, and Content Packs; `J` and controller `R3` cycle stations. Twelve focused contracts and `radio-behavior-qualification-v1` retain packaged-inventory, decoder, input, fallback, rules-isolation, and gameplay-RNG-isolation evidence while production packs remain unapproved.
- Production `VisualHierarchyPolicy` now caps particles, per-event emission, shake sources and strength, full-screen flashes, popups, overlays, head-effect outlines, and popup text. Peak feedback is limited to death prevention, death, major achievement or grid completion, and maximum combo. The live renderer uses pressure-aware caption color, bounded caption text, protection-first outline selection, a permanent head-direction marker, and policy-owned terminal opacity. `visual-hierarchy-qualification-v1` measures standard and high-contrast palettes and writes five hash-verified PNG review frames for quiet, busy, warning, recovery, and game-over states without advancing rules.
- `performance-qualification-v1` now measures 40 live Godot frames each for minimum, default, and maximum-safe effects. The maximum mixed scene fills the 2,112-cell board with 2,107 snake cells, three hazards, food, and a power signal while drawing 160 particles and three popups. CI enforces 12 audio channels, at most 2,400 logical draw submissions, gross shared-host p95/max frame ceilings, and an identical final rules hash after 256 steps per profile. The 60 FPS target, driver draw calls, allocation, memory, thermals, and long-session acceptance remain named-hardware gates.
- `VibeLevelDirector` now exclusively maps combo 0/3/5/10/20 to Grounded, Flow, Heat, Overdrive, and Transcendent. Combo feedback, stingers, visual priority, board palette, HUD role, and bounded trail rendering consume typed director output instead of repeating thresholds. Five level definitions declare background/HUD/trail/particle/camera/music/stinger/accessibility budgets, transitions fire once, critical gameplay stays dominant, and every palette remains above 4.56:1 gameplay contrast. `vibe-level-qualification-v1` gates 13 fixed scenes and seven accessibility profiles with unchanged rules hashes and score categories.
- Hosted Windows, macOS, and Linux Godot jobs retain every native qualification JSON for 14 days, including viewport, audio, presentation-frame, throughput, property-campaign, and content-eligibility evidence.
- Lock-derived `dependency-inventory-v1` evidence records unique NuGet and Python packages, every source-lock SHA-256, a combined lock-set digest, pinned Godot/.NET tools, runtime identifier, full Git revision, and dirty-state provenance. Native qualification validates and retains it per platform.
- Export qualification stages the install, user profile, and log under paths with spaces and non-ASCII characters, makes the installed player temporarily read-only, proves a real adjacent write is rejected, launches from a fresh profile with writable data and logs outside the install, verifies every installed file is unchanged, and retains `artifact-read-only-install-v1` evidence on Windows, macOS, and Linux CI.
- Pure C# content-pack schema 1 parsing enforces exact fields, duplicate-field rejection, bounded UTF-8 documents, canonical encoding, safe paths, semantic ranges, dependencies, complete inventory allowlists, file metadata, cleared rights/credits, and radio station contracts.
- Native pack-set resolution treats the core as mandatory while independently isolating absent, removed, malformed, incompatible, tampered, and duplicate optional packs. Godot retains `core-only-offline-v1` evidence across launch, menu, run, critical feedback, settings, content-pack browse, death, restart, and recovery.
- Immutable optional-pack removal consent protects core, requires a current installed-pack version, makes cancel lossless, removes only the selected optional pack on confirm, and has no save/profile/replay deletion capability. The Godot `C` / west-button content screen publishes that separation to keyboard and controller players.
- `OptionalPackStore` validates canonical installed manifests, exact file allowlists, sizes, SHA-256 payloads, safe roots, entry bounds, and link rejection. Confirmed removal uses a same-volume move into recoverable quarantine, restore revalidates content before moving it back, and packaged Godot smoke proves unrelated player data stays unchanged.
- Installed optional assets are exposed only as bounded bytes and media metadata after complete pack validation plus a second size/hash check. Quarantine entries are rediscoverable after restart, and tampered quarantine remains recoverable in place but cannot be restored.
- Strict `release-signing-policy-v1` keeps signing material out of source, artifacts, and ordinary CI; locks Authenticode, Developer ID/hardened-runtime/notarization, and Linux provenance routes; and emits artifact-manifest-linked `release-signing-readiness-v1` evidence. Debug builds are explicitly not promotable.
- Tag and manual release qualification attest artifact manifests through a separate least-privilege GitHub OIDC/Sigstore job. Detached provenance bundles and qualified players retain for 30 days; actual platform signing remains a protected release operation.
- `ReleaseOutputPlan` defines exact Windows ZIP, macOS app-bundle ZIP, and Linux tar.gz direct-download outputs plus portable/app-bundle store-depot shapes. Qualification packaging rehashes the exact manifest allowlist, repeats package creation byte-for-byte, emits separate SHA-256 checksums, keeps optional packs/player data separate, preserves uninstall data, and remains explicitly non-publishable.
- Pure `AudioOutputRecoveryTracker` deduplicates unavailable/restored transitions, sanitizes diagnostic reasons, and applies monotonic bounded retry timing without referencing Godot or rules state.
- Dual-runtime `achievement_candidates_rules_v1` fixture and parity suite with the product flag enabled (score candidates, already-unlocked suppression, empty zero-score emission) without flipping default-off core/power corpora (PD-009).
- Pure `AchievementsBrowseReport` projects the rules-local catalog against permanent unlocks (summary, rarity progress, filtered entries, preview) and drives menu and ended-run unlock captions.
- Shell achievements browse screen (`U` / left shoulder) lists the full rules-local catalog with unlock markers; smoke covers open/return transitions and `achievements_browse_open`.
- Multi-power synergy and anti-synergy campaign tests for the nine-power portfolio (protection handoffs, cross-family composition, same-kind anti-stack, restore, restart cleanup).
- Architecture boundary tests ban Console.Write from pure Rules sources.
- Architecture boundary tests ban Task.Delay from pure Rules sources.
- Architecture boundary tests ban Thread.Sleep from pure Rules sources.
- Architecture boundary tests ban Process.Start and GetCurrentProcess from pure Rules sources.
- Achievements maximum unlock capacity is locked at 256 and verified above catalog size.
- Main menu previews the first unlocked achievement IDs when any permanent unlocks exist.
- Achievements documents reject empty-string and null unlockedIds entries.
- Architecture boundary tests ban wall-clock, global random, and env access from Persistence sources.
- Python CoreSimulation accepts already_unlocked_achievements to suppress known profile unlocks during dual-runtime experiments.
- AchievementsDocument.IsUnlocked helper for profile unlock queries.
- Ended-run overlay shows RUN UNLOCKS progress from permanent profile unlocks.
- Achievements documents accept schema_version as an alias for schemaVersion on load.
- Headless smoke asserts structured `achievements_load` after shell startup unlock restore.
- Ended-run overlay can show UNLOCK SAVED feedback when permanent unlocks are written.
- Architecture boundary test locks Rules→Persistence one-way dependency (no cycles with Game).

- Main menu shows rules-local run unlock count (`RUN UNLOCKS n/total`) from `achievements.json`.
- Inventory gate writes `content-eligibility-evidence-v1` JSON under `TestResults/native` for pack-approval handoffs.
- Achievements document golden serialization and absolute user-data root rejection coverage.

- Pure `ContentEligibilityReport` summarizes inventory ship, rights, and media-type eligibility breakdowns for pack-approval handoffs while exportEligible remains zero.

- Godot shell loads and saves permanent achievement unlocks; SnakeRun.ApplyProfileUnlocks suppresses already-owned candidates without affecting scores or state hashes.
- Restart clears session achievement counters so the next run cannot inherit unlock metrics.
- Death-cause contract fixtures for SelfCollision, Starvation, and closed None set; state-machine campaigns assert session-counter restore parity.
- Pure `AchievementsDocument` schema 1 and atomic `AchievementsStore` for permanent catalog unlock IDs under `achievements.json` (profile unlock foundation).
- Architecture boundary tests ban filesystem I/O surfaces from pure `VibeSnake.Rules` sources (V040-09).
- Versioned pure-rules property campaign report producer (`rules-property-campaign-v1`) writing `TestResults/native/property_campaign.json` with seeds, invariants, failure payload, and one-command reproduction (V040-10).
- Canonical state schema 3 and `fnv1a64-canonical-json-v4` include session achievement counters (`sessionFoodEaten`, `sessionWraps`, `sessionNearMisses`, `sessionPowerupsCollected`, `sessionMaxCombo`) so mid-run restore preserves unlock eligibility (PD-010). Schema 2 states remain intact and fail compatibility.
- Pure `RunConfig.ComputeConfigHash` / `SerializeCanonicalConfig` (`sha256-canonical-runconfig-v1`) and `SnakeRun.ConfigHash` for score and replay metadata without altering the step state hash.
- Pure `InputBindingsDocument.TryRemapAction` for conflict-safe single-action remapping without mutating the source document.
- Pure `InputBindingsDocument.TrySwapActions` for atomic two-action binding exchange without intermediate conflicts.
- Shell accessibility hotkeys: F7 master mute, F9 high contrast, F10 reduced motion, F11 fullscreen, each persisting preferences; `ShellSettings` toggle helpers and headless smoke coverage.
- Pure `ControllerConnectionTracker` with sanitized connect/disconnect captions; shell seeds joypads, shows menu notices, and pauses a run when the last controller disconnects.
- Optional `configHash` / `configHashAlgorithm` fields on offline crash reports; rules throughput evidence JSON records the effective config hash.
- Pure `RunScoreIdentity` for fair-score categories using ruleset contract plus effective config hash.

- Master volume step hotkeys (`=` / `-` and keypad equivalents) with clamp, unmute-on-raise, and preference persistence.
- Ended-run overlay shows a compact `RunScoreIdentity` support caption (ruleset contract, score, config-hash prefix).
- Text scale step hotkeys (F5 down, F6 up) with preferences schema clamp (0.85..1.5) and persistence.
- Flash-free toggle (F4) and open-diagnostics action (F12) for the offline support path; F12 also copies the diagnostics absolute path to the clipboard.
- Dual-runtime `combo_expired` event: Python `CoreSimulation` emits it in native order; `EnableComboExpiredEvent` defaults true; regenerated `core_rules_v4` (35 cases) and golden state hash.
- Dual-runtime near-miss scoring: `CoreSimulation` applies clutch/body near-miss through the production detector; `EnableNearMiss` defaults true; `core_rules_v4` includes clutch on deadline food rescue.
- Pure `AchievementCatalog` candidate evaluation for run-local metrics; `SnakeRun` session counters for food, wraps, max combo, and power collections; expanded personality document validation coverage.
- Terminal `RunEventKind.AchievementCandidate` emission gated by `RunConfig.EnableAchievementCandidates` (default false for shared-fixture parity; product runs enable true); shell ACHIEVEMENT captions via catalog index; `IndexOf` / `DefinitionAt` helpers.
- Replay envelopes store `configHash` / `configHashAlgorithm`; verification rejects config identity drift (`ConfigIdentityDiverged`); unsupported config-hash algorithms fail closed on read.
- Offline `StructuredLocalLog` JSONL writer with `DiagnosticLogLevel` filter, path sanitization, 1 MiB rotation, and shell hooks for session start, diagnostics open, preferences faults, and controller connect/disconnect.
- Python dual-runtime `qa.achievement_candidates` catalog matching native IDs/order; `CoreSimulation` optional `enable_achievement_candidates` (default false) emits terminal `achievement_candidate` events.
- Godot headless smoke asserts structured session log contains `smoke_session_start` and `open_diagnostics` event codes.
- `RunStepResult` equality/operator coverage; README screenshot presentation-source fingerprint refresh after dual-runtime QA source changes.
- Flash-free presentation softens high-intensity captions, lengthens caption dwell, and skips non-critical audio cues while keeping death/victory/pause/confirm.
- Enabling reduced motion zeros screen-shake intensity so the preference cannot leave residual shake.
- `ShellSettings.EffectiveScreenShakeIntensity` forces zero under reduced motion or flash-free for future camera effects.

### Fixed

- Release exports no longer require checkout-only `Main.cs` localization inspection or the authoring-only power-decision JSON during packaged smoke. Source migration and contract audits remain mandatory in the editor qualification, while the packaged player independently verifies its closed localization catalog, pseudo-locale layout, and live feature paths.
- Flash-free no longer suppresses unrelated audio cues, and reduced motion no longer shortens the player's caption reading window.
- Input binding documents reject unsupported device classes, cross-device tokens, unknown adapter tokens, and physical-axis conflicts before save or InputMap application.
- Controller remaps suppress overlapping secondary stick fallbacks, and capture ignores low-amplitude axis drift.
- Terminal `AchievementCandidate` events emit at most once per run; restored terminal states do not re-fire on idle `Step()`.
- Architecture boundary tests also ban HTTP client and System.Net.Http/Sockets from Rules sources and Persistence assembly references.
- Generated state-machine campaign covering once-only `AchievementCandidate` emission across terminal idle steps and terminal restore.
- Shell `WriteLocalCrashReport` helper pairs structured Error log lines with offline crash reports; smoke asserts `smoke_crash_probe`.
- Controller input-binding load faults write the same offline crash report and structured Error path as keyboard load faults.
- Replay envelopes accept optional shell-supplied `appVersion` on capture (product and smoke recorders pass `ProductIdentity.AppVersion`); legacy envelopes without the field remain readable; smoke asserts stored replays retain the version.
- `ProductIdentity.AppVersion` centralizes the shell product version for crash reports and replays; Godot `*.cs.uid` companions are gitignored.
- Shell structured log records `replay_finalized` and `replay_finalize_failed` around terminal replay capture.
- Shell structured log records `run_start` when a product run is created or restarted, and `run_won` / `run_dead` when it ends; smoke death path mirror-completes the terminal step, saves synchronously, and asserts `run_dead`, `replay_finalized`, and post-restart `run_start`.
- Native and exported player smoke harnesses accept 1-4 isolated replays so storage smoke and death-restart can each leave a verified envelope.
- Mid-run restore preserves session achievement counters under canonical state schema 3 (replaces the prior schema 2 characterization gap).
- CI gates `ProductIdentity.AppVersion` against `pyproject.toml` package version via `scripts/check_product_version.py`.
- README screenshot presentation-source fingerprint refreshed after dual-runtime achievement-candidate QA wiring.
- `InputBindingsDocument.TryRemapAction` preserves fractional axis thresholds instead of rounding them to integers.
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
- `LocalDiagnostics.EnsureDiagnosticsDirectory` and interactive shell `OpenDiagnosticsDirectory` for in-game open-folder support without network paths.
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
