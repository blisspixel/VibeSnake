# Release Checklist

The current alpha does not meet this checklist. See [STATUS.md](STATUS.md) for the active blockers and [ROADMAP.md](../../ROADMAP.md) for the versioned dependency path. This checklist is the final 1.0 go or no-go view; the roadmap defines which release establishes each item.

## Product scope

- [ ] The fun thesis and intended run arc are validated through fresh-player, returning-player, controller, and accessibility cohorts.
- [ ] Release ruleset and included modes are frozen.
- [ ] Every scored run records mode, rules version, difficulty policy, and DDA policy.
- [x] All nine power-ups match their player-facing descriptions.
- [x] Scoring, starvation, wrapping, DDA, and leaderboard rules are documented.
- [ ] First-run onboarding is usable without external documentation.
- [ ] At least one complete keyboard-only and controller-only flow is verified.
- [ ] Replay and AI spectator behavior use the released rules engine without changing human progression.
- [ ] Broadcast Tour is finite, rules-versioned, replayable, free of filler and manipulative schedules, and grants expression or content access without permanent gameplay power.
- [ ] Fixed-seed keyboard and controller observations confirm exact buffered turns, readable next-cell decisions, attributable death, deliberate restart, and a specific reason to begin another run.
- [ ] The same core observations pass with sound muted and with zero-shake, reduced-motion, and flash-free profiles.

## Saves and configuration

- [x] Every active save file has a schema version and one owner.
- [x] The legacy high-score file migrates into one leaderboard.
- [x] Writes are atomic and corruption recovery is tested.
- [x] Saves use the operating-system user-data location.
- [x] Every supported runtime configuration key is validated and consumed.
- [x] Audio and fullscreen settings persist across restart.
- [ ] Input, accessibility, and expanded audio preferences migrate across restart.
- [ ] Reset and corrupt-backup recovery are available through confirmed player-facing flows.

## Assets and packaging

- [x] Core and optional-radio manifest schema fails closed on unsafe structure, stale inventory metadata, uncleared rights, incompatible versions, bad dependencies, and optional-pack corruption.
- [ ] Radio delivery strategy is selected and documented.
- [ ] A minimal core asset pack is sufficient for offline play.
- [ ] Optional radio packs use versioned manifests, hashes, and compatibility ranges.
- [ ] Every shipped asset has source and license metadata.
- [ ] Runtime lookup works without the repository tree.
- [ ] Wheel or desktop package builds reproducibly.
- [ ] A clean environment installs and launches the built artifact.
- [ ] Uninstall behavior does not remove user saves without consent.
- [ ] Download size, installed size, and optional content size are published.
- [ ] Archive, generation, test, secret, and local-state files are absent from player artifacts.
- [ ] The minimal core pack provides a complete rights-cleared soundtrack and critical cue set, and the Coil identity remains recognizable with radio disabled.

## Automated quality

- [x] Ruff formatting and lint gates pass locally.
- [x] Source policy and dependency-lock freshness checks pass locally.
- [x] Documentation link check passes locally.
- [x] Deterministic tests pass locally on Python 3.11, 3.12, 3.13, and 3.14.
- [x] Project line coverage is at least 80 percent locally.
- [x] New gameplay behavior has integration tests through public boundaries.
- [x] Save migration fixtures cover the previous unversioned format.
- [x] Built-artifact smoke test runs outside the checkout.
- [ ] Hosted CI is green for the release commit or tag.
- [x] Deterministic rules and replay fixtures produce stable state hashes locally; the separate three-platform identity gate remains open below.
- [x] A seeded reference-core QA runner checks per-step invariants and immediate trace replay.
- [ ] Full-run QA covers every power, death path, restart, AI policy, persistence boundary, and replay operation.
- [ ] Power event evidence covers offer, detour, collection, activation, expiry, consumption, recovery, and death adjacency for all nine powers.
- [ ] Fixed, exploratory, and previous-failure seed corpora produce no unexplained invariant or divergence failure.
- [ ] Balance reports have no unreviewed dominant strategy, useless power, impossible seed, or extreme outlier.
- [ ] AI channel automation proves equal rules, policy separation, no stalls, commentary coverage, repeated switching, deterministic replay, and immediate same-seed challenge behavior.
- [ ] Broadcast Tour validation finds no unreachable event, dependency cycle, impossible goal, duplicate reward, grind outlier, score-category contamination, or save-migration failure.
- [ ] Critical engine and persistence boundaries meet their branch-coverage gates.
- [ ] Release artifacts include checksums, a dependency inventory or SBOM, and provenance.

## Input, display, and accessibility

- [ ] Keyboard, mouse, and controller bindings are action-based and remappable.
- [ ] Controller connection, removal, deadzone, and prompt switching are verified.
- [x] Focus loss pauses safely and never leaks buffered input.
- [ ] Windowed, borderless, and fullscreen layouts preserve aspect ratio and pointer mapping.
- [ ] Every interactive element has visible focus.
- [ ] Important text and UI meet documented contrast targets at every supported resolution.
- [ ] Maximum text scale leaves every required action reachable.
- [ ] Reduced-motion, zero-shake, flash-free, high-contrast, and color-independent cues are verified.
- [ ] Master, Music, SFX, and UI audio controls operate independently.
- [ ] Every critical audio cue has a visual or textual counterpart.
- [ ] One typed Vibe Level director is the only authority for presentation intensity; audio, HUD, background, particles, camera, and haptics do not infer competing levels.
- [ ] Every death cause and recovery resource communicates through at least two practical channels that survive muted and reduced-motion play.

## Manual quality

- [ ] An extended session containing multiple complete runs finishes without crash or leaked state.
- [ ] Pause, focus loss, fullscreen, restart, and quit behavior are verified.
- [ ] Keyboard, mouse, Xbox-layout, and PlayStation-layout controllers are tested.
- [ ] Each power-up is collected, expires, stacks, and crosses a death boundary as designed.
- [ ] All nine powers telegraph type and lifetime before commitment, produce a real route decision, and pass the reviewed synergy and anti-synergy scenarios.
- [ ] No tenth power or default Mutation Fork ships without retained seeded and human evidence that it adds planning without clutter or paralysis.
- [ ] Every station starts, changes track, mutes, resumes, and handles a missing file.
- [ ] SFX and music are balanced on headphones and speakers.
- [ ] Text remains readable at supported resolutions.
- [ ] Reduced-motion, shake, contrast, and volume controls are reviewed.
- [ ] A new profile and a migrated profile both complete a run and retain progress.
- [ ] Clean install, update, repair, rollback, optional-pack removal, and application removal are exercised.
- [ ] Reliability campaigns cover repeated launches, deterministic simulation steps, repeated restarts, missing assets, unavailable audio, corrupt data, and failed writes.
- [ ] Fresh human validation confirms control clarity, death attribution, escalation readability, meaningful power choices, recovery payoff, audio fatigue, and desire to replay.
- [ ] Human evidence retains negative and neutral outcomes, exact build and rules identity, fixed seeds, input devices, accessibility profiles, observations, and resulting keep, revise, or remove decisions.
- [ ] Cosmetic offerings are curated authored sets rather than an advertised combinatorial count; every set passes contrast, head recognition, body continuity, accessory bounds, trail occlusion, preview, and meaningful-unlock review.

## Platform artifacts

- [ ] Windows x64 builds and passes the full native artifact matrix.
- [ ] macOS Universal builds, signs, enables hardened runtime, notarizes, staples, and passes the full native artifact matrix on Apple Silicon and Intel.
- [ ] Linux x64 builds and passes executable-permission, runtime-baseline, desktop, display, audio, and full native artifact checks.
- [ ] The same rules fixtures produce identical state hashes on all three platforms.
- [ ] Supported operating-system versions, renderer requirements, controller coverage, and known platform differences are published accurately.

## Release materials

- [ ] Version and changelog are updated.
- [ ] README install instructions match the artifact.
- [ ] Screenshots and trailer show current behavior.
- [ ] License, third-party notices, privacy statement, and support route are included.
- [ ] Accessibility features, supported platforms, controller support, offline behavior, and content sizes are disclosed accurately.
- [ ] Known issues are concise and player-facing.
- [ ] Rollback or hotfix procedure is documented.
- [ ] Published artifacts match the candidate revision, manifests, screenshots, checksums, and release notes.
- [ ] Private vulnerability reporting is enabled and its end-to-end report flow is tested.
- [ ] A concrete private conduct-reporting route is published and tested before contributions or official community spaces open.
- [ ] Dependabot alerts and security updates are enabled; automated fixes follow the project's review policy.
- [ ] The default branch is protected by a ruleset that requires the complete CI workflow and prevents force pushes and deletion.
- [ ] Repository description, topics, issue forms, support links, and security links resolve from a clean signed-out browser session.
- [ ] Secret scanning and push protection are enabled, and no `.env`, credential, signing key, private report, or player data is in source or artifacts.

## Final commands

```powershell
python scripts/lock_python_dependencies.py
python scripts/lock_python_dependencies.py --profile runtime
python -m pip_audit --strict --disable-pip --require-hashes --requirement requirements-ci.lock
python -m pip_audit --strict --disable-pip --require-hashes --requirement requirements-runtime.lock
python -m ruff format --check src tests scripts
python -m ruff check src tests scripts
python scripts/check_source_policy.py
python scripts/check_docs.py
python scripts/capture_readme_screenshots.py --check
python scripts/visual_generate_badges.py --check
python scripts/visual_generate_logo.py --check
python scripts/content_inventory.py --check --release-ready
python -m vibesnake.qa.shared_traces --check
python -m vibesnake.qa.shared_rule_traces --check
python -m vibesnake.qa.shared_power_traces --check
python -m vibesnake.qa.shared_phase_shift_traces --check
python -m vibesnake.qa.shared_last_stand_traces --check
python -m vibesnake.qa.shared_remaining_power_traces --check
python -m vibesnake.qa --seeds 0 1 2 3 4 --steps 500 --output qa_reports/core.json
python -m pytest --cov=vibesnake --cov-report=term-missing --cov-report=xml
python -m build
./scripts/test_native.ps1
./scripts/test_native_export.ps1 -BuildMode Release
```

Run the packaged-player command on clean Windows, macOS, and Linux native
runners with the pinned Godot version. Retain each generated artifact manifest
and smoke log as release evidence.

The build command is listed as the target gate, not evidence that current artifacts are playable. Validate the installed result in a clean environment before release.
