# Project Tools

This directory contains explicit command-line tools. Runtime code belongs under `src/` or `game/`, deterministic tests belong under `tests/` or `native/tests/`, and generated outputs belong under ignored artifact directories.

## Required quality and release gates

| Tool | Ownership |
| --- | --- |
| `check_source_policy.py` | Executable anti-slop, Unicode, Python placeholder, and signing-material exclusion policy |
| `lock_python_dependencies.py` | CI and player-runtime hash-lock freshness checks and explicit regeneration |
| `check_docs.py` | Canonical documentation discovery and relative-link validation |
| `check_product_version.py` | Align canonical `VERSION`, native `ProductIdentity.AppVersion`, and the equivalent PEP 440 package version |
| `check_agent_interop.py` | Validate the closed machine-readable MCP, Agent Plugins, Agent Skill, MCP Apps, and OKF baseline, canonical UTC lifecycle metadata, absolute review dates, source alignment, version-bound public-contract digests, documentation pins, and optional read-only integrity of pinned Agent Plugins specification and schema bytes |
| `generate_agent_knowledge.py` | Deterministically render and freshness-check the Open Knowledge Format 0.2 bundle from canonical sources and the interoperability baseline |
| `close_agent_preview.py` | One-command Agent Arena preview close-out: patch public-contract digests, regenerate knowledge, check interop and docs, run focused native tests. Invoked by root `close-agent-preview.cmd` so cmd.exe can set the repo SDK before any .NET global tool starts. |
| `validate_agent_plugin.py` | Enforce Vibe Snake's intentionally narrow Agent Plugins stdio producer and containment profile, exact packaged launch declaration, required components, and complete checksums |
| `capture_readme_screenshots.py` | Isolated current-build README screenshot capture and freshness verification |
| `visual_generate_badges.py` | Deterministic radio-station badge generation and byte verification |
| `visual_generate_logo.py` | Preferred brand-logo hash and dimension verification |
| `content_inventory.py` | Deterministic source-content inventory and release blocker report |
| `content_packs.py` | Core and optional pack manifest qualification |
| `assemble_radio_pack.py` | Deterministic, fail-closed assembly of one approved optional radio pack with manifest, checksums, and curation evidence |
| `assert_godot_toolchain.ps1` | SHA-512 archive, extracted editor SHA-256, and exact build identity verification |
| `native_artifact_policy.ps1` | Shared prohibited-path rules for native bundles |
| `platform_path_policy.ps1` | Absolute environment-path validation for tooling |
| `test_powershell_gates.ps1` | Executable-spoofing, artifact-path, and ordinary-CI credential-boundary regression checks |
| `test_native.ps1` | Native rules, coverage, balance, reliability, fault/triage, localization, and capture-sharing evidence, format, Godot import, and scene smoke |
| `test_native_coverage.ps1` | Shared local/CI native test and coverage gate with validated module floors and one clean rebuild retry for truncated Coverlet streams |
| `write_dependency_inventory.ps1` | Lock-derived NuGet/Python dependency inventory and source-revision provenance |
| `check_candidate_freeze.py` | Validate the inactive V090-01 policy and, after every prerequisite passes, prepare its deterministic frozen-surface SHA-256 baseline |
| `check_manual_product_matrix.py` | Validate the exact V090-07 handoff and fail closed when retained sessions omit platform flows, devices, settings profiles, candidate identity, or safe evidence |
| `check_external_validation.py` | Validate the V090-08 controlled-participant handoff, exact clean-candidate chain, retained report files, comprehension results, finding closure, and affected-gate reruns |
| `check_release_materials.py` | Validate the V090-09 document foundation and exact candidate OS, size, input, media, notice, support, and evidence-bound marketing record |
| `check_release_rehearsal.py` | Validate the V090-10 staged artifact, update, rollback, removal, withdrawal, protected-data, retained-file, and operational-authority record |
| `check_stable_promotion.py` | Validate the final protected 1.0 tag rebuild, ten accepted upstream decisions, public artifacts, optional pack, install smokes, preserved evidence, and stable contract |
| `check_release_matrix.py` | Cross-bind the three native CI artifact, source, state, dependency, signing-readiness, read-only-install, deterministic-package, launch, lifecycle, reliability, fault, performance, and accessibility identities |
| `assemble_unsigned_preview.py` | Fail closed unless an alpha tag, canonical version, complete Release matrix, three deterministic packages, checksums, detached provenance, and one approved radio pack all match, then assemble explicit unsigned-preview assets |
| `test_native_export.ps1` | Read-only exported-player smoke, external user-data/log, artifact qualification, signing readiness, candidate reliability/fault/performance/accessibility and mouse evidence, optional clean-launch campaigns, and lifecycle/migration preflight |
| `inspect_native_artifact.ps1` | Payload rules, portability scan, and SHA-256 manifest |
| `install_godot.ps1` | Checksum-verified Godot editor bootstrap |
| `install_godot_templates.ps1` | Checksum-verified export-template bootstrap |

These entry points are kept at `scripts/` root because README, CI, and release documentation invoke them directly.

`ValidateArtifactManifest` also builds deterministic qualification-only release archives after validating the manifest and signing-readiness policy. It writes `release_output_plan.json` and `SHA256SUMS` beside the versioned package and never marks that unsigned output publishable.

## Player install and updates

| Script | Purpose |
| --- | --- |
| `install_player.ps1` | Legacy Windows bootstrap for the frozen Python reference |
| `install_player.sh` | Legacy macOS/Linux bootstrap for the frozen Python reference |

After install, players use the package CLI:

```text
vibesnake              # play
vibesnake update        # pull GitHub main and reinstall
vibesnake doctor        # health check
vibesnake version
```

## Visual production

`visual_generate_badges.py` is deterministic and enforced by CI using project-owned
pixel glyphs. `visual_generate_logo.py` hash-checks the preferred handcrafted brand
mark under `assets/images/logo.png`.
`capture_readme_screenshots.py` builds the native game, asks Godot to render four
staged product screens with isolated user data, and checks exact committed bytes,
dimensions, README references, and native presentation-source freshness.

Legacy credentialed audio-generation, grading, curation, rename, and mutation
programs are preserved only in the ignored local archive. They are not release
tools. A future audio-admission command must operate on an ignored candidate
workspace, default to analysis-only behavior, require explicit paid execution,
cap spend, preserve immutable inputs, pin external media tools, emit provenance,
and remain unable to write directly into public assets.

## Manual tools

- `manual/`: intentionally interactive or perceptual checks with no pytest collection side effects.

Retired executable source is removed instead of hidden behind lint or test exclusions. Historical decisions and reports belong in the ignored local archive; useful automatic behavior is rebuilt as deterministic tests or QA scenarios.
