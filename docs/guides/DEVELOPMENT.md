# Development Guide

## Prerequisites

- Python 3.11, 3.12, 3.13, or 3.14 for oracle, fixture, documentation, and quality tooling. Python 3.14 is recommended.
- A desktop environment for visible playtesting.
- Git if the project is placed under version control.
- Optional external-service credentials only when running content-generation tools.
- For native qualification work, the stable .NET SDK 10.0.303 and Godot 4.7.1 .NET editor. Exact values live in [native/toolchain.json](../../native/toolchain.json).

Python 3.10 reaches end of life in October 2026, so the alpha no longer carries it toward 1.0. Python 3.15 remains a prerelease line and is outside the supported range until its final release and dependency matrix pass. The source reference uses Pygame Community Edition 2.5.8 within major version 2 because it publishes CPython 3.11 through 3.14 wheels for the three development platforms. See the [official Python version status](https://devguide.python.org/versions/) and [pygame-ce package record](https://pypi.org/project/pygame-ce/).

## Set up on Windows

```powershell
git clone https://github.com/blisspixel/VibeSnake.git
cd VibeSnake
py -3.14 -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --require-hashes --only-binary=:all: -r requirements-ci.lock
python -m pip install --no-deps --no-build-isolation -e .
```

## Set up on macOS or Linux

```bash
git clone https://github.com/blisspixel/VibeSnake.git
cd VibeSnake
python3.14 -m venv .venv
source .venv/bin/activate
python -m pip install --require-hashes --only-binary=:all: -r requirements-ci.lock
python -m pip install --no-deps --no-build-isolation -e .
```

## Run the native game

```powershell
./play.ps1
```

On macOS or Linux:

```bash
./play.sh
```

Both launchers call the same PowerShell 7 path. They install and verify the pinned Godot editor when needed, build `game/VibeSnake.Game.sln`, and launch `game/project.godot`. The first run downloads the platform editor archive. Later runs reuse the verified repository-local cache.

To finish the current Agent Arena preview slice from Windows `cmd.exe` without first fixing a global-tool PowerShell, run:

```bat
close-agent-preview.cmd
```

That sets `DOTNET_ROOT` to the repository `.dotnet` SDK, patches public-contract digests, regenerates knowledge, checks interop and docs, and runs the focused Agent Arena native tests. Pass `--commit` to create a local commit after those gates pass. It does not push.

The editable Python install still registers `vibesnake`, `vibesnake status`, `vibesnake update`, `vibesnake doctor`, and `vibesnake version` for frozen-oracle and migration work. It is not the default product launcher.

## Set up the native toolchain

The repository resolver in [global.json](../../global.json) requires the exact stable 10.0.303 SDK, rejects previews and other patches, and prefers a repository-local `.dotnet/` installation. Install .NET 10.0.303 through Microsoft's official package instructions or a reviewed, integrity-verified installer. A system installation is valid; a repository-local installation under `.dotnet/` is also discovered automatically. Confirm that `dotnet --version` prints `10.0.303` before qualification. Do not execute a mutable downloaded installer without an independent integrity check.

Install the checksum-verified Godot build on Windows, macOS, or Linux with PowerShell 7:

```powershell
./scripts/install_godot.ps1
```

The script reads the exact archive and SHA-512 from [native/toolchain.json](../../native/toolchain.json), places the developer cache under `.tools/godot/`, compares the extracted executable bytes with the executable inside that verified archive, verifies `godot --version`, and reports the executable path. Both `.dotnet/` and `.tools/` are ignored build prerequisites, not repository content.

Install the matching .NET export templates for the current platform with:

```powershell
./scripts/install_godot_templates.ps1
```

The template installer downloads the official combined archive, verifies its pinned SHA-512, and installs only the current platform's required files under Godot's versioned user template directory. The official archive is approximately 1.2 GB; the temporary download is removed after verification and selective extraction. Pass `-ArchivePath` to reuse an already downloaded verified archive.

## Native quality loop

```powershell
./scripts/test_native.ps1
```

The script performs the checksum-bound editor check, PowerShell gate regressions, locked restore, release build, format and analyzer check, native tests with coverage enforcement, Godot import, and deterministic scene smoke. An existing extraction can be supplied only with the checksum-pinned archive that proves its bytes:

```powershell
./scripts/test_native.ps1 -GodotExecutable "C:\path\to\Godot_console.exe" -GodotArchivePath "C:\path\to\Godot_verified.zip"
```

The audio evidence file is schema 2 with kind `audio-mixing-policy-v2`. It covers all 31 cues and 992 rapid retriggers, plus playback-free cooldown/polyphony/priority/interruption policy, bounded SFX/UI voices, real bus routing and music duck/restore, immediate isolated saved volumes, and output-topology polling and repair. `sfx-catalog-qualification-v1` additionally requires unique PCM fingerprints and runtime IDs, exact provenance/license declarations, measured peak bounds, no clipping, and distinct navigation/combo/restart/achievement/death/power identities.

The scene smoke also writes `TestResults/native/multimodal_feedback.json`. Its `multimodal-feedback-v1` contract requires four starvation time/text/shape/color phases, shared score and combo emphasis with a static reduced-motion fallback, nine unique power icons/names/states/activation cues, explicit pre-consumption protection language, distinct collision/starvation text and geometry, five muted/accessibility profiles, and an unchanged rules hash.

The same smoke requires `visual-hierarchy-qualification-v1`, `performance-qualification-v1`, `candidate-accessibility-audit-v1`, `vibe-level-qualification-v1`, and `broadcast-qualification-v1` evidence. Together they lock presentation capacity and priority, five deterministic review frames, minimum/default/maximum-safe frame statistics, twelve SHA-256-bound accessibility areas, 150 percent text across eight display classes, all Vibe Level states and transitions, eight planned station identities, four safe host boundaries, no-repeat radio and host bags, critical-cue priority, caption fallback, fatigue caps, and presentation/RNG/rules isolation. Named-hardware performance, retained accessibility review, approved broadcast audio, and listening review remain separate human gates.

`mode-contract-qualification-v2` locks the two product-mode identities, feature sets, effective score categories, board, pause, seed, restart, Classic minimal mechanics, Vibe pressure mechanics, deterministic hashes, DDA opt-out isolation, cross-mode score isolation, and remappable keyboard/controller selection routes. `adaptive-fairness-qualification-v1` locks default-on `vibe-bounded-hunger-v1`, the zero-to-two-tick drain bound, Support/Standard/Pressure behavior, closed inputs, replay safety, preference round-trip, explicit score metadata, Vibe-only achievement eligibility, and all three score categories.

The import phase proves the editor can load and build the project. The scene smoke starts the real main scene, loads the rules and persistence assemblies, executes seeded movement and canonical restoration, validates keyboard/controller remap capture, conflict swap/cancel, InputMap application, focus-loss and last-controller-disconnect pause safety, the shell transition graph, and typed power feedback priority.

Required input and settings evidence includes `input_cadence.json` for exactly-once keyboard, D-pad, and stick turns under three render schedules, plus `settings_screen.json` for 6 sections, 34 described rows, raw keyboard/controller routes, Vibe adaptation opt-out/category isolation, default-off local playtest consent, bounded stick deadzone, digital D-pad fallback, Master mono downmix, reset safety, schema-7 atomic persistence, and recoverable save failure. `local_playtest_summaries.json` covers consent round-trip, exact balance-only fields, local export, deletion cancel/confirm, retention, and upload absence. `mode_contracts.json` and `adaptive_fairness.json` cover the mode and DDA contracts described above.

The smoke also requires onboarding, run-end, player-data recovery, bare-loop, accessibility, viewport, shell-presentation, audio-fallback, spectator-experience, and core-only optional-pack evidence. It records and reloads a live terminal replay in isolated user data, verifies read-only import and compatibility feedback, exercises bounded replay browse/playback, clean-capture, and equal-rules spectator controls through raw keyboard/controller routes, verifies privacy-safe atomic run-summary and local-league persistence, gates new runs during replay work, preserves queued terminal saves, and gives quit a bounded save-completion window. Warnings, leaked objects, missing evidence, missing replays, and incomplete atomic files fail the smoke before it prints `VIBESNAKE_GODOT_SMOKE_OK`. See [Replay System](../engineering/REPLAYS.md) for the complete contract.

Before Godot import, native qualification writes `TestResults/native/dependency_inventory.json` from every committed NuGet and Python lock. The gate verifies its package uniqueness, source-lock references and hashes, combined digest, Git revision, runtime ID, and pinned Godot/.NET versions. Hosted platform jobs retain the file with the other qualification JSON.

Qualify an exported player separately:

```powershell
./scripts/test_native_export.ps1
```

This command installs the checksum-bound editor and matching export templates when needed, exports the current platform to a unique temporary directory outside the checkout, stages install, fresh user-data, and log paths containing spaces and non-ASCII characters, makes the installed player read-only, rejects an adjacent write probe, and launches the packaged player headlessly with isolated user data and logs outside the install. It requires the deterministic smoke hash, rejects engine warnings and leaked objects, proves the complete installed-file digest is unchanged, validates candidate long-simulation, spectator-restart, seven-fault, crash-triage, and divergence-triage evidence, writes `artifact-read-only-install-v1`, and writes schema 3 `artifact-manifest.json` after inspecting and hashing the complete bundle. Release mode additionally proves that the compiled supported player excludes Agent Arena runtime payloads and entry-point markers. It refuses a non-empty output directory and any output beneath the repository. Use `-BuildMode Release` for a release-template qualification or `-OutputDirectory` for an explicit external destination.

## Local quality loop

```powershell
python scripts/lock_python_dependencies.py
python scripts/lock_python_dependencies.py --profile runtime
python -m pip_audit --strict --disable-pip --require-hashes --requirement requirements-ci.lock
python -m pip_audit --strict --disable-pip --require-hashes --requirement requirements-runtime.lock
python -m ruff format --check src tests scripts
python -m ruff check src tests scripts
python scripts/check_source_policy.py
python scripts/check_docs.py
python scripts/check_product_version.py
python scripts/check_candidate_freeze.py
python scripts/validate_agent_plugin.py integrations/vibesnake-agent-plugin
python scripts/check_agent_interop.py
python scripts/generate_agent_knowledge.py --check
./scripts/package_agent_plugin.ps1 -OutputRoot TestResults/agent-plugin -Force
python scripts/validate_agent_plugin.py TestResults/agent-plugin/portable/vibesnake-agent --require-mcp
./scripts/package_agent_host.ps1 -OutputRoot TestResults/agent-host -Force
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
python -m vibesnake.qa.shared_achievement_candidate_traces --check
python -m vibesnake.qa --seeds 0 1 2 3 4 --steps 500 --output qa_reports/core.json
python -m pytest --cov=vibesnake --cov-report=term-missing --cov-report=xml
```

The suite configures dummy SDL drivers and temporary save storage through [tests/conftest.py](../../tests/conftest.py).

`requirements.txt`, `requirements-runtime.txt`, and `requirements-dev.txt` are
human-edited constraint inputs. `requirements-runtime.lock` is the player and
local build graph for Python 3.11 through 3.14, and `requirements-ci.lock` is the
development and CI graph for the same range. Both locks use exact versions and
SHA-256 hashes. After an intentional input change, regenerate and recheck the
affected profile with:

```powershell
python scripts/lock_python_dependencies.py --write
python scripts/lock_python_dependencies.py
python scripts/lock_python_dependencies.py --profile runtime --write
python scripts/lock_python_dependencies.py --profile runtime
```

Regeneration requires exactly `uv 0.11.33`; ordinary installation and CI do
not. Each lock header records that resolver version. Both freshness identities
include `pyproject.toml` plus their ordered requirement inputs. A stale
resolution or changed build contract therefore fails before tests. Local Git
checkouts can install the repository-owned hooks with `pre-commit install`; CI
invokes the same commands directly.

The gameplay QA command runs the frozen Python reference adapter. Shared fixtures prove the implemented Python-to-C# rules scope, and the native export command proves the current platform's packaged player. The native Godot build is the default source player; Python remains in the tree for oracle reproduction and migration evidence through 1.0. Remaining platform, physical-device, content, and human acceptance work is tracked in [TECHNOLOGY_STRATEGY.md](../decisions/TECHNOLOGY_STRATEGY.md) and the [roadmap](../../ROADMAP.md).

## Project conventions

The complete engineering contract is in [CODE_QUALITY_STANDARDS.md](../engineering/CODE_QUALITY_STANDARDS.md). The rules below are the daily working subset.

- Keep gameplay behavior in testable model or service boundaries.
- Route new state transitions through `Game.transition_to` where possible.
- Avoid importing external services from runtime modules.
- Use `VIBESNAKE_DATA_DIR` in tests that touch persistence.
- Treat assets as dependencies with size, origin, license, and owner metadata.
- Change asset classifications in `config/content_policy.json`, regenerate `config/content_inventory.json`, and never approve unresolved rights.
- Update canonical docs when behavior, status, commands, counts, or support policy changes.
- Do not use archived documents as specifications.

## Adding a feature

1. Write the observable player contract in the relevant canonical document.
2. Add a focused model test for pure logic.
3. Add an integration test through `Game` if the feature changes a run.
4. Implement the smallest cross-module change that satisfies the contract.
5. Exercise the feature visibly when rendering, audio, or controls change.
6. Run the full local quality loop.
7. Update [STATUS.md](../release/STATUS.md), [ROADMAP.md](../../ROADMAP.md), and [CHANGELOG.md](../../CHANGELOG.md) as appropriate.

## Adding a power-up

Follow the completion contract in [POWERUPS.md](../design/POWERUPS.md). Register the type in `POWERUP_TYPES`, define its design category, connect its effect to core rule resolution, and test both activation and restoration. A subclass that only sets an unused flag is not complete.

## Adding an AI personality

Use [AI_PLAYERS.md](../design/AI_PLAYERS.md) and place loadable JSON under `assets/ai/custom/`. Validate behavior across fixed scenarios before describing a personality as safer, faster, or more effective.

## Audio production

The game does not need external APIs to play. Legacy credentialed generation,
grading, renaming, and normalization tools are preserved only in the ignored
local archive because they do not meet the current safety, reproducibility, and
quality contract. Do not use them as release tooling. The roadmap requires one
audited admission pipeline with explicit execution, cost limits, pinned media
tools, immutable source preservation, and machine-readable provenance before a
candidate can enter public source.

Read [AUDIO.md](../content/AUDIO.md) before generating, renaming, grading, or
deleting tracks. Never place API keys in source files, documentation,
inventories, or generated reports.

## Packaging caution

Editable installs work because runtime code can find the checkout's `assets/` directory. A normal wheel is not yet a supported distribution. Do not publish an artifact until the [release checklist](../release/RELEASE_CHECKLIST.md) is complete.
