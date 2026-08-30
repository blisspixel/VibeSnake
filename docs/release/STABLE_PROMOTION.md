# Stable 1.0 Promotion

[Release state](README.md) | [Release checklist](RELEASE_CHECKLIST.md) | [Release rehearsal](REHEARSAL.md) | [Signing](SIGNING.md)

Status: stable-promotion guard qualified, protected execution pending.

Version 1.0 is a promotion of a proven candidate, not another feature milestone. The machine-readable authority is [`config/stable_promotion_v1.json`](../../config/stable_promotion_v1.json). The validator checks a retained protected-workflow record but never tags, signs, uploads, withdraws, or publishes anything.

## Mandatory upstream decisions

All ten records must have `passed: true`, `releaseAcceptance: true`, and the exact promotion source revision:

1. three-platform release matrix;
2. complete manual product matrix;
3. controlled external validation;
4. exact candidate release materials accepted after structural completion, artifact reconciliation, claim approval, visible image review, and video playback review;
5. release and rollback rehearsal;
6. content and optional-pack approval;
7. named-hardware performance acceptance;
8. retained accessibility human review;
9. structured human playtest acceptance;
10. protected platform signing acceptance.

A protocol-qualified handoff with zero sessions is not an accepted decision. For release materials, `candidateMaterialComplete: true` without `releaseAcceptance: true` is also insufficient. Missing people, hardware, content approval, credentials, or platform evidence cannot be converted to a pass by the promotion guard.

## Protected rebuild

The final record requires:

- application version and tag name exactly `1.0.0`;
- tag object revision equal to the reviewed source revision;
- a retained numeric protected-workflow run ID;
- Windows x64, macOS Universal, and Linux x64 public artifact, manifest, provenance, and checksum files;
- SHA-256 agreement between every retained file, manifest, provenance bundle, checksum entry, and published install result;
- the separately packaged approved optional pack and its manifest;
- one install and deterministic smoke from the actual public file on each platform.

Qualification-only, unsigned, unattested, locally copied, manually renamed, or manually uploaded output does not meet this contract.

## Preserved release record

Retain at least one nonempty file in every category:

- build logs;
- manifests;
- dependency inventory or SBOM;
- checksums;
- migration fixtures;
- previous supported artifacts;
- support and operational record.

The stable record has one SHA-256 map whose keys exactly equal every artifact, manifest, provenance bundle, checksum, optional pack, upstream decision, public-install result, and preserved evidence path it references. Tampering or an unreferenced extra hash blocks promotion.

## Stable contract

Promotion acknowledges these exact compatibility promises:

1. Patch releases preserve scored rules unless a disclosed correctness or exploit fix requires a change.
2. Save migrations remain non-destructive and tested.
3. Existing score categories retain their rules identity.
4. Removed content remains visibly missing or incompatible instead of silently substituted.
5. Accessibility support remains regression tested.
6. Core play remains offline with no required account or network.

Changing one of these promises requires an explicit future contract review. It cannot happen as an incidental packaging edit.

## Validation commands

Qualify the checked-in guard:

```powershell
python scripts/check_stable_promotion.py `
  --output TestResults/stable-promotion/stable_promotion_handoff.json
```

Validate the retained protected-workflow record:

```powershell
python scripts/check_stable_promotion.py `
  --record C:\retained-vibesnake-evidence\stable-promotion\record.json `
  --expected-revision 0123456789abcdef0123456789abcdef01234567 `
  --output C:\retained-vibesnake-evidence\stable-promotion\decision.json
```

The repository currently contains no promotion record. `promotionComplete` and `releaseAcceptance` therefore remain false.
