# Development Guide

## Prerequisites

- Python 3.11, 3.12, 3.13, or 3.14. Python 3.14 is recommended.
- A desktop environment for visible playtesting.
- Git if the project is placed under version control.
- Optional external-service credentials only when running content-generation tools.
- For native qualification work, the stable .NET SDK 10.0.302 and Godot 4.7.1 .NET editor. Exact values live in [native/toolchain.json](../../native/toolchain.json).

Python 3.10 reaches end of life in October 2026, so the alpha no longer carries it toward 1.0. Python 3.15 remains a prerelease line and is outside the supported range until its final release and dependency matrix pass. The source reference uses Pygame Community Edition 2.5.7 or newer within major version 2 because it publishes CPython 3.11 through 3.14 wheels for the three development platforms. See the [official Python version status](https://devguide.python.org/versions/) and [pygame-ce package record](https://pypi.org/project/pygame-ce/).

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

## Run

```powershell
vibesnake
# or
python -m vibesnake
# or, if a local .venv exists
./play.ps1
```

| Command | Purpose |
| --- | --- |
| `vibesnake` / `vibesnake play` | Launch the Python alpha |
| `vibesnake update` | Fast-forward this checkout from GitHub `main` and reinstall |
| `vibesnake status` | Compare local HEAD to GitHub `main` without changing files |
| `vibesnake doctor` | Check Python, assets, and the offline radio library |
| `vibesnake version` | Print the installed package version |

Use `vibesnake update --dry-run` to inspect without writing. The editable install also registers the `vibesnake` console script.

## Set up the native toolchain

The repository resolver in [global.json](../../global.json) accepts the stable 10.0.302 SDK patch line, rejects previews, and prefers a repository-local `.dotnet/` installation. Install .NET 10.0.302 through Microsoft's official package instructions or a reviewed, integrity-verified installer. A system installation is valid; a repository-local installation under `.dotnet/` is also discovered automatically. Confirm the selected SDK with `dotnet --version` before qualification. Do not execute a mutable downloaded installer without an independent integrity check.

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

The import phase proves the editor can load and build the project. The scene smoke starts the real main scene, loads the rules and persistence assemblies, executes seeded movement and canonical restoration, validates every required logical input binding, exercises focus-loss pause safety and menu return, validates typed Shield feedback priority, and plays every finite fallback cue through the dummy audio backend. It also records a live terminal replay, saves and reloads it under an isolated user-data root, verifies read-only import and bounded compatibility feedback, exercises background latest-replay verification, proves that replay work gates new runs, queues terminal saves without loss, and gives quit one bounded save-completion window even when a task never returns. Warnings, leaked objects, missing replays, and incomplete atomic files fail the smoke before it prints `VIBESNAKE_GODOT_SMOKE_OK`. See [Replay System](../engineering/REPLAYS.md) for the complete contract.

Qualify an exported player separately:

```powershell
./scripts/test_native_export.ps1
```

This command installs the checksum-bound editor and matching export templates when needed, exports the current platform to a unique temporary directory outside the checkout, launches the packaged player headlessly, requires the deterministic smoke hash, rejects engine warnings and leaked objects, and writes schema 2 `artifact-manifest.json` evidence after inspecting and hashing the complete bundle. It refuses a non-empty output directory and any output beneath the repository. Use `-BuildMode Release` for a release-template qualification or `-OutputDirectory` for an explicit external destination.

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

The gameplay QA command runs the current Python reference adapter. Shared fixtures prove the implemented Python-to-C# rules scope, and the native export command proves the current platform's packaged player. Complete behavior parity and retained macOS and Linux evidence remain active 0.3 work in [TECHNOLOGY_STRATEGY.md](../decisions/TECHNOLOGY_STRATEGY.md).

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
