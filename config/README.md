# Project Configuration and Content Policy

This directory contains authored project inputs and generated content authority. Player preferences do not belong here.

| File | Ownership |
| --- | --- |
| `content_policy.json` | Human-reviewed source classification, rights, pack intent, and shipping state |
| `content_inventory.json` | Generated hashes, sizes, integrity results, duplicates, policy metadata, and export eligibility |
| `radio_network_plan.json` | Creative production plan for the eight-station network |
| `snake_news_segments.json` | Authored Bureau segment concepts and generation parameters |

Historical track-production state belongs in the ignored local audio workspace,
not in this public configuration directory. Regenerate or verify the source
inventory with `python scripts/content_inventory.py`. Normal runtime settings use
`assets/config/config.json`, while player choices use the operating system's
user-data directory.
