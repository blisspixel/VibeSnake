# Python Test Suite

`tests/` contains deterministic automated tests only. Interactive listening, visual review, paid APIs, and other perceptual checks belong under `scripts/manual/`.

| Directory | Scope |
| --- | --- |
| `audio/` | Radio discovery, control, failure recovery, and playback orchestration |
| `core/` | Movement, food, scoring, persistence, achievements, metrics, and rendering contracts owned by core models |
| `fixtures/shared/` | Versioned Python-to-C# parity scenarios |
| `input/` | Keyboard, mouse, and controller mapping behavior |
| `integration/` | Game construction, state flow, rendering, HUD, and full power behavior |
| `powerups/` | Individual power lifecycle contracts |
| `qa/` | Invariants, policies, simulation, reports, content, and shared traces |
| `rendering/` | Menus, effects, and procedural backgrounds |

Every test is isolated from real player data, network services, ambient randomness, and visible display or audio devices. A failing seed or fixed defect becomes a permanent regression when its expectation is valid.
