# Creator Content Validation

## Scope

Vibe Snake 1.0 accepts data-only custom AI personalities and optional radio packs. It does not load scripts, native libraries, assemblies, shaders, arbitrary resources, or general plugins from creator content. Validation parses JSON metadata and exact inventory records; it never executes submitted content.

The native `ValidateCreatorContent` command emits schema-1 JSON using contract `creator-content-validation-v1`. Every report includes `executesContent: false` and `arbitraryCodeSupported: false`. Exit code 0 means valid and compatible, 1 means content was rejected, and 2 means command usage was invalid.

## Build the validator

From the repository root:

```powershell
dotnet build native/tools/ValidateCreatorContent/ValidateCreatorContent.csproj `
  --configuration Release `
  --locked-mode
```

Use the built DLL through the pinned .NET runtime:

```powershell
dotnet native/tools/ValidateCreatorContent/bin/Release/net10.0/ValidateCreatorContent.dll
```

## Validate a personality

The filename stem is the custom ID unless `--id` is supplied:

```powershell
dotnet native/tools/ValidateCreatorContent/bin/Release/net10.0/ValidateCreatorContent.dll `
  personality docs/content/examples/personality.schema1.json `
  --id route_planner
```

Current authored files should use [personality.schema.json](schemas/personality.schema.json). The matching [personality example](examples/personality.schema1.json) is valid. IDs use lowercase letters, digits, and underscores, cannot collide with a built-in ID, and remain visibly `CUSTOM / UNOFFICIAL`. All six traits are finite numbers from 0 through 1. RGB color has exactly three integer channels from 0 through 255.

The runtime parser still reads legacy 0.2 files that omit `schemaVersion` or use `schema_version`, but new creator files should use the published schema exactly.

### Personality codes

| Code | Meaning |
| --- | --- |
| `personality-success` | Schema, values, ID, and color are valid |
| `personality-empty` | File contains no JSON content |
| `personality-invalid-json` | JSON cannot be parsed |
| `personality-unsupported-schema` | Schema version is outside the supported range |
| `personality-missing-field` | A required field is absent |
| `personality-invalid-type` | A field has the wrong JSON type |
| `personality-out-of-range` | Text or trait bounds fail, including non-finite numbers |
| `personality-invalid-color` | RGB does not contain three integer channels from 0 through 255 |
| `personality-path-unsafe` | Path, extension, link, or requested custom ID is unsafe |
| `personality-io-error` | File cannot be read |
| `personality-unknown-field` | Document contains an unrecognized field |
| `personality-duplicate-field` | Document repeats a field or both schema spellings |
| `personality-too-large` | File exceeds the bounded character or byte limit |
| `personality-reserved-id` | ID collides with a built-in personality |
| `personality-capacity-exceeded` | A runtime directory contains more than 64 custom files |
| `personality-duplicate-id` | Two runtime files claim the same custom ID |

The single-file command can return every parser or ID code. Capacity and cross-file duplicate codes come from bounded runtime-directory loading.

## Validate radio packs

Use [radio-pack.schema.json](schemas/radio-pack.schema.json) for structure. The [radio-pack example](examples/radio-pack.schema1.json) is illustrative and intentionally cannot qualify until all placeholder inventory hashes, file metadata, rights evidence, and compatibility values are replaced with exact approved values.

Pack-set validation needs the exact inventory, the one core manifest shipped by the target build, and zero or more optional radio manifests:

```powershell
dotnet native/tools/ValidateCreatorContent/bin/Release/net10.0/ValidateCreatorContent.dll `
  pack-set config/content_inventory.json 1.0.0 vibesnake-core 4 `
  path/to/vibesnake.core.json `
  path/to/vibesnake.radio.example-signal.json
```

The current repository has no production core or radio manifest because export eligibility is still zero. The command is complete, but the example command cannot pass until the human content gates approve those release inputs.

Validation requires canonical JSON, exact inventory policy identity, exact file IDs, relative paths, sizes, SHA-256 hashes, roles, media types, runtime use, credits, cleared rights, station track membership, dependency ranges, game-version range, and rules-version range. A radio track must be `audio/mpeg`, role `radio-track`, and optional. A radio pack depends only on `vibesnake.core`.

Station identity uses the canonical underscore form, such as `flow_signal`. The filesystem-safe pack slug uses hyphens, so that station belongs to pack `vibesnake.radio.flow-signal`. Validation performs this conversion exactly and rejects a mismatched pair.

### Pack-set and compatibility codes

| Code | Meaning |
| --- | --- |
| `pack-set-valid` | Every manifest is canonical, unique, and compatible |
| `pack-set-incompatible` | At least one valid manifest does not support the requested product identity |
| `pack-set-invalid` | Inventory, JSON, canonical encoding, allowlist, rights, metadata, or filesystem validation failed |
| `core-kind-required` | First manifest is not the one offline core pack |
| `optional-kind-invalid` | A later manifest is not an optional radio pack |
| `pack-id-collision` | Two manifests claim one ID; neither override semantics nor last-writer wins is allowed |
| `compatible` | One manifest supports the requested identity and installed dependencies |
| `game-version-too-old` | Requested game is below the pack minimum |
| `game-version-too-new` | Requested game reaches or exceeds the pack maximum |
| `ruleset-mismatch` | Pack targets another ruleset ID |
| `rules-version-too-old` | Requested rules version is below the pack minimum |
| `rules-version-too-new` | Requested rules version reaches or exceeds the pack maximum |
| `missing-dependency` | Exact required dependency is absent |
| `dependency-version-too-old` | Installed dependency is below the pack minimum |
| `dependency-version-too-new` | Installed dependency reaches or exceeds the pack maximum |

## Multiple-pack precedence and collisions

The core always resolves first. Optional radio manifests resolve in ordinal pack-ID order only after all IDs prove unique. A duplicate optional ID is a hard collision, not an override. Packs cannot replace core files, another station, player data, preferences, achievements, progression, scores, or replays. Runtime installation gives each pack its own ID-named directory and requires its complete allowlist with no extra files.

No load order grants behavioral precedence. Optional radio metadata contributes one unique station ID. Missing, malformed, incompatible, duplicated, removed, or tampered optional packs remain isolated while a valid core stays playable.

## Security and compatibility boundary

- JSON is parsed with bounded document, field, array, text, path, count, and numeric limits.
- Duplicate and unknown fields fail where the schema is closed.
- Paths must remain relative and traversal-free; installed pack links and reparse points are rejected.
- Creator payload bytes are never executed by validation.
- Personality files produce typed policy values only.
- Radio manifests admit MP3 bytes only through exact approved inventory entries.
- Arbitrary code plugins remain outside 1.0.
- Hashes establish reviewed-byte integrity, not publisher authenticity. Release provenance and platform signing remain separate.

See [Content Pack Contract](CONTENT_PACKS.md) for runtime isolation and [Assets, Rights, and Content Packs](CONTENT_PIPELINE.md) for approval and export eligibility.
