# Score identity and achievement mode audit

Status: V070-08 complete automated foundation (2026-08-08).

Every current native personal-best entry records enough identity to prevent incomparable runs from sharing a category. The rules engine supplies deterministic rules and mode facts; the product flow supplies run purpose and seed origin because identical rules cannot reveal whether a run came from a person, tutorial, replay, AI, or modified session.

## Run-purpose taxonomy

| Run kind | Seed category | Competitive | Display category |
| --- | --- | --- | --- |
| `normal-human` | `fresh-local` | Yes | `normal-human` |
| `tutorial` | `tutorial-scripted` | No | `tutorial` |
| `practice` | `practice-local` | No | `practice` |
| `seeded-challenge` | `fixed-challenge` | Yes, separate from normal play | `seeded-challenge` |
| `ai` | `ai-simulation` | No | `ai` |
| `replay` | `recorded-replay` | No | `replay` |
| `modified` | `modified-local` | No | `modified` |
| `legacy-0.2` | `legacy-unknown` | No | `Legacy 0.2` |

The pair is closed. Mixing a run kind with another seed category is invalid. Tutorial, practice, AI, replay, modified, and legacy identities cannot update a current personal best. Normal-human and seeded-challenge runs may use identical rules and seeds but still receive distinct category keys.

## Persisted score fields

`personal_bests.json` schema 2 stores these 14 fields for each current entry:

| Field | Purpose |
| --- | --- |
| `rulesetId`, `rulesVersion` | Exact deterministic rules behavior. |
| `modeId`, `modeVersion` | Exact Classic or Vibe contract. |
| `runKindId`, `seedCategoryId` | Product purpose and seed origin from the closed table above. |
| `scoreCategoryId` | Fair scoring category, including Vibe DDA on/off separation. |
| `difficultyPolicyId` | Disclosed fixed difficulty/cadence policy. |
| `adaptationEnabled`, `adaptivePolicyId` | Explicit DDA state and policy identity. |
| `displayCategoryId` | Stable player-facing category identity. |
| `configHash`, `configHashAlgorithm` | Complete effective run configuration identity. |
| `bestScore` | Best terminal score inside only that exact category. |

Score, terminal status, and captured adaptive state may vary without changing the category. Mode, purpose, seed class, rules, difficulty, DDA policy, or effective configuration may not.

Schema 1 personal-best entries lacked explicit mode, purpose, seed, difficulty, and DDA fields. They are preserved under `Legacy 0.2`, remain noncompetitive, and serialize only in schema 2 after migration. The migration does not pretend that missing historical facts are known.

## Native top-ten history and browser

`score_history.json` schema 1 stores at most ten rows per exact category and at most 64 categories. Each row repeats the 14 identity fields above with `score` in place of `bestScore`, then adds a monotonic `sequence`, bounded `playerLabel`, bounded `recordedAtUtc`, and closed `sourceId`. Native terminal rows use `native-terminal`; existing personal-best rows seed history once through the idempotent `native-personal-best-v2` source. A score outside its category's top ten does not displace a retained row.

Keyboard V or Down and controller Down open Local Scores from the menu or run end. Left and right browse categories. The screen identifies competitive state, mode, score category, DDA state, rules version, config prefix, personal best, rank, bounded player label, and source for every retained row. Keyboard and controller use the same navigation, confirm, cancel, and import flow.

Legacy import is deliberately staged and explicit:

1. Copy the frozen Python alpha's `high_scores.json` to `user://imports/high_scores.json`.
2. Open Local Scores and choose import with R or controller North.
3. Review the source path and confirm, or cancel without a write.

The importer accepts only Python schema 1, at most ten rows, and at most 64 KiB. It validates exact fields, bounds and sanitizes local labels, records the source SHA-256, preserves the source byte-for-byte, and refuses repeat import. Imported rules, mode, seed, difficulty, DDA, and config facts are unknown, so every imported row appears only in the noncompetitive `Legacy 0.2` category. It never updates a current personal best.

## Achievement audit

The reviewed source is [`config/achievement_mode_audit_v1.json`](../../config/achievement_mode_audit_v1.json). It accounts for all 25 Python-reference achievements and maps the 17 implemented native rules-local definitions.

Classic remains a minimal no-progression mode under the frozen `classic@1` contract. All 17 native candidates therefore remain explicitly Vibe-only. This is an exclusion decision, not an accidental inability to evaluate Classic metrics. Score thresholds are not shared because Classic and Vibe use different score models; combo, near-miss, and power conditions also reference systems Classic does not have.

| Reference achievement | Native identity | Classic | Vibe | Decision |
| --- | --- | --- | --- | --- |
| `baby_steps` | None | Excluded | Excluded | Defer profile progression |
| `just_a_taste` | Same | Excluded | Eligible | Keep Vibe-only |
| `wrap_around` | Same | Excluded | Eligible | Keep Vibe-only |
| `powered_up` | Same | Excluded | Eligible | Keep Vibe-only |
| `quick_reflexes` | Same | Excluded | Eligible | Keep Vibe-only |
| `getting_longer` | Same | Excluded | Eligible | Keep Vibe-only |
| `first_bite` | Same | Excluded | Eligible | Keep Vibe-only |
| `century` | Same | Excluded | Eligible | Keep Vibe-only |
| `high_roller` | Same | Excluded | Eligible | Keep Vibe-only |
| `legend` | Same | Excluded | Eligible | Keep Vibe-only |
| `combo_starter` | Same | Excluded | Eligible | Keep Vibe-only |
| `combo_king` | Same | Excluded | Eligible | Keep Vibe-only |
| `growing_up` | `growing_strong` | Excluded | Eligible | Keep Vibe-only |
| `long_boi` | `serpent` | Excluded | Eligible | Keep Vibe-only |
| `newcomer` | None | Excluded | Excluded | Defer profile progression |
| `regular` | None | Excluded | Excluded | Defer profile progression |
| `veteran` | None | Excluded | Excluded | Defer profile progression |
| `close_call` | Same | Excluded | Eligible | Keep Vibe-only |
| `power_hungry` | Same | Excluded | Eligible | Keep Vibe-only |
| `survivor` | `endurance` | Excluded | Eligible | Keep Vibe-only |
| `marathon_runner` | `marathon` | Excluded | Eligible | Keep Vibe-only |
| `iron_will` | None | Excluded | Excluded | Defer redundant threshold |
| `snake_charmer` | None | Excluded | Excluded | Defer redundant threshold |
| `night_owl` | None | Excluded | Excluded | Remove wall-clock condition |
| `early_bird` | None | Excluded | Excluded | Remove wall-clock condition |

Rules-local candidates cannot unlock in tutorial, practice, AI, replay, modified, or legacy contexts even when a matching metric snapshot exists. Product flows must apply the same run-purpose eligibility before writing progression.

`score-identity-qualification-v1` locks all eight contexts, two competitive contexts, 14 personal-best fields, 18 history fields, a ten-row category cap, schema-1 personal-best migration, the 25-row audit hash, 17 native Vibe candidates, zero Classic candidates, and eight explicitly excluded reference-only definitions. `score-browser-qualification-v1` adds raw keyboard/controller routes, category navigation, two-step confirmation, lossless cancellation, exact-once import, source preservation, visible noncompetitive legacy classification, existing-personal-best visibility, and shared reset/recovery ownership.
