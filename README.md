<p align="center">
  <img src="assets/images/logo.png" alt="Vibe Snake logo" width="220">
</p>

<p align="center">
  <a href="https://github.com/blisspixel/VibeSnake/actions/workflows/ci.yml"><img src="https://github.com/blisspixel/VibeSnake/actions/workflows/ci.yml/badge.svg" alt="CI status"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache--2.0-blue.svg" alt="Apache-2.0 license"></a>
  <img src="https://img.shields.io/badge/Godot-4.7.1-478CBF.svg" alt="Godot 4.7.1">
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4.svg" alt=".NET 10.0">
</p>

# Vibe Snake

Vibe Snake is a native Godot and C# arcade successor to Snake. It combines a clean wraparound core with starvation pressure, combos, tactical powers, cosmetic progression, AI spectator channels, replays, and an offline radio framework.

The product path is Godot 4.7.1, .NET 10, and pure C# rules. The older Python/Pygame implementation remains a frozen behavior oracle and migration reference. New product work belongs in `game/` and `native/`.

This is active `0.3.0-alpha.1` development. Automated Windows, macOS, and Linux qualification is extensive, but Store-ready 1.0 is not ready. Physical-device review, named-hardware performance, approved content packs, signing, and structured human playtesting remain release gates. See [current status](docs/release/STATUS.md) and the [roadmap](ROADMAP.md) for the evidence-backed details.

## Current native build

<table>
  <tr>
    <th>Main menu</th>
    <th>Vibe mode gameplay</th>
  </tr>
  <tr>
    <td><img src="docs/images/screenshots/main-menu.png" alt="Native Godot main menu with the Vibe Snake logo, Start Run, Customize, AI Channels, Settings, Help, and other choices"></td>
    <td><img src="docs/images/screenshots/powers-run.png" alt="Native Godot Vibe mode gameplay with terrain detail, combo and hunger HUD, powers, food, obstacles, and a visible bait marker"></td>
  </tr>
  <tr>
    <th>Customization</th>
    <th>Let's Play / AI channel</th>
  </tr>
  <tr>
    <td><img src="docs/images/screenshots/customization.png" alt="Native Godot customization browser with curated sets, cosmetic attributes, color swatches, and a detailed live snake preview"></td>
    <td><img src="docs/images/screenshots/ai-channel.png" alt="Native Godot AI spectator channel with the gameplay board visible above a thin two-line broadcast ticker"></td>
  </tr>
</table>

These are direct 1280 by 720 captures from the current Godot renderer. Their hashes, dimensions, README references, and native presentation-source fingerprint are enforced by the [capture manifest](docs/images/screenshots/manifest.json).

## Highlights

- Classic and Vibe modes with separate rules and score identities.
- Nine tactical powers, including the visible `T` bait trail.
- Detailed pixel-art snakes, procedural terrain, multimodal feedback, and accessibility profiles.
- Keyboard, mouse, D-pad, analog stick, and remappable controller input.
- 4:3, 16:9, 16:10, square, ultrawide, borderless, and exclusive-fullscreen presentation contracts.
- Idle pointer hiding, focus-safe pausing, reduced motion, flash-free presentation, high contrast, mono audio, and scalable text.
- Curated cosmetic sets, progression goals, Broadcast Tour, achievements, local scores, replays, and recovery tools.
- Let's Play / AI channels with equal-rules matches, a thin live ticker, contextual playback controls, standings, lore, and seed challenges.
- Post-1.0 Agent Arena source developer preview where an external agent uses exact one-step play or a separate maximum-16-step event-and-lesson-target-stopping burst through a separately launched local MCP host while a human joins through the command-line watch handoff and a read-only live Godot viewer that labels actual advancement, stop facts, paused rules, and coalesced updates, with six selectable Signal School practices, named rivals, Style Contracts, closed public intent, replay-bound lesson outcomes, verified replays, and bounded idle-session reclamation.
- Offline-first saves and content with no account or network requirement.

The complete player-facing behavior is in the [player guide](docs/guides/PLAYER_GUIDE.md).

## Play from source

Prerequisites are Git, PowerShell 7, and the .NET 10.0.303 SDK. The launcher installs and verifies the pinned Godot 4.7.1 .NET editor on first use, builds the native game, and starts it.

Windows:

```powershell
git clone https://github.com/blisspixel/VibeSnake.git
cd VibeSnake
./play.ps1
```

macOS or Linux:

```bash
git clone https://github.com/blisspixel/VibeSnake.git
cd VibeSnake
./play.sh
```

The current floating source build and checksums are published under [player-latest](https://github.com/blisspixel/VibeSnake/releases/tag/player-latest) from the newest successfully qualified `main` push. A newer qualified revision may cancel a superseded publisher before it finishes. The release contains the source archive, Python reference wheel and sdist, and `SHA256SUMS.txt`. The source archive includes development previews such as the Agent Plugin manifest and skill, the MCP host source and packaging script, and the generated Open Knowledge Format bundle. It is not a signed native player or a preassembled supported Agent Plugin. Versioned native alpha releases have a separate fail-closed pipeline, and the first tag remains blocked on approved packaged content and an exact artifact review. See [native release outputs](docs/release/PACKAGING.md) and [agent play integration](docs/engineering/AGENT_PLAY.md).

## Controls

| Action | Default input |
| --- | --- |
| Navigate or move | Arrow keys, WASD, D-pad, analog stick, or mouse |
| Confirm | Enter, controller south button, or left click |
| Back | Escape, controller east button, or right click |
| Pause | P, controller Start, or middle click |
| Radio | J or controller R3 |
| Fullscreen | F11 |
| Help | H |

All gameplay and shell actions are available through remappable keyboard and controller routes. See [input and lifecycle](docs/design/INPUT.md) for the exact action contract.

## Documentation

| Need | Start here |
| --- | --- |
| Browse all documentation | [Documentation hub](docs/README.md) |
| Play, configure, or troubleshoot | [Player guide](docs/guides/PLAYER_GUIDE.md) |
| Review accessibility features and limitations | [Accessibility guide](docs/guides/ACCESSIBILITY.md) |
| Understand the game and experience goals | [Game design](docs/design/GAME_DESIGN.md), [fun strategy](docs/design/FUN_DESIGN.md), and the post-1.0 [Agent Arena](docs/design/AGENT_ARENA.md) |
| See verified implementation status | [Current status](docs/release/STATUS.md) |
| Follow the path through 1.0 | [Roadmap](ROADMAP.md) |
| Understand Godot, C#, and the Python migration | [Technology strategy](docs/decisions/TECHNOLOGY_STRATEGY.md) |
| Connect an agent in the post-1.0 preview | [Agent play integration](docs/engineering/AGENT_PLAY.md) |
| Find code and directory ownership | [Repository map](docs/engineering/REPOSITORY_MAP.md) |
| Set up development and run checks | [Development guide](docs/guides/DEVELOPMENT.md) and [testing guide](docs/engineering/TESTING.md) |
| Work on audio, assets, or content packs | [Audio](docs/content/AUDIO.md), [content pipeline](docs/content/CONTENT_PIPELINE.md), and [content packs](docs/content/CONTENT_PACKS.md) |
| Prepare or audit a release | [Release checklist](docs/release/RELEASE_CHECKLIST.md) and [known issues](docs/release/KNOWN_ISSUES.md) |

## Development

The native quality loop builds the Godot project, runs the C# contract suite with enforced line and branch coverage, checks formatting and analyzers, validates the pinned toolchain, and executes the real scene smoke:

```powershell
./scripts/test_native.ps1
```

Python reference and cross-runtime checks remain temporarily in CI while their authoritative validators and fixture generators move to .NET. They are test-only scaffolding, not a second product path. The ordered removal gates, full command set, dependency locks, screenshot workflow, and packaged-player qualification are documented in the [migration map](docs/engineering/MIGRATION_MAP.md#repository-wide-python-retirement) and [development guide](docs/guides/DEVELOPMENT.md). Contribution requirements are in [CONTRIBUTING.md](CONTRIBUTING.md).

## License

Source code, documentation, and assets marked rights-cleared in the generated inventory are available under the [Apache License 2.0](LICENSE). See [NOTICE](NOTICE), [credits](CREDITS.md), and the [asset licensing policy](docs/content/ASSET_LICENSING.md) for the content boundary.
