# Content Pack Contract

## Purpose and current status

Vibe Snake must remain fully playable offline while allowing its large radio library to ship separately. The implemented schema 1 contract defines one required core pack and zero or more optional station packs. It validates identity, compatibility, dependencies, exact file metadata, cleared rights, credits, station track order, and the approved source-inventory allowlist before any content is loaded.

The contract and optional-pack resolver are implemented and tested. No real source asset or release pack is approved yet. The current source inventory deliberately reports zero export-eligible files, so a production manifest cannot pass this validator until file-level rights and quality review are complete.

## Authorities

| File | Authority |
| --- | --- |
| [packs.py](../../src/vibesnake/content/packs.py) | Schema 1 structure, inventory matching, compatibility, dependency, core, radio, and resolution rules |
| [content_packs.py](../../scripts/content_packs.py) | Build-time qualification command for canonical manifests |
| [test_content_packs.py](../../tests/qa/test_content_packs.py) | Normal, malformed, unsafe, incomplete, tampered, incompatible, missing, and duplicate-pack contracts |
| [content_inventory.json](../../config/content_inventory.json) | Exact source file hashes, sizes, policy state, rights state, and export eligibility |
| [CONTENT_PIPELINE.md](CONTENT_PIPELINE.md) | Source classification, rights review, media integrity, and approval workflow |

The Python validator is the executable qualification reference for 0.3. The native content service will implement the same observable contract before player assets enter Godot exports.

## Boundary

```mermaid
flowchart LR
    Source[Source assets] --> Policy[Reviewed policy]
    Policy --> Inventory[Deterministic inventory]
    Inventory --> Validator[Pack validator]
    CoreManifest[Core manifest] --> Validator
    RadioManifest[Station manifest] --> Validator
    Validator --> Core[Required offline core]
    Validator --> Radio[Accepted optional radio]
    Core --> Play[Playable game]
    Radio -. optional .-> Play
    Rejected[Missing, invalid, incompatible, or tampered radio] -. isolated .-> Play
```

The source tree is never a runtime search path for the target player. A manifest admits an exact allowlist of approved inventory entries. A directory scan cannot silently add files to a pack.

## Pack classes

### Required core

The one core pack has ID `vibesnake.core`, kind `core`, no dependencies, and at least one file whose inventory use is `required`. Its eventual acceptance is broader than schema validation: the built player must prove launch, menu navigation, one complete run, critical visual and audio feedback, settings, death, restart, and recovery with every optional pack absent.

The core pack must remain small enough to make the base game a complete purchase or download. Radio stations, archives, production material, and nonessential alternates do not enter it merely to increase content volume.

### Optional radio

Each radio pack has kind `radio` and ID `vibesnake.radio.<station-id>`. It depends only on a compatible `vibesnake.core` version. Every included file has optional runtime use. Radio metadata supplies:

- A stable station ID.
- A player-facing station name.
- An ordered, unique list of track asset IDs.
- Track entries that resolve to `audio/mpeg` files with role `radio-track`.

A station pack may later include approved badges, stingers, host segments, or metadata files outside `trackIds`, but every file still belongs to the same exact inventory allowlist and rights record.

## Schema 1 fields

The top-level document is strict. Unknown or missing fields fail qualification.

| Field | Contract |
| --- | --- |
| `schemaVersion` | Integer `1` |
| `id` | Lowercase dotted or hyphenated stable ID |
| `version` | Strict `MAJOR.MINOR.PATCH` pack version |
| `kind` | `core` or `radio` |
| `displayName` | Non-empty player-facing name |
| `description` | Non-empty purpose statement |
| `compatibility` | Game-version and ruleset half-open ranges |
| `inventory` | Inventory schema, asset root, and policy SHA-256 binding |
| `dependencies` | Exact pack IDs and semantic-version ranges |
| `files` | Exact approved file allowlist |
| `credits` | Source, license, attribution, and review evidence |
| `radio` | `null` for core or strict station metadata for radio |

Version ranges use `minInclusive` and `maxExclusive`. Schema 1 accepts stable three-part versions only. A range whose minimum is not lower than its maximum is invalid.

The ruleset range contains a stable ID plus integer version bounds. Current packs target `vibesnake-core` version 4 through, but not including, version 5. A future rules change does not silently reinterpret an old pack.

## File admission

Every file entry contains:

- Inventory asset ID in the form `asset:<relative-path>`.
- Relative POSIX path with no traversal, absolute path, backslash, or case-insensitive collision.
- Media type, positive byte size, and lowercase SHA-256.
- Runtime role and required or optional use.
- A credit ID.

The validator compares ID, path, media type, bytes, SHA-256, role, and runtime use to the generated inventory. It also requires:

- The inventory pack ID equals the manifest pack ID.
- Shipping state is `approved`.
- Export eligibility is true.
- Basic media integrity is `valid`.
- The entry is not a duplicate copy.
- Rights state is `cleared`.
- Source, license, attribution, and review evidence exactly match the referenced manifest credit.
- Manifest IDs equal the complete approved inventory allowlist for that pack, with nothing missing or added.

This makes a policy change, byte change, renamed path, altered credit, or unexpected file an explicit review event.

## Compatibility results

Structurally valid manifests receive an actionable compatibility result before file loading.

| Code | Meaning |
| --- | --- |
| `compatible` | Game, ruleset, and dependencies satisfy the manifest |
| `game-version-too-old` | The app is below the supported range |
| `game-version-too-new` | The app is at or above the exclusive maximum |
| `ruleset-mismatch` | The stable ruleset ID differs |
| `rules-version-too-old` | The rules version is below the range |
| `rules-version-too-new` | The rules version is at or above the exclusive maximum |
| `missing-dependency` | A required pack is absent |
| `dependency-version-too-old` | An installed dependency is below its range |
| `dependency-version-too-new` | An installed dependency is at or above its range |
| `invalid-pack` | An optional document fails schema, inventory, rights, hash, or station validation |
| `core-unavailable` | Optional content cannot load because the required core is incompatible |

The pack-set resolver treats an invalid core as fatal. It evaluates optional documents independently. Missing optional content is normal, and malformed, incompatible, duplicate, or tampered optional content is reported and skipped while a compatible core stays ready.

## Integrity and trust boundary

Per-file SHA-256 detects changed or substituted payloads relative to the reviewed inventory. The inventory policy hash detects classification changes. Canonical JSON makes manifest diffs and checksums stable across platforms.

These hashes provide integrity evidence, not publisher authenticity by themselves. Release artifacts still need retained build provenance, a published manifest checksum, and platform signing where applicable. The game must never execute scripts or native code from a content pack in 1.0.

## Qualification command

Once real manifests exist, qualify exactly one core plus any intended optional packs:

```powershell
python scripts/content_packs.py `
  config/packs/vibesnake.core.json `
  config/packs/vibesnake.radio.flow-signal.json `
  --game-version 0.3.0 `
  --ruleset-id vibesnake-core `
  --ruleset-version 4
```

The command first regenerates and compares the authoritative inventory, requires canonical manifest encoding, validates every allowlisted file and credit, then resolves compatibility. It exits nonzero on any build-time rejection. Runtime optional-pack isolation is a separate tested behavior and does not lower the release gate.

No production command can pass today because no inventory entry is export eligible. That is the intended fail-closed state.

## Order for the first real packs

1. Define the smallest player-visible core content set required by the native vertical slice.
2. Resolve or remove empty and duplicate candidates before selection.
3. Create file-specific policy rules with reviewed source, license, attribution, and quality evidence.
4. Regenerate the inventory and confirm only the selected entries become export eligible under final pack IDs.
5. Generate canonical credits and manifests from those approved entries.
6. Run full decode and format-specific quality checks in addition to the basic inventory screen.
7. Make the native content service load the core only through the validated manifest.
8. Prove core-only launch and the full required offline flow from a read-only installation.
9. Add one station pack and prove missing, removed, incompatible, incomplete, hash-mismatched, and corrupt states without blocking core play.
10. Connect export inspection so player artifacts contain exactly the manifest allowlists and generated credits.
11. Measure compressed, installed, decoded-memory, scan, and startup impact for each real pack.
12. Repeat the artifact and install lifecycle on Windows, macOS, and Linux.

## Completion gate

V030-09 is complete only when the strict contract is implemented in the native content service, one approved core manifest passes and supports the entire offline vertical slice, at least one approved station manifest passes independently, export inspection consumes the same allowlists, removal and tamper states are exercised, actual size and performance evidence is retained, and player-facing errors explain recovery without exposing machine paths.
