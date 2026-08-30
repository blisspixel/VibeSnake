# Manual Product Matrix

V090-07 uses one closed contract for physical candidate review. The automated protocol and exact-candidate
handoff are qualified. Manual execution is not complete until retained sessions cover every required cell and
`releaseAcceptance` becomes `true`.

The source contract is [qa_manual_product_matrix_v2.json](../../config/qa_manual_product_matrix_v2.json). It
defines the candidate record, artifact rows, atomic flow observations, required flows, devices, mouse
capabilities, settings profiles, results, and release rules.

## Prepare an exact candidate workspace

Select a Release run only after approved content, credits, inventory, and production manifests are present in
the exact source revision. Historical run `32421705560` is automated qualification evidence, not the current
physical candidate. Download the retained manifests, qualification evidence, and aggregate matrix from the
newly selected Release run.
The preparer does not trust the aggregate file by itself. It independently recomputes the complete matrix from
the platform evidence, requires an exact structural match with the retained aggregate, and only then projects
the three packages into four physical platform rows.

```powershell
$reviewRunId = "REPLACE"
$reviewRevision = "REPLACE_WITH_40_CHARACTER_REVISION"

gh run download $reviewRunId --repo blisspixel/VibeSnake `
  --pattern "vibesnake-*-manifest" `
  --pattern "vibesnake-*-qualification-evidence" `
  --pattern "vibesnake-release-matrix" `
  --dir "TestResults/release-review/run-$reviewRunId"

python scripts/manual/prepare_product_review.py `
  "TestResults/release-review/run-$reviewRunId" `
  --expected-revision $reviewRevision `
  --release-run-id $reviewRunId `
  --output-root TestResults/manual-product-review
```

The ignored workspace contains:

- `candidate.json`, binding the run, Release-matrix hash, revision, version, file names, package hashes and
  sizes, artifact-manifest hashes, and all four platform rows;
- `templates/`, with one intentionally incomplete session template per platform row;
- `sessions/evidence/`, with one safe location per platform row for retained screenshots, short video, logs,
  and observations;
- `REVIEW.md`, with exact execution instructions;
- `workspace-manifest.json`, hashing every generated handoff file.

The tool refuses an existing destination and cannot set release acceptance or publication eligibility true.
Validate the handoff before physical execution:

```powershell
python scripts/check_manual_product_matrix.py `
  --candidate "TestResults/manual-product-review/$reviewRevision/candidate.json" `
  --output "TestResults/manual-product-review/$reviewRevision/handoff-decision.json"
```

No current physical handoff exists. The zero-session `e87db6e` workspace is superseded and must not be used
for release acceptance.

## Required scope

The matrix has four platform rows and 36 required flows, producing 144 base platform-flow cells:

| Platform row | Artifact | Architecture |
| --- | --- | --- |
| Windows x64 | `windows-x64` | x86-64 |
| macOS Universal on Apple Silicon | `macos-universal` | arm64 |
| macOS Universal on Intel | `macos-universal` | x86-64 |
| Linux x64 | `linux-x64` | x86-64 |

Every row covers first launch, tutorial, Classic, Vibe, both death causes, all nine powers, all six settings
sections, achievements, customization, scores, radio, five optional-pack states, AI channels, replays, reset,
recovery, focus loss, and quit.

The 144 cells measure platform-by-flow coverage only. Additional acceptance dimensions require 432
complete-device flow cells, 16 mouse-capability cells, and 32 platform-profile cells.

On every platform row, retained observations must cover:

- All 36 flows with keyboard.
- All 36 flows with an Xbox-layout controller.
- All 36 flows with a PlayStation-layout controller.
- Mouse menu targeting, settings navigation, gameplay direction, and Back.
- Sound device absent, sound muted, zero shake, reduced motion, flash-free, high contrast, maximum text scale,
  and missing optional content on passing observations.

The candidate revision, application version, and artifact SHA-256 are mandatory and must match
`candidate.json`. A platform row cannot combine results from different artifact hashes. The Apple Silicon and
Intel rows must report the same SHA-256 because both execute the same macOS Universal artifact.

## Record a session

Copy the applicable `.json.template` file into `sessions/` with a unique ID such as
`product-matrix-001.json`. Preserve its candidate identity fields, replace every placeholder, and record only
what was actually observed. Each result binds one flow to one device, any mouse capabilities demonstrated, and
the active settings profiles. A session can cover some or all flows, and multiple sessions may contribute to
one platform row. Use separate sessions when another device must execute the same flow.

Every flow result needs at least one safe relative evidence path, and the referenced file must exist relative
to the session file. Allowed results are `pass`, `fail`, and `blocked`. A failed or blocked required flow is
retained evidence and prevents acceptance. An inaccessible required flow is a P1 release blocker.

```json
{
  "schemaVersion": 2,
  "kind": "vibesnake-manual-product-matrix-session-v2",
  "sessionId": "product-matrix-001",
  "candidateRevision": "0123456789abcdef0123456789abcdef01234567",
  "artifactSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "appVersion": "0.3.0-alpha.1",
  "platformRowId": "windows-x64",
  "operatingSystemVersion": "Windows version and build",
  "hardwareClass": "Published minimum hardware class",
  "renderer": "Compatibility renderer and driver identity",
  "executedUtc": "2026-08-20T12:00:00Z",
  "results": [
    {
      "flowId": "first-launch",
      "inputDeviceId": "keyboard",
      "inputCapabilityIds": [],
      "settingsProfileIds": [
        "sound-muted",
        "maximum-text-scale"
      ],
      "result": "pass",
      "evidencePaths": [
        "evidence/windows-x64/first-launch.png"
      ]
    }
  ]
}
```

## Validate retained sessions

```powershell
python scripts/check_manual_product_matrix.py `
  --candidate C:\retained-vibesnake-evidence\manual-product-matrix\candidate.json `
  --sessions C:\retained-vibesnake-evidence\manual-product-matrix\sessions `
  --output C:\retained-vibesnake-evidence\manual-product-matrix\decision.json
```

The validator rejects unknown or duplicate fields, unsafe or missing retained evidence, a missing or invalid
candidate record, mismatched revisions, versions, or package hashes, mixed candidate revisions, multiple
artifact hashes for one platform row, different hashes between the two macOS Universal architecture rows,
missing flows, missing complete device-to-flow cells, missing mouse capabilities, missing platform profiles,
and any failed or blocked result. Merely naming a device or profile cannot earn coverage. Only passing atomic
observations count, and acceptance requires all 624 declared coverage cells.

Screenshots, short video, output-device observations, and sanitized logs are suitable evidence. Do not retain
private system paths, controller serial numbers, account identifiers, or unrelated device information.
