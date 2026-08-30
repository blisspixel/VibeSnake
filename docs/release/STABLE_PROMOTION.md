# Stable 1.0 Promotion

[Release state](README.md) | [Release checklist](RELEASE_CHECKLIST.md) | [Release rehearsal](REHEARSAL.md) | [Signing](SIGNING.md)

Status: stable-promotion guard qualified, protected execution and release acceptance pending.

Version 1.0 is a promotion of a proven candidate, not another feature milestone. [`config/stable_promotion_v1.json`](../../config/stable_promotion_v1.json) closes the protected rebuild and preservation contract. [`config/stable_upstream_acceptance_v1.json`](../../config/stable_upstream_acceptance_v1.json) separately closes the exact decision kind, field, and ordered gate authority for each upstream acceptance. The native validator checks those authorities and a retained protected-workflow record. It never performs human review, hardware execution, signing, tagging, upload, withdrawal, installation, or publication.

## Mandatory upstream decisions

All ten records must have `passed: true`, `releaseAcceptance: true`, and the exact promotion source revision and application version. Each decision ID accepts only its named kind:

1. `release-matrix-acceptance-v1` for the three-platform Release matrix;
2. `manual-product-matrix-acceptance-v1` for the complete manual product matrix;
3. `external-validation-acceptance-v1` for controlled external validation;
4. `release-materials-acceptance-v1`, accepted after structural completion, artifact reconciliation, claim approval, visible image review, and video playback review;
5. `release-rehearsal-handoff-v2`, with validated retained execution evidence and the same candidate artifact and manifest identities;
6. `content-approval-acceptance-v1` for core content and the optional pack;
7. `hardware-performance-acceptance-v1` for named-hardware performance;
8. `accessibility-human-review-acceptance-v1` for retained accessibility review;
9. `human-playtest-acceptance-v1` for structured human playtest acceptance;
10. `platform-signing-acceptance-v1` for protected platform signing and provenance.

Eight decisions use the authority's closed generic acceptance schema. Their gate records must appear once in the declared order, name non-personal operational roles, and retain unique evidence whose hashes form the exact referenced-file closure. Content approval also binds the optional-pack and optional-pack-manifest hashes. Platform signing also binds the unsigned input artifact and manifest maps plus the signed public artifact, manifest, and provenance maps.

Release materials and rehearsal are special decisions with their own checked-in exact schemas. A protocol-qualified handoff with zero sessions is not an accepted decision. The structural `release-materials-handoff-v2` is never an accepted material decision, even when `candidateMaterialComplete: true`. The later material-acceptance decision and rehearsal v2 decision must each be complete, same-revision, and free of pending gates and errors. Missing people, hardware, content approval, credentials, or platform evidence cannot be converted to a pass by the promotion guard.

Every upstream decision is cross-bound to the stable record's source revision and application version. Nine review decisions agree on the three-platform unsigned candidate artifact and manifest cohort. The material decision's manifest map is cross-bound through the rehearsal, which names and hashes that exact accepted decision. The platform-signing decision takes the unsigned cohort as its input, then binds the byte-changing signed public artifact and manifest maps plus final provenance to the stable record. Content approval also agrees on the optional pack. Decision paths, evidence paths, and retained hash keys are unique portable paths beneath one link-free trust root, so one favorable file or one aliased evidence file cannot satisfy multiple authorities.

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

The stable record has one SHA-256 map whose keys exactly equal every artifact, manifest, provenance bundle, checksum, optional pack, upstream decision, public-install result, and preserved evidence path it references. The validator rechecks bounded retained files and a final stable snapshot. Tampering, path aliasing, cross-platform identity drift, or an unreferenced extra hash blocks promotion.

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

Inspect the checked-in guard without writing evidence:

```powershell
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj `
  --configuration Release --no-restore -- stable .
```

Write the pending foundation handoff used by CI:

```powershell
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj `
  --configuration Release --no-restore -- stable-write `
  TestResults/stable-promotion/stable_promotion_handoff.json .
```

Validate the retained protected-workflow record:

```powershell
dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj `
  --configuration Release --no-restore -- stable-record `
  C:\retained-vibesnake-evidence\stable-promotion\record.json `
  0123456789abcdef0123456789abcdef01234567 `
  C:\retained-vibesnake-evidence\stable-promotion\decision.json .
```

Both writing routes emit canonical `stable-promotion-handoff-v2` JSON bound to the two checked-in contract hashes. The record route additionally binds the exact source revision. The foundation handoff can report `guardQualified: true`, but it has no record hash or protected run ID and keeps `recordIntegrityQualified`, `protectedWorkflowAttested`, `promotionComplete`, and `releaseAcceptance` false with the exact pending gates. Only `stable-record` can set those completion fields after validating the complete retained record.

The repository currently contains no promotion record. Protected execution, `promotionComplete`, and `releaseAcceptance` therefore remain pending and false.
