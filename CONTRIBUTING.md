# Contributing

Vibe Snake is in alpha. Changes should improve the playable experience while keeping source behavior, documentation, and tests aligned.

External contribution intake is currently closed until the repository has tested private security and conduct-reporting routes. Public source review is welcome, but maintainers must not accept pull requests or operate official community spaces before both confidential channels are available and documented. Keep the GitHub pull-request feature disabled while this policy is in force.

## Before changing code

1. Read the [status](docs/release/STATUS.md) and [roadmap](ROADMAP.md).
2. Use Python 3.11 through 3.14 in a virtual environment.
3. Install the hash-locked development graph from `requirements-ci.lock`, then install the project in editable mode without resolving a second graph.
4. Check the relevant subsystem guide in the [documentation hub](docs/README.md).
5. Apply the [code quality standards](docs/engineering/CODE_QUALITY_STANDARDS.md) to the change and its evidence.

## Definition of done

A change is complete when:

- Its behavior is covered by deterministic tests.
- The full suite retains at least 80 percent line coverage.
- The dependency-lock checks confirm the CI and runtime graphs match their requirement inputs.
- `python -m ruff format --check src tests scripts` passes.
- `python -m ruff check src tests scripts` passes.
- `dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- all .` passes for documentation, product-version, source, candidate-freeze, dependency-lock, project-logo, station-badge, content-inventory, README screenshots, release-material foundation, release-rehearsal foundation, stable-promotion foundation, exact achievement-candidate, Last Stand, and Phase Shift fixture freshness, and Agent Plugin policy.
- The source and assembled Agent Plugin, interoperability baseline, and generated OKF bundle pass their drift and containment gates.
- `dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- screenshots .` passes.
- `dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- badges .` passes.
- `dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- logo .` passes.
- `./scripts/test_native.ps1` passes.
- User-facing behavior and known limitations are reflected in the canonical docs.
- Save-data changes include a compatibility or migration decision.
- New assets have a documented license, source, size, and runtime ownership path.

Run the local CI equivalent before handing off a change:

```powershell
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- locks .
python -m pip_audit --strict --disable-pip --require-hashes --requirement requirements-ci.lock
python -m pip_audit --strict --disable-pip --require-hashes --requirement requirements-runtime.lock
python -m ruff format --check src tests scripts
python -m ruff check src tests scripts
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- all .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- plugin integrations/vibesnake-agent-plugin
python scripts/check_agent_interop.py
python scripts/generate_agent_knowledge.py --check
./scripts/package_agent_plugin.ps1 -OutputRoot TestResults/agent-plugin -Force
./scripts/package_agent_host.ps1 -OutputRoot TestResults/agent-host -Force
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- plugin TestResults/agent-plugin/portable/vibesnake-agent --require-mcp
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- screenshots .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- badges .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- logo .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- inventory .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- materials .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- rehearsal .
python -m vibesnake.qa.shared_traces --check
python -m vibesnake.qa.shared_rule_traces --check
python -m vibesnake.qa.shared_power_traces --check
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- phase-shift .
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- last-stand .
python -m vibesnake.qa.shared_remaining_power_traces --check
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- achievement-candidates .
python -m vibesnake.qa --seeds 0 1 2 3 4 --steps 500 --output qa_reports/core.json
python -m pytest --cov=vibesnake --cov-report=term-missing --cov-report=xml
./scripts/test_native.ps1
```

Changes to native code, the Godot shell, export presets, artifact inspection, or
runtime packaging must also qualify the packaged player for the current operating
system:

```powershell
./scripts/test_native_export.ps1
```

Windows, macOS, and Linux artifact evidence remains a hosted release gate even
when the current-platform export passes locally.

## Test boundaries

The default suite is deterministic and does not call paid APIs or require a visible display. Manual and external-service validation programs are listed in the [testing guide](docs/engineering/TESTING.md). Do not silently move network-dependent checks into the default suite.

## Documentation policy

Markdown anywhere under `docs/` is canonical except the source-pointer policy under `docs/research/`. Raw research, superseded plans, and historical reports remain in the ignored local archive and must not be cited as current status or added to a public commit.

When behavior and documentation disagree, update both in the same change. Historical documents stay unchanged unless their organization or archive labeling is being corrected.
