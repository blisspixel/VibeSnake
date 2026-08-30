# Release and Rollback Rehearsal

[Release state](README.md) | [Packaging](PACKAGING.md) | [Signing](SIGNING.md) | [Recovery](../guides/RECOVERY.md)

Status: V090-10 handoff qualified, staged execution pending.

The release rehearsal uses the exact artifacts intended for release. It proves that the staged files can be acquired, verified, installed, exercised, withdrawn, and replaced without losing existing player data. The machine-readable authority is [`config/release_rehearsal_v1.json`](../../config/release_rehearsal_v1.json).

## Entry gate

Begin only after the exact candidate has:

1. a clean source revision and canonical version;
2. final Windows x64, macOS Universal, and Linux x64 artifact and manifest hashes;
3. completed protected signing and platform verification;
4. accepted release materials with `releaseAcceptance: true` bound to the same revision;
5. a preserved previous supported artifact for every platform;
6. a retained migration fixture set that represents every supported save schema.

Qualification-only packages, unsigned placeholders, an unapproved optional pack, or current alpha documentation cannot satisfy this entry gate. A direct `materials-candidate` handoff proves structural completion only and cannot satisfy the accepted-material decision by itself.

## Staged record

Keep the rehearsal outside the source checkout. The strict record binds:

- candidate and previous application versions;
- exact candidate revision and controlled staged-location ID;
- candidate artifact, previous artifact, and candidate manifest paths and SHA-256 values for all three platforms;
- the later accepted release-material decision file and SHA-256, not the structural candidate handoff alone;
- every migration fixture and a deterministic set digest;
- platform operation results and retained evidence;
- withdrawal result;
- publish, halt, replace, and communicate roles;
- one SHA-256 map covering every retained file referenced by the record.

Role IDs identify operational responsibility, such as `release-publish-role`. Do not put a person's name, account, email, credential, or signing identity in the public rehearsal record.

## Platform operations

Run all eleven operations on every artifact platform:

1. Download from the controlled staged location.
2. Verify the expected checksum.
3. Verify the platform signature, notarization, or attestation required by the signing policy.
4. Install using the intended channel shape.
5. Launch the installed player.
6. Create and reload a save.
7. Install and use the exact approved optional content.
8. Remove optional content without removing player data.
9. Update from the preserved previous version to the candidate.
10. Roll back from the candidate to the previous version.
11. Remove the application while retaining player data.

Each result is `pass`, `fail`, or `blocked` and has at least one retained evidence file. Any value other than `pass` blocks acceptance.

Before update, create a protected preexisting user-data fixture and record its SHA-256. Record the same fixture after rollback and application removal. The two values must match exactly. New candidate save data may be tested separately, but it cannot replace or weaken the preservation check.

## Withdrawal drill

Exercise the controlled withdrawal path without touching public production channels. The retained result must show that:

- the candidate became unavailable at the staged location;
- the previous artifact was restored;
- player data remained available;
- player-facing communication was prepared.

Do not delete the withdrawn candidate, its build logs, manifests, checksums, signing evidence, or failure evidence. Removal from a channel is different from evidence destruction.

## Authority drill

Verify one operational role for each action:

| Operation | Required capability |
| --- | --- |
| `publish` | Promote only the reviewed candidate files |
| `halt` | Stop or prevent promotion when a blocker appears |
| `replace` | Withdraw the candidate and restore an approved previous artifact |
| `communicate` | Publish accurate status, workaround, withdrawal, or recovery instructions |

Each role requires retained authorization evidence. The rehearsal file proves capability boundaries, not the identity of a person.

## Validation commands

Qualify the repository handoff:

```powershell
python scripts/check_release_rehearsal.py `
  --output TestResults/release-rehearsal/release_rehearsal_handoff.json
```

Validate the retained staged execution:

```powershell
python scripts/check_release_rehearsal.py `
  --record C:\retained-vibesnake-evidence\release-rehearsal\record.json `
  --expected-revision 0123456789abcdef0123456789abcdef01234567 `
  --output C:\retained-vibesnake-evidence\release-rehearsal\decision.json
```

Acceptance requires all 33 platform-operation cells, exact hashes, unchanged protected user data, a complete withdrawal result, and all four authority records. The checked-in handoff has no rehearsal record and therefore reports `rehearsalComplete: false` and `releaseAcceptance: false`.
