# Python Test Suite

`tests/` contains deterministic automated tests for the frozen Python behavior oracle. Native product and repository contracts live under `native/tests/`. Interactive listening and physical review use the native commands described in `scripts/manual/README.md` and retain their evidence separately from automated tests.

| Directory | Scope |
| --- | --- |
| `audio/` | Radio discovery, control, failure recovery, and playback orchestration |
| `core/` | Movement, food, scoring, persistence, achievements, metrics, and rendering contracts owned by core models |
| `fixtures/shared/` | Versioned Python-to-C# parity scenarios, including frozen Python-origin vectors with native freshness ownership |
| `input/` | Keyboard, mouse, and controller mapping behavior |
| `integration/` | Game construction, state flow, rendering, HUD, and full power behavior |
| `powerups/` | Individual power lifecycle contracts |
| `qa/` | Invariants, policies, simulation, reports, and shared traces |
| `rendering/` | Menus, effects, and procedural backgrounds |

Every test is isolated from real player data, network services, ambient randomness, and visible display or audio devices. A failing seed or fixed defect becomes a permanent regression when its expectation is valid.
