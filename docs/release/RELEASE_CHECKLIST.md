# Release Checklist

The current development revision does not meet this checklist. See [STATUS.md](STATUS.md) for the active blockers and [ROADMAP.md](../../ROADMAP.md) for the versioned dependency path. This checklist is the final 1.0 go or no-go view; the roadmap defines which release establishes each item. A checked item has candidate-independent repository evidence today. Unchecked items require an exact candidate, protected operation, physical review, content decision, or human acceptance and must not be inferred from automation alone.

## First native alpha checkpoint

- [x] One canonical SemVer controls native identity and release tags, with an explicit PEP 440 mapping for Python packages.
- [x] Source snapshots cannot publish versioned releases.
- [x] Native alpha assembly requires the exact Windows, macOS, and Linux Release matrix, deterministic package hashes, manifests, checksums, and detached provenance.
- [x] Alpha assets are named and disclosed as unsigned previews without changing stable publication eligibility.
- [x] Alpha publication requires a deterministic, separately attached radio archive bound to exact inventory, curation, manifest, size, and checksum evidence.
- [ ] Core and radio content have approved export allowlists and a production optional-pack output.
- [ ] The exact downloaded three-platform artifacts pass the manual launch, display, input, audio, and content review.
- [ ] Create `v0.3.0-alpha.1` only after the two preceding gates close and hosted CI is green on its source revision.

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
- [x] Input, accessibility, and expanded audio preferences migrate across restart.
- [x] Reset and corrupt-backup recovery are available through confirmed player-facing keyboard and controller flows.

## Assets and packaging

- [x] Core and optional-radio manifest schema fails closed on unsafe structure, stale inventory metadata, uncleared rights, incompatible versions, bad dependencies, and optional-pack corruption.
- [x] Radio delivery is a separate checked release download with bounded drag-and-drop installation below player data.
- [ ] A minimal core asset pack is sufficient for offline play.
- [x] Optional radio-pack artifacts require versioned manifests, hashes, compatibility ranges, exact curation decisions, and native revalidation.
- [ ] Every shipped asset has source and license metadata.
- [x] Native runtime lookup works without the repository tree.
- [x] Native desktop qualification packages build reproducibly.
- [x] Hosted Windows, macOS, and Linux runners launch the built artifact outside the checkout with a fresh external profile.
- [x] Automated application removal preserves external player data.
- [ ] Download size, installed size, and optional content size are published.
- [x] Archive, generation, test, secret, and local-state files are absent from qualified player artifacts.
- [ ] The minimal core pack provides a complete rights-cleared soundtrack and critical cue set, and the Coil identity remains recognizable with radio disabled.

## Automated quality

- [x] Ruff formatting and lint gates pass locally.
- [x] Source policy and dependency-lock freshness checks pass locally.
- [x] Documentation link check passes locally.
- [x] Deterministic tests pass locally on Python 3.11, 3.12, 3.13, and 3.14.
- [x] The temporary Python reference suite meets its 80 percent line-coverage floor, and every measured native module meets the separate 90 percent line and 85 percent branch floors.
- [x] New gameplay behavior has integration tests through public boundaries.
- [x] Save migration fixtures cover the previous unversioned format.
- [x] Built-artifact smoke test runs outside the checkout.
- [ ] Hosted CI is green for the release commit or tag.
- [x] Deterministic rules and replay fixtures produce stable state hashes locally; the separate three-platform identity gate remains open below.
- [x] A seeded reference-core QA runner checks per-step invariants and immediate trace replay.
- [ ] Full-run QA covers every power, death path, restart, AI policy, persistence boundary, and replay operation.
- [x] Power event evidence covers offer, detour, collection, activation, expiry, consumption, recovery, and death adjacency for all nine powers.
- [x] Fixed, exploratory, and previous-failure seed corpora produce no unexplained invariant or divergence failure.
- [ ] Balance reports have no unreviewed dominant strategy, useless power, impossible seed, or extreme outlier.
- [x] AI channel automation proves equal rules, policy separation, no stalls, commentary coverage, repeated switching, deterministic replay, and immediate same-seed challenge behavior.
- [x] Broadcast Tour validation finds no unreachable event, dependency cycle, impossible goal, duplicate reward, grind outlier, score-category contamination, or save-migration failure.
- [x] Every measured native module meets the current 90 percent line and 85 percent branch gates; 0.4 still raises the branch target to 90 percent.
- [ ] Release artifacts include checksums, a dependency inventory or SBOM, and provenance.

## Input, display, and accessibility

- [x] Keyboard, mouse, and controller bindings are action-based and remappable.
- [x] Automated controller connection, removal, deadzone, prompt-family, and deliberate-input switching contracts pass; physical-family review remains in the manual gate.
- [x] Focus loss pauses safely and never leaks buffered input.
- [x] Automated windowed, borderless, and fullscreen layouts preserve aspect ratio and pointer mapping across the required eight-case display matrix.
- [x] Required automated interactive flows retain visible, non-color focus state.
- [ ] Important text and UI meet documented contrast targets at every supported resolution.
- [x] Automated maximum-text-scale layouts leave every required action reachable.
- [x] Reduced-motion, zero-shake, flash-free, high-contrast, and color-independent cue contracts pass automated qualification.
- [x] Master, Music, SFX, and UI audio controls operate independently.
- [x] Every critical audio cue has a visual or textual counterpart.
- [x] One typed Vibe Level director is the only authority for presentation intensity; audio, HUD, background, particles, camera, and haptics do not infer competing levels.
- [x] Every death cause and recovery resource communicates through at least two automated channels that survive muted and reduced-motion play.

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
- [x] Hosted Windows, macOS, and Linux qualification produces the same deterministic rules state hash.
- [ ] Supported operating-system versions, renderer requirements, controller coverage, and known platform differences are published accurately.

## Release materials

- [ ] Version and changelog are updated.
- [ ] README install instructions match the artifact.
- [ ] Screenshots and trailer show current behavior.
- [ ] License, third-party notices, privacy statement, and support route are included.
- [ ] Accessibility features, supported platforms, controller support, offline behavior, and content sizes are disclosed accurately.
- [ ] Known issues are concise and player-facing.
- [x] Rollback, withdrawal, replacement, and hotfix procedures are documented; staged execution remains open.
- [ ] Published artifacts match the candidate revision, manifests, screenshots, checksums, and release notes.
- [ ] `materials-candidate` writes a passing, structurally complete `release-materials-handoff-v2` with `releaseAcceptance: false`; artifact-manifest reconciliation, marketing-claim approval, visible image review, and video playback review remain required.
- [ ] A retained `release-materials-acceptance-v1` decision closes those four gates for the exact revision, version, candidate, and artifact manifests; the structural handoff alone is insufficient.
- [ ] `rehearsal-record` validates all 33 staged platform-operation cells, exact retained files, protected-data preservation, withdrawal, and operational authority, then writes a same-revision `release-rehearsal-handoff-v2` accepted by the stable-promotion contract.
- [ ] Ten exact-kind upstream acceptance decisions bind the same stable revision and version under `stable_upstream_acceptance_v1.json`; nine review decisions bind the unsigned cohort, and signing binds that cohort as input plus the signed public artifacts, manifests, and provenance as output.
- [ ] `stable-record` validates the retained protected 1.0 rebuild, public installs, smoke results, preservation evidence, contract acknowledgements, and exact hashes, then writes `stable-promotion-handoff-v2` with `promotionComplete: true` and `releaseAcceptance: true`.
- [ ] Protected signing, tagging, upload, installation, and publication are executed externally by the named authorities; the validator only checks retained evidence and does not perform those operations.
- [ ] Private vulnerability reporting is enabled and its end-to-end report flow is tested.
- [ ] A concrete private conduct-reporting route is published and tested before contributions or official community spaces open.
- [x] Dependabot alerts and security updates are enabled; the GitHub API verified alerts plus unpaused automated security fixes on 2026-08-13, and every proposed fix still follows the project's review policy.
- [x] CodeQL default setup analyzes Actions, C#, and Python weekly; the first default-suite scan passed on 2026-08-13, and its one initial workflow alert was fixed rather than dismissed.
- [x] Repository Actions policy permits GitHub-owned actions only and requires immutable commit-SHA references; the committed workflow test independently rejects tags and unapproved actions.
- [x] The active default-branch ruleset requires the aggregate `CI complete` check and prevents force pushes and deletion. The repository-admin role retains an explicit always-bypass for the documented single-maintainer direct-to-main workflow; a bypassed revision cannot publish `player-latest` until the complete CI workflow succeeds.
- [ ] Repository description, topics, issue forms, support links, and security links resolve from a clean signed-out browser session.
- [ ] Secret scanning and push protection are enabled, and no `.env`, credential, signing key, private report, or player data is in source or artifacts.

## Final commands

```powershell
python -m pip_audit --strict --disable-pip --require-hashes --requirement requirements-ci.lock
python -m pip_audit --strict --disable-pip --require-hashes --requirement requirements-runtime.lock
python -m ruff format --check src tests scripts
python -m ruff check src tests scripts
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- source .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- all .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- plugin integrations/vibesnake-agent-plugin
python scripts/check_agent_interop.py
python scripts/generate_agent_knowledge.py --check
./scripts/package_agent_plugin.ps1 -OutputRoot TestResults/agent-plugin -Force
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- plugin TestResults/agent-plugin/portable/vibesnake-agent --require-mcp
./scripts/package_agent_host.ps1 -OutputRoot TestResults/agent-host -Force
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- screenshots .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- badges .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- logo .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- inventory-release .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- materials-candidate C:\retained-vibesnake-evidence\release-materials\candidate.json 0123456789abcdef0123456789abcdef01234567 C:\retained-vibesnake-evidence\release-materials\decision.json .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- rehearsal-record C:\retained-vibesnake-evidence\release-rehearsal\record.json 0123456789abcdef0123456789abcdef01234567 C:\retained-vibesnake-evidence\release-rehearsal\decision.json .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- stable-record C:\retained-vibesnake-evidence\stable-promotion\record.json 0123456789abcdef0123456789abcdef01234567 C:\retained-vibesnake-evidence\stable-promotion\decision.json .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- movement .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- core-rules .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- shield .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- phase-shift .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- last-stand .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- remaining-powers .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- achievement-candidates .
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
