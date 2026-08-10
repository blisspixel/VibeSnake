# Manual Product Matrix

V090-07 uses one closed contract for physical candidate review. The automated handoff is qualified, but manual execution is not complete until retained sessions cover every required cell and `releaseAcceptance` becomes `true`.

The source contract is [qa_manual_product_matrix_v1.json](../../config/qa_manual_product_matrix_v1.json). Validate the handoff without claiming execution:

```powershell
python scripts/check_manual_product_matrix.py `
  --output TestResults/manual-product-matrix/manual_product_matrix_handoff.json
```

## Required scope

The matrix has four platform rows and 36 required flows, producing 144 platform-flow cells:

| Platform row | Artifact | Architecture |
| --- | --- | --- |
| Windows x64 | `windows-x64` | x86-64 |
| macOS Universal on Apple Silicon | `macos-universal` | arm64 |
| macOS Universal on Intel | `macos-universal` | x86-64 |
| Linux x64 | `linux-x64` | x86-64 |

Every row covers first launch, tutorial, Classic, Vibe, both death causes, all nine powers, all six settings sections, achievements, customization, scores, radio, five optional-pack states, AI channels, replays, reset, recovery, focus loss, and quit.

The retained sessions must collectively cover:

- Keyboard.
- Mouse, including menu targeting, settings navigation, gameplay direction, and Back.
- At least one Xbox-layout controller.
- At least one PlayStation-layout controller.
- Sound device absent, sound muted, zero shake, reduced motion, flash-free, high contrast, maximum text scale, and missing optional content.

The exact candidate revision and artifact SHA-256 are mandatory. A platform row cannot combine results from different artifact hashes. The Apple Silicon and Intel rows must report the same SHA-256 because both execute the same macOS Universal artifact.

## Session record

Store each session as a UTF-8 JSON file outside the source tree during execution. A session can cover some or all flows, and multiple sessions may contribute to one platform row. Each flow result needs at least one safe relative evidence path, and the referenced retained file must exist relative to the session file.

```json
{
  "schemaVersion": 1,
  "kind": "vibesnake-manual-product-matrix-session-v1",
  "sessionId": "product-matrix-001",
  "candidateRevision": "0123456789abcdef0123456789abcdef01234567",
  "artifactSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
  "appVersion": "0.9.0",
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
  "executedUtc": "2026-08-09T12:00:00Z",
  "results": [
    {
      "flowId": "first-launch",
      "result": "pass",
      "evidencePaths": [
        "windows-x64/product-matrix-001/first-launch.png"
      ]
    }
  ]
}
```

Allowed results are `pass`, `fail`, and `blocked`. A failed or blocked required flow prevents acceptance. An inaccessible required flow is a P1 release blocker.

## Validate retained sessions

Point the validator at a directory containing only session JSON records:

```powershell
python scripts/check_manual_product_matrix.py `
  --sessions C:\retained-vibesnake-evidence\manual-product-matrix `
  --output TestResults/manual-product-matrix/manual_product_matrix.json
```

The validator rejects unknown or duplicate fields, unsafe or missing retained evidence, mixed candidate revisions, multiple artifact hashes for one platform row, different hashes between the two macOS Universal architecture rows, missing flows, missing devices, missing settings profiles, and any failed or blocked result. It accepts the matrix only when all 144 platform-flow cells pass and all required coverage dimensions are present.

Screenshots, short video, output-device observations, and sanitized logs are suitable evidence. Do not retain private system paths, controller serial numbers, account identifiers, or unrelated device information.
