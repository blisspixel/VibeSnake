# Assets, Rights, and Content Packs

## Purpose

The canonical asset tree contains rights-cleared images, configuration, AI data, documentation, and production metadata. Rights-unverified audio, lyrics, transcripts, rejected tracks, generated candidates, analysis output, copied research, and working stems live in the ignored local archive. Nothing enters a native player merely because it exists in the workspace. The content pipeline creates an explicit boundary between local source material, public source, and releasable game content.

The current foundation inventories every canonical source asset, records its exact bytes and policy classification, and blocks release use until the asset is explicitly approved. The owner's Apache-2.0 declaration is recorded separately from provider-aware provenance, technical analysis, listening, curation, and pack approval.

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

The 2026-08-01 clean-clone inventory contains 18 rights-cleared files totaling
95,377 bytes.

| Classification | Files | Current meaning |
| --- | ---: | --- |
| Blocked runtime candidates | 11 | Rights-cleared source assets not yet approved for a release pack |
| Excluded source material | 7 | Development examples, documentation, and production metadata |
| Export eligible | 0 | Deliberately zero until selected files have complete rights and quality review |
| Structurally valid | 18 | Passed the current bounded JSON, Markdown, and decoded PNG scanline checks |
| Empty | 0 | Every zero-byte media candidate was removed from the source inventory |
| Byte-identical extras | 1 | One duplicate file beyond the first copy in one hash group |

No audio binary is present in the public-source inventory. The ignored local archive preserves 95 radio MP3 candidates that passed the MPEG structural gate before isolation. The project owner has declared an Apache-2.0 release intent, while historical records identify service-assisted generation for at least part of the library. A candidate can return to the canonical tree only after its generation plan and applicable service terms are bound to its provenance record and its decode, loudness, clipping, listening, lyrics, station, and pack reviews pass.

The complete canonical breakdown is generated in `summary` inside
`config/content_inventory.json`. Current policy separates reference core
material, radio artwork, and development-only material. The ignored `archive/source-assets` collection
currently preserves 423 files totaling 740,801,845 bytes and is not part of the
clean-clone inventory. It includes unresolved and rejected media, copied research
inputs, working lyrics, retired production tooling, superseded documents, and
historical reports with private workstation paths.

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

## Commands

Verify that policy, bytes, and the checked-in inventory agree:

```powershell
python scripts/content_inventory.py --check
python scripts/visual_generate_badges.py --check
python scripts/visual_generate_logo.py --check
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
