# Assets, Rights, and Content Packs

## Purpose

The canonical asset tree contains rights-cleared images, configuration, AI data, documentation, production metadata, and the public eight-station offline radio MP3 library. Optional ignored local archives on developer machines may still hold historical or rejected material; they are not part of a clean clone. Nothing enters a native player merely because it exists in the workspace. The content pipeline creates an explicit boundary between public source, pack approval, and releasable game content.

The current foundation inventories every canonical source asset, records its exact bytes and policy classification, and blocks native export until the asset is explicitly pack-approved. Apache-2.0 rights clearance for the radio catalog is separate from loudness, listening, credits, and pack allowlist approval.

## Authorities

| File | Authority |
| --- | --- |
| [content_policy.json](../../config/content_policy.json) | Human-reviewed classification, pack intent, runtime use, shipping state, and rights status |
| [content_inventory.json](../../config/content_inventory.json) | Generated file-level paths, logical IDs, media types, sizes, SHA-256 hashes, integrity results, duplicate links, and policy metadata |
| [inventory.py](../../src/vibesnake/content/inventory.py) | Strict policy, inventory, integrity, duplication, and release-blocker rules |
| [content_inventory.py](../../scripts/content_inventory.py) | Check, regeneration, and release-readiness command |
| [test_content_inventory.py](../../tests/qa/test_content_inventory.py) | Normal, stale, malformed, ambiguous, unsafe, corrupt, duplicate, and release-blocker contracts |
| [CONTENT_PACKS.md](CONTENT_PACKS.md) | Executable core and optional-radio manifest schema, compatibility rules, allowlists, and failure isolation |

Edit the policy. Do not hand-edit the generated inventory.

## Current measured inventory

The 2026-08-04 public inventory contains 114 rights-cleared files totaling
340,378,770 bytes, including 95 radio MP3 tracks under `assets/audio/radio/`.

| Classification | Files | Current meaning |
| --- | ---: | --- |
| Runtime radio tracks | 95 | Public GTA-style station catalog (`vibesnake-radio`), rights-cleared, still blocked for native pack export until quality and credit gates pass |
| Runtime images and reference data | 11 | Logo, station badges, AI data, runtime config (rights-cleared, pack-blocked until export approval) |
| Excluded source material | 8 | Development examples, documentation, and production metadata |
| Export eligible | 0 | Deliberately zero until selected files complete loudness, listening, credit, and pack allowlist review |
| Structurally valid | 114 | Passed the current bounded JSON, Markdown, MPEG structural, and decoded PNG scanline checks |
| Byte-identical extras | 1 | One duplicate file beyond the first copy in one hash group |

The project owner released the curated radio catalog under Apache-2.0 as original Vibe Snake soundtrack material. Structural MPEG checks and rights clearance do not replace loudness, clipping, listening, station credit, or pack-manifest approval. Export eligibility stays zero until those gates pass for the selected core and radio manifests.

The complete canonical breakdown is generated in `summary` inside
`config/content_inventory.json`. Current policy separates reference core
material, radio tracks, radio artwork, and development-only material.

## V080-05 curation handoff

[content_curation_v1.json](../../config/content_curation_v1.json) is the exact review queue over the current inventory policy. It accounts for all 95 runtime-radio asset IDs once across the eight canonical stations and separates pending, approved, and rejected decisions without moving or deleting source files. Candidate inventories range from 11 to 13 tracks per station. Qualification confirms zero duplicate radio bytes, zero temporary/test filename tokens, cleared rights, structural MPEG integrity, and distinct station names, inclusion rules, hosts, and visual identities.

This automated pass is not listening approval. `content-curation-qualification-v1` truthfully retains zero approved radio tracks, zero authored core-music candidates, zero full-decode evidence, zero loudness evidence, zero human listening reviews, zero production manifests, and zero export-eligible files. A track moves from pending only after retained technical and listening evidence exists; the policy and generated inventory then receive a separately reviewed, narrow approval change.

`ContentCreditsDocument` implements `content-credits-v1`. It takes exact validated content-pack manifests and deterministically produces a human-readable credits and third-party notices document with stable pack, credit, and file ordering. It writes no timestamp or machine path and rejects missing core, duplicate pack IDs, unknown credit references, and oversized output. No production notices document is generated yet because no production manifest is approved.

## Policy contract

Every asset must match exactly one policy rule. A rule defines:

- Stable rule ID and one or more relative POSIX glob patterns.
- Role and intended pack ID.
- Runtime use: `required`, `optional`, or `none`.
- Shipping state: `approved`, `blocked`, or `excluded`.
- Rights status, source, license, attribution, and review note.

Ambiguous matches, unmatched files, rules that match nothing, unsafe paths, duplicate rule IDs, missing metadata, and unknown values fail validation. An `approved` rule is invalid unless rights are `cleared`. An `excluded` rule cannot claim runtime use.

Current broad rules record the owner's project-wide license declaration, but approval should use the narrowest practical file-specific rule and an auditable provenance record. A folder-wide status change is not a substitute for binding a service-assisted generation batch to its plan, model state, applicable terms, attribution, and redistribution evidence.

## Generated inventory contract

Each generated entry records:

- Path-derived logical asset ID and normalized relative path.
- Detected media type and byte size.
- SHA-256 content hash.
- Basic integrity status and diagnostic.
- Role, pack ID, runtime use, shipping state, and export eligibility.
- Rights metadata copied from the matching policy rule.
- Policy rule ID.
- Canonical duplicate target when another file has identical bytes.

The document has no timestamp or machine path, so unchanged bytes and policy produce unchanged output on every platform. Its policy hash makes a classification-only change visible even when asset bytes do not change.

## Integrity boundary

The current automatic checks verify:

- JSON parses as UTF-8.
- Text, Markdown, and CSV decode as UTF-8.
- PNG has a valid signature, one first-position IHDR, a complete bounded chunk walk, matching CRCs, supported color and bit-depth combinations, bounded dimensions and pixel count, consecutive image-data chunks, one terminal IEND, and no trailing bytes.
- WAV has a valid RIFF/WAVE structure, supported PCM, float, or extensible format metadata, and non-empty audio data.
- MP3 stays within the inspection-size ceiling, has bounded ID3 metadata when present, and contains two consecutive complete MPEG frames with compatible version, layer, and sample-rate metadata.
- Every asset is a regular in-tree file rather than a symbolic link.
- Paths do not collide when case is ignored.
- Exact duplicates are reported by SHA-256 and size.

This is an inventory-integrity screen, not full media qualification. Approved audio also needs complete decode, duration, channel, sample-rate, loudness, true-peak, clipping, silence, repetition, and listening checks. Approved images need complete decode, color-space, dimension, readability, and visual review. Rights clearance remains a human and legal provenance decision represented by policy, never inferred from a filename or hash.

Run the local full-decode and loudness admission pass with FFmpeg before listening review:

```powershell
python scripts/manual/analyze_radio_audio.py
```

The ignored `TestResults/radio-audio/radio_audio_qualification.json` output binds every result to the inventory, curation plan, source SHA-256, decoder versions, and operating-system class. It uses a provisional offline-radio admission band of `-18 LUFS` plus or minus `2 LU` and a `-1 dBTP` ceiling, based on EBU R 128 loudness and true-peak measurement with the EBU R 128 S2 interim streaming level. It reports normalization gain and predicted post-gain peak without rewriting any source byte. The campaign rehashes all 95 files after concurrent work, including files whose decoder failed, so a source mutation cannot hide behind a missing measurement row. A passing report is technical evidence only. It cannot change curation decisions, export eligibility, or human listening status.

Prepare one complete station for listening only after the current full-library analysis exists:

```powershell
python scripts/manual/prepare_radio_review_copies.py --station the_bureau
```

The ignored `TestResults/radio-review/the_bureau/` set contains lossless FLAC copies and `review-copy-manifest.json`. The tool trims only measured edge silence, runs FFmpeg's file-oriented two-pass `loudnorm` filter at the provisional target, preserves source channels and sample rate, removes inherited metadata, fully decodes and remeasures the output, and rehashes the complete source set before atomically publishing the station. A post-normalization edge miss permits at most two additional measured corrections; each removes only the policy excess plus a 0.1-second margin and reruns both passes. See the official [FFmpeg loudnorm and silenceremove filter documentation](https://ffmpeg.org/ffmpeg-filters.html).

The first local `the_bureau` campaign produces 12 of 12 technically passing copies totaling 219,715,853 bytes. Integrated loudness measures from `-18.0` to `-17.9 LUFS`, maximum true peak is `-1.5 dBTP`, maximum leading/trailing/internal silence is `1.9`/`0.0`/`4.152608` seconds, all twelve second passes remain linear, and a complete second campaign reproduces all twelve output SHA-256 values exactly. The manifest still fixes `releaseApproved`, `sourceReplacementApproved`, and `exportEligibilityChanged` to false and `humanListeningStatus` to pending. These copies are a listening queue, not source replacements or a pack.

Verify those exact copies and prepare the intentionally incomplete listening record:

```powershell
python scripts/manual/review_radio_copies.py `
  TestResults/radio-review/the_bureau `
  --verify-inputs `
  --output TestResults/radio-review/the_bureau/listening-handoff.json

python scripts/manual/review_radio_copies.py `
  TestResults/radio-review/the_bureau `
  --prepare-template TestResults/radio-review/the_bureau/listening-review.json.template
```

The verifier rehashes every FLAC and binds the template to the exact review-copy manifest. For every track, the reviewer must record full playback, clipping or distortion, start and end quality, relative level, station identity, and sustained comfort on both headphones and speakers. Copy the template to `listening-review.json`, replace every placeholder, and validate the completed record:

```powershell
python scripts/manual/review_radio_copies.py `
  TestResults/radio-review/the_bureau `
  --review-record TestResults/radio-review/the_bureau/listening-review.json `
  --output TestResults/radio-review/the_bureau/listening-decision.json
```

An honest rejection is a complete listening record but cannot approve source replacement. `--require-approved` is the fail-closed gate for the later source-replacement workflow. The command never changes source, curation, release approval, or export eligibility.

## Commands

Verify that policy, bytes, and the checked-in inventory agree:

```powershell
python scripts/content_inventory.py --check
python scripts/visual_generate_badges.py --check
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- logo .
```

Regenerate after an intentional asset or policy change:

```powershell
python scripts/content_inventory.py --write
python scripts/content_inventory.py --check
```

Exercise the future release gate:

```powershell
python scripts/content_inventory.py --check --release-ready
```

The last command currently fails by design. It reports every runtime candidate that remains blocked plus any invalid runtime file or approved duplicate. CI runs `--check` so no asset or classification can change silently. Release qualification must eventually run `--release-ready` against the selected core and optional-pack inputs.

## Adding or changing an asset

1. Decide whether the file is runtime content, production material, development reference, or archive content.
2. Put it in the corresponding source boundary. Do not place prompts, reports, transcripts, or candidates in a future release pack directory.
3. Add or refine one non-ambiguous policy rule.
4. Record truthful source, license, attribution, and review status. Use `unverified` and `blocked` when evidence is incomplete.
5. Regenerate the inventory and inspect size, hash, integrity, duplicates, pack, and release blockers.
6. Run the content tests and full quality loop.
7. For release approval, complete format-specific quality review and attach the evidence referenced by the policy.

## Executable pack boundary

Schema 1 pack validation now consumes two allowlisted classes rather than treating the source tree as releasable content:

1. A minimal offline core pack containing only the assets required for launch, menu, one full run, readable critical feedback, settings, death, restart, and recovery.
2. Optional radio packs containing station manifests, compatible app versions, track metadata, exact hashes and sizes, rights records, credits, and deterministic missing-pack behavior.

The implemented reference validator requires exact approved-inventory allowlists, matching file hashes and rights-derived credits, semantic-version and ruleset ranges, a dependency-free `vibesnake.core`, and station-specific optional manifests. Its resolver proves that a missing, invalid, incompatible, duplicate, or tampered optional pack does not block a valid core. The native content service, first approved manifests, export integration, and size evidence remain. See [CONTENT_PACKS.md](CONTENT_PACKS.md) for the full executable contract and ordered completion gate.
