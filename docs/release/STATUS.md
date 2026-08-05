# Current Status

Snapshot date: 2026-08-05

## Executive assessment

Vibe Snake is a substantial alpha with a reliable engineering baseline on GitHub. The canonical repository is [blisspixel/VibeSnake](https://github.com/blisspixel/VibeSnake): a single `main` branch, open PR list kept empty, and green hosted CI on every push.

**Product path:** Godot 4.7.1 .NET + pure C# rules/persistence. Hosted runners export and smoke native players outside the checkout on Windows, macOS, and Linux **without Python**. All nine powers are complete pure C# contracts; the Godot shell renders markers, HUD, multi-power feedback, Slow-Mo/Boost cadence, achievements unlocks/browse, and logical input.

**Python/Pygame:** Frozen behavior oracle and temporary source-playable alpha. Not the 1.0 runtime. Dual-runtime JSON fixtures exist only so C# can prove parity with the oracle. New player features belong in `native/` and `game/`, not in expanding Python gameplay.

The public checkout still includes the eight-station offline radio library (95 tracks), adaptive Python presentation, and a reference player path (`vibesnake play|update|status|doctor|version`). Continuous [player-latest](https://github.com/blisspixel/VibeSnake/releases/tag/player-latest) packages rebuild the **reference** player from `main`. Pack export approval (`exportEligible` still zero), installer/archive shapes, physical-controller evidence, HW frame evidence, and structured playtesting remain open.

### What is next (and why)

See [ROADMAP.md Product path](../../ROADMAP.md#product-path-read-this-first). Short form:

1. **Godot shell depth** - remapping, controller evidence, scaling, audio stress (players receive this artifact).
2. **Installer/archive shapes (V030-10)** - continuous export is not a store install yet.
3. **First export-eligible packs (V030-08/09)** - inventory is classified; ship packs are empty by gate.
4. **C# evidence depth** - corpus compaction, replay browse UI; not new Python systems.
5. **Human gates** - HW p50/p95/p99, feel, physical controller.

## Verified quality baseline

| Area | Verified state |
| --- | --- |
| Version | 0.2.1 alpha |
| Canonical remote | [blisspixel/VibeSnake](https://github.com/blisspixel/VibeSnake), sole branch `main` |
| Ship runtime (target) | Godot 4.7.1 .NET + pure C# rules; packaged player without Python |
| Oracle / alpha checkout | Python 3.11-3.14 + Pygame CE 2.5.7+ (reference only) |
| Player delivery | Native export smokes on CI; reference `vibesnake update` + floating `player-latest` source/wheel packages from `main` |
| Hosted CI | Python oracle matrix (3.11-3.14), native rules on Windows/macOS/Linux, and native Godot player export smoke on all three platforms pass on tip of `main` |
| Python deterministic tests | 466 passing and 3 environment-dependent radio skips on the supported interpreters in CI |
| Python line coverage | About 87 percent measured with an 80 percent floor enforced by configuration and CI |
| Native toolchain | Godot 4.7.1 Mono and .NET SDK 10.0.302 pinned and verified |
| Native contract tests | **451** passing on .NET 10 with an 80 percent line floor per module |
| Cross-language parity | 100 movement cases (25,600 steps), 35 core-rule cases, 8 Shield, 6 Phase Shift, 5 Last Stand, 9 remaining-power, and 4 achievement-candidate product-path cases pass |
| Godot integration | Headless import plus seeded rules, restoration, logical input with schema-1 InputMap apply, VirtualViewport letterbox draw, multi-bus volume apply, focus-loss pause, audio buses, fourteen fallback cues, full nine-power markers and captions, cadence-aware stepping, achievements load/save and catalog browse, live replay recording, isolated atomic save, and clean shutdown smoke on hosted runners |
| Native artifacts | Windows, macOS, and Linux player smokes run outside the checkout on matching hosted runners **without Python**; continuous Python reference player-latest packages still publish from `main` |
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
| Achievements | Working | Twenty-five Python profile achievements remain in the reference alpha. Native pure `AchievementCatalog` evaluates run-local candidates; product runs emit once-only terminal `AchievementCandidate` events with shell captions (flag default off for default dual-runtime corpora; dedicated product-path fixture proves flag-on parity). Schema 1 `achievements.json` stores permanent unlock IDs; shell load/save, `ApplyProfileUnlocks`, and full-catalog browse (`U`/LB via `AchievementsBrowseReport`) are live in Godot. |
| Cosmetics | Working | Five cosmetic axes yield 10,800 combinations with versioned, validated, atomic persistence. |
| Leaderboard | Working | One top-ten repository owns persistence; legacy single-score import is one-time. |
| Save durability | Working with UX debt | Schema-versioned atomic repositories in OS user-data directories with migrations and corrupt backups. Native replay store is bounded and fail-closed. In-game reset and backup recovery UI remain open. |
| Player preferences | Working | Sound state, volume, and fullscreen selection persist across launches. |
| AI spectator mode | Working | Ten built-in personalities plus JSON-loaded custom personalities. AI runs do not advance human progression. |
| Radio | Public offline library | Eight stations and 95 original MP3 tracks ship under `assets/audio/radio/` with Apache-2.0 project intent. Prefix discovery assigns every track once. Native pack export still requires loudness, credit, and allowlist approval (`exportEligible` remains zero). |
| Sound effects | Partial | Procedural Python and native fallbacks cover critical cues; authored SFX catalog and mix review remain. |
| Power-ups | Native complete; Godot presentation wired; Python oracle complete | All nine powers are pure C# with lifecycle, collision, and food-interaction contracts plus multi-power synergy campaigns. Godot draws pickups, active state, hazards, composite HUD, and cadence. Shared parity fixtures cover Shield, Phase Shift, Last Stand, and the remaining six power types. |
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

- Pure C# rules kernel + Godot shell advanced as the **primary product surface** (nine powers, multi-power synergy campaigns, achievements profile + browse, schema 3 session counters, property campaign evidence, content eligibility reports, architecture purity bans).
- Dual-runtime achievement-candidate product-path fixture proves flag-on parity without flipping default-off corpora.
- Hosted CI exercises native rules and native player smoke on Windows, macOS, and Linux without Python.
- Single public `main` branch with green CI; Dependabot version-update spam disabled.
- Reference path: eight-station offline radio in public source, adaptive Python presentation, `player-latest` packages, player CLI.

Earlier 0.2.0 foundations remain in effect; see [CHANGELOG.md](../../CHANGELOG.md).

## Release blockers

1. **Godot shell depth:** physical multi-controller evidence, remapping UI/glyphs, scaling matrix, authored audio stress, feel review.
2. **Installer/archive shapes and release manifests (V030-10)** while continuous export smokes already pass.
3. **First export-eligible core/radio packs** (`exportEligible` still zero): loudness, credits, allowlists.
4. Expand native artifact smokes through controller, audio failure, scaling, and lifecycle paths with retained evidence.
5. Replace remaining repository-relative asset assumptions with the target content service for ship packs.
6. C# QA depth: permanent corpus compaction, replay browse/playback UI, broader campaigns (powers already pure C#).
7. In-game save reset confirmation and clear corrupt-backup recovery (Godot UX).
8. Structured playtests for controls, difficulty, power choices, readability, audio fatigue, accessibility, restart desire.
9. Event-to-SFX, Vibe Level, radio broadcast, and accessibility pass for release-quality feedback.

**Do not treat Python feature work as progress toward 1.0.** Keep the oracle green; ship in Godot/C#.

The recommended implementation order is in the [roadmap](../../ROADMAP.md) (**Product path** section).

Current roadmap milestone: **0.3.0** technology qualification and native vertical slice. Later milestones stay gated behind clean native artifacts so new behavior is built in the architecture players receive.
