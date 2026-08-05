# User-data directory contracts

Status: V030-10 published layout (2026-08-04).

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
| `preferences.json` | `PreferencesStore` | Schema 2 multi-bus audio and accessibility settings |
| `achievements.json` | `AchievementsStore` | Schema 1 permanent unlock IDs for rules-local achievement candidates |
| `diagnostics/` | `LocalDiagnostics` | Offline crash reports with path sanitization; `EnsureDiagnosticsDirectory()` creates the folder for open-folder UI |
| `logs/vibesnake.jsonl` | `StructuredLocalLog` | Append-only structured JSONL support log with level filter, path sanitization, 1 MiB rotation, and rotated-file retention |
| `input/*.input_bindings.json` | `InputBindingsStore` | Logical action bindings by device class |

Future native-owned rows (not yet writers in shipping code):

| Relative path | Intended owner | Purpose |
| --- | --- | --- |
| `profiles/` | Future profile store | Native profiles after dual-runtime freeze |
| `screenshots/` | Future capture service | Manual and smoke captures |
| `packs/` | Future content service | Installed optional packs (never write into the install tree) |
| `tmp/` | Future services | Short-lived work; safe to delete on start |

## Separation rules

1. **Install tree is read-only.** Player data never lives next to the executable in a production install.
2. **Optional packs are removable without wiping saves.** Pack removal consent is separate from profile reset and from application uninstall.
3. **Replays are independent of save schema.** Unsupported replays remain on disk.
4. **Logs must not embed secrets.** Paths shown to players are sanitized; smoke diagnostics stay bounded.
5. **Spaces and non-ASCII** path segments are allowed in the user-data root; writers must not assume ASCII-only segments.
6. **Fresh profile** means an empty or missing root: the game creates required directories on first write and never requires a pre-seeded tree.

## Recovery

| Situation | Expected behavior |
| --- | --- |
| Corrupt JSON save | Non-overwriting backup; defaults loaded; original not deleted |
| Future schema save | No downgrade write; document left intact |
| Corrupt or incompatible replay | Source file retained; actionable load code returned |
| Missing optional pack | Core play continues with fallback audio and inventory messaging |
| Disk full / lock timeout | Fail closed; no partial replace of the previous good file |

## Verification

- Python path resolution and migration: `tests/core/test_paths.py`, `tests/core/test_persistence.py`.
- Native replay roots, spaces, non-ASCII, budgets, and locks: `ReplayStoreTests`.
- Godot smoke requires an explicit isolated user-data root.

## Open V030-10 work

Pure C# `ReleaseArtifactManifest` (schema 2) validates export inspection documents and declares installer/archive shapes (`portable-folder` for Windows/Linux, `app-bundle-zip` for macOS). The `ValidateArtifactManifest` tool is invoked by `inspect_native_artifact.ps1` after writing the manifest so export smokes fail closed on schema or payload drift. Signing separation, store-channel packaging, in-game reset/recovery UI, and optional-pack removal UX remain open. This document freezes the directory contract those features must respect.
