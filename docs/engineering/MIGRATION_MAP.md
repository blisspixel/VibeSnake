# Python-to-Native Migration Ownership Map

Status: V030-12 expanded procedures (2026-08-04).

This map assigns every Python reference subsystem to its target C# or Godot owner. During migration, prefer changing one owner. Do not add major new player-facing features to both runtimes.

## Ownership matrix

| Python owner | Target owner | Port state | Notes |
| --- | --- | --- | --- |
| `core/snake.py` movement, wrap, body | `VibeSnake.Rules` | Done | Shared movement and core-rule fixtures |
| `core/scoring.py` combo, bonuses | `VibeSnake.Rules` | Done | Shared core-rule fixtures |
| Starvation timer / deadline | `VibeSnake.Rules` | Done | Exact order with collision |
| Food spawn | `VibeSnake.Rules` | Done | PCG32 free-cell selection |
| Power manager spawn cadence | `VibeSnake.Rules` | Partial | Shield auto-spawn only; other kinds injected for tests |
| Shield | `VibeSnake.Rules` + Godot | Done | Parity `shield_rules_v1`; shell markers and cues |
| Phase Shift | `VibeSnake.Rules` + Godot | Done | Parity `phase_shift_rules_v1`; shell markers and body tint |
| Last Stand | `VibeSnake.Rules` + Godot | Done | Parity `last_stand_rules_v1`; recovery captions |
| Slow-Mo / Boost tempo | `VibeSnake.Rules` + Godot | Done | Parity in `remaining_powers_rules_v1`; `RulesCadenceClock` shell drain |
| Magnet | `VibeSnake.Rules` + Godot | Done | Parity remaining-powers; shell markers |
| Bait | `VibeSnake.Rules` + Godot | Done | Parity remaining-powers; bait mark draw |
| Gluttony | `VibeSnake.Rules` + Godot | Done | Parity remaining-powers; body tint |
| Segment Detach | `VibeSnake.Rules` + Godot | Done | Parity remaining-powers; hazard draw; collect-after-move |
| Input devices | Godot `GameActions` + Persistence bindings | Partial | Logical actions, schema-1 store, and InputMap apply for keyboard/controller; remapping UI and glyphs open |
| Menus / HUD / cosmetics | Godot presentation | Partial | Thin vertical slice only |
| Audio buses / SFX | Godot `AudioFallback` | Partial | Fourteen fallback cues; authored packs open |
| Radio playback | Godot content service | Not started | Python has full offline radio |
| Persistence (profile, scores) | Godot + future store | Partial | Replay store native; profiles still Python |
| Replays | `VibeSnake.Persistence` + Rules | Done for rules slice | Browser UI open |
| AI personalities | Future pure rules AI module | Not started | Keep out of rules until deterministic boundary complete |
| Content inventory / packs | Shared policy + native allowlists | Partial | Schema 1 validators exist; exportEligible=0 |
| Config | Rules config + Godot settings UI | Partial | Ruleset identity frozen |

## Port order (locked)

1. Shield, Phase Shift, Last Stand (collision recovery matrix): done
2. Slow-Mo and Boost (tempo modifiers): done (rules + shell cadence)
3. Magnet, Bait, Gluttony, Segment Detach: done (rules + shell + shared fixtures)
4. Presentation polish, radio, progression UI on Godot: **current**
5. Content service and pack allowlists before shipping native asset payloads: next dependency

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

- Keep Python playable via `vibesnake` until 0.3 artifact gates accept the native path.
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

## Feature freeze rule

No new scored mode, power type, or ruleset identity change lands in both runtimes in the same change. Prefer native-only after the rules port for that subsystem is complete.
