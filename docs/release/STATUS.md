# Current Status

Snapshot date: 2026-08-01

## Executive assessment

Vibe Snake is a substantial, playable alpha with a distinctive audiovisual identity and a reliable local engineering baseline. All nine power-ups work end to end. Save ownership, schema migration, corruption protection, runtime configuration validation, and player preference persistence are implemented. A seeded reference-core QA laboratory checks generated command sequences, per-step invariants, and immediate trace replay. The 1.0 target is Godot 4 .NET with deterministic rules in pure C# and first-class Windows, macOS, and Linux artifacts. The native foundation now builds, runs, exports, launches, and passes artifact inspection locally on Windows. The game is not release-ready because differential parity, complete vertical-slice behavior, macOS and Linux export evidence, asset isolation, hosted CI evidence, and structured player validation remain.

## Verified quality baseline

| Area | Verified state |
| --- | --- |
| Version | 0.2.0 alpha |
| Supported development runtimes | Python 3.11, 3.12, 3.13, and 3.14 |
| Runtime dependency | Pygame Community Edition 2.5.7 or newer within major version 2 |
| Python deterministic tests | 466 passing and 3 environment-dependent radio skips locally on Python 3.11, 3.12, 3.13, and 3.14 |
| Python line coverage | 87.16 percent measured on Python 3.14, with an 80 percent floor enforced by configuration and CI on every supported interpreter |
| Native toolchain | Godot 4.7.1 Mono and .NET SDK 10.0.302 pinned and verified locally |
| Native contract tests | 177 passing on .NET 10. Rules coverage is 91.73 percent line and 87.77 percent branch; persistence coverage is 90.73 percent line and 84.48 percent branch; aggregate coverage is 91.55 percent line, 87.26 percent branch, and 97.53 percent method. The line floor is 80 percent per module. |
| Cross-language parity | 100 movement cases with 25,600 compared steps, 35 targeted core-rule cases, and 8 targeted Shield cases pass. The Shield corpus covers collection on entry, pickup and active expiry, collision consumption and prevention, expiry precedence, starvation bypass, the simultaneous collision and starvation boundary, normalized state, and ordered power events. |
| Godot integration | Headless import plus seeded rules, strict restoration, logical input, focus-loss pause, audio buses, all finite fallback cues, typed Shield feedback, live terminal replay recording, isolated atomic save, exact reload, read-only import, bounded future-schema feedback, background latest-replay input, lossless save queuing, run-start gating, save-aware quit, and clean shutdown pass locally on Windows; the gate rejects engine warnings, leaked objects, missing replay output, and incomplete temporary files |
| Windows native artifact | Debug player launches outside the checkout, emits state hash `643077d90db75e8c`, writes and verifies one replay under an isolated user-data root, and passes a 198-file, 189,615,786-byte SHA-256 inventory with no Python runtime, `.env` variant, checkout path, NuGet lock file, engine warning, or leaked object; the schema 2 manifest binds the editor executable to its pinned archive; two independently inspected payloads produced manifest SHA-256 `bae7d6369d61c6a57f2fe295f0308c238acc6ccd1e057c20abffc880e8c2ae74` |
| Static policy | Ruff passes across `src`, `tests`, and `scripts`; the executable source-policy gate covers every active source, workflow, and canonical-document file |
| Dependency integrity | A universal Python 3.11 through 3.14 lock contains 51 exact hash-verified packages and rejects stale requirement or package-metadata inputs; locked NuGet restore audits all direct and transitive packages with warnings as errors |
| CI definition | Python 3.11 through 3.14 plus .NET rules, Godot headless, checksum-verified export, outside-checkout player smoke, artifact inspection, and artifact upload matrices for Windows, macOS, and Linux |
| Documentation links | Local checker included in `scripts/check_docs.py` |

The local commands equivalent to CI pass. This workspace is not currently a Git repository, so no hosted GitHub Actions run can be verified from here. Moving the source reference to Pygame Community Edition removes the legacy Pygame `pkg_resources` warnings and provides native wheels for the full Python 3.11 through 3.14 matrix.

## Feature status

| System | Status | Evidence and qualification |
| --- | --- | --- |
| Core movement | Working | Four-direction movement, queued input, self-collision, phase overlap, and edge wrapping are implemented and tested. |
| Scoring | Working | Base points, speed bonus, length bonus, bonus points, and smoothly interpolated 1x to 10x combos are implemented. |
| Starvation | Working | A 30-second timer, clamped warning state, exact-deadline food rescue, move-then-starve order, Last Stand recovery, death telemetry, and player-run finalization are wired and tested. |
| Menus and overlays | Working | Twelve game states render headlessly and menu navigation is tested. The former nonfunctional difficulty row has been removed, so every advertised settings control works. |
| Input | Working with native qualification debt | The Python alpha covers keyboard, WASD, mouse, and gamepad paths. The native shell centralizes logical keyboard and any-controller movement, confirm, back, pause, replay verification, and quit actions, accepts one dropped replay only outside an active run, and safely pauses on focus loss. Physical-device, hot-plug, glyph, and remapping evidence remain. |
| Achievements | Working | Twenty-five current achievement conditions are evaluated, displayed, saved with the profile, and restored. |
| Cosmetics | Working | Five cosmetic axes yield 10,800 combinations. Current appearance and five loadouts use versioned, validated, atomic persistence. |
| Leaderboard | Working | One top-ten repository owns persistence. The HUD reads it, and the former single-score file is imported exactly once. |
| Save durability | Working with UX debt | Four Python schema-versioned repositories use OS user-data storage, atomic replacement, schema migration, corrupt-file backups, and future-version write protection. The native replay store adds strict UTF-8 import, exact compatibility results, cross-process transaction locking, same-directory no-overwrite atomic writes, idempotent payload matching, deterministic verification-work bounds, and 256-file and 256-MiB fail-closed limits. Reset confirmation, backup recovery, replay browsing, and replay deletion are not exposed in the UI. |
| Player preferences | Working | Sound state, volume, and fullscreen selection persist across launches. |
| AI spectator mode | Working | Ten built-in personalities plus JSON-loaded custom personalities can control runs. AI runs do not advance human progression. |
| Radio | Local review system with release debt | Eight station identities and prefix-based playlists work. The ignored local archive preserves 95 structurally screened MP3 candidates, which can be auditioned only through an explicit `VIBESNAKE_AUDIO_DIR` overlay. No radio binary is in the would-be public commit while provider-aware provenance, decode, loudness, clipping, listening, lyrics, station, and pack review remain open. |
| Sound effects | Partial | The Python alpha defines eat, loss, magnet, and fallback music paths and synthesizes deterministic in-memory cues when approved eat, loss, or magnet files are absent. The native shell registers Music, SFX, and UI buses and synthesizes confirm, back, pause, food, Shield spawn, Shield activation, Shield expiry, Shield break, death, and victory fallbacks. A typed resolver gives Shield recovery feedback one cue and persistent text caption. Independent settings, device-loss recovery, the complete typed event catalog, mix review, and final approved SFX remain. |
| Power-ups | Python working, native Shield complete | All nine change real Python runs through the main loop. Shield is the first complete pure C# contract, including deterministic spawn, collection on entry, pickup and active expiry, collision consumption, starvation bypass, anti-stacking, saturated-board discard, restart, restore, replay, state hash, ordered events, grid-safe pickup marker, active countdown, active outline, captions, and fallback cues. The other eight powers remain unported. |
| Adaptive difficulty | Not active | The unwired Python controller and its unvalidated aggregate were removed. A future policy requires deterministic integration, disclosure, opt-out rules, separate score categories, automated stability evidence, and structured observation. |
| Configuration | Working | Schema version 1 validates types and ranges, applies named or custom resolutions, controls collectible visibility, and supplies safe defaults. Changes still require restart. |
| Packaging | Blocked | Runtime assets live outside the Python package and use source-tree-relative paths. A wheel can install code without producing a self-contained playable game. |
| Automated gameplay QA | Foundation working | The Python reference runner has three seeded policies, per-step invariants, property-generated input sequences, replayed trace hashes, JSON reports, and a CI-friendly exit status. Native parity failures retain a schema 1 first-divergence bundle with the shortest executed failing prefix and exact reproduction command. Native tests now cover live replay mirroring, divergence, canonical bounds, verification-work limits, future compatibility, integrity, strict encoding, bounded diagnostics, traversal, conflicts, concurrent idempotence and capacity, lock contention, I/O failure, and source preservation. Full powers, DDA, AI, profile persistence, presentation, and artifact coverage still depend on the completed deterministic engine. |
| Content inventory | Foundation working | A deterministic policy and generated inventory classify and hash all 18 clean-clone assets totaling 95,377 bytes, run bounded JSON and decoded PNG checks, report one duplicate AI personality copy in one group, exclude 7 non-runtime files, and block 11 rights-cleared runtime candidates pending pack approval. A further 423 files and 740,801,845 bytes of unresolved or rejected tracks, generated candidates, analysis, copied research, working lyrics, superseded documents, retired production tooling, historical production records, private-path reports, and a working stem are preserved in the ignored `archive/source-assets` collection. No source asset is release-approved yet. |
| Content pack contract | Foundation working | Schema 1 strictly validates one dependency-free `vibesnake.core` and station-specific optional radio manifests against the exact approved inventory allowlist, rights-derived credits, hashes, version ranges, and ruleset identity. Optional-pack resolution isolates missing, invalid, incompatible, duplicate, or tampered stations from a valid core. No real manifest can pass while export eligibility remains zero. |
| Target technology | Qualification in progress | Godot 4.7.1 and .NET 10.0.302 are pinned. The pure C# kernel proves movement, input buffering, food, growth, smooth combo scoring, speed and length bonuses, starvation, collision, grid completion, the complete Shield contract, PCG32 randomness, typed ordered events, explicit restart, snapshots, strict canonical state schema 2 restoration, generated state-machine continuation, explicit `vibesnake-core@4` identity, a canonical replay envelope, live mirror-verified recording, and `fnv1a64-canonical-json-v3` hashes. The platform-neutral persistence assembly owns replay files without contaminating rules with clocks or file APIs. Shared fixtures compare the implemented rules scope against Python. The Godot shell proves logical keyboard and controller defaults, focus pause, basic audio buses and fallback cues, typed Shield presentation, terminal replay recording and verification, menu, run, death, restart, back, and quit paths. It is not yet feature-equivalent to the Python reference. |

## Inventory facts

- Radio library: 95 structurally screened MP3 candidates assigned exactly once across eight stations in the ignored local review archive; none are part of public source.
- Public audio footprint: one rights-cleared cue-metadata JSON file and no audio binaries under `assets/audio`; gameplay cues have procedural fallbacks.
- Achievements: 25 definitions across common, rare, epic, and legendary tiers.
- AI: 10 built-in personalities and one loadable custom personality in the checkout.
- Cosmetics: 12 colors, 6 patterns, 5 eye styles, 6 accessories, and 5 trails.
- Game flow: 12 enumerated states managed by an explicit transition map.

An ignored local inventory preserves 186 historical production records. It is
candidate-review evidence, not the live playlist or release source of truth.

## Improvements completed in this audit

- Replaced the stale, oversized README with a concise entry point and canonical documentation map.
- Added deterministic pytest collection rules so archived and paid-API scripts do not break CI.
- Repaired stale tests to match current movement and scoring contracts.
- Added an enforced 80 percent line-coverage floor and broad headless rendering coverage.
- Added full-tree Ruff, executable anti-slop policy, hash-locked dependency, source-content, documentation, shared-fixture, and Python 3.11 through 3.14 CI gates.
- Fixed radio discovery so all remaining 95 candidates are assigned once without cross-station duplication.
- Corrected all 25 achievement conditions and added persistent achievement state.
- Prevented AI runs from changing human progression and made starvation deaths finalize human runs.
- Completed all nine power-up contracts and added main-loop integration tests.
- Unified leaderboard ownership and added one-time legacy high-score import.
- Added schema version 1 migrations, validation, atomic writes, corruption backups, and future-schema guards to player data.
- Moved normal saves to platform user-data directories with a non-destructive one-time checkout migration.
- Added validated configuration, functional resolution presets, configured collectible visibility, and persistent audio and fullscreen preferences.
- Added a seeded reference-core QA laboratory with food-seeking, survival, and abusive-input policies, per-step invariants, action traces, determinism hashes, JSON reports, and property-based tests.
- Defined a research-informed fun thesis and detailed refinement strategy for escalation, powers, progression, customization, radio, AI spectators, lore, offline comparison, and human playtesting.
- Selected Godot 4 .NET with a pure C# rules assembly as the gated target architecture and made Windows, macOS, and Linux mandatory 1.0 platforms.
- Pinned Godot 4.7.1 Mono and .NET SDK 10.0.302 with a cross-platform engine archive manifest and checksum-verifying bootstrap.
- Added pure `VibeSnake.Rules` and `VibeSnake.Persistence` assemblies, 177 xUnit contracts with both modules above the 80 percent line floor, and exact aggregate coverage reporting.
- Added 100 Python-generated movement fixtures with 25,600 matching native steps, 35 targeted core-rule cases, and 8 targeted Shield cases covering collection, timers, collision recovery, starvation bypass, the simultaneous collision and starvation boundary, normalized state, and ordered events.
- Added a Godot C# qualification scene whose headless seeded replay passes through the real rules assembly.
- Added exact .NET export-template pins, native export presets, a selective checksum-verifying template installer, and outside-checkout player qualification.
- Built and launched the first self-contained Windows x64 Godot player without Python, then recorded a complete SHA-256 artifact manifest.
- Added an artifact gate that rejects Python payloads, development and secret files, invalid paths, machine-specific checkout paths, missing platform payloads, and malformed macOS archives.
- Expanded CI definitions to run native rules, Godot headless smoke, native player export and smoke, artifact inspection, and artifact upload on Windows, macOS, and Linux.
- Qualified 466 passing Python tests on Python 3.11 through 3.14 and measured 87.16 percent line coverage on Python 3.14.
- Corrected one-tick-late food collection and added matching Python and C# entry semantics.
- Accepted and locked exact starvation ordering, collision precedence, and immediate full-grid victory across production Python, the QA reference, shared fixtures, and native rules.
- Added immutable ordered native event detail, explicit terminal restart, canonical JSON state schema 2, rules version 4, and the `fnv1a64-canonical-json-v3` hash contract.
- Added strict canonical-state restoration and generated native state-machine campaigns that prove continuation across active, terminal, and restarted runs.
- Added centralized logical Godot actions, keyboard and controller defaults, focus-loss pause safety, deliberate resume, back and quit flows, and headless lifecycle proof.
- Added native Music, SFX, and UI buses plus finite cached 16-bit stereo PCM fallbacks for confirm, back, pause, food, Shield spawn, Shield activation, Shield expiry, Shield break, death, and victory.
- Removed the fallback-audio shutdown leak found by the headless smoke gate and made native smoke fail on engine warnings, leaked objects, or a missing success marker.
- Hardened packaged-player qualification to own the launched process through exit and to reject export-only lock metadata inside Godot resource packs.
- Made radio unmute and track advance skip unreadable MP3s and try the rest of the station before falling silent.
- Added a strict content policy, deterministic 18-file clean-clone SHA-256 inventory, bounded JSON and decoded PNG scanline checks, duplicate reporting, and release blockers for unapproved runtime content. Unverified binaries and historical production records are isolated from public source.
- Added a strict schema 1 core and optional-radio pack validator, canonical build-time qualification command, exact inventory allowlists, rights-derived credits, compatibility and dependency checks, and optional failure isolation.
- Added retained schema 1 first-divergence evidence with fixture and case identity, seed, shortest executed failing prefix, normalized state and events, native canonical state and hash, platform metadata, and exact filtered reproduction.
- Added explicit `vibesnake-core@4` ruleset identity and a canonical replay schema containing the initial state, step-indexed logical commands, checkpoints, outcome, compatibility diagnostics, and SHA-256 payload integrity.
- Made all three Python-generated parity corpora declare `vibesnake-core@4` and an explicit injected-or-normalized randomness policy, with matching C# assertions.
- Ported Shield as the first complete native power contract and added collection-on-entry, spawn, duration, expiry, consumption, collision prevention, starvation bypass, restart, restore, replay, anti-stacking, and saturated-board contracts.
- Added a grid-safe Shield pickup marker, visible pickup and active countdowns, active head outline, persistent text feedback, and typed fallback cue selection to the Godot slice.
- Added live replay recording with rejected-attempt retention, per-step deterministic mirror comparison, exact final-state comparison, bounded canonical envelopes, and fail-closed diagnostics.
- Added bounded, idempotent, no-overwrite atomic replay storage with strict UTF-8 import, exact compatibility and verification results, source preservation, replay-count and byte limits, latest-replay input, dropped-file inspection, and isolated editor and packaged-player smoke.

## Release blockers

1. Add delta reduction beyond the retained first failing prefix, then extend the proven Shield pattern through the other eight powers.
2. Complete the native vertical slice with physical-controller and hot-plug proof, remapping, accessible presentation, authored audio and device-failure proof, scaling, and reviewed feel parity.
3. Run and retain the defined macOS Universal and Linux x64 exports on native hosted runners, then expand all three artifact smokes through user data, controller, audio failure, scaling, and lifecycle paths.
4. Select the minimal real core and radio manifests under the implemented schema, qualify the 11 blocked clean-clone runtime candidates, and admit local audio candidates only after file-level rights, content, and quality evidence passes.
5. Replace repository-relative asset paths with the target content service and prove allowlisted artifact contents.
6. Run the Python, C#, Godot, and native artifact matrices in hosted CI from a real Git remote.
7. Complete the deterministic rules port and expand QA to powers, DDA, AI, persistence, replays, presentation, and reliability campaigns.
8. Add in-game save reset confirmation and clear corrupt-backup recovery.
9. Conduct iterative structured playtests for controls, difficulty, power choices, escalation, readability, audio fatigue, accessibility, restart, and replay desire.
10. Complete the event-to-SFX, Vibe Level, radio broadcast, and accessibility pass needed for release-quality feedback.

The recommended implementation order is in the [roadmap](../../ROADMAP.md).

The current roadmap milestone is 0.3.0, the technology qualification and native vertical slice. Later feature milestones remain gated behind trace parity and clean native artifacts so new behavior is always built and tested in the architecture players will receive.
