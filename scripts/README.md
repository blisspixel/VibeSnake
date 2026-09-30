# Project Tools

This directory contains explicit command-line tools. Runtime code belongs under `src/` or `game/`, deterministic tests belong under `tests/` or `native/tests/`, and generated outputs belong under ignored artifact directories.

## Required quality and release gates

| Tool | Ownership |
| --- | --- |
| `close_agent_preview.py` | One-command Agent Arena preview close-out: call the native public-contract digest and Agent Knowledge writers, check interop and docs, then run focused native tests. Invoked by root `close-agent-preview.cmd` so cmd.exe can set the repo SDK before any .NET global tool starts. |
| `package_agent_host.ps1` | Assemble the current-RID unsigned self-contained Agent Host package with closed manifest, lock-derived inventory, unsigned provenance, checksums, and isolated user-data policy, then validate it with native `host-package` |
| `assert_godot_toolchain.ps1` | SHA-512 archive, extracted editor SHA-256, and exact build identity verification |
| `native_artifact_policy.ps1` | Shared prohibited-path rules for native bundles |
| `platform_path_policy.ps1` | Absolute environment-path validation for tooling |
| `test_powershell_gates.ps1` | Executable-spoofing, artifact-path, and ordinary-CI credential-boundary regression checks |
| `test_native.ps1` | Native rules, coverage, balance, reliability, fault/triage, localization, and capture-sharing evidence, format, Godot import, scene smoke, and Godot watch against the packaged Agent Host |
| `test_native_coverage.ps1` | Shared local/CI native test and coverage gate with live output, bounded pass/retry classification, validated module floors, and one clean rebuild retry for truncated Coverlet streams |
| `write_dependency_inventory.ps1` | Lock-derived NuGet/Python dependency inventory and source-revision provenance |
| `test_native_export.ps1` | Read-only exported-player smoke, external user-data/log, artifact qualification, signing readiness, candidate reliability/fault/performance/accessibility and mouse evidence, optional clean-launch campaigns, and lifecycle/migration preflight |
| `inspect_native_artifact.ps1` | Payload rules, portability scan, and SHA-256 manifest |
| `install_godot.ps1` | Checksum-verified Godot editor bootstrap |
| `install_godot_templates.ps1` | Checksum-verified export-template bootstrap |

These entry points are kept at `scripts/` root because README, CI, and release documentation invoke them directly.

The native `RepositoryChecks` command owns canonical documentation discovery, relative-link validation, changelog contract-release uniqueness, product-version alignment, source policy, candidate-freeze validation, deterministic freeze-baseline preparation, CI/runtime dependency-lock validation and generation, project-logo PNG identity, deterministic station-badge generation and exact-byte freshness, deterministic content-inventory generation and release readiness, README screenshot capture and freshness, V090-09 release-material structural qualification, V090-10 release-rehearsal qualification, stable-promotion qualification, achievement-candidate, Last Stand, Phase Shift, Shield, and Remaining Powers fixture generation/freshness, and source and packaged Agent Plugin validation. Run `dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- all .` from the repository root. Use `achievement-candidates [repository-root]`, `last-stand [repository-root]`, `phase-shift [repository-root]`, `shield [repository-root]`, and `remaining-powers [repository-root]` to verify the five fixed reviewed corpora. Use the corresponding `-write` routes only to restore their exact canonical bytes. Use `badge-write [repository-root]` or `inventory-write [repository-root]` only after an intentional source change, use `screenshots-write <godot-executable> [repository-root]` for staged native recapture, use `inventory-release [repository-root]` for the fail-closed content route, and use `plugin <plugin-root> [--require-mcp]` for an isolated plugin tree. Use `host-package <package-root> [repository-root]` for an assembled Agent Host package, `release-matrix <download-root> <expected-revision> <Debug|Release> <output>` for a qualified download root, `unsigned-preview <channel-root> <provenance-root> <radio-pack-root> <matrix> <version-root> <tag> <expected-revision> <output>` for fail-closed unsigned alpha assembly, `content-packs <repository-root> <manifest> [manifest ...] [--inventory <path>] [--game-version <version>] [--ruleset-id <id>] [--ruleset-version <version>]` for canonical pack qualification, and `radio-pack <repository-root> <manifest> <output> [--curation <path>] [--inventory <path>]` for one already-approved radio archive, `interop-upstream [repository-root]` for the scheduled three-URL integrity probe, and `product-review-prepare <repository-root> <release-evidence-root> <expected-revision> <release-run-id> <owner/name> <output-root>` for a pending manual-review workspace, and `radio-listening <repository-root> prepare-template <review-directory> <template-path>`, `radio-listening <repository-root> verify-inputs <review-directory> <output-path>`, or `radio-listening <repository-root> review-record <review-directory> <record-path> <output-path> [require-approved]` for a hash-bound listening record. Use `radio-audio <repository-root> qualify <inventory> <curation> <output> <ffmpeg> <ffprobe> <workers> <timeout-seconds> [replace]` for a full-decode qualification that does not modify sources, curation, inventory, or export eligibility. Use `radio-review <repository-root> prepare <station> <inventory> <curation> <analysis> <output-root> <ffmpeg> <ffprobe> <workers> <timeout-seconds> [replace]` for one station review-copy set. Listening stays pending, and the command does not modify sources, curation, inventory, or export eligibility. Use `radio-preview <repository-root> list <directory>` to list the fixed eight-station catalog from the ignored archive or an external directory. The command does not play audio, write a listening record, or approve a track. Those eleven routes are not part of `all`. The product-review route recomputes a downloaded Release matrix into pending session templates and cannot set release acceptance or publication eligibility. The listening route rehashes exact review copies and cannot change release approval, export eligibility, curation, or source bytes. The upstream probe is not an ordinary local gate. Publication eligibility stays false. Unsigned-preview assembly and radio-pack assembly do not approve radio content or change export eligibility. Manual-product commands are `manual-matrix [repository-root]`, `manual-matrix-write <output> [repository-root]`, and `manual-matrix-record <sessions-directory> <candidate> <output> [repository-root]`. External-validation commands are `external-validation [repository-root]`, `external-validation-write <output> [repository-root]`, and `external-validation-record <sessions-directory> <candidate-ledger> <findings> <output> [repository-root]`. Both foundations are part of `all`. Zero retained sessions keep execution and release acceptance false. Release-material commands are `materials [repository-root]`, `materials-write <output> [repository-root]`, and `materials-candidate <candidate> <expected-revision> <output> [repository-root]`. A successful candidate route proves structural validity only: its output can be passing and candidate-complete while release acceptance remains false pending human viewing, playback, claim approval, and artifact-manifest size reconciliation. Release-rehearsal commands are `rehearsal [repository-root]`, `rehearsal-write <output> [repository-root]`, and `rehearsal-record <record> <expected-revision> <output> [repository-root]`. The record route requires a later accepted release-material decision for the same revision; the checked-in foundation does not claim staged execution or release acceptance. Stable-promotion commands are `stable [repository-root]`, `stable-write <output> [repository-root]`, and `stable-record <record> <expected-revision> <output> [repository-root]`. The record route validates a protected 1.0 promotion record and never tags, signs, uploads, or publishes anything.

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
