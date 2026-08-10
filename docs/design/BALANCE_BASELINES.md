# Observed Balance Baselines

This document records the first deterministic AI simulation observations for `vibesnake-core@4`. They are descriptive baselines, not balance targets and not evidence that a mode is fair, fun, readable, or satisfying to a person.

## Baseline contract

- Evidence kind: `observed-balance-baseline-evidence-v1`
- Classification: `ai-simulation-observation`
- Fixed corpus: 100 reviewed seeds, 0 through 99
- Matrix: Classic, Vibe with DDA, and Vibe without DDA across all nine laboratory policies
- Sample size: 100 runs per variant and policy, 2,700 runs total
- Limit: 900 rules steps per run
- Distribution hash: `b535409a0b76a6ce5911497de1de76107f59b70516137cf9a83c8dc02da0f792`
- Reference AI policies: safe survivor, greedy food, risk seeking, power hunting, boundary walker, and seeded personality
- Additional stress instruments: idle, input chaos, and replay ghost
- Human target ranges: none established

The checked-in baseline contract is `config/balance_baseline_v1.json`. The native suite recreates `TestResults/native/balance_baselines.json`, records all 2,700 per-run state hashes and metrics, and rejects any distribution whose canonical hash differs. The same controller seed is used for a policy and gameplay seed across all three variants so mode comparisons do not silently change agent behavior.

The 2026-08-08 refresh intentionally changes only the Vibe distributions after `power-decisions-v1` connected all nine deterministic offers and family anti-redundancy to the product mode. Classic remains unchanged. This is a descriptive drift baseline, not a tuning decision or human target.

Percentiles use nearest-rank selection over each 100-run group. `Food/1k` is aggregate food per 1,000 observed steps. `Power E/P/A` means spawn encounters, pickups, and activations across the group. `Death S/C` means starvation and self-collision. `Outcome cap/dead/won` separates runs still active at the 900-step observation cap from terminal outcomes.

## Observed distributions

### Classic

| Policy | Score p50/p95 | Steps p50/p95 | Final length p50/p95/max | Food/1k | Combo p95/max | Power E/P/A | Death S/C | Outcome cap/dead/won |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `safe-survivor-v1` | 360/400 | 900/900 | 37/41/43 | 39.764 | 0/0 | 0/0/0 | 0/8 | 92/8/0 |
| `greedy-food-v1` | 360/400 | 900/900 | 37/41/43 | 40.341 | 0/0 | 0/0/0 | 0/19 | 81/19/0 |
| `risk-seeking-v1` | 20/20 | 900/900 | 3/3/4 | 2.267 | 0/0 | 0/0/0 | 0/0 | 100/0/0 |
| `power-hunting-v1` | 360/400 | 900/900 | 37/41/43 | 40.341 | 0/0 | 0/0/0 | 0/19 | 81/19/0 |
| `boundary-walker-v1` | 0/0 | 900/900 | 1/1/2 | 0.011 | 0/0 | 0/0/0 | 0/0 | 100/0/0 |
| `idle-v1` | 0/0 | 900/900 | 1/1/2 | 0.056 | 0/0 | 0/0/0 | 0/0 | 100/0/0 |
| `input-chaos-v1` | 0/10 | 900/900 | 1/2/3 | 0.222 | 0/0 | 0/0/0 | 0/0 | 100/0/0 |
| `personality-seeded-v1` | 150/380 | 900/900 | 16/39/42 | 19.658 | 0/0 | 0/0/0 | 0/10 | 90/10/0 |
| `replay-ghost-v1` | 360/400 | 900/900 | 37/41/43 | 40.341 | 0/0 | 0/0/0 | 0/19 | 81/19/0 |

### Vibe with DDA

| Policy | Score p50/p95 | Steps p50/p95 | Final length p50/p95/max | Food/1k | Combo p95/max | Power E/P/A | Death S/C | Outcome cap/dead/won |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `safe-survivor-v1` | 3277/3908 | 900/900 | 36/40/42 | 39.635 | 39/41 | 290/10/10 | 0/8 | 92/8/0 |
| `greedy-food-v1` | 3425/4080 | 900/900 | 37/41/45 | 40.303 | 40/44 | 284/12/12 | 0/13 | 87/13/0 |
| `risk-seeking-v1` | 34/39 | 848/874 | 3/3/4 | 2.404 | 2/3 | 200/0/0 | 100/0 | 0/100/0 |
| `power-hunting-v1` | 2355/3659 | 900/900 | 33/40/45 | 38.657 | 37/41 | 287/195/199 | 0/8 | 92/8/0 |
| `boundary-walker-v1` | 0/0 | 800/800 | 1/1/2 | 0.012 | 0/1 | 201/4/5 | 99/0 | 1/99/0 |
| `idle-v1` | 0/0 | 800/826 | 1/1/2 | 0.062 | 0/1 | 201/3/4 | 99/0 | 1/99/0 |
| `input-chaos-v1` | 0/13 | 800/900 | 1/2/4 | 0.245 | 1/3 | 216/3/4 | 84/0 | 16/84/0 |
| `personality-seeded-v1` | 743/3111 | 900/900 | 14/37/38 | 19.8 | 34/38 | 268/162/173 | 20/11 | 69/31/0 |
| `replay-ghost-v1` | 3425/4080 | 900/900 | 37/41/45 | 40.303 | 40/44 | 284/12/12 | 0/13 | 87/13/0 |

### Vibe without DDA

| Policy | Score p50/p95 | Steps p50/p95 | Final length p50/p95/max | Food/1k | Combo p95/max | Power E/P/A | Death S/C | Outcome cap/dead/won |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `safe-survivor-v1` | 3277/3908 | 900/900 | 36/40/42 | 39.635 | 39/41 | 290/10/10 | 0/8 | 92/8/0 |
| `greedy-food-v1` | 3425/4080 | 900/900 | 37/41/45 | 40.303 | 40/44 | 284/12/12 | 0/13 | 87/13/0 |
| `risk-seeking-v1` | 34/39 | 649/674 | 3/3/4 | 3.144 | 2/3 | 200/0/0 | 100/0 | 0/100/0 |
| `power-hunting-v1` | 2355/3659 | 900/900 | 33/40/45 | 38.657 | 37/41 | 287/195/199 | 0/8 | 92/8/0 |
| `boundary-walker-v1` | 0/0 | 600/600 | 1/1/2 | 0.017 | 0/1 | 201/4/5 | 99/0 | 1/99/0 |
| `idle-v1` | 0/0 | 600/600 | 1/1/2 | 0.083 | 0/1 | 200/1/1 | 100/0 | 0/100/0 |
| `input-chaos-v1` | 0/13 | 600/900 | 1/2/3 | 0.207 | 1/1 | 207/1/1 | 93/0 | 7/93/0 |
| `personality-seeded-v1` | 743/3111 | 900/900 | 14/37/38 | 21.019 | 34/38 | 262/160/172 | 26/11 | 63/37/0 |
| `replay-ghost-v1` | 3425/4080 | 900/900 | 37/41/45 | 40.303 | 40/44 | 284/12/12 | 0/13 | 87/13/0 |

## Interpretation boundary

The fixed policies reveal reproducible mechanics, not intended player performance. For example, the idle and boundary policies expose the exact 600-step fixed starvation boundary and the 800-step support extension, while power hunting demonstrates that Vibe power offers can become actual route decisions. Classic correctly reports no hunger, combo, or power activity. These observations can identify drift and guide later hypotheses.

No row is an accepted score, survival, food-rate, death-rate, combo, or power target. Human targets may be proposed only after V070-05 through V070-07 collect privacy-bounded local facts and reviewed formative, targeted, and fresh validation playtests. Any later target must remain separate from this AI-only evidence and record the decision that introduced it.
