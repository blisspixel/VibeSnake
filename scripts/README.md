# Project Tools

This directory contains explicit command-line tools. Runtime code belongs under `src/` or `game/`, deterministic tests belong under `tests/` or `native/tests/`, and generated outputs belong under ignored artifact directories.

## Required quality and release gates

| Tool | Ownership |
| --- | --- |
| `check_agent_interop.py` | Validate the closed machine-readable MCP, Agent Plugins, Agent Skill, MCP Apps, and OKF baseline, canonical UTC lifecycle metadata, absolute review dates, source alignment, version-bound public-contract digests, documentation pins, and optional read-only integrity of pinned Agent Plugins specification and schema bytes |
| `generate_agent_knowledge.py` | Deterministically render and freshness-check the Open Knowledge Format 0.2 bundle from canonical sources and the interoperability baseline |
| `close_agent_preview.py` | One-command Agent Arena preview close-out: patch public-contract digests, regenerate knowledge, check interop and docs, run focused native tests. Invoked by root `close-agent-preview.cmd` so cmd.exe can set the repo SDK before any .NET global tool starts. |
| `package_agent_host.ps1` | Assemble the current-RID unsigned self-contained Agent Host package with closed manifest, lock-derived inventory, unsigned provenance, checksums, and isolated user-data policy |
| `validate_agent_host_package.py` | Enforce the AA-10 host-package manifest, inventory, provenance, isolation, and checksum contract |
| `content_packs.py` | Core and optional pack manifest qualification |
| `assemble_radio_pack.py` | Deterministic, fail-closed assembly of one approved optional radio pack with manifest, checksums, and curation evidence |
| `assert_godot_toolchain.ps1` | SHA-512 archive, extracted editor SHA-256, and exact build identity verification |
| `native_artifact_policy.ps1` | Shared prohibited-path rules for native bundles |
| `platform_path_policy.ps1` | Absolute environment-path validation for tooling |
| `test_powershell_gates.ps1` | Executable-spoofing, artifact-path, and ordinary-CI credential-boundary regression checks |
| `test_native.ps1` | Native rules, coverage, balance, reliability, fault/triage, localization, and capture-sharing evidence, format, Godot import, scene smoke, and Godot watch against the packaged Agent Host |
| `test_native_coverage.ps1` | Shared local/CI native test and coverage gate with live output, bounded pass/retry classification, validated module floors, and one clean rebuild retry for truncated Coverlet streams |
| `write_dependency_inventory.ps1` | Lock-derived NuGet/Python dependency inventory and source-revision provenance |
| `check_manual_product_matrix.py` | Validate the schema-2 V090-07 protocol, exact Release-derived candidate, and atomic retained observations; fail closed on missing platform-flow, complete-device, mouse-capability, platform-profile, artifact-identity, or safe-evidence coverage |
| `manual/prepare_product_review.py` | Independently recompute retained three-platform Release evidence and prepare a deterministic, intentionally incomplete four-row physical-review workspace |
| `manual/review_radio_copies.py` | Rehash exact lossless review copies, prepare a pending headphone/speaker record, and validate explicit per-track listening decisions without changing approval state |
| `check_external_validation.py` | Validate the V090-08 controlled-participant handoff, exact clean-candidate chain, retained report files, comprehension results, finding closure, and affected-gate reruns |
| `check_release_matrix.py` | Cross-bind the three native CI artifact, source, state, dependency, signing-readiness, read-only-install, deterministic-package, launch, lifecycle, reliability, fault, performance, and accessibility identities |
| `assemble_unsigned_preview.py` | Fail closed unless an alpha tag, canonical version, complete Release matrix, three deterministic packages, checksums, detached provenance, and one approved radio pack all match, then assemble explicit unsigned-preview assets |
| `test_native_export.ps1` | Read-only exported-player smoke, external user-data/log, artifact qualification, signing readiness, candidate reliability/fault/performance/accessibility and mouse evidence, optional clean-launch campaigns, and lifecycle/migration preflight |
| `inspect_native_artifact.ps1` | Payload rules, portability scan, and SHA-256 manifest |
| `install_godot.ps1` | Checksum-verified Godot editor bootstrap |
| `install_godot_templates.ps1` | Checksum-verified export-template bootstrap |

These entry points are kept at `scripts/` root because README, CI, and release documentation invoke them directly.

The native `RepositoryChecks` command owns canonical documentation discovery, relative-link validation, changelog contract-release uniqueness, product-version alignment, source policy, candidate-freeze validation, deterministic freeze-baseline preparation, CI/runtime dependency-lock validation and generation, project-logo PNG identity, deterministic station-badge generation and exact-byte freshness, deterministic content-inventory generation and release readiness, README screenshot capture and freshness, V090-09 release-material structural qualification, V090-10 release-rehearsal qualification, stable-promotion qualification, achievement-candidate, Last Stand, Phase Shift, Shield, and Remaining Powers fixture generation/freshness, and source and packaged Agent Plugin validation. Run `dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- all .` from the repository root. Use `achievement-candidates [repository-root]`, `last-stand [repository-root]`, `phase-shift [repository-root]`, `shield [repository-root]`, and `remaining-powers [repository-root]` to verify the five fixed reviewed corpora. Use the corresponding `-write` routes only to restore their exact canonical bytes. Use `badge-write [repository-root]` or `inventory-write [repository-root]` only after an intentional source change, use `screenshots-write <godot-executable> [repository-root]` for staged native recapture, use `inventory-release [repository-root]` for the fail-closed content route, and use `plugin <plugin-root> [--require-mcp]` for an isolated plugin tree. Release-material commands are `materials [repository-root]`, `materials-write <output> [repository-root]`, and `materials-candidate <candidate> <expected-revision> <output> [repository-root]`. A successful candidate route proves structural validity only: its output can be passing and candidate-complete while release acceptance remains false pending human viewing, playback, claim approval, and artifact-manifest size reconciliation. Release-rehearsal commands are `rehearsal [repository-root]`, `rehearsal-write <output> [repository-root]`, and `rehearsal-record <record> <expected-revision> <output> [repository-root]`. The record route requires a later accepted release-material decision for the same revision; the checked-in foundation does not claim staged execution or release acceptance. Stable-promotion commands are `stable [repository-root]`, `stable-write <output> [repository-root]`, and `stable-record <record> <expected-revision> <output> [repository-root]`. The record route validates a protected 1.0 promotion record and never tags, signs, uploads, or publishes anything.

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

Native `RepositoryChecks -- badges .` verifies exact canonical bytes for all eight
station badges using project-owned pixel glyphs, integer-only drawing, and its own
closed RGB PNG encoder. Use `RepositoryChecks -- badge-write .` to regenerate them.
The `logo` route separately hash-checks the preferred handcrafted brand mark under
`assets/images/logo.png`.
Native `RepositoryChecks -- screenshots .` verifies the four README captures by
closed schema, canonical LF manifest, complete PNG integrity, exact hashes,
dimensions, README references, and native presentation-source freshness. Its
`screenshots-write` route builds the game, asks an explicit pinned Godot executable
to render into temporary staging with isolated user data, validates the complete
set before replacement, and writes the manifest last. Cross-host pixel identity is
not claimed, so every intentional recapture still requires visible review.

Legacy credentialed audio-generation, grading, curation, rename, and mutation
programs are preserved only in the ignored local archive. They are not release
tools. A future audio-admission command must operate on an ignored candidate
workspace, default to analysis-only behavior, require explicit paid execution,
cap spend, preserve immutable inputs, pin external media tools, emit provenance,
and remain unable to write directly into public assets.

## Manual tools

- `manual/`: intentionally interactive or perceptual checks with no pytest collection side effects.

Retired executable source is removed instead of hidden behind lint or test exclusions. Historical decisions and reports belong in the ignored local archive; useful automatic behavior is rebuilt as deterministic tests or QA scenarios.
