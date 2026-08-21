# Project Configuration and Content Policy

This directory contains authored project inputs and generated content authority. Player preferences do not belong here.

| File | Ownership |
| --- | --- |
| `content_policy.json` | Human-reviewed source classification, rights, pack intent, and shipping state |
| `content_inventory.json` | Generated hashes, sizes, integrity results, duplicates, policy metadata, and export eligibility |
| `content_curation_v1.json` | Exact per-station pending, approved, and rejected candidate decisions bound to one inventory policy hash |
| `qa_seed_corpora.json` | Reviewed fixed, exploratory, and previous-failure deterministic laboratory seeds |
| `qa_balance_baseline_seeds.json` | Reviewed 100-seed corpus for observed balance baselines |
| `balance_baseline_v1.json` | Hash-locked AI simulation baseline with no human target ranges |
| `balance_experiments_v1.json` | Target-first, one-family experiment guard; intentionally empty until human review establishes ranges |
| `qa_human_playtest_protocol.json` | Closed V070-06 cohorts, stages, scenarios, observation fields, privacy rules, and automated handoff allowlist |
| `qa_manual_product_matrix_v1.json` | Exact V090-07 Release-derived candidate, platform artifact, flow, input, settings-profile, session, and release-acceptance contract |
| `qa_external_validation_v1.json` | Exact V090-08 controlled distribution, cohort, comprehension, report, clean-candidate, finding, repair, and privacy contract |
| `release_materials_v1.json` | Exact V090-09 required documents, platform and input disclosures, candidate media roles, claim identities, retained hashes, and publication rules |
| `release_rehearsal_v1.json` | Exact V090-10 staged artifacts, 33 platform-operation cells, withdrawal, user-data preservation, retained hashes, and release-authority contract |
| `stable_promotion_v1.json` | Exact 1.0 protected tag, upstream acceptance, public artifacts, optional pack, provenance, install, preservation, and compatibility contract |
| `power_decision_contract_v1.json` | V070-09 nine-power families, lifecycle aggregates, seeded synergy scenarios, privacy boundary, and default-off Mutation Fork decision gate |
| `achievement_mode_audit_v1.json` | Reviewed Classic/Vibe eligibility and exclusion decision for all 25 reference achievements |
| `release_signing_policy.json` | Strict non-secret platform signing, notarization, checksum, and provenance routes |
| `candidate_freeze_policy_v1.json` | Inactive V090-01 freeze boundary, prerequisites, frozen contract surfaces, permitted change classes, and required change evidence |
| `radio_network_plan.json` | Creative production plan for the eight-station network |
| `snake_news_segments.json` | Authored Bureau segment concepts and generation parameters |

Historical track-production state belongs in the ignored local audio workspace,
not in this public configuration directory. Regenerate or verify the source
inventory with `python scripts/content_inventory.py`. Normal runtime settings use
`assets/config/config.json`, while player choices use the operating system's
user-data directory.
