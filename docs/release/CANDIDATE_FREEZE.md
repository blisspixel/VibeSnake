# Candidate Freeze

Status: prepared but inactive. The 0.8.0 acceptance gate, a clean revision, green CI, and release-matrix readiness must all pass before activation.

## Contract

V090-01 freezes six player and creator contract surfaces:

1. Deterministic rules.
2. Save schemas and persistence behavior.
3. Replay schema and compatibility behavior.
4. Content manifests.
5. Input defaults.
6. Accessibility defaults.

The machine-readable authority is [`config/candidate_freeze_policy_v1.json`](../../config/candidate_freeze_policy_v1.json). Native `RepositoryChecks freeze` resolves every declared pattern and rejects missing, empty, unsafe, reordered, broadened, duplicate, malformed, or non-UTF-8 policy data. The combined native repository check runs this route on Windows, macOS, and Linux. The current state is `pre-freeze`, so no baseline exists and no freeze is being claimed.

Once active, CI also requires a closed `candidate-freeze-baseline-v1` manifest. Every resolved file is bound to its contract IDs and SHA-256 digest. An added, removed, renamed, or modified frozen file fails the check until the candidate decision record and reviewed baseline are intentionally updated.

## Activation procedure

1. Close the full 0.8.0 acceptance gate with retained evidence.
2. Start from a clean revision whose complete CI matrix is green.
3. Confirm the Windows x64, macOS Universal, and Linux x64 candidate matrix is ready.
4. Mark all four policy prerequisites `passed`.
5. Prepare the baseline with an exact lowercase 40-character revision and second-precision UTC timestamp:

   ```powershell
   dotnet run --project native/tools/RepositoryChecks/RepositoryChecks.csproj -- freeze-baseline <revision> <YYYY-MM-DDTHH:MM:SSZ> .
   ```

6. Review the baseline, set the policy state to `frozen`, and copy the revision, timestamp, manifest path, and manifest SHA-256 into the activation record.
7. Run the complete local qualification and CI matrix before tagging a candidate.

Activation is invalid if a prerequisite remains open, the baseline is absent, its own hash drifts, its revision or timestamp differs from the policy, or any frozen file differs.

## Candidate changes

Only defect, compatibility, performance, documentation, and release-operation changes are eligible. Every candidate change record must contain:

- change kind;
- failed gate;
- P0 through P3 severity;
- exact reproduction;
- verification evidence;
- affected frozen contract IDs;
- risk assessment;
- rollback procedure.

P0 and P1 always block release. P2 requires an explicit fix or ship decision, including a player-facing workaround when applicable. P3 may enter known issues only when it does not mislead or block play. A requested feature without a failed release gate waits until after 1.0.

The baseline is a change detector, not approval by itself. A justified change to a frozen surface still requires a new clean candidate, complete affected-gate reruns, a reviewed baseline replacement, and a green supported-platform matrix.
