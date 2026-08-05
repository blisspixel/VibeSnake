# Current Status

Snapshot date: 2026-08-05

## Executive assessment

Vibe Snake is a substantial, playable alpha with a distinctive audiovisual identity and a reliable engineering baseline on GitHub. The canonical repository is [blisspixel/VibeSnake](https://github.com/blisspixel/VibeSnake): a single `main` branch, open PR list kept empty, and green hosted CI on every push. All nine power-ups work end to end in the Python reference. Saves, schema migration, corruption protection, runtime configuration validation, and player preferences are implemented. The public checkout includes the full eight-station offline radio library (95 tracks), adaptive 4:3-first presentation, and a player path (`vibesnake play|update|status|doctor|version` plus `play.*` and install scripts). Continuous [player-latest](https://github.com/blisspixel/VibeSnake/releases/tag/player-latest) artifacts rebuild from `main`.

The gated 1.0 target remains Godot 4 .NET with deterministic pure C# rules and first-class Windows, macOS, and Linux players. The native foundation builds, tests, exports, and smokes on hosted Windows, macOS, and Linux runners. All nine powers are complete pure C# contracts with unit coverage, and the Godot shell now renders markers, HUD status, feedback cues, and Slow-Mo/Boost cadence for the full portfolio. Pack export approval, deeper parity fixtures, physical-controller evidence, and structured playtesting remain open.

## Verified quality baseline

| Area | Verified state |
| --- | --- |
| Version | 0.2.1 alpha |
| Canonical remote | [blisspixel/VibeSnake](https://github.com/blisspixel/VibeSnake), sole branch `main` |
| Supported development runtimes | Python 3.11, 3.12, 3.13, and 3.14 |
| Runtime dependency | Pygame Community Edition 2.5.7 or newer within major version 2 |
| Player delivery | `vibesnake update` against GitHub `main`; install scripts; floating `player-latest` release with source zip, wheels, and checksums |
| Hosted CI | Python quality matrix (3.11-3.14), native rules on Windows/macOS/Linux, and native player export smoke on all three platforms pass on tip of `main` |
| Python deterministic tests | 466 passing and 3 environment-dependent radio skips on the supported interpreters in CI |
| Python line coverage | About 87 percent measured with an 80 percent floor enforced by configuration and CI |
| Native toolchain | Godot 4.7.1 Mono and .NET SDK 10.0.302 pinned and verified |
| Native contract tests | 421 passing on .NET 10 with an 80 percent line floor per module |
| Cross-language parity | 100 movement cases (25,600 steps), 35 targeted core-rule cases, 8 Shield, 6 Phase Shift, 5 Last Stand, and 9 remaining-power cases pass |
| Godot integration | Headless import plus seeded rules, restoration, logical input with schema-1 InputMap apply, VirtualViewport letterbox draw, multi-bus volume apply, focus-loss pause, audio buses, fourteen fallback cues, full nine-power markers and captions, cadence-aware stepping, live replay recording, isolated atomic save, and clean shutdown smoke on hosted runners |
| Native artifacts | Windows, macOS, and Linux player smokes run outside the checkout on matching hosted runners; continuous Python player-latest packages publish from `main` |
| Static policy | Ruff, source-policy, documentation links, screenshot fingerprint, logo and badge hashes, content inventory, and dependency locks are CI gates |
| Dependency integrity | Universal Python 3.11 through 3.14 hash-locked graph; locked NuGet restore with audit |
| Public content inventory | 114 classified files totaling 340,378,770 bytes (95 radio MP3 tracks, 9 PNG images, 7 JSON, 3 Markdown); all rights-cleared; 0 export-eligible for native packs until pack quality gates pass |
| Documentation links | Local checker in `scripts/check_docs.py` |

## Feature status

| System | Status | Evidence and qualification |
| --- | --- | --- |
| Core movement | Working | Four-direction movement, queued input, self-collision, phase overlap, and edge wrapping are implemented and tested. |
| Scoring | Working | Base points, speed bonus, length bonus, bonus points, and smoothly interpolated 1x to 10x combos are implemented. Native near-miss awards are on by default via `RunConfig.EnableNearMiss` after dual-runtime fixture regen. |
| Starvation | Working | A 30-second timer, warning state, food rescue, move-then-starve order, Last Stand recovery, death telemetry, and player-run finalization are wired and tested. |
| Menus and overlays | Working | Twelve game states render headlessly; menu navigation is tested; retro-modern title, settings, and pause chrome with adaptive framing. |
| Adaptive presentation | Working (Python alpha) | Preferred 4:3 framing, integer pixel scaling, and letterboxing for phone, square, and wide windows through `AdaptiveDisplay`. |
| Input | Working with native qualification debt | Python alpha covers keyboard, WASD, mouse, and gamepad paths. Native shell centralizes logical keyboard and any-controller movement, confirm, back, pause, replay verification, and quit; schema-1 bindings store applies to the InputMap with opposite-device preservation and pure TryRemapAction conflict checks; accessibility hotkeys cover mute, volume, text scale, contrast, motion, flash-free, fullscreen, and diagnostics open; pure controller connection tracker pauses on last disconnect. Physical multi-controller hardware evidence, glyphs, and full remapping UI remain. |
| Achievements | Working | Twenty-five achievement conditions evaluate, display, save with the profile, and restore. Native pure `AchievementCatalog` evaluates run-local candidates; product runs emit once-only terminal `AchievementCandidate` events with shell captions (flag default off for dual-runtime fixture stability). Schema 1 `achievements.json` stores permanent unlock IDs; shell load/save and `ApplyProfileUnlocks` suppress already-owned candidates. |
| Cosmetics | Working | Five cosmetic axes yield 10,800 combinations with versioned, validated, atomic persistence. |
| Leaderboard | Working | One top-ten repository owns persistence; legacy single-score import is one-time. |
| Save durability | Working with UX debt | Schema-versioned atomic repositories in OS user-data directories with migrations and corrupt backups. Native replay store is bounded and fail-closed. In-game reset and backup recovery UI remain open. |
| Player preferences | Working | Sound state, volume, and fullscreen selection persist across launches. |
| AI spectator mode | Working | Ten built-in personalities plus JSON-loaded custom personalities. AI runs do not advance human progression. |
| Radio | Public offline library | Eight stations and 95 original MP3 tracks ship under `assets/audio/radio/` with Apache-2.0 project intent. Prefix discovery assigns every track once. Native pack export still requires loudness, credit, and allowlist approval (`exportEligible` remains zero). |
| Sound effects | Partial | Procedural Python and native fallbacks cover critical cues; authored SFX catalog and mix review remain. |
| Power-ups | Python complete; native complete; Godot presentation wired | All nine powers are pure C# with lifecycle, collision, and food-interaction contracts. Godot draws pickups, active state, hazards, composite HUD, and cadence. Shared parity fixtures cover Shield, Phase Shift, Last Stand, and the remaining six power types. |
| Adaptive difficulty | Not active | Removed unwired controller. Future policy requires deterministic integration, disclosure, opt-out, separate score categories, and evidence. |
| Configuration | Working | Schema version 1 validates types and ranges; changes still require restart. |
| Player CLI and updates | Working | `vibesnake play`, `update`, `status`, `doctor`, and `version`; `play.ps1` / `play.sh` / `play.bat`; install scripts; GitHub `main` fast-forward reinstall. |
| Packaging | Partial | Source and wheel player-latest artifacts exist. Runtime assets still use source-tree-relative paths; a bare wheel is not a fully self-contained game without assets. |
| Automated gameplay QA | Foundation working | Seeded policies, invariants, property-generated input, replay hashes, JSON reports. Native parity retains first-divergence bundles with automated delta-reduced command prefixes. Full powers, DDA, AI, and presentation campaigns still depend on the completed deterministic engine. |
| Content inventory | Foundation working | Deterministic policy and generated inventory cover 114 public assets including the radio library. Export eligibility is deliberately zero until pack quality gates pass. `ContentEligibilityReport` and `content-eligibility-evidence-v1` JSON summarize ship/rights/media breakdowns for pack-approval handoffs. |
| Content pack contract | Foundation working | Schema 1 validates core and optional radio manifests against inventory allowlists. ContentBudgetReport measures inventory totals; ContentService resolve codes deny non-exportEligible packaging. No production manifest is export-approved yet. |
| Target technology | Qualification in progress | Godot 4.7.1 and .NET 10.0.302 pinned. Pure C# kernel covers core rules and all nine powers. Godot shell honors full-power presentation and tempo cadence. Hosted multi-platform player smoke exists. Pack export, deeper parity fixtures, and feel review remain open. |

## Inventory facts

- Radio library: 95 original MP3 tracks assigned exactly once across eight stations in public source under `assets/audio/radio/`.
- Public inventory: 114 files, 340,378,770 bytes, all rights-cleared, structurally valid; 106 blocked for pack export, 8 excluded development references; 0 export-eligible.
- Achievements: 25 definitions across common, rare, epic, and legendary tiers.
- AI: 10 built-in personalities and one loadable custom personality in the checkout.
- Cosmetics: 12 colors, 6 patterns, 5 eye styles, 6 accessories, and 5 trails.
- Game flow: 12 enumerated states managed by an explicit transition map.

An optional ignored local `archive/` may preserve historical production records on developer machines. It is not part of the public repository or release authority.

## Improvements completed since the prior audit

- Native achievements schema 1 profile unlocks, schema 3 session counters, property campaign evidence, and content eligibility reports landed under dual-runtime gates.
- Moved the project to a flat public GitHub repository with green CI and a single `main` branch.
- Shipped the eight-station offline radio soundtrack in public source with Apache-2.0 release intent.
- Added adaptive 4:3-first presentation and retro-modern menu chrome for the Python alpha.
- Restored the preferred Snakev2 brand logo with CI hash gates and refreshed README captures.
- Added player CLI update path, install scripts, play launchers, and continuous `player-latest` release packaging.
- Disabled Dependabot version-update PR spam so the public branch list stays clean.
- Hosted CI now exercises native rules and native player smoke on Windows, macOS, and Linux.

Earlier 0.2.0 foundations (nine powers, save schemas, QA laboratory, pure C# Shield, replay storage, content policy, toolchain pins) remain in effect; see [CHANGELOG.md](../../CHANGELOG.md).

## Release blockers

1. Complete for rules, shared fixtures, and Godot presentation of all nine powers; delta reduction and inventory export locks are automated.
2. Complete the native vertical slice with physical-controller and hot-plug proof, remapping, accessible presentation, authored audio, scaling feel review, and reviewed UX parity.
3. Expand artifact smokes through controller, audio failure, scaling, and lifecycle paths on all three platforms with retained evidence; inventory exportEligible remains zero by gate.
4. Qualify the first real core and radio pack manifests (export eligibility is still zero): loudness, credits, allowlists, and pack approval for selected public assets.
5. Replace repository-relative asset paths with the target content service and prove allowlisted artifact contents.
6. Complete deterministic rules port and expand QA to powers, DDA, AI, persistence, replays, presentation, and reliability campaigns.
7. Add in-game save reset confirmation and clear corrupt-backup recovery.
8. Conduct iterative structured playtests for controls, difficulty, power choices, escalation, readability, audio fatigue, accessibility, restart, and replay desire.
9. Complete the event-to-SFX, Vibe Level, radio broadcast, and accessibility pass needed for release-quality feedback.

The recommended implementation order is in the [roadmap](../../ROADMAP.md).

The current roadmap milestone is 0.3.0, technology qualification and native vertical slice. Later feature milestones remain gated behind trace parity and clean native artifacts so new behavior is always built and tested in the architecture players will receive.
