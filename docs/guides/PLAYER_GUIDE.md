# Player Guide

## Start the game

Vibe Snake currently runs from a source checkout of
[blisspixel/VibeSnake](https://github.com/blisspixel/VibeSnake). Follow the
installation commands in the [root README](../../README.md), then launch with:

```powershell
vibesnake
```

or:

```powershell
python -m vibesnake
```

The game opens at the main menu. Press Enter to begin a human run or L to browse AI channels.

## Keep it updated

From the same checkout (with your virtual environment active):

```powershell
vibesnake update
```

This fast-forwards `main` from GitHub and reinstalls the package. Local saves stay
in the operating system user-data directory. Optional checks:

```powershell
vibesnake doctor
vibesnake version
vibesnake update --dry-run
```

Player source zips and Python wheels are produced by the GitHub Actions
`Player build` workflow on `main` and attached to version tags.

## Core loop

1. Guide the snake to food.
2. Eat frequently enough to beat the 30-second starvation clock.
3. Chain food within three seconds to build a score multiplier.
4. Use edge wrapping to escape bad routes.
5. Collect power-ups when the detour is worth the risk.
6. Avoid the snake's own body. The outer edge wraps instead of killing the snake.

## Controls

### Movement

| Device | Input |
| --- | --- |
| Keyboard | Arrow keys or WASD |
| Mouse | Click in the desired direction relative to the snake's head |
| Gamepad | D-pad, left analog stick, or face buttons |

The snake cannot reverse directly into itself. Rapid valid turns are buffered.

### Global and menu controls

| Key | Action |
| --- | --- |
| Enter | Start, confirm, or choose an AI channel |
| P | Pause or resume a run |
| Escape | Close a screen, pause, or leave a mode depending on context |
| F11 | Toggle fullscreen |
| C | Open customization or play again from game over |
| V | Open high scores |
| A | Open achievements |
| S | Open settings |
| H | Open or close help |
| L | Browse or leave AI spectator channels |
| Q | Quit from supported screens |

### Radio controls

| Key | Action |
| --- | --- |
| M | Toggle radio playback |
| R | Move to the next station |
| `[` and `]` | Previous or next station |
| 1 through 8 | Select a station directly |

## Scoring

Food starts at 10 base points. The combo count increases with each food item and resets after more than three seconds without food.

| Combo count | Multiplier at milestone |
| --- | --- |
| 0 | 1x |
| 3 | 2x |
| 5 | 3x |
| 10 | 5x |
| 20 | 10x cap |

Values between milestones are linearly interpolated, so every step improves the multiplier. Eating within 1.5 seconds adds a 50 percent speed bonus. Long snakes receive an additional length-based bonus. Near-miss and clutch events can add bonus points.

## Survival and wrapping

The playfield wraps horizontally and vertically. Crossing an edge is safe and counts toward progression. Self-collision remains fatal unless an active effect prevents it.

The starvation indicator begins escalating after 20 seconds without food. A legal move into food on the exact deadline succeeds and resets the clock. If that final move misses food, the move completes visibly and starvation then resolves. Last Stand is evaluated only after the missed final move, so a successful clutch eat does not consume it.

## Power-ups

Nine collectible power-ups alter movement, routing, growth, food placement, or survival. Shield blocks one crash, Phase Shift crosses occupied cells, Gluttony scores without growth, and Last Stand can rescue either collision or starvation. Bait affects the next food spawn, while Segment Detach creates temporary walls from your own tail. The exact durations, tradeoffs, and collision precedence are in [POWERUPS.md](../design/POWERUPS.md).

## Progression

- Achievements unlock at the end of human runs and persist in the player profile.
- Human apples, wraps, scores, combos, and game counts unlock cosmetic options.
- AI spectator runs do not change human progression.
- Five cosmetic layers can be combined and saved as loadouts.
- Qualifying scores enter a local top-ten table.

Save ownership, schemas, migrations, backups, and recovery limitations are in [PROGRESSION.md](../design/PROGRESSION.md).

## AI spectator mode

Press L at the main menu, choose a personality with Up or Down, and press Enter. The selected AI controls the snake while you can change radio stations or start another channel. Custom JSON personalities placed under `assets/ai/custom/` appear in the browser at startup.

## Troubleshooting

### Pygame does not install

Use Python 3.11 through 3.14. Install the declared `pygame-ce` dependency rather than the legacy `pygame` distribution so Python 3.14 receives a supported native wheel.

### The game launches without music

The clean clone intentionally contains procedural gameplay cues but no approved radio pack. For local rights and quality review, set `VIBESNAKE_AUDIO_DIR` to a reviewed overlay containing a `radio/` directory, then launch from the checkout. An installed wheel is not yet a supported player artifact.

### Display or audio fails in a remote environment

The normal game requires desktop display and audio support. Automated tests use SDL dummy drivers, but that mode is intended for validation rather than play.

### Progress is in the wrong location

Normal saves use the operating system's user-data directory: `%LOCALAPPDATA%\VibeSnake` on Windows, `~/Library/Application Support/VibeSnake` on macOS, and the XDG data location on Linux. `VIBESNAKE_DATA_DIR` overrides that location for portable or development use. See [CONFIGURATION.md](CONFIGURATION.md).
