# Native Release Outputs

Source snapshots use unique `player-<run-id>-<attempt>` prerelease tags. Choose the
newest published source snapshot from the [releases list](https://github.com/blisspixel/VibeSnake/releases),
then download every asset from that one tag. The former `player-latest` URL remains
an historical snapshot and no longer moves to new builds.

An administrator must enable repository release immutability before enabling this
publisher. The workflow token has contents permission, not Administration:read,
so it cannot query that setting. The publisher stages all assets
in a draft, downloads and compares their exact bytes and inventory, checks the
checksum manifest, and only then publishes. It verifies the published release's
immutable flag before reporting success. If an administrator disables the setting,
publication can produce a mutable release and the job fails; restore the repository
setting before any subsequent publication. Published snapshots are retained;
there is no automated release deletion. A failed draft remains available for
inspection and can be resumed with the same run/attempt identity. A workflow rerun
gets a new attempt tag. Repeating publication for an already published identity
only verifies its bytes; it cannot update them. Source previews remain separate
from signed native player qualification.


Status: deterministic qualification and unsigned alpha assembly are implemented; first-alpha approval, signed publication, and storefront submission remain pending.

The three native jobs are joined by `release-matrix-qualification-v1`. The aggregate gate requires exactly Windows x64, macOS Universal, and Linux x64 rows from one source revision and build mode, with one deterministic smoke hash and lock-set digest. Each row cross-checks the artifact manifest SHA-256 against signing readiness, verifies immutable external user data and logs under the read-only install smoke, and retains the deterministic package digest. The provenance job cannot run unless this complete unsigned matrix passes.

Release rows also require `candidate-install-lifecycle-preflight-v1`. The exact exported player must pass first launch and a hash-identical repair copy; preserve source bytes while migrating preferences schemas 1 through 6, personal-best schema 1, and local-playtest-summary schema 1; reject and preserve a future preferences schema; bind optional-pack and player-data recovery evidence; and prove application removal does not remove external player data. This preflight is not a substitute for selected-channel installer update, rollback, and removal tests.

## Immutable source and reference channel

`player-*` is deliberately separate from native artifact qualification. A same-repository push to `main` must complete the full CI workflow successfully before the source workflow checks out that exact qualified SHA. The workflow exposes no manual-dispatch or pull-request checkout path; diagnostic packaging is performed locally with the documented build commands. Each immutable release's notes include the exact qualified revision's ten latest Unreleased entries and full-changelog link, so published development changes track the same bytes as its downloads.

The release contains exactly:

- `VibeSnake-player-source.zip`.
- One normalized Python reference wheel.
- One normalized Python reference sdist.
- `SHA256SUMS.txt` covering those three payloads.

The source ZIP excludes generated native `bin/` and `obj/` directories, Godot's `.godot/` import cache, and Python `*.egg-info/` metadata at every depth. Source builds recreate their local runtime caches through the root launcher.

The source ZIP includes development-only Agent Arena inputs: the Agent Plugins 1.0.0 manifest and skill, MCP host source, deterministic plugin assembly script, and generated Open Knowledge Format 0.2 bundle. The assembled `mcp.json` and framework-dependent host output are generated and validated in CI but are not published as a supported plugin in this channel. `player-*` is therefore neither a native player artifact nor the AA-10 supported symbolic desktop package. AA-10's current-RID self-contained host package is a separate unsigned preview assembled by `scripts/package_agent_host.ps1` and validated in CI. It carries a lock-derived NuGet inventory and unsigned source-revision provenance; native rules CI launches the packaged executable and requires a live viewer stream, and Godot's headless watch smoke consumes that pipe. `publication_eligible` stays false until signing exists.

Publication checks out the CI-qualified commit with persisted Git credentials disabled. The publisher creates a unique tag at that commit and rejects an existing tag pointing elsewhere. It resumes uploads only for the matching draft, verifies the downloaded asset inventory and bytes, then publishes and checks immutable prerelease state. An already published identity is verified without mutation. GitHub availability remains an external dependency. Consumers must verify `SHA256SUMS.txt` after download.

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

Versioned tags are not handled by the source-snapshot workflow. A tag such as `v0.3.0-alpha.1` can reach the native prerelease publisher only after all three matching-platform `Release` jobs, the aggregate release matrix, detached provenance, canonical version checks, and the release-ready content inventory pass. A separate tag job requires exactly one station manifest, binds it to the exact inventory and `approved-for-alpha-release` listening decisions, and emits a deterministic `.vibesnake-pack.zip`, canonical manifest, assembly evidence, and checksums. The preview assembler then rehashes each qualification package and its two manifests, validates every checksum and matrix row, independently inspects the stored radio archive and its 80 MiB compressed and 120 MiB installed budgets, rejects extra files, and copies the exact bytes to explicit `-unsigned-preview` names. It emits one aggregate manifest and checksum file. Stable publication eligibility remains false.

The content gate currently fails because no source asset is export-eligible, the curation record remains pending human listening review, and no production station manifest exists. No tag should be created until the first approved core and radio-pack output exists and the exact downloaded native artifacts receive the documented human review.

The approved radio pack is a separate release download, never part of a base player archive. A player installs it by dropping the `.vibesnake-pack.zip` onto a non-running game screen. The game preserves the download, extracts through bounded same-volume staging below player data, revalidates the exact manifest allowlist, sizes, and hashes, refuses overwrite, and activates the station only after an atomic move succeeds.

## Files emitted beside each package

| Output | Purpose |
| --- | --- |
| `VibeSnake-<version>-<platform>-qualification.<archive>` | Exact qualified player payload |
| `artifact-manifest.json` | Schema-3 source, toolchain, smoke identity, Release preview-exclusion state, sizes, and per-file SHA-256 |
| `release_output_plan.json` | Channel shape, separation guarantees, package size/hash, deterministic repeat result, and publication blockers |
| `SHA256SUMS` | Package, artifact-manifest, and output-plan SHA-256 values |

The aggregate alpha release also includes one `VibeSnake-<version>-vibesnake.radio.<station>-<pack-version>.vibesnake-pack.zip`, its renamed manifest and assembly evidence, `unsigned_preview_manifest.json`, and `SHA256SUMS.txt`. The aggregate manifest records the pack ID, version, station, track count, byte count, and SHA-256 without mixing optional content into any platform player archive.

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
