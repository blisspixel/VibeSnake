# Python-to-Native Migration Ownership Map

Status: V030-12 foundation (2026-08-04).

This map assigns every Python reference subsystem to its target C# or Godot owner. During migration, prefer changing one owner. Do not add major new player-facing features to both runtimes.

## Ownership matrix

| Python owner | Target owner | Port state | Notes |
| --- | --- | --- | --- |
| `core/snake.py` movement, wrap, body | `VibeSnake.Rules` | Done | Shared movement and core-rule fixtures |
| `core/scoring.py` combo, bonuses | `VibeSnake.Rules` | Done | Shared core-rule fixtures |
| Starvation timer / deadline | `VibeSnake.Rules` | Done | Exact order with collision |
| Food spawn | `VibeSnake.Rules` | Done | PCG32 free-cell selection |
| Power manager spawn cadence | `VibeSnake.Rules` | Partial | Shield auto-spawn only; other kinds injected for tests |
| Shield | `VibeSnake.Rules` | Done | Parity `shield_rules_v1` |
| Phase Shift | `VibeSnake.Rules` | Done | Parity `phase_shift_rules_v1` |
| Last Stand | `VibeSnake.Rules` | Done | Parity `last_stand_rules_v1` |
| Slow-Mo / Boost tempo | `VibeSnake.Rules` + Godot cadence | Rules done | Snapshot cadence numerator/denominator; shell must honor scale |
| Magnet | `VibeSnake.Rules` | Done | One-cell food pull each rules step |
| Bait / Gluttony | `VibeSnake.Rules` | Not started | Food spawn bias and no-growth scoring |
| Segment Detach | `VibeSnake.Rules` | Not started | Obstacle ownership |
| Input devices | Godot `GameActions` | Partial | Logical actions present; remapping open |
| Menus / HUD / cosmetics | Godot presentation | Partial | Thin vertical slice only |
| Audio buses / SFX | Godot `AudioFallback` | Partial | Fallback cues; authored packs open |
| Radio playback | Godot content service | Not started | Python has full offline radio |
| Persistence (profile, scores) | Godot + future store | Partial | Replay store native; profiles still Python |
| Replays | `VibeSnake.Persistence` + Rules | Done for rules slice | Browser UI open |
| AI personalities | Future pure rules AI module | Not started | Keep out of rules until deterministic boundary complete |
| Content inventory / packs | Shared policy + native allowlists | Partial | Schema 1 validators exist; exportEligible=0 |
| Config | Rules config + Godot settings UI | Partial | Ruleset identity frozen |

## Port order (locked)

1. Shield, Phase Shift, Last Stand (collision recovery matrix): done
2. Slow-Mo and Boost (tempo modifiers): rules done
3. Magnet (food pull): done
4. Bait, Gluttony (food geometry): **current**
5. Segment Detach (obstacles)
5. Presentation, radio, progression UI on Godot after rules parity for each system

## Rollback

- Keep Python playable via `vibesnake` until 0.3 artifact gates accept the native path.
- Shared fixtures are the contract: a native regression must not silently change fixture expectations without a `PARITY_DECISIONS.md` entry.
- Replay schema rejections leave files intact.
- Do not delete Python power modules until every power has native parity fixtures and Godot presentation coverage.

## Feature freeze rule

No new scored mode, power type, or ruleset identity change lands in both runtimes in the same change. Prefer native-only after the rules port for that subsystem is complete.
