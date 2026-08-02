import unittest
import json
import tempfile
from pathlib import Path
from types import SimpleNamespace

import pygame

from vibesnake.audio.radio_manager import RadioManager
from vibesnake.core.high_scores import HighScoreTable
from vibesnake.rendering.hud import HUD
from vibesnake.data import settings


class TestHUD(unittest.TestCase):
    def setUp(self):
        self.temp_dir = tempfile.TemporaryDirectory()
        self.data_dir = Path(self.temp_dir.name)

    def tearDown(self):
        self.temp_dir.cleanup()

    def make_hud(self):
        return HUD(HighScoreTable(self.data_dir))

    def test_initial_high_score_is_zero(self):
        hud = self.make_hud()
        self.assertEqual(hud.high_score, 0)

    def test_save_and_load_high_score(self):
        hud = self.make_hud()
        hud.update_high_score(42)
        self.assertEqual(hud.high_score, 42)

        hud2 = self.make_hud()
        self.assertEqual(hud2.high_score, 42)

    def test_update_high_score_does_not_lower_score(self):
        hud = self.make_hud()
        hud.update_high_score(50)
        hud.update_high_score(10)
        self.assertEqual(hud.high_score, 50)
        self.assertEqual(len(hud.high_score_table.scores), 1)

    def test_malformed_json_file(self):
        (self.data_dir / "high_scores.json").write_text("{not: valid json}", encoding="utf-8")

        hud = self.make_hud()
        self.assertEqual(hud.high_score, 0)  # Should fallback to 0

    def test_missing_file_handled_gracefully(self):
        hud = self.make_hud()
        self.assertEqual(hud.high_score, 0)

    def test_save_file_created(self):
        hud = self.make_hud()
        hud.update_high_score(99)
        self.assertTrue((self.data_dir / "high_scores.json").exists())


if __name__ == "__main__":
    unittest.main()


def test_draw_score_covers_radio_combo_and_powerup_variants(tmp_path):
    """The complete HUD should render all supported status indicators."""
    pygame.init()
    pygame.display.set_mode((settings.WIDTH, settings.HEIGHT))
    surface = pygame.Surface((settings.WIDTH, settings.HEIGHT))
    hud = HUD(HighScoreTable(tmp_path))

    station = SimpleNamespace(key="flow_signal", name="Flow Signal")
    radio = SimpleNamespace(is_playing=True, get_current_station=lambda: station)

    powerups = []
    for name in (
        "ShieldPowerUp",
        "SlowMoPowerUp",
        "MagnetPowerUp",
        "BoostPowerUp",
        "PhaseShiftPowerUp",
        "GluttonyPowerUp",
        "BaitPowerUp",
        "LastStandPowerUp",
        "SegmentDetachPowerUp",
        "MysteryPowerUp",
    ):
        powerup = type(name, (), {})()
        powerup.activated = True
        powerup.active = True
        powerup.duration = 10
        powerup.timer = 2
        powerups.append(powerup)

    inactive = type("InactivePowerUp", (), {})()
    inactive.activated = False
    inactive.active = False
    powerups.append(inactive)

    for multiplier in (1.0, 2.0, 3.0, 5.0, 10.0):
        hud.draw_score(surface, 123, multiplier, 4, radio, powerups)

    hud.draw_score(surface, 0)
    hud.draw_game_over(surface)
    assert hud._load_badge("flow_signal") is hud._load_badge("flow_signal")
    assert hud._load_badge("missing") is None


def test_every_radio_station_has_a_loadable_hud_badge(tmp_path):
    pygame.init()
    pygame.display.set_mode((1, 1))
    hud = HUD(HighScoreTable(tmp_path))

    for station in RadioManager.STATIONS:
        badge = hud._load_badge(station.key)
        assert badge is not None, station.key
        assert badge.get_size() == (40, 40)


def test_hud_reads_migrated_legacy_and_named_score_formats(tmp_path):
    numeric_dir = tmp_path / "numeric"
    numeric_dir.mkdir()
    (numeric_dir / "highscore.json").write_text("73", encoding="utf-8")
    legacy = HUD(HighScoreTable(numeric_dir))
    assert legacy.high_score == 73
    assert legacy.high_score_name == "Anonymous"

    named_dir = tmp_path / "named"
    named_dir.mkdir()
    (named_dir / "highscore.json").write_text(
        json.dumps({"high_score": 91, "name": "ADA"}),
        encoding="utf-8",
    )
    named = HUD(HighScoreTable(named_dir))
    assert named.high_score == 91
    assert named.high_score_name == "ADA"
