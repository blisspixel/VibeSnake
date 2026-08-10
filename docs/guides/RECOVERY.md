# Save and Recovery Guide

[Player guide](PLAYER_GUIDE.md) | [User-data contract](../engineering/USER_DATA.md) | [Privacy](../../PRIVACY.md)

Status: native recovery flows are automated; final candidate wording and physical review are pending.

## Start safely

Do not delete or overwrite a save to repair a problem. Quit the game, preserve the complete user-data directory, and work from a copy when manual inspection is necessary. A future-schema document, corrupt replay, invalid optional pack, or incomplete backup is intentionally left in place so a newer build or support review can recover it.

The native game stores data below Godot's platform user-data root for application name `Vibe Snake`. The Data settings screen and F12 diagnostics view expose the resolved location without requiring a player to guess it. The temporary Python alpha uses `%LOCALAPPDATA%\VibeSnake` on Windows, `~/Library/Application Support/VibeSnake` on macOS, and `$XDG_DATA_HOME/vibesnake` or `~/.local/share/vibesnake` on Linux.

## Reset one category

The native Data settings screen separates these categories:

- preferences and input bindings;
- progression, achievements, and onboarding;
- local scores and personal bests;
- replays, replay exports, and offline comparisons;
- installed optional content.

A reset is a two-step confirmed action. Before removal, the game copies only the selected allowlisted files into `user://backups/<backup-id>/`, records byte lengths and SHA-256 values, verifies the copy, and rechecks that the source did not change. Cancel is read-only. A failure leaves current data intact.

Local playtest summaries are deliberately separate. Their confirmed deletion permanently removes the source, application-owned exports, and interrupted-write temporary files without creating a recovery backup.

## Restore a backup

Open the Data settings screen, choose recovery, and inspect the backup status. A valid backup restores only when the current target paths are absent. The game never overwrites current data during restore and never mutates the backup. If current data conflicts, keep it or separately reset the same categories before attempting restore again.

Corrupt, incomplete, oversized, future, path-unsafe, or hash-mismatched backups remain on disk and are not offered as valid restores. Keep them for support review. Do not edit `backup.json` or move payload files within a backup.

## Common failures

| Situation | Safe result and next action |
| --- | --- |
| Corrupt JSON | The source is preserved or copied to a non-overwriting corrupt backup; defaults may load. Keep the file and inspect diagnostics. |
| Future schema | The game refuses a downgrade write. Keep the document for a newer compatible build. |
| Disk full or write denied | The committed file remains unchanged. Free space or repair permissions, then retry. |
| Replay is invalid or incompatible | The replay remains in place with an actionable status. Do not replace it with an unverified file. |
| Optional pack is missing | Core play continues with fallback content. Reinstall the exact validated pack if wanted. |
| Optional pack is removed | The pack is held in recoverable quarantine and revalidated before restoration. |
| Optional pack is tampered | It remains isolated. Obtain an exact trusted package rather than editing the manifest. |
| Restore conflicts with current data | No overwrite occurs. Preserve current data, then decide which category should be reset. |
| Application was removed | Player data remains outside the install tree. Reinstalling the same or a compatible newer build can reuse it. |

## Logs and diagnostics

Local logs are written to `user://logs/vibesnake.jsonl` with bounded rotation. Crash and divergence diagnostics stay below `user://diagnostics/`. Review every file before sharing it. Remove private paths, names, account information, device serials, and unrelated system details. The game does not upload these files.

## Complete local removal

Application uninstall preserves player data. To remove local data intentionally, first use the separate in-game deletion controls for categories and local summaries. Quit the game, verify that no wanted backup, replay export, run card, or player-supplied import remains, then remove the resolved Vibe Snake user-data directory manually. This cannot be undone.
