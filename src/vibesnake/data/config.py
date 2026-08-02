"""Validated runtime configuration with safe embedded defaults."""

import json
from copy import deepcopy
from pathlib import Path


SCHEMA_VERSION = 1

DEFAULT_CONFIG = {
    "schema_version": SCHEMA_VERSION,
    "resolution_preset": "classic",
    "resolutions": {
        "hd": {"grid_width": 64, "grid_height": 33, "description": "1280x720 (16:9 HD)"},
        "classic": {"grid_width": 40, "grid_height": 30, "description": "800x660 (4:3 Classic)"},
        "fullhd": {"grid_width": 96, "grid_height": 51, "description": "1920x1080 (16:9 Full HD)"},
        "ultrawide": {"grid_width": 106, "grid_height": 42, "description": "2120x900 (21:9 Ultrawide)"},
    },
    "grid_width": 40,
    "grid_height": 30,
    "cell_size": 20,
    "fps": 60,
    "logic_tick": 0.1,
    "colors": {
        "background": [50, 153, 213],
        "snake": [0, 255, 0],
        "food": [213, 50, 80],
        "text": [255, 255, 255],
    },
    "powerups": {
        "spawn_interval": 15.0,
        "enabled": True,
        "visible_duration": 6.0,
    },
    "sound": {
        "enabled": True,
        "volume": 0.8,
    },
}


def merge_dicts(default: dict, override: dict) -> dict:
    """Recursively merge an override into a base dictionary."""
    for key, value in override.items():
        if isinstance(value, dict) and isinstance(default.get(key), dict):
            default[key] = merge_dicts(default[key], value)
        else:
            default[key] = value
    return default


def _warn(key: str, value, fallback) -> None:
    print(f"[Config] Invalid {key}={value!r}; using {fallback!r}")


def _positive_number(value) -> bool:
    return not isinstance(value, bool) and isinstance(value, (int, float)) and value > 0


def _positive_int(value) -> bool:
    return not isinstance(value, bool) and isinstance(value, int) and value > 0


def _rgb(value) -> bool:
    return (
        isinstance(value, list)
        and len(value) == 3
        and all(not isinstance(channel, bool) and isinstance(channel, int) and 0 <= channel <= 255 for channel in value)
    )


def validate_config(config: dict) -> dict:
    """Return a complete, range-checked configuration dictionary."""
    result = deepcopy(config)
    defaults = DEFAULT_CONFIG

    schema_version = result.get("schema_version", 0)
    if not isinstance(schema_version, int) or isinstance(schema_version, bool):
        _warn("schema_version", schema_version, SCHEMA_VERSION)
        schema_version = SCHEMA_VERSION
    if schema_version > SCHEMA_VERSION:
        print(f"[Config] Schema {schema_version} is newer than supported {SCHEMA_VERSION}; using embedded defaults")
        return deepcopy(DEFAULT_CONFIG)
    result["schema_version"] = SCHEMA_VERSION

    for key in ("grid_width", "grid_height", "cell_size", "fps"):
        if not _positive_int(result.get(key)):
            _warn(key, result.get(key), defaults[key])
            result[key] = defaults[key]

    if not _positive_number(result.get("logic_tick")) or result["logic_tick"] > 1.0:
        _warn("logic_tick", result.get("logic_tick"), defaults["logic_tick"])
        result["logic_tick"] = defaults["logic_tick"]

    colors = result.get("colors")
    if not isinstance(colors, dict):
        _warn("colors", colors, defaults["colors"])
        colors = deepcopy(defaults["colors"])
        result["colors"] = colors
    for key, fallback in defaults["colors"].items():
        if not _rgb(colors.get(key)):
            _warn(f"colors.{key}", colors.get(key), fallback)
            colors[key] = deepcopy(fallback)

    powerups = result.get("powerups")
    if not isinstance(powerups, dict):
        _warn("powerups", powerups, defaults["powerups"])
        powerups = deepcopy(defaults["powerups"])
        result["powerups"] = powerups
    if not _positive_number(powerups.get("spawn_interval")):
        _warn("powerups.spawn_interval", powerups.get("spawn_interval"), defaults["powerups"]["spawn_interval"])
        powerups["spawn_interval"] = defaults["powerups"]["spawn_interval"]
    if not isinstance(powerups.get("enabled"), bool):
        _warn("powerups.enabled", powerups.get("enabled"), defaults["powerups"]["enabled"])
        powerups["enabled"] = defaults["powerups"]["enabled"]
    if not _positive_number(powerups.get("visible_duration")):
        _warn(
            "powerups.visible_duration",
            powerups.get("visible_duration"),
            defaults["powerups"]["visible_duration"],
        )
        powerups["visible_duration"] = defaults["powerups"]["visible_duration"]

    sound = result.get("sound")
    if not isinstance(sound, dict):
        _warn("sound", sound, defaults["sound"])
        sound = deepcopy(defaults["sound"])
        result["sound"] = sound
    if not isinstance(sound.get("enabled"), bool):
        _warn("sound.enabled", sound.get("enabled"), defaults["sound"]["enabled"])
        sound["enabled"] = defaults["sound"]["enabled"]
    volume = sound.get("volume")
    if isinstance(volume, bool) or not isinstance(volume, (int, float)) or not 0.0 <= volume <= 1.0:
        _warn("sound.volume", volume, defaults["sound"]["volume"])
        sound["volume"] = defaults["sound"]["volume"]
    else:
        sound["volume"] = float(volume)

    resolutions = result.get("resolutions")
    if not isinstance(resolutions, dict):
        _warn("resolutions", resolutions, defaults["resolutions"])
        resolutions = deepcopy(defaults["resolutions"])
    valid_resolutions = {}
    for name, resolution in resolutions.items():
        if (
            isinstance(name, str)
            and isinstance(resolution, dict)
            and _positive_int(resolution.get("grid_width"))
            and _positive_int(resolution.get("grid_height"))
        ):
            valid_resolutions[name] = deepcopy(resolution)
    if not valid_resolutions:
        _warn("resolutions", resolutions, defaults["resolutions"])
        valid_resolutions = deepcopy(defaults["resolutions"])
    result["resolutions"] = valid_resolutions

    preset = result.get("resolution_preset", defaults["resolution_preset"])
    if preset != "custom" and preset not in valid_resolutions:
        _warn("resolution_preset", preset, defaults["resolution_preset"])
        preset = defaults["resolution_preset"]
        if preset not in valid_resolutions:
            valid_resolutions[preset] = deepcopy(defaults["resolutions"][preset])
    result["resolution_preset"] = preset
    if preset != "custom":
        result["grid_width"] = valid_resolutions[preset]["grid_width"]
        result["grid_height"] = valid_resolutions[preset]["grid_height"]

    return result


def load_config(path=None) -> dict:
    """Load, merge, and validate a JSON configuration file."""
    if path is None:
        project_root = Path(__file__).resolve().parents[3]
        path = project_root / "assets" / "config" / "config.json"
    else:
        path = Path(path)

    merged = deepcopy(DEFAULT_CONFIG)
    try:
        if path.exists():
            with open(path, "r", encoding="utf-8") as stream:
                user_config = json.load(stream)
            if not isinstance(user_config, dict):
                raise ValueError("configuration root must be a JSON object")
            merged = merge_dicts(merged, user_config)
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"[Config] Failed to load {path.name}: {error}")

    return validate_config(merged)
