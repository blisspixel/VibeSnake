# Configuration

Vibe Snake separates developer-controlled runtime balance from player-controlled preferences.

## Runtime configuration

The source checkout reads [assets/config/config.json](../../assets/config/config.json) once during module import. Values are recursively merged over the safe defaults in [config.py](../../src/vibesnake/data/config.py), validated, and exposed as constants through [settings.py](../../src/vibesnake/data/settings.py). Restart the process after changing this file.

The current configuration schema is version 1. Malformed JSON falls back to embedded defaults. Invalid individual values produce a console warning and use the corresponding default. A schema newer than the running game is not interpreted and causes the whole runtime configuration to fall back safely.

## Supported keys

| Key | Type and range | Checkout value | Effect |
| --- | --- | ---: | --- |
| `schema_version` | integer, at most 1 | 1 | Configuration compatibility |
| `resolution_preset` | preset name or `custom` | `hd` | Selects board dimensions |
| `resolutions` | object of positive grid dimensions | four presets | Defines named board sizes |
| `grid_width` | positive integer | 64 | Custom board width in cells |
| `grid_height` | positive integer | 33 | Custom board height in cells |
| `cell_size` | positive integer | 20 | Grid-to-pixel conversion |
| `fps` | positive integer | 60 | Render-loop target |
| `logic_tick` | number above 0 and at most 1 | 0.05 | Base movement interval in seconds |
| `colors.background` | three integers from 0 to 255 | `[34, 139, 34]` | Global background color |
| `colors.snake` | three integers from 0 to 255 | `[50, 255, 50]` | Default snake color |
| `colors.food` | three integers from 0 to 255 | `[213, 50, 80]` | Food color |
| `colors.text` | three integers from 0 to 255 | `[255, 255, 255]` | Main text color |
| `powerups.enabled` | boolean | `true` | Enables scheduled power-up spawning |
| `powerups.spawn_interval` | positive number | 8.0 | Target spawn cadence in seconds |
| `powerups.visible_duration` | positive number | 8.0 | Lifetime of an uncollected power-up |
| `sound.enabled` | boolean | `true` | Initial sound preference for a new player |
| `sound.volume` | number from 0 to 1 | 0.8 | Initial volume preference for a new player |

For a named resolution preset, the preset's `grid_width` and `grid_height` override the top-level dimensions. Set `resolution_preset` to `custom` to use the top-level dimensions directly. Total window height includes the fixed 60-pixel HUD, which is why the `hd` grid of 64 by 33 cells at 20 pixels produces a 1280 by 720 window.

The preset descriptions are human-readable metadata. Unknown keys have no runtime contract and should not be treated as extension points.

## Player preferences

The Settings menu and related hotkeys persist these values in `preferences.json` through [UserSettings](../../src/vibesnake/core/user_settings.py):

| Preference | Range | Behavior |
| --- | --- | --- |
| `sound_enabled` | boolean | Restores muted or enabled audio on launch |
| `volume` | 0 to 1 | Applies to radio and connected sound effects |
| `fullscreen` | boolean | Restores the selected display mode on launch |

Runtime `sound` values provide defaults only when a player preference has not been saved. Preference writes are versioned, atomic, and protected against corruption and unsupported future schemas. See [PROGRESSION.md](../design/PROGRESSION.md) for persistence details.

## Environment variables

### `VIBESNAKE_DATA_DIR`

Overrides the directory used by all save repositories.

```powershell
$env:VIBESNAKE_DATA_DIR = "C:\temp\vibesnake-data"
python -m vibesnake
```

Normal runs use the operating system's user-data directory. Tests set this variable to a temporary directory so they never read or overwrite player files.

### SDL variables

Headless validation uses:

```powershell
$env:SDL_VIDEODRIVER = "dummy"
$env:SDL_AUDIODRIVER = "dummy"
$env:PYGAME_HIDE_SUPPORT_PROMPT = "1"
```

These settings are for automated checks, not normal play.

## Validation workflow

After changing configuration, run the focused tests and then launch visibly:

```powershell
python -m pytest tests/core/test_config.py tests/powerups/test_manager.py
python -m vibesnake
```

Automated tests cover merging, invalid types and ranges, future schemas, named and custom resolution behavior, and configured power-up visibility.
