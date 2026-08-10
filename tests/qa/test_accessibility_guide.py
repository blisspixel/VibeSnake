"""Contracts for the published native accessibility feature guide."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
GUIDE = ROOT / "docs" / "guides" / "ACCESSIBILITY.md"


def test_accessibility_guide_publishes_exact_supported_boundaries() -> None:
    text = GUIDE.read_text(encoding="utf-8")

    required_support = (
        "85 to 150 percent",
        "15.86:1 standard and 21:1 high contrast",
        "Keyboard-only use",
        "Controller-only use",
        "Remapping",
        "Single-action navigation",
        "Master, Music, SFX, and UI",
        "Mono output",
        "Visual alternatives",
        "Reduced motion",
        "Flash safety",
        "no full-screen flashes",
        "eight supported display classes",
        "P1 release blocker",
    )
    for boundary in required_support:
        assert boundary in text

    required_human_boundaries = (
        "Retained visible audit on Windows, macOS, and Linux.",
        "Maximum-text-scale platform captures.",
        "Physical keyboard-only and controller-only required-flow review.",
        "Candidate review by players who use relevant accessibility settings.",
        "Human focus, contrast, readability, audio, and photosensitivity review.",
    )
    for boundary in required_human_boundaries:
        assert boundary in text


def test_accessibility_guide_is_linked_from_player_documentation() -> None:
    for relative_path in ("README.md", "docs/README.md", "docs/guides/README.md"):
        text = (ROOT / relative_path).read_text(encoding="utf-8")
        assert "ACCESSIBILITY.md" in text
