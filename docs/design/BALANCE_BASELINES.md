# Observed Balance Baselines

This document records the first deterministic AI simulation observations for `vibesnake-core@4`. They are descriptive baselines, not balance targets and not evidence that a mode is fair, fun, readable, or satisfying to a person.

## Baseline contract

- Evidence kind: `observed-balance-baseline-evidence-v1`
- Classification: `ai-simulation-observation`
- Fixed corpus: 100 reviewed seeds, 0 through 99
- Matrix: Classic, Vibe with DDA, and Vibe without DDA across all nine laboratory policies
- Sample size: 100 runs per variant and policy, 2,700 runs total
- Limit: 900 rules steps per run
- Distribution hash: `00e12226dee8f50b7fd7124ebf16dcbb4b20fee72691dba6723b971c2c9ab952`
- Reference AI policies: safe survivor, greedy food, risk seeking, power hunting, boundary walker, and seeded personality
- Additional stress instruments: idle, input chaos, and replay ghost
- Human target ranges: none established

The checked-in baseline contract is `config/balance_baseline_v1.json`. The native suite recreates `TestResults/native/balance_baselines.json`, records all 2,700 per-run state hashes and metrics, and rejects any distribution whose canonical hash differs. The same controller seed is used for a policy and gameplay seed across all three variants so mode comparisons do not silently change agent behavior.

The 2026-08-08 refresh intentionally changed only the Vibe distributions after `power-decisions-v1` connected all nine deterministic offers and family anti-redundancy to the product mode. The 2026-08-21 refresh again changes only Vibe after product geodesic placement moved offers off the shortest food route; Classic remains unchanged. Food-seeking policies pick up fewer powers; power-hunting still converts most encounters. This is a descriptive drift baseline, not a tuning decision or human target.

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
| `safe-survivor-v1` | 3271/3906 | 900/900 | 36/40/41 | 39.499 | 39/40 | 291/7/7 | 0/7 | 93/7/0 |
| `greedy-food-v1` | 3423/3933 | 900/900 | 37/40/45 | 40.132 | 39/44 | 284/9/9 | 0/13 | 87/13/0 |
| `risk-seeking-v1` | 34/39 | 848/874 | 3/3/4 | 2.404 | 2/3 | 200/0/0 | 100/0 | 0/100/0 |
| `power-hunting-v1` | 2244/3600 | 900/900 | 33/40/45 | 38.485 | 37/44 | 279/192/194 | 0/14 | 86/14/0 |
| `boundary-walker-v1` | 0/0 | 800/800 | 1/1/2 | 0.012 | 0/1 | 201/7/8 | 99/0 | 1/99/0 |
| `idle-v1` | 0/13 | 800/850 | 1/2/3 | 0.112 | 1/1 | 202/6/6 | 98/0 | 2/98/0 |
| `input-chaos-v1` | 0/13 | 800/900 | 1/2/3 | 0.208 | 1/1 | 216/9/11 | 84/0 | 16/84/0 |
| `personality-seeded-v1` | 712/3313 | 900/900 | 17/38/41 | 19.95 | 35/37 | 269/157/160 | 18/10 | 72/28/0 |
| `replay-ghost-v1` | 3423/3933 | 900/900 | 37/40/45 | 40.132 | 39/44 | 284/9/9 | 0/13 | 87/13/0 |

### Vibe without DDA

| Policy | Score p50/p95 | Steps p50/p95 | Final length p50/p95/max | Food/1k | Combo p95/max | Power E/P/A | Death S/C | Outcome cap/dead/won |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `safe-survivor-v1` | 3271/3906 | 900/900 | 36/40/41 | 39.499 | 39/40 | 291/7/7 | 0/7 | 93/7/0 |
| `greedy-food-v1` | 3423/3933 | 900/900 | 37/40/45 | 40.132 | 39/44 | 284/9/9 | 0/13 | 87/13/0 |
| `risk-seeking-v1` | 34/39 | 649/674 | 3/3/4 | 3.144 | 2/3 | 200/0/0 | 100/0 | 0/100/0 |
| `power-hunting-v1` | 2244/3600 | 900/900 | 33/40/45 | 38.485 | 37/44 | 279/192/194 | 0/14 | 86/14/0 |
| `boundary-walker-v1` | 0/0 | 600/600 | 1/1/2 | 0.017 | 0/1 | 201/4/5 | 99/0 | 1/99/0 |
| `idle-v1` | 0/13 | 600/627 | 1/2/3 | 0.116 | 1/1 | 201/3/3 | 99/0 | 1/99/0 |
| `input-chaos-v1` | 0/13 | 600/900 | 1/2/3 | 0.206 | 1/1 | 208/6/7 | 92/0 | 8/92/0 |
| `personality-seeded-v1` | 712/3313 | 900/900 | 17/38/41 | 21.062 | 35/37 | 262/156/161 | 25/10 | 65/35/0 |
| `replay-ghost-v1` | 3423/3933 | 900/900 | 37/40/45 | 40.132 | 39/44 | 284/9/9 | 0/13 | 87/13/0 |

## Interpretation boundary

The fixed policies reveal reproducible mechanics, not intended player performance. For example, the idle and boundary policies expose the exact 600-step fixed starvation boundary and the 800-step support extension, while power hunting demonstrates that Vibe power offers can become actual route decisions. Classic correctly reports no hunger, combo, or power activity. These observations can identify drift and guide later hypotheses.

No row is an accepted score, survival, food-rate, death-rate, combo, or power target. Human targets may be proposed only after V070-05 through V070-07 collect privacy-bounded local facts and reviewed formative, targeted, and fresh validation playtests. Any later target must remain separate from this AI-only evidence and record the decision that introduced it.
