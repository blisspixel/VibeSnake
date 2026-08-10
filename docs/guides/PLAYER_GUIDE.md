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
vibesnake status
vibesnake version
vibesnake update --dry-run
./play.ps1
```

Every successful `main` push refreshes the floating
[player-latest](https://github.com/blisspixel/VibeSnake/releases/tag/player-latest)
download (source zip, wheels, checksums). Version tags publish the same artifacts
under a release name.

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

### Display and pointer behavior

Open Settings, then Display, to choose windowed, borderless fullscreen, or exclusive fullscreen. The 4:3 classic, 16:9, and 16:10 size presets apply to windowed mode. Fullscreen always fills the active display and does not add a second aspect-ratio frame. The 1280 by 720 game canvas is fitted without stretching or cropping. In fullscreen, the mouse pointer hides after 1.5 seconds without movement, reappears immediately when moved, and is restored whenever the game loses focus.

### Customize

Select Customize or press C to browse the eight authored native cosmetic sets. Up and Down move between sets, Left and Right change pages, Enter or controller A equips an unlocked set, and R or controller Y saves it as a loadout. The large live preview shows the same 4 by 4 sub-pixel pattern, eye, accessory, and trail treatment used during gameplay. Locked sets show their exact progression requirement. These visual choices never change hitboxes, movement, scoring, powers, AI, or input.

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
The player-facing reset, verified-backup, restore, diagnostics, and local-removal procedure is in the [save and recovery guide](RECOVERY.md). Review the [privacy statement](../../PRIVACY.md) before sharing a log or diagnostic file.

## AI spectator mode

In the native Godot product build, select LET'S PLAY / AI CHANNELS or press L at the main menu to open the local AI broadcast circuit. Up and Down select personality, rivalry, rules, seed class and slot, playback speed, explanation level, or prediction; Left and Right change the selected value; Enter or controller A starts the equal-rules matchup.

During a broadcast, Enter or A pauses, Down or D-pad Down advances one step while paused, Up or D-pad Up switches the viewed rival, and Left or Right changes playback speed. H or controller L3 hides or restores the spectator overlay without changing the run. R or controller Y restarts the same selection. After both lanes finish, C or controller X starts an exact-seed human challenge under identical rules. Escape or controller B returns first to selection and then to the menu.

Predictions are informational only. AI matches and seed challenges award no currency and cannot advance ordinary human progression. Invalid unofficial channels safely fall back to the balanced built-in channel. The frozen Python reference player retains its older L-based AI browser and local custom-JSON workflow; that is not the 1.0 product path.

## Optional lore archive

From native spectator selection, press U or controller LB to open the Coil Archive. Left and Right change the depth filter between All, Surface, Discoverable, and Archive. Up and Down move through the filtered entries. Escape or controller B returns to spectator selection, then to the main menu.

All 19 surface entries are available immediately and summarize the eight stations, ten rivals, and nine mutations. Discoverable and archive entries open from existing local progression rewards, spectator milestones, and replay counts. A locked row names its exact local requirement. Lore browsing is offline and optional, never appears over an active run, awards no progression, and is not required to understand controls, danger, scoring, powers, accessibility, or death.

## Offline comparisons and household ghosts

In the native Godot replay browser, press U or controller LB to open Offline Comparisons. The browser has four fixed household rival slots. Up and Down select a slot. To import, place a native verified replay at `user://imports/household-rival.vibesnake-replay.json`, then press U or controller LB. Import copies the replay into the selected empty slot and never changes or removes the source. An occupied slot is never overwritten. Modified, incompatible, oversized, or unreadable files are rejected and remain where they were.

Press Enter or controller A on a verified slot to race its ghost. The player and ghost start with the same rules, mode, configuration, and gameplay seed. The outlined ghost is visual only: its body, commands, collision, score, powers, and random state cannot affect the player. The HUD shows the household slot, ghost score, score delta, and length delta. Normal movement, pause, restart, keyboard, D-pad, and stick controls remain active. Restart recreates the same comparison. The race uses an isolated seeded-challenge score identity and cannot award ordinary progression.

Press C or controller X on a verified slot to export a local privacy-safe run card. Each card contains closed versioned run facts such as score, peak combo, length, mode, seed, station, powers, selected look, and verification state. It contains no player name or private machine path and does not upload anything.

Press F8 or controller Select/Back to prepare deletion of the selected copied slot. Enter or controller A confirms; Escape or controller B cancels without writing. Deleting a slot never deletes the original import source or an exported run card. Escape or controller B outside a deletion prompt returns to the replay browser.

## Troubleshooting

### Pygame does not install

Use Python 3.11 through 3.14. Install the declared `pygame-ce` dependency rather than the legacy `pygame` distribution so Python 3.14 receives a supported native wheel.

### The game launches without music

The clean clone intentionally contains procedural gameplay cues but no approved radio pack. For local rights and quality review, set `VIBESNAKE_AUDIO_DIR` to a reviewed overlay containing a `radio/` directory, then launch from the checkout. An installed wheel is not yet a supported player artifact.

### Display or audio fails in a remote environment

The normal game requires desktop display and audio support. Automated tests use SDL dummy drivers, but that mode is intended for validation rather than play.

### Progress is in the wrong location

Normal saves use the operating system's user-data directory: `%LOCALAPPDATA%\VibeSnake` on Windows, `~/Library/Application Support/VibeSnake` on macOS, and the XDG data location on Linux. `VIBESNAKE_DATA_DIR` overrides that location for portable or development use. See [CONFIGURATION.md](CONFIGURATION.md).
