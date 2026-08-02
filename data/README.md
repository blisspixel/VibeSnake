# Legacy and Production Data

This directory is not the normal player-data location.

- Root JSON save files, when present locally, are ignored migration inputs for non-destructive import into the operating system's user-data directory.
- `generation_history/` and `generation_results.json`, when present locally, are ignored production history.
- Sanitized migration fixtures that belong in a clean clone live under `tests/`.

New tests use an isolated temporary directory through `VIBESNAKE_DATA_DIR`. New production reports belong in an explicit generated-output directory and should remain ignored unless they become a reviewed fixture or canonical inventory input.
