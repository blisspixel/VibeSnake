# Custom AI Personalities

JSON files in `custom/` are loaded as spectator channels at game startup. Files in `examples/` are templates and are not loaded automatically.

```json
{
  "name": "Route Planner",
  "description": "Prefers safe lines and deliberate turns.",
  "aggression": 0.45,
  "risk_tolerance": 0.2,
  "patience": 0.85,
  "greed": 0.35,
  "chaos": 0.02,
  "power_up_priority": 0.4,
  "color": [80, 180, 255]
}
```

Trait values are intended to be between 0.0 and 1.0. RGB channels should be integers between 0 and 255. The current loader does not validate or clamp custom values, so malformed files are skipped or may behave unexpectedly.

The filename stem becomes the personality key. Restart the game after adding or changing a file, then press L at the main menu to browse channels.

See the full [AI player guide](../../docs/design/AI_PLAYERS.md) for decision behavior, testing limitations, and planned validation.
