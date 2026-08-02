"""Tests for runtime configuration merging, validation, and presets."""

import json

from vibesnake.data.config import DEFAULT_CONFIG, load_config, merge_dicts, validate_config


def test_recursive_merge_preserves_nested_defaults():
    merged = merge_dicts(
        {"sound": {"enabled": True, "volume": 0.8}},
        {"sound": {"volume": 0.25}},
    )
    assert merged == {"sound": {"enabled": True, "volume": 0.25}}


def test_validation_replaces_invalid_values_with_safe_defaults():
    config = merge_dicts(
        json.loads(json.dumps(DEFAULT_CONFIG)),
        {
            "resolution_preset": "custom",
            "grid_width": -1,
            "logic_tick": "fast",
            "colors": {"food": [999, 0, 0]},
            "powerups": {"enabled": "yes", "visible_duration": 0},
            "sound": {"volume": 2.0},
        },
    )

    validated = validate_config(config)

    assert validated["grid_width"] == DEFAULT_CONFIG["grid_width"]
    assert validated["logic_tick"] == DEFAULT_CONFIG["logic_tick"]
    assert validated["colors"]["food"] == DEFAULT_CONFIG["colors"]["food"]
    assert validated["powerups"]["enabled"] is True
    assert validated["powerups"]["visible_duration"] == 6.0
    assert validated["sound"]["volume"] == 0.8


def test_named_resolution_preset_controls_grid_dimensions(tmp_path):
    config_file = tmp_path / "config.json"
    config_file.write_text(
        json.dumps(
            {
                "resolution_preset": "hd",
                "grid_width": 5,
                "grid_height": 5,
            }
        ),
        encoding="utf-8",
    )

    config = load_config(config_file)

    assert config["grid_width"] == 64
    assert config["grid_height"] == 33


def test_custom_resolution_preserves_explicit_dimensions(tmp_path):
    config_file = tmp_path / "config.json"
    config_file.write_text(
        json.dumps(
            {
                "resolution_preset": "custom",
                "grid_width": 48,
                "grid_height": 27,
            }
        ),
        encoding="utf-8",
    )

    config = load_config(config_file)

    assert config["grid_width"] == 48
    assert config["grid_height"] == 27


def test_invalid_preset_falls_back_even_with_partial_resolution_map():
    config = json.loads(json.dumps(DEFAULT_CONFIG))
    config["resolution_preset"] = "missing"
    config["resolutions"] = {
        "only": {"grid_width": 10, "grid_height": 10},
    }

    validated = validate_config(config)

    assert validated["resolution_preset"] == "classic"
    assert validated["grid_width"] == 40
    assert validated["grid_height"] == 30


def test_malformed_and_future_configurations_fall_back(tmp_path):
    malformed = tmp_path / "malformed.json"
    malformed.write_text("not json", encoding="utf-8")
    assert load_config(malformed)["grid_width"] == DEFAULT_CONFIG["grid_width"]

    future = tmp_path / "future.json"
    future.write_text(json.dumps({"schema_version": 999, "grid_width": 1}), encoding="utf-8")
    assert load_config(future) == DEFAULT_CONFIG
