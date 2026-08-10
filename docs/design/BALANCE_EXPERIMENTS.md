# Balance experiment discipline

Status: V070-07 guard active, targets pending human review (2026-08-08).

The machine-readable experiment registry is [`config/balance_experiments_v1.json`](../../config/balance_experiments_v1.json). It currently contains no target ranges and no experiments. That empty state is deliberate: AI simulation and an unreviewed collection of local run facts cannot authorize human balance targets.

No starvation, speed, combo, power frequency, power weight, near-miss, or DDA-bound value may be described as tuned until the V070-06 structured-human evidence establishes a target range and the complete experiment contract below is retained.

## Eligible families

Each experiment changes exactly one of these families:

- `starvation`;
- `speed`;
- `combo`;
- `power-frequency`;
- `power-weights`;
- `near-miss`;
- `dda-bounds`.

A change that crosses families must be split into separate experiments with separate hypotheses and decisions. Presentation-only work is not a balance experiment, but it must still prove unchanged rules and score identity.

## Required hypothesis

Before changing a value, state one intended effect on competence, autonomy, tension, or recovery. Define the target metric and accepted range from reviewed human and automated evidence. Raising average score is not an accepted intent by itself.

An experiment record requires all 18 registry fields:

| Field | Purpose |
| --- | --- |
| `experimentId` | Stable review identifier. |
| `status` | Planned, running, reviewed, or blocked lifecycle state. |
| `balanceFamily` | Exactly one eligible family. |
| `intendedExperienceEffect` | Competence, autonomy, tension, or recovery. |
| `hypothesis` | Falsifiable reason the change should produce that effect. |
| `targetMetric` | Metric observed in both the baseline and candidate where applicable. |
| `targetRange` | Range written before the candidate change. |
| `baselineConfigHash` | Exact effective baseline configuration identity. |
| `candidateConfigHash` | Exact one-family candidate configuration identity. |
| `rulesetId` | Ruleset identity. |
| `rulesVersion` | Rules behavior version. |
| `seedCorpusSha256` | Reviewed fixed-seed corpus identity. |
| `automatedResultSha256` | Immutable result bundle identity. |
| `humanScenarioId` | Relevant scenario from the structured protocol. |
| `humanEvidenceReferences` | De-identified observation references, including negative and neutral outcomes. |
| `result` | Observed automated and human result, separate from interpretation. |
| `decision` | `keep`, `revert`, or `blocked`. |
| `decisionReason` | Evidence-backed interpretation supporting the decision. |

## Execution order

1. Review V070-06 observations and establish a target range.
2. Register one family, intended effect, hypothesis, metric, and range before editing the candidate.
3. Record baseline and candidate config hashes, rules identity, and fixed-corpus hash.
4. Run the complete fixed corpus and compare distributions, outliers, invariant results, and effect size.
5. Run the relevant human scenario on the exact candidate build.
6. Preserve observation separately from interpretation.
7. Decide `keep`, `revert`, or `blocked` and record why.
8. If kept, rerun every affected qualification and the fresh human validation flow.

The current `balance-experiment-guard-v1` evidence must report zero human target ranges, zero experiments, and `tuningEligible: false`. This is a passing governance state, not a completed tuning milestone.
