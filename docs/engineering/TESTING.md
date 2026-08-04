# Testing and Quality Gates

## Required local checks

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
python scripts/content_inventory.py --check
python -m vibesnake.qa.shared_traces --check
python -m vibesnake.qa.shared_rule_traces --check
python -m vibesnake.qa.shared_power_traces --check
python -m vibesnake.qa.shared_phase_shift_traces --check
python -m vibesnake.qa.shared_last_stand_traces --check
python -m vibesnake.qa.shared_remaining_power_traces --check
python -m vibesnake.qa --seeds 0 1 2 3 4 --steps 500 --output qa_reports/core.json
python -m pytest --cov=vibesnake --cov-report=term-missing --cov-report=xml
```

Native qualification checks:

```powershell
./scripts/test_native.ps1
```

This command verifies the editor bytes against the executable inside the pinned SHA-512 archive, then verifies its exact version, flavor, and official commit identity. It uses locked dependencies, builds with warnings as errors, verifies formatting and analyzers, enforces the C# line-coverage floor, imports the Godot project, and runs the real seeded scene smoke.

Packaged-player qualification for the current operating system:

```powershell
./scripts/test_native_export.ps1
```

This gate verifies the checksum-bound editor and official export template, rejects export warnings, launches outside the checkout, requires a deterministic smoke hash, inspects required platform payloads, rejects Python runtimes, environment files, and development content, scans project payloads for source-machine paths, and writes a schema 2 per-file manifest containing the archive and executable checksums.

The configured coverage floor is 80 percent line coverage across `vibesnake`. A run below the floor fails even if every assertion passes.

The staged quality expansion for artifact smoke tests, branch coverage, deterministic replay, accessibility, simulation, content validation, and release candidates is defined in the [roadmap quality ladder](../../ROADMAP.md#quality-ladder).

The automatic testing architecture, campaign policies, invariant catalog, balance reports, presentation checks, platform matrix, and boundary with human playtesting are in [AUTOMATED_QA.md](AUTOMATED_QA.md).

## Current baseline

As of 2026-08-01:

- 466 deterministic tests pass locally on Python 3.11, 3.12, 3.13, and 3.14; three radio integration cases skip when the optional radio pack is absent.
- Python line coverage is 87.16 percent on Python 3.14, above the enforced 80 percent gate that CI applies to every supported interpreter.
- 177 native C# contract tests pass on .NET 10. `VibeSnake.Rules` measures 91.73 percent line and 87.77 percent branch coverage; `VibeSnake.Persistence` measures 90.73 percent line and 84.48 percent branch coverage; aggregate native coverage is 91.55 percent line, 87.26 percent branch, and 97.53 percent method.
- One hundred shared movement traces compare 25,600 Python and C# steps; 35 targeted core fixtures cover command acceptance, queue overflow, food, growth, every current combo, speed, length, score ceiling, monotonic combo expiry, stable off-path food, normalized random-stream use and respawns, collision precedence, tail movement, wrapping, exact starvation, full-grid victory, and ordered events; 8 targeted Shield fixtures cover entry collection, pickup and active expiry, collision consumption and prevention, expiry precedence, starvation bypass, the simultaneous collision and starvation boundary, normalized state, and ordered power events.
- The Godot 4.7.1 project imports and completes seeded rules, canonical restoration, logical input, focus loss, audio buses, all finite PCM fallback cues, typed Shield feedback, live terminal replay recording, isolated atomic storage, exact reload, read-only import, bounded future-schema feedback, background latest-replay input, and clean-shutdown smoke paths on Windows. Any engine warning, leaked object, missing replay, or leftover temporary file fails qualification.
- A packaged Windows x64 debug player launches outside the checkout and reports state hash `643077d90db75e8c`.
- The Windows distribution contains 198 files totaling 189,615,786 bytes before its manifest and passes isolated replay storage, complete SHA-256 inventory, required Rules, Persistence, and Game payload, project-payload path, no-Python, no-export-lock, no-checkout-path, no-engine-warning, and no-leaked-object checks.
- Two independent Windows payloads passed the checksum-bound schema 2 inspector and produced the same manifest SHA-256, `bae7d6369d61c6a57f2fe295f0308c238acc6ccd1e057c20abffc880e8c2ae74`.
- Ruff and the anti-slop source policy pass across all active source, tests, scripts, native code, workflows, and canonical documentation.
- Every game-state renderer and menu has a headless smoke test.
- Keyboard, mouse, and simulated gamepad paths are covered.
- Save migration, corruption recovery, future-schema protection, and atomic-write failure are covered.
- The reference gameplay QA runner passes seeded policy campaigns, immediate trace replay, and property-generated input sequences.
- A parity mismatch writes a schema 1 JSON bundle with fixture, case, seed, shortest failing step prefix, normalized states and events, actual canonical state and hash, rules and runtime identity, and a one-command reproduction. CI uploads the bundle even though the test job failed.
- The deterministic content gate classifies and hashes the public inventory (114 files including 95 radio MP3s), performs bounded structural checks including decoded PNG scanlines and MPEG structure, reports one duplicate copy in one group, excludes development-only material, and keeps export eligibility at zero until pack quality gates pass.
- Schema 1 content-pack tests reject unknown fields, unsafe or colliding paths, stale bytes or hashes, incomplete approved allowlists, uncleared rights, mismatched credits, invalid semantic-version or ruleset ranges, bad station track lists, dependency errors, malformed optional packs, and optional failures that incorrectly block a valid offline core.
- Station badge checks render all eight icons with the pinned Pillow graph and project-owned pixel glyphs, then compare exact PNG bytes with the checked-in set.

The CI workflow declares Python 3.11, 3.12, 3.13, and 3.14. The complete suite passes locally on all four versions, but a hosted run cannot be confirmed until this workspace is connected to a Git repository and remote.

## Test layers

| Layer | Purpose | Location |
| --- | --- | --- |
| Unit | Pure scoring, movement, death telemetry, persistence, achievements, and individual power-up rules | `tests/core/`, `tests/powerups/` |
| Rendering | Headless menu, HUD, snake cosmetic, particle, and background execution | `tests/rendering/`, `tests/integration/` |
| Integration | Game construction, state dispatch, gameplay sequences, persistence boundaries | `tests/integration/`, selected root tests |
| Property and stateful | Generated values and action sequences with minimized reproductions | `tests/qa/`, expanding with the deterministic engine |
| Gameplay QA | Seeded automated policies, per-step invariants, trace replay, and machine-readable campaign reports | `src/vibesnake/qa/` |
| Native rules and replay | Engine-free movement, randomness, canonical serialization and restoration, generated action sequences, state hash, input, food, collision, starvation, Shield lifecycle and recovery, live replay mirroring, compatibility, deterministic verification-work accounting, and determinism contracts | `native/tests/VibeSnake.Rules.Tests/` |
| Native persistence | Strict encoding, bounded reads, source-preserving import, traversal rejection, sequential and concurrent idempotence, cross-process lock contention, atomic conflict behavior, concurrent file and byte capacity, I/O results, post-load verification, spaces, and non-ASCII paths | `native/tests/VibeSnake.Rules.Tests/ReplayStoreTests.cs` |
| Godot integration | Real engine import, C# assembly loading, scene startup, logical input, focus lifecycle, fallback audio, typed Shield feedback, deterministic continuation, isolated replay recording and storage, bounded background verification, lossless terminal-save queuing, replay-work run gating, save-aware quit, bounded import feedback, and clean process exit | `game/` and CI `godot-smoke` jobs |
| Native artifact | Checksum-verified export, outside-checkout packaged launch, bundle inventory, prohibited-content checks, and per-file hashes | `scripts/test_native_export.ps1`, `scripts/inspect_native_artifact.ps1`, and CI `godot-smoke` jobs |
| Source content | Exact classification, media integrity, SHA-256 inventory, duplicate detection, rights status, and export eligibility | `config/content_policy.json`, `config/content_inventory.json`, and `scripts/content_inventory.py` |
| Content packs | Exact approved allowlists, canonical manifests, compatibility, dependencies, rights-derived credits, station tracks, and isolated optional rejection | `src/vibesnake/content/packs.py`, `scripts/content_packs.py`, and `tests/qa/test_content_packs.py` |
| Manual | Visible gameplay, audio judgment, and other irreducibly perceptual checks | `scripts/manual/` |

## Deterministic collection boundary

`tests/` contains only deterministic automated tests. Broken legacy validators that duplicated current coverage or imported removed APIs have been deleted. Genuinely perceptual tools live under `scripts/manual/` with explicit entry points and no import-time side effects.

Pytest uses `testpaths = ["tests"]`, so manual tools and retired scripts cannot be collected accidentally.

## Isolation

The test session sets:

- `SDL_VIDEODRIVER=dummy`
- `SDL_AUDIODRIVER=dummy`
- `PYGAME_HIDE_SUPPORT_PROMPT=1`
- `VIBESNAKE_DATA_DIR` to a temporary directory

JSON save files are cleared before each test, and the temporary directory is removed after the session. Tests must not write to the player's `data/` directory.

## Coverage policy

Coverage is a floor, not the objective. Prefer tests that verify player-visible contracts and cross-module behavior. In particular:

- A power-up needs a `Game.update` integration test.
- Persistence changes need restart, migration, corruption, and failed-write tests.
- Rendering needs representative branch coverage plus visible review.
- Audio needs control-flow tests plus listening review.
- AI needs fixed-scenario behavior tests plus seeded tournament metrics.

Do not exclude difficult runtime modules merely to raise the percentage.

Automatic gameplay QA must not be described as proof of fun. It finds correctness defects, divergence, balance outliers, and reproducible stress cases. Human tests still own comprehension, feel, tension, delight, fatigue, aesthetics, and replay desire.

## CI

[.github/workflows/ci.yml](../../.github/workflows/ci.yml) runs the Python quality matrix, retains seeded QA evidence, builds and tests the pure C# rules on Windows, macOS, and Linux, and runs the real Godot headlessly on all three systems. Each native runner also installs a checksum-verified platform template, exports its packaged player, launches it outside the checkout, inspects and hashes the artifact, and uploads its manifest. Full qualified player bundles are uploaded for tagged and manually dispatched runs. The native jobs use locked NuGet dependencies, formatting checks, warnings as errors, an 80 percent C# coverage floor, and checksum-verified engine archives.

Hosted execution is still unverified because this workspace is not connected to a Git repository. The next CI expansion is broader Python-to-C# differential evidence, screenshots, content allowlists, dependency inventories, and provenance. The defined artifact matrix must run successfully from a real remote before macOS or Linux support is claimed.

## Manual release checks

Automated SDL dummy rendering cannot verify appearance, font fallback, music balance, controller feel, or fullscreen behavior. Use [RELEASE_CHECKLIST.md](../release/RELEASE_CHECKLIST.md) for the human pass.
