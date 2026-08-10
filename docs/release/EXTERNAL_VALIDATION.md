# Controlled External Validation

[Release state](README.md) | [Roadmap](../../ROADMAP.md) | [Human playtesting](../design/HUMAN_PLAYTESTING.md) | [Manual product matrix](MANUAL_PRODUCT_MATRIX.md)

Status: V090-08 handoff qualified, controlled participant execution pending.

This is the release-candidate validation loop for people using real packaged artifacts outside the repository. The machine-readable authority is [`config/qa_external_validation_v1.json`](../../config/qa_external_validation_v1.json). Its validator proves that retained records are structurally complete and connected to exact clean candidates. It cannot prove what a participant understood or felt.

## Entry gate

Do not distribute a candidate until all of these are true:

1. The source revision is clean and fixed to a lowercase 40-character revision.
2. Windows x64, macOS Universal, and Linux x64 artifact SHA-256 values are retained in the candidate ledger.
3. The native Release matrix, manual product matrix protocol, accessibility guide, and structured human-playtest protocol match the candidate.
4. Consent handling and controlled distribution are prepared outside the public repository.
5. No known P0 or P1 defect permits the affected flow to begin.

The checked-in handoff deliberately has no candidate ledger or participant sessions. It reports `externalValidationComplete: false` and `releaseAcceptance: false` until the retained execution records are supplied.

## Required cohorts

The final candidate must include all four cohorts:

| Cohort | Boundary |
| --- | --- |
| `clean-install-fresh-keyboard` | Clean install, no repository exposure, keyboard flow |
| `clean-install-fresh-controller` | Clean install, no repository exposure, Xbox-layout or PlayStation-layout controller flow |
| `accessibility-focused-fresh` | Clean install, no repository exposure, at least one non-default accessibility profile |
| `returning-regression` | Clean install of the final candidate after earlier observations or repairs |

The final candidate must also cover all three packaged artifact platforms and retain keyboard, mouse, Xbox-layout controller, and PlayStation-layout controller use. The protocol does not turn a small arbitrary participant count into evidence of broad appeal. Continue until the required cohorts and platforms are covered, material repairs are rechecked, and blocking findings are resolved.

## Fresh-participant checks

Ask after the observed play, without teaching the intended answer first. Every fresh session must record pass, fail, or blocked for all six checks:

1. Explain the death cause.
2. Identify an available recovery.
3. Describe a route decision caused by a power.
4. Recognize escalation.
5. State whether another run is wanted.
6. Record why or why not another run is wanted.

Negative and neutral answers are valid observations and must not be discarded. A fresh-session fail or blocked result prevents external-validation acceptance.

## Retained record set

Keep one directory for de-identified records. The validator receives three inputs together:

- `sessions/`: one strict JSON document per observed session, plus every relative evidence file it references;
- `candidate-ledger.json`: an ordered list of clean candidate revisions and the exact artifact hash for each platform;
- `findings.json`: structured defect, comprehension, accessibility, and crash findings with severity and resolution.

Each session retains:

- pseudonymous session and participant IDs;
- cohort, candidate revision, artifact platform and SHA-256, and application version;
- clean-install and repository-exposure state;
- input devices and accessibility profiles;
- controlled-distribution ID, separate-consent confirmation, and UTC execution time;
- a de-identified retained outcome file for each defect, comprehension, accessibility, and crash report family;
- all six comprehension results, crash observation state, finding IDs, and supporting evidence paths.

Use safe forward-slash relative paths. Every referenced file must exist relative to the JSON document that names it.

## Candidate and repair loop

The first ledger row has no predecessor. Every later row must:

1. identify the immediately previous revision in `supersedesRevision`;
2. state that its source tree was clean;
3. identify the finding or findings that caused the replacement;
4. list every affected automated or human gate;
5. map every affected gate ID to one or more retained rerun evidence files.

A fixed finding must name its resolution revision, link to the replacement candidate that lists it as a trigger, and retain verification evidence. Sessions cannot use an undeclared revision or an artifact hash different from the candidate ledger.

## Finding decisions

Use P0 through P3 from the release roadmap. P0 and P1 findings cannot receive a ship decision. P2 findings must close through a fix or an explicit ship decision with a player-facing workaround. A closed finding always needs retained verification evidence. Any open P0, P1, or P2 blocks acceptance.

## Validation command

First qualify the repository handoff:

```powershell
python scripts/check_external_validation.py `
  --output TestResults/external-validation/external_validation_handoff.json
```

Then validate a retained execution set outside the checkout:

```powershell
python scripts/check_external_validation.py `
  --sessions C:\retained-vibesnake-evidence\external-validation\sessions `
  --candidate-ledger C:\retained-vibesnake-evidence\external-validation\candidate-ledger.json `
  --findings C:\retained-vibesnake-evidence\external-validation\findings.json `
  --output C:\retained-vibesnake-evidence\external-validation\decision.json
```

The gate accepts only the final candidate with all cohorts, all artifact platforms, all four input device classes, complete fresh-participant comprehension, exact candidate and artifact identity, retained report files, a valid repair chain, and no unresolved blocking finding.

## Privacy boundary

Consent records stay separate and outside the repository. Session JSON uses IDs matching `external-[0-9]{3}` and contains no names, accounts, contact details, raw input, raw timing, device serials, private system paths, or unrelated device information. Review and de-identify written reports, screenshots, videos, and logs before retaining them. Do not commit participant evidence to the public source tree.
