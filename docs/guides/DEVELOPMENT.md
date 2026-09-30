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

## Source builds

The current floating source build and checksums are published under [player-latest](https://github.com/blisspixel/VibeSnake/releases/tag/player-latest) from the newest successfully qualified `main` push. A newer qualified revision may cancel a superseded publisher before it finishes. The release contains the source archive, Python reference wheel and sdist, and `SHA256SUMS.txt`. The source archive includes development previews such as the Agent Plugin manifest and skill, the MCP host source and packaging script, and the generated Open Knowledge Format bundle. It is not a signed native player or a preassembled supported Agent Plugin. Versioned native alpha releases have a separate fail-closed pipeline, and the first tag remains blocked on approved packaged content and an exact artifact review. See [native release outputs](../release/PACKAGING.md) and [agent play integration](../engineering/AGENT_PLAY.md).

An opt-in System One watch can ask Jev, or a local compatible endpoint, for one legal action per step and print a factual ticker. It is not one of the ten offline Let's Play personalities. The command and route contract are in [Jev watch](../engineering/JEV_PLAY.md).

## Repository qualification

Python reference and cross-runtime checks remain temporarily in CI while their remaining validators move to .NET. Documentation, product-version, source-policy, candidate-freeze, dependency-lock, project-logo, Agent Knowledge, offline agent-interoperability baseline and contract-digest validation, station-badge, content-inventory, README screenshot capture and freshness, release-material, release-rehearsal, stable-promotion, achievement-candidate fixture generation and freshness, Last Stand fixture generation and freshness, Phase Shift fixture generation and freshness, Shield fixture generation and freshness, Remaining Powers fixture generation and freshness, Core Rules fixture generation and freshness, Movement fixture generation and freshness, and Agent Plugin qualification now run through native `RepositoryChecks`.

The release-material routes validate the closed V090-09 foundation and can write either a pending foundation handoff or an exact candidate-bound structural decision. A passing candidate route sets `candidateMaterialComplete: true` while keeping `releaseAcceptance: false` pending artifact-manifest reconciliation, marketing-claim approval, visible image review, and video playback review. The rehearsal routes validate the V090-10 foundation or an externally executed, retained three-platform record. They require a separately accepted material decision and write `release-rehearsal-handoff-v2` with the exact revision stable promotion consumes. Stable promotion uses a separate checked-in exact-kind authority for ten upstream decisions and rejects a generic favorable JSON object. Nine review decisions bind the unsigned artifact and manifest cohort. Platform signing binds that cohort as its input and the byte-changing signed public artifacts, manifests, and provenance as its result. Optional content is cross-bound separately. The routes write `stable-promotion-handoff-v2`, with protected execution and release acceptance explicitly pending in the foundation. The validator never performs review, signing, tagging, installation, upload, or publication.

Achievement-candidate, Last Stand, Phase Shift, Shield, Remaining Powers, Core Rules, and Movement tooling render 167 frozen reviewed Python-origin vectors without executing native rules. They reproduce their exact 2,682-byte, 3,596-byte, 3,534-byte, 4,489-byte, 9,548-byte, 57,031-byte, and 999,087-byte canonical LF identities, and retain separate live native parity consumers. The six small renderers keep their shared 65,536-byte fixed-fixture boundary. Movement uses a separate 1,000,000-byte lifecycle with the same stable-read, safe-path, atomic-replacement, cleanup, and exact self-verification guarantees. Agent Knowledge applies the hardened fixed-path lifecycle to five exact Open Knowledge Format 0.2 concepts and derives the current frame v9 and survival v1 contracts from canonical source. Agent interoperability uses the same strict bounded-input and atomic replacement contracts for review dates, canonical source alignment, documentation pins, SemVer history, and exact host and plugin digests. The scheduled upstream probe is native `interop-upstream`, outside ordinary CI, and network-only.

The screenshot route stages Godot output outside the committed evidence set, performs full PNG and canonical-manifest validation, and replaces the manifest last so interrupted multi-file capture fails closed. Ruff retains full Python syntax ownership while the frozen oracle exists. The remaining Python tools are explicit migration work. They are test-only scaffolding, not a second product path. Their CI and package-tool graph is audited. Agent Host package validation now runs through native `host-package`, outside `all`, because it qualifies an assembled package directory. Manual-product and external-validation handoffs now run natively inside `all`. Release-matrix qualification and unsigned-preview assembly now run natively through `release-matrix` and `unsigned-preview`, both outside `all`. Publication eligibility stays false. Content-pack qualification and approved-radio assembly now run natively through `content-packs` and `radio-pack`, both outside `all`. They call the existing persistence pack APIs, require an already-approved curation record before writing a pack, and do not approve radio content or change export eligibility. Exact-candidate workspace preparation is native `product-review-prepare`, outside `all` and ordinary CI. It writes pending session templates and cannot set release acceptance or publication eligibility. Hash-bound radio listening records are native `radio-listening` outside `all` and ordinary CI and cannot change release approval, export eligibility, curation, or source bytes. The remaining manual operator scripts are the FFmpeg analysis, review-copy preparation, and sample preview tools. Scheduled upstream integrity uses native `interop-upstream` outside `all` and ordinary CI. The frozen oracle stays until equivalent native gates exist. The ordered removal gates are in the [migration map](../engineering/MIGRATION_MAP.md#repository-wide-python-retirement).

## Local quality loop

```powershell
python -m pip_audit --strict --disable-pip --require-hashes --requirement requirements-ci.lock
python -m pip_audit --strict --disable-pip --require-hashes --requirement requirements-runtime.lock
python -m ruff format --check src tests scripts
python -m ruff check src tests scripts
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- source .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- all .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- plugin integrations/vibesnake-agent-plugin
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- knowledge .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- interop .
./scripts/package_agent_plugin.ps1 -OutputRoot TestResults/agent-plugin -Force
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- plugin TestResults/agent-plugin/portable/vibesnake-agent --require-mcp
./scripts/package_agent_host.ps1 -OutputRoot TestResults/agent-host -Force
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- screenshots .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- badges .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- logo .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- inventory .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- materials .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- rehearsal .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- movement .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- core-rules .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- shield .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- phase-shift .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- last-stand .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- remaining-powers .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- achievement-candidates .
python -m vibesnake.qa --seeds 0 1 2 3 4 --steps 500 --output qa_reports/core.json
python -m pytest --cov=vibesnake --cov-report=term-missing --cov-report=xml
```

The `achievement-candidates` route compares the checked fixture with a closed native rendering of the four reviewed Python-origin vectors. It does not execute C# rules, so `SharedAchievementCandidateTraceParityTests` remains an independent live consumer. Use `achievement-candidates-write [repository-root]` only to restore the fixed fixture's exact canonical LF bytes after review.

The `last-stand` route applies the same independence rule to five reviewed Python-origin recovery vectors. It reproduces the fixed 3,596-byte corpus without executing `SnakeRun`; `SharedLastStandTraceParityTests` remains the live behavior consumer. Use `last-stand-write [repository-root]` only to restore those exact reviewed bytes.

The `phase-shift` route reproduces six reviewed Python-origin effect vectors as the fixed 3,534-byte corpus without executing `SnakeRun`; `SharedPhaseShiftTraceParityTests` remains the live behavior consumer. Use `phase-shift-write [repository-root]` only to restore those exact reviewed bytes.

The `shield` route reproduces eight reviewed Python-origin protection vectors as the fixed 4,489-byte corpus without executing `SnakeRun`; `SharedShieldTraceParityTests` remains the live behavior consumer. Use `shield-write [repository-root]` only to restore those exact reviewed bytes.

The `remaining-powers` route reproduces nine reviewed Python-origin Slow-Mo, Boost, Magnet, Bait, Gluttony, and Segment Detach vectors as the fixed 9,548-byte corpus without executing `SnakeRun`; `SharedRemainingPowerTraceParityTests` remains the live behavior consumer. Use `remaining-powers-write [repository-root]` only to restore those exact reviewed bytes. All five fixture routes share bounded generation and reads, symbolic/reparse-point and portable-case-alias rejection, pre-mutation sibling reservation, flushed same-directory replacement, immediate fixed-path revalidation, cleanup, and exact post-write verification.

The read-only `materials [repository-root]` route validates the closed V090-09 contract and current ten-document foundation. Use `materials-write <output> [repository-root]` to write the canonical pending `release-materials-handoff-v2` CI evidence with candidate completion and release acceptance both false. Only retained candidate workspaces use `materials-candidate <candidate> <expected-revision> <output> [repository-root]`. A passing candidate route sets `candidateMaterialComplete: true` but keeps `releaseAcceptance: false` pending artifact-manifest reconciliation, marketing-claim approval, visible image review, and video playback review. It is structural evidence, not an accepted rehearsal or promotion input.

The read-only `rehearsal [repository-root]` route validates the exact V090-10 protocol foundation. Use `rehearsal-write <output> [repository-root]` for the canonical pending `release-rehearsal-handoff-v2` CI evidence. Only retained staged workspaces use `rehearsal-record <record> <expected-revision> <output> [repository-root]`. The record route requires a separate `release-materials-acceptance-v1` decision for the same revision, version, candidate, and three artifact manifests, plus all 33 passing platform-operation cells, withdrawal evidence, protected-data preservation, and four verified operational roles. It validates retained evidence and never performs an install, approval, signing action, withdrawal, or publication.

The suite configures dummy SDL drivers and temporary save storage through [tests/conftest.py](../../tests/conftest.py).

`requirements.txt`, `requirements-runtime.txt`, and `requirements-dev.txt` are
human-edited constraint inputs. `requirements-runtime.lock` is the player and
local build graph for Python 3.11 through 3.14, and `requirements-ci.lock` is the
development and CI graph for the same range. Both locks use exact versions and
SHA-256 hashes. After an intentional input change, regenerate and recheck the
affected profile with:

```powershell
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- lock-write ci .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- lock-write runtime .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- locks .
```

Regeneration requires exactly `uv 0.11.33`; ordinary installation and CI do
not. The native writer prefers the checkout `.venv` resolver and refuses an
incompatible version instead of silently falling back. Each lock header records
that resolver version. Both freshness identities
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
