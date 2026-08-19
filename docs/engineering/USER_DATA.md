# User-data directory contracts

Status: V080-11 native progression, replay, offline comparison, and recovery layout (2026-08-11).

This document is the authoritative layout for player-writable paths on Windows, macOS, and Linux. Domain rules never invent paths. Python uses `vibesnake.data.paths`. The Godot shell supplies an absolute user-data root to `VibeSnake.Persistence` (normally Godot `user://` resolved to a filesystem path, or an explicit smoke override).

## Roots

| Runtime | Override | Windows default | macOS default | Linux default |
| --- | --- | --- | --- | --- |
| Python alpha | `VIBESNAKE_DATA_DIR` | `%LOCALAPPDATA%\VibeSnake` | `~/Library/Application Support/VibeSnake` | `$XDG_DATA_HOME/vibesnake` or `~/.local/share/vibesnake` |
| Godot native shell | Godot project user dir / `--smoke-user-data-root=` | Godot `user://` under the platform Godot user-data root for application name `Vibe Snake` | same | same |

Notes:

- Python uses the exact folder names `VibeSnake` on Windows and macOS and lowercase `vibesnake` on Linux XDG paths, matching `paths.py`.
- Native smokes must pass an isolated absolute `--smoke-user-data-root=` and never write into the developer's real profile.
- `ReplayStore` rejects non-absolute user-data roots.

## Python repository layout (under the Python root)

| Relative path | Owner | Purpose |
| --- | --- | --- |
| `player_profile.json` | Profile repository | Achievements and session counters |
| `customization.json` | Cosmetics repository | Loadout and unlock state |
| `high_scores.json` | Leaderboard repository | Top-ten scores |
| `preferences.json` | Preferences repository | Sound, volume, fullscreen |
| `highscore.json` | Legacy import only | Read-once migration source; not an active writer target |
| `.legacy-data-migrated-v1.json` | Path migrator | Marker preventing resurrected checkout copies |
| `*.corrupt.bak` / non-overwriting corrupt backups | `json_store` | Unreadable documents preserved for recovery |

## Native layout (under the Godot-supplied absolute root)

| Relative path | Owner | Purpose |
| --- | --- | --- |
| `replays/` | `ReplayStore` | Bounded atomic replay envelopes |
| `replays/.vibesnake-replay-store.lock` | `ReplayStore` | Cross-process store lock |
| `replays/*.vibesnake-replay.json` | `ReplayStore` | Individual verified replay files |
| `replay-exports/replay_*.vibesnake-replay.json` | `ReplayStore` | Player-requested canonical exports of verified replays; 256 files and 256 MiB maximum |
| `offline-challenges/ghosts/household-rival-<1-4>.vibesnake-ghost.json` | `OfflineChallengeStore` | Four fixed source-preserving copies of verified household rival replays |
| `offline-challenges/run-cards/run-card_<replay-hash>.vibesnake-run-card.json` | `OfflineChallengeStore` | Closed privacy-safe 26-field cards; 64 files and 4 MiB maximum |
| `offline-challenges/.vibesnake-offline-challenge.lock` | `OfflineChallengeStore` | Exclusive import, card-export, and deletion lock |
| `imports/household-rival.vibesnake-replay.json` | Player-supplied read-only import source | Explicit native household rival inbox; import never modifies or removes it |
| `preferences.json` | `PreferencesStore` | Schema 7 gameplay, local playtest consent, multi-bus audio, mono output, display, accessibility, and controller settings |
| `achievements.json` | `AchievementsStore` | Schema 1 permanent unlock IDs for rules-local achievement candidates |
| `onboarding.json` | `OnboardingStore` | Schema 1 tutorial decision and revision |
| `progression.json` | `ProgressionStore` | Schema 1 exact human-run goals, one highlighted goal, selected cosmetic set, five saved set loadouts, earned expression rewards, and dependency-closed Broadcast Tour completion |
| `personal_bests.json` | `PersonalBestStore` | Schema 2 bounded personal bests separated by rules, mode, run kind, seed category, difficulty, DDA, and config identity; schema 1 migrates visibly to `Legacy 0.2` |
| `score_history.json` | `ScoreHistoryStore` | Schema 1 bounded top ten per exact score category with one-time Python top-ten import state |
| `imports/high_scores.json` | Player-supplied read-only import source | Optional frozen Python schema-1 top ten; native import never modifies or removes it |
| `diagnostics/` | `LocalDiagnostics` | Offline crash reports with path sanitization; `EnsureDiagnosticsDirectory()` creates the folder for open-folder UI |
| `logs/vibesnake.jsonl` | `StructuredLocalLog` | Append-only structured JSONL support log with level filter, path sanitization, 1 MiB rotation, and rotated-file retention |
| `input/*.input_bindings.json` | `InputBindingsStore` | Logical action bindings by device class |
| `packs/<pack-id>/pack.json` | `OptionalPackStore` | Canonical installed optional-pack manifest |
| `packs/<pack-id>/<payload>` | `OptionalPackStore` | Exact manifest-allowlisted optional payloads |
| `packs/.removed/<receipt-name>/` | `OptionalPackStore` | Recoverable same-volume quarantine after explicit removal confirmation |
| `packs/.optional-pack-store.lock` | `OptionalPackStore` | Exclusive pack lifecycle operation lock |
| `backups/<backup-id>/backup.json` | `PlayerDataRecoveryService` | Strict schema 1 reset-backup manifest with categories, relative paths, byte lengths, and SHA-256 hashes |
| `backups/<backup-id>/payload/` | `PlayerDataRecoveryService` | Verified copy of only the confirmed allowlisted reset targets |
| `backups/.building-<backup-id>/` | `PlayerDataRecoveryService` | Detectable interrupted backup staging; never offered for restore |
| `.player-data-recovery.lock` | `PlayerDataRecoveryService` | Exclusive reset, inspection, and restore operation lock |
| `playtest-summaries/summaries.json` | `LocalPlaytestSummaryStore` | Schema 2 explicit-opt-in, balance-only terminal run facts with exact nine-row per-power lifecycle aggregates; newest 200 and 512 KiB maximum; identity-verified schema-1 migration |
| `playtest-summaries/summaries.json.tmp` | `LocalPlaytestSummaryStore` | Atomic-write staging owned by the summary store and removed by confirmed summary deletion |
| `playtest-summaries/exports/playtest-summaries_*.json` | `LocalPlaytestSummaryStore` | Player-requested local exports; newest 20 maximum |
| `playtest-summaries/exports/playtest-summaries_*.json.tmp` | `LocalPlaytestSummaryStore` | Interrupted export staging removed by confirmed summary deletion |
| `agent_arena/exhibition_archive.json` | `AgentExhibitionArchiveStore` | Preview-only bounded archive of verified exhibition receipts plus saved lane replay file names. Outside Persistence. |
| `agent_arena/agent_passports.json` | `AgentPassportStore` | Preview-only bounded public agent records assembled from verified receipts. Outside Persistence. Never stores a display name or human profile. |
| `agent_arena/*.corrupt.json` | Preview arena stores | Quarantined unreadable documents. Not repaired in place. |

Future native-owned rows (not yet writers in shipping code):

| Relative path | Intended owner | Purpose |
| --- | --- | --- |
| `profiles/` | Future profile store | Native profiles after dual-runtime freeze |
| `screenshots/` | Future capture service | Manual and smoke captures |
| `tmp/` | Future services | Short-lived work; safe to delete on start |

## Separation rules

1. **Install tree is read-only.** Player data never lives next to the executable in a production install.
2. **Optional packs are removable without wiping saves.** Pack removal consent is separate from profile reset and from application uninstall.
3. **Replays are independent of save schema.** Unsupported replays remain on disk.
4. **Logs must not embed secrets.** Paths shown to players are sanitized; smoke diagnostics stay bounded.
5. **Spaces and non-ASCII** path segments are allowed in the user-data root; writers must not assume ASCII-only segments.
6. **Fresh profile** means an empty or missing root: the game creates required directories on first write and never requires a pre-seeded tree.
7. **Reset categories are fixed.** Preferences owns `preferences.json` and `input/`; progression owns `achievements.json`, `onboarding.json`, and `progression.json`; local scores owns `personal_bests.json` and `score_history.json`; replays owns `replays/`, `replay-exports/`, and `offline-challenges/`; optional content owns `packs/`. Player-supplied files below `imports/` are never reset targets. A plan cannot add another target.
8. **Backups remain player data.** They are never packaged into the install, never uploaded automatically, and never restored over current data.
9. **Playtest summaries are explicit and separate.** Collection defaults off, contains only the closed fields in [the summary contract](../design/PLAYTEST_SUMMARIES.md), has no upload path, and is deleted through its own confirmed Data action rather than a reset category.

## Recovery

| Situation | Expected behavior |
| --- | --- |
| Corrupt JSON save | Non-overwriting backup; defaults loaded; original not deleted |
| Future schema save | No downgrade write; document left intact |
| Corrupt or incompatible replay | Source file retained; actionable load code returned |
| Replay export request for an unverified entry | Reject the export and preserve both stored replay and existing exports |
| Replay deletion cancelled | The read-only plan is discarded and no file changes |
| Replay deletion plan becomes stale | Recheck timestamp, size, and content hash under the store lock; reject without deletion |
| Confirmed current replay deletion | Permanently remove exactly one selected stored replay and preserve existing exports |
| Modified, incompatible, oversized, missing, or changed household rival source | Reject import, preserve the source bytes, and write no slot |
| Household rival slot already occupied | Reject no-overwrite import and preserve both the existing slot and source |
| Ghost deletion cancelled or consent becomes stale | Preserve the copied slot, original source, and run cards |
| Confirmed current ghost deletion | Permanently remove exactly one copied household slot and preserve its original source and run cards |
| Duplicate run-card export | Return idempotent success only when existing bytes match; never overwrite different data |
| Dropped approved `.vibesnake-pack.zip` | Preserve the download, validate the bounded exact archive, extract through `user://packs/.staging/`, atomically move one new pack into place, and activate it only after complete revalidation |
| Invalid, traversing, duplicate, oversized, tampered, or already-installed pack archive | Reject without overwrite or partial installation; core play and the source download remain unchanged |
| Missing optional pack | Core play continues with fallback audio and inventory messaging |
| Removed optional pack | Validated pack moves to recoverable quarantine; restart-safe discovery and restore revalidate manifest and payload before activation |
| Tampered installed or quarantined pack | Pack remains isolated and is not activated or moved into the active set |
| Disk full / lock timeout | Fail closed; no partial replace of the previous good file |
| Progression store unavailable or save rejected | Keep the valid in-memory change for the current session, show session-only status, and never emit a saved event |
| Background replay save faults while quit is pending | Cancel quit, keep the session open, show the failed-save status, and retain a sanitized local diagnostic when storage permits |
| Background player-data reset or restore faults while quit is pending | Cancel quit, keep the failure visible, and require a new deliberate quit request |
| Confirmed category reset | Copy bounded allowlisted files, verify source/copy SHA-256 and strict manifest, recheck source stability, then remove only the listed targets |
| Cancelled reset | Planning is read-only; no directory, manifest, or player-data write occurs |
| Corrupt or incomplete reset backup | Preserve it, show `user://backups/<id>`, block restore, and offer keep/open-location choices |
| Restore conflicts with current data | Refuse without overwrite; the player may keep current data or separately reset the same categories first |
| Valid non-conflicting reset backup | Reverify every file, restore without mutating the backup, and reload native runtime repositories |
| Invalid, future, oversized, or identity-tampered playtest summary data | Preserve the source, reject capture and export, and do not overwrite it |
| Valid schema-1 playtest summary data | Verify each original summary identity, add exact zeroed power rows, recompute schema-2 identities, and persist only on the next authorized write |
| Confirmed playtest-summary deletion | Permanently remove the source, application-owned exports, and interrupted-write temporary files without creating a backup |
| Invalid, future, or oversized Python score import | Preserve the source, block import, and leave native history unchanged |
| Valid confirmed Python score import | Preserve the source, record its SHA-256 exactly once, and show rows only under noncompetitive `Legacy 0.2` |

## Verification

- Python path resolution and migration: `tests/core/test_paths.py`, `tests/core/test_persistence.py`.
- Native replay roots, spaces, non-ASCII, budgets, locks, complete browser metadata/status, verified export, exact deletion consent, stale-plan rejection, and export preservation: `ReplayStoreTests`.
- Native stable seed codes, exact run reconstruction, equal-rules ghost isolation, four-slot import bounds, source preservation, modified/incompatible rejection, atomic idempotent run cards, and stale-safe ghost deletion: `OfflineChallengeTests`.
- Native optional-pack canonical manifests, bounded archive installation, traversal, duplicate, case-collision, symbolic-link, invalid-UTF-8, compressed/installed size, store-capacity, rollback, exact allowlists, file hashes, entry limits, bounded asset reads, stale consent, restart-safe quarantine discovery, tamper isolation, restore, and player-data preservation: `OptionalPackStoreTests`.
- Native separated reset/recovery planning, strict manifests, exact paths, budgets, lock contention, backup hashing, corruption, interrupted staging, no-overwrite restore, and all five categories: `PlayerDataRecoveryServiceTests`.
- Native local playtest consent, exact schema-2 fields and nine-row power aggregates, identity-verified schema-1 migration, strict identities, count and byte limits, export retention, corruption preservation, and permanent deletion: `PreferencesDocumentTests` and `LocalPlaytestSummaryStoreTests`.
- Native score-history ranking, identity, strict parsing, category and byte limits, existing-best migration, Python source preservation, exact-once import, and reset ownership: `ScoreHistoryDocumentTests` and `PlayerDataRecoveryServiceTests`.
- Native goal, cosmetic, reward, and Tour identity, strict parsing, bounds, dependency closure, unearned-reward rejection, atomic writes, and reset ownership: `ProgressionCatalogTests`, `BroadcastTourSessionTests`, `ProgressionDocumentTests`, and `PlayerDataRecoveryServiceTests`.
- Godot `local-playtest-summary-qualification-v1` evidence covers raw keyboard consent, preference round-trip, terminal capture, local export, raw controller deletion, lossless cancellation, permanent confirmation, the exact 26-field summary and nine-row nested allowlists, and upload absence. `power-decision-qualification-v1` separately proves all eight aggregate lifecycle stages and the bounded death-adjacency window.
- Godot `player-data-recovery-qualification-v1` evidence covers exact confirmation, cancel-without-write, backup-before-removal, integrity, separate category reset, corruption/conflict rejection, restore, visible locations, keyboard/controller routes, and faulted-operation quit cancellation.
- Godot `score-browser-qualification-v1` evidence covers raw keyboard/controller entry, navigation, confirmation, cancellation, top-ten bounds, exact score fields, source-preserving import, visible legacy classification, and recovery ownership.
- Godot `replay-browser-qualification-v2` evidence covers raw keyboard/controller replay routes, complete metadata/status shape, closed speed set, HUD toggle, pause/step/restart/return, verified export, lossless delete cancel, exact confirmed delete, export preservation, and progression isolation.
- Godot `offline-comparison-qualification-v1` evidence covers raw keyboard/controller comparison routes, stable tamper-evident seed codes, four fixed slots, source-preserving import, modified/incompatible rejection, a live equal-rules ghost, ghost isolation, private atomic run cards, exact deletion, progression isolation, and core-offline operation.
- Godot `progression-qualification-v1` evidence covers raw keyboard/controller goal highlighting, Tour and cosmetic flows, store round-trip, explicit session-only behavior when the store is unavailable, fixed-seed practice isolation, reduced-motion notification bounds, canonical rival/station references, and zero human-distribution claims.
- Godot smoke requires an explicit isolated user-data root.
- Export smoke stages install, fresh user-data, and log paths containing spaces and non-ASCII characters, makes the install tree read-only, requires an adjacent write probe to fail, launches with isolated user data and logs outside that tree, and writes `artifact-read-only-install-v1` only after the complete installed-file digest remains unchanged.

## Remaining release work

Pure C# `ReleaseArtifactManifest` (schema 3) validates export inspection documents, including the Release-only Agent Arena exclusion state, and declares qualified input shapes (`portable-folder` for Windows/Linux, `app-bundle-zip` for macOS). `ReleaseOutputPlan` defines versioned Windows ZIP, macOS app-bundle ZIP, and Linux tar.gz direct downloads plus portable/app-bundle store depots. Qualification packaging verifies the exact input allowlist, reproduces package bytes, and emits separate checksums while remaining non-publishable. The read-only install and external-write contract is an automated platform gate. Optional-pack installation is a bounded, no-overwrite, same-volume staged move from an approved archive; removal consent is targeted and structurally unable to remove saves, profiles, or replays. `OptionalPackStore` validates installed payloads, moves confirmed packs into recoverable quarantine without recursive deletion, and revalidates restore. `PlayerDataRecoveryService` and the Data settings screen own player-facing separated reset and fail-closed recovery. Player-facing per-pack removal/recovery management, quarantine cleanup policy, selected-store integration, protected signing, and human recovery-language review remain open. This document freezes the directory contract those features must respect.
