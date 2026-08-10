# Native Release Outputs

Status: deterministic qualification and unsigned alpha assembly are implemented; first-alpha approval, signed publication, and storefront submission remain pending.

The three native jobs are joined by `release-matrix-qualification-v1`. The aggregate gate requires exactly Windows x64, macOS Universal, and Linux x64 rows from one source revision and build mode, with one deterministic smoke hash and lock-set digest. Each row cross-checks the artifact manifest SHA-256 against signing readiness, verifies immutable external user data and logs under the read-only install smoke, and retains the deterministic package digest. The provenance job cannot run unless this complete unsigned matrix passes.

Release rows also require `candidate-install-lifecycle-preflight-v1`. The exact exported player must pass first launch and a hash-identical repair copy; preserve source bytes while migrating preferences schemas 1 through 6, personal-best schema 1, and local-playtest-summary schema 1; reject and preserve a future preferences schema; bind optional-pack and player-data recovery evidence; and prove application removal does not remove external player data. This preflight is not a substitute for selected-channel installer update, rollback, and removal tests.

## Output contract

The qualified Godot export is an input, not the final download. `ReleaseOutputPlan` converts its platform identity and signing-readiness state into exact direct-download and store-depot shapes. `ValidateArtifactManifest` rehashes every input file, rejects an added or missing file, builds the qualification package twice, requires byte-identical package hashes, and writes separate manifest, checksum, and output-plan files.

| Platform | Qualified input | Direct-download shape | Store-depot shape |
| --- | --- | --- | --- |
| Windows x64 | Portable folder | Versioned ZIP archive | Portable folder |
| macOS Universal | App-bundle ZIP | Versioned app-bundle ZIP | Expanded app bundle |
| Linux x64 | Portable folder | Versioned tar.gz archive | Portable folder |

Qualification packages end in `-qualification` and always report `publicationEligible: false`. They prove archive layout and reproducibility without pretending that an unsigned artifact is ready for players.

## Unsigned native alpha channel

Canonical alpha versions use SemVer in [VERSION](../../VERSION), such as `0.3.0-alpha.1`. Python package metadata uses the equivalent PEP 440 spelling, such as `0.3.0a1`. CI rejects drift among `VERSION`, `ProductIdentity.AppVersion`, the Python package, and the source fallback.

Versioned tags are not handled by the source-snapshot workflow. A tag such as `v0.3.0-alpha.1` can reach the native prerelease publisher only after all three matching-platform `Release` jobs, the aggregate release matrix, detached provenance, canonical version checks, and the release-ready content inventory pass. The assembler then rehashes each qualification package and its two manifests, validates its checksum file and matrix row, rejects extra files, and copies the exact bytes to explicit `-unsigned-preview` names. It emits one aggregate manifest and checksum file. Stable publication eligibility remains false.

The content gate currently fails because no source asset is export-eligible. No tag should be created until the first approved core and radio-pack output exists and the exact downloaded native artifacts receive the documented human review.

## Files emitted beside each package

| Output | Purpose |
| --- | --- |
| `VibeSnake-<version>-<platform>-qualification.<archive>` | Exact qualified player payload |
| `artifact-manifest.json` | Source, toolchain, smoke identity, sizes, and per-file SHA-256 |
| `release_output_plan.json` | Channel shape, separation guarantees, package size/hash, deterministic repeat result, and publication blockers |
| `SHA256SUMS` | Package, artifact-manifest, and output-plan SHA-256 values |

The archive contains exactly the files listed in `artifact-manifest.json`. The manifest, output plan, and checksums remain separate promotion outputs. Optional content uses the separate `.vibesnake-pack.zip` contract and never enters the base archive merely because it is installed on a build machine. Player profiles, saves, preferences, achievements, replays, logs, diagnostics, quarantined packs, and temporary data are excluded.

## Reproducibility

Windows ZIP entries use stable ordinal ordering and a fixed ZIP timestamp. Linux tar.gz entries use ordinal ordering, fixed timestamps and ownership, normalized read permissions, and executable permission only for the game binary and launcher. The macOS qualification package copies the already-qualified app-bundle ZIP exactly. Every platform writes the candidate twice and fails if byte count or SHA-256 differs.

This is deterministic packaging of one qualified input. It does not claim that independently compiled binaries are reproducible across runners.

## Publication boundary

A non-qualification package requires a `Release` build with a full source revision. Publication still remains false until all applicable blockers are discharged:

- Protected platform signing for Windows and macOS.
- Verification of the signed output.
- Checksums regenerated after signing and notarization.
- Final artifact provenance.
- Explicit direct-download or storefront approval.

Linux has no invented platform-signature claim, but still requires final permission, runtime-baseline, checksum, provenance, and channel review.

Unsigned alpha publication is a narrow prerelease exception, not a weakening of this stable boundary. Release notes must disclose unsigned Windows/macOS packages, Gatekeeper behavior, prerelease compatibility risk, and the separate optional-content boundary. Store or stable output still requires every protected operation above.

Application uninstall and optional-pack removal are separate operations. The packaging contract never includes player data and declares that uninstall preserves it. Any future installer must retain that behavior unless a distinct, explicit player-data removal choice is designed and qualified.

## Remaining channel work

- Select the direct-download host and any storefronts.
- Define installer behavior only if a selected Windows channel needs an installer instead of the qualified ZIP.
- Add storefront metadata, depot mapping, update, rollback, and clean-removal qualification for each selected channel.
- Run the packager after final platform signing and retain the complete signed candidate evidence chain.
