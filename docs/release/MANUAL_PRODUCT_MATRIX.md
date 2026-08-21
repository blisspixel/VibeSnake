# Manual Product Matrix

V090-07 uses one closed contract for physical candidate review. The automated protocol and exact-candidate
handoff are qualified. Manual execution is not complete until retained sessions cover every required cell and
`releaseAcceptance` becomes `true`.

The source contract is [qa_manual_product_matrix_v1.json](../../config/qa_manual_product_matrix_v1.json). It
defines the candidate record, artifact rows, session rows, required flows, devices, settings profiles, results,
and release rules.

## Prepare an exact candidate workspace

Download the retained manifests, qualification evidence, and aggregate matrix from the selected Release run.
The preparer does not trust the aggregate file by itself. It independently recomputes the complete matrix from
the platform evidence, requires an exact structural match with the retained aggregate, and only then projects
the three packages into four physical platform rows.

```powershell
gh run download 32421705560 --repo blisspixel/VibeSnake `
  --pattern "vibesnake-*-manifest" `
  --pattern "vibesnake-*-qualification-evidence" `
  --pattern "vibesnake-release-matrix" `
  --dir TestResults/release-review/run-32421705560

python scripts/manual/prepare_product_review.py `
  TestResults/release-review/run-32421705560 `
  --expected-revision e87db6ecf0a720c49d0cab48a39637f260ccc597 `
  --release-run-id 32421705560 `
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
  --candidate TestResults/manual-product-review/e87db6ecf0a720c49d0cab48a39637f260ccc597/candidate.json `
  --output TestResults/manual-product-review/e87db6ecf0a720c49d0cab48a39637f260ccc597/handoff-decision.json
```

The current handoff is candidate-qualified with zero sessions, zero completed cells, and release acceptance
false.

## Required scope

The matrix has four platform rows and 36 required flows, producing 144 platform-flow cells:

| Platform row | Artifact | Architecture |
| --- | --- | --- |
| Windows x64 | `windows-x64` | x86-64 |
| macOS Universal on Apple Silicon | `macos-universal` | arm64 |
| macOS Universal on Intel | `macos-universal` | x86-64 |
| Linux x64 | `linux-x64` | x86-64 |

Every row covers first launch, tutorial, Classic, Vibe, both death causes, all nine powers, all six settings
sections, achievements, customization, scores, radio, five optional-pack states, AI channels, replays, reset,
recovery, focus loss, and quit.

The retained sessions must collectively cover:

- Keyboard.
- Mouse, including menu targeting, settings navigation, gameplay direction, and Back.
- At least one Xbox-layout controller.
- At least one PlayStation-layout controller.
- Sound device absent, sound muted, zero shake, reduced motion, flash-free, high contrast, maximum text scale,
  and missing optional content.

The candidate revision, application version, and artifact SHA-256 are mandatory and must match
`candidate.json`. A platform row cannot combine results from different artifact hashes. The Apple Silicon and
Intel rows must report the same SHA-256 because both execute the same macOS Universal artifact.

## Record a session

Copy the applicable `.json.template` file into `sessions/` with a unique ID such as
`product-matrix-001.json`. Preserve its candidate identity fields, replace every placeholder, and record only
what was actually observed. A session can cover some or all flows, and multiple sessions may contribute to one
platform row.

Every flow result needs at least one safe relative evidence path, and the referenced file must exist relative
to the session file. Allowed results are `pass`, `fail`, and `blocked`. A failed or blocked required flow is
retained evidence and prevents acceptance. An inaccessible required flow is a P1 release blocker.

```json
{
  "schemaVersion": 1,
  "kind": "vibesnake-manual-product-matrix-session-v1",
  "sessionId": "product-matrix-001",
  "candidateRevision": "0123456789abcdef0123456789abcdef01234567",
  "artifactSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "appVersion": "0.3.0-alpha.1",
  "platformRowId": "windows-x64",
  "operatingSystemVersion": "Windows version and build",
  "hardwareClass": "Published minimum hardware class",
  "renderer": "Compatibility renderer and driver identity",
  "inputDeviceIds": [
    "keyboard",
    "mouse",
    "xbox-layout-controller"
  ],
  "settingsProfileIds": [
    "sound-muted",
    "maximum-text-scale"
  ],
  "executedUtc": "2026-08-20T12:00:00Z",
  "results": [
    {
      "flowId": "first-launch",
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
missing flows, missing devices, missing settings profiles, and any failed or blocked result. It accepts the
matrix only when all 144 platform-flow cells pass and all required coverage dimensions are present.

Screenshots, short video, output-device observations, and sanitized logs are suitable evidence. Do not retain
private system paths, controller serial numbers, account identifiers, or unrelated device information.
