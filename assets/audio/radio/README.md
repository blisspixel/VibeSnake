# Vibe Snake Radio Library

This directory is the offline GTA-style radio catalog for the Python reference
player. Tracks are discovered by filename prefix and assigned to the eight
in-world stations defined in `src/vibesnake/audio/radio_manager.py`.

| Station key | Example prefixes |
| --- | --- |
| `flow_signal` | `flow_signal_`, `ambient_`, `chill_` |
| `chaos_theory` | `chaos_theory_`, `jazz_` |
| `global_coil` | `global_coil_`, `world_`, `soul_` |
| `ourotron` | `ourotron_`, `synthwave_` |
| `the_pit` | `the_pit_`, `dance_` |
| `the_bureau` | `the_bureau_` |
| `the_strike` | `the_strike_`, `rock_` |
| `underground_scales` | `underground_scales_`, `hiphop_` |

These tracks are part of the Vibe Snake world and soundtrack. License and
attribution follow the repository Apache-2.0 terms and root `NOTICE`. Station
lore lives in `docs/design/WORLD_BIBLE.md` and the creative plan in
`config/radio_network_plan.json`.

Runtime discovery needs no config: place matching `*.mp3` files here and start
the game. Without this directory the player still runs with procedural SFX only.
