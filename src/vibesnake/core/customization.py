"""Versioned cosmetic appearance and five bounded loadout slots.

Cosmetics never change starting power or scored rules. The current five visual
axes expose 10,800 raw combinations, but that count is an implementation fact,
not a quality target. Release curation must remove unreadable, clipped, or
thematically redundant combinations.
"""

from dataclasses import asdict, dataclass, fields
from typing import Optional, Tuple, Union
import json
from pathlib import Path

from vibesnake.data.json_store import (
    UnsupportedSchemaVersionError,
    atomic_write_json,
    backup_corrupt_file,
)
from vibesnake.data.paths import get_data_dir


@dataclass
class SnakeCustomization:
    """Serializable cosmetic selections for one snake appearance.

    The mutable value holds color, pattern, eyes, accessory, and trail choices.
    Rendering and unlock validation are owned by their respective systems.
    """

    # Base appearance
    base_color: Tuple[int, int, int] = (50, 255, 50)  # Default green
    secondary_color: Optional[Tuple[int, int, int]] = None  # For gradients
    color_style: str = "solid"  # solid, gradient, metallic

    # Pattern overlay
    pattern: str = "none"  # none, stripes, dots, scales, checker, zigzag
    pattern_color: Tuple[int, int, int] = (255, 255, 255)

    # Eyes
    eye_style: str = "cute"  # cute, angry, sleepy, derp, laser
    eye_color: Tuple[int, int, int] = (255, 255, 255)

    # Accessories
    accessory: str = "none"  # none, hat, crown, sunglasses, headphones, bowtie
    accessory_color: Tuple[int, int, int] = (255, 215, 0)

    # Trail effect
    trail: str = "none"  # none, sparkle, smoke, rainbow, fire

    def to_dict(self) -> dict:
        """Return all cosmetic fields as a JSON-serializable dictionary."""
        return asdict(self)

    @classmethod
    def from_dict(cls, data: dict) -> "SnakeCustomization":
        """Restore known cosmetic fields and ignore fields from newer schemas."""
        if not isinstance(data, dict):
            raise TypeError("customization must be a JSON object")
        allowed_fields = {field.name for field in fields(cls)}
        return cls(**{key: value for key, value in data.items() if key in allowed_fields})


class CustomizationManager:
    """Persist one active cosmetic appearance and five bounded loadout slots.

    Data is schema-versioned and written atomically in the operating system's
    user-data directory unless a test path is injected. Cosmetic state never
    changes starting powers, scoring, collision, or unlock requirements.
    """

    SCHEMA_VERSION = 1

    def __init__(self, data_dir: Optional[Union[str, Path]] = None):
        """Resolve storage, establish defaults, and restore saved cosmetics."""
        self.data_dir = get_data_dir(data_dir)
        self.custom_file = self.data_dir / "customization.json"
        self.current_customization = SnakeCustomization()
        self.loadouts = []  # List of saved customization loadouts
        self._write_blocked = False
        self._load_customizations()

    def _load_customizations(self):
        """Load saved customizations from file."""
        if self.custom_file.exists():
            try:
                with open(self.custom_file, "r", encoding="utf-8") as f:
                    data = json.load(f)
                    if not isinstance(data, dict):
                        raise ValueError("customization root must be a JSON object")
                    schema_version = int(data.get("schema_version", 0))
                    if schema_version > self.SCHEMA_VERSION:
                        self._write_blocked = True
                        raise UnsupportedSchemaVersionError(
                            f"customization schema {schema_version} is newer than supported {self.SCHEMA_VERSION}"
                        )
                    # Load current customization
                    if isinstance(data.get("current"), dict):
                        self.current_customization = SnakeCustomization.from_dict(data["current"])
                    # Load saved loadouts
                    if isinstance(data.get("loadouts"), list):
                        self.loadouts = [
                            SnakeCustomization.from_dict(loadout)
                            for loadout in data["loadouts"][:5]
                            if isinstance(loadout, dict)
                        ]
                if schema_version < self.SCHEMA_VERSION:
                    self._save_customizations()
            except UnsupportedSchemaVersionError as e:
                print(f"[Customization] Failed to load: {e}")
                self.current_customization = SnakeCustomization()
                self.loadouts = []
            except Exception as e:
                backup = backup_corrupt_file(self.custom_file)
                print(f"[Customization] Failed to load: {e}")
                if backup:
                    print(f"[Customization] Preserved unreadable save at {backup.name}")
                # Use defaults
                self.current_customization = SnakeCustomization()
                self.loadouts = []

    def _save_customizations(self):
        """Save customizations to file."""
        if self._write_blocked:
            print("[Customization] Save skipped because the file uses a newer schema")
            return
        try:
            atomic_write_json(
                self.custom_file,
                {
                    "schema_version": self.SCHEMA_VERSION,
                    "current": self.current_customization.to_dict(),
                    "loadouts": [loadout.to_dict() for loadout in self.loadouts],
                },
            )
        except Exception as e:
            print(f"[Customization] Failed to save: {e}")

    def update_customization(self, customization: SnakeCustomization):
        """Update current customization."""
        self.current_customization = customization
        self._save_customizations()

    def save_loadout(self, slot: int):
        """
        Save current customization to a loadout slot (0-4).

        Args:
            slot: Loadout slot index (0-4)
        """
        if 0 <= slot < 5:
            # Extend loadouts list if needed
            while len(self.loadouts) <= slot:
                self.loadouts.append(SnakeCustomization())

            self.loadouts[slot] = self.current_customization
            self._save_customizations()
            print(f"[Customization] Saved to slot {slot + 1}")

    def load_loadout(self, slot: int):
        """
        Load customization from a loadout slot.

        Args:
            slot: Loadout slot index (0-4)
        """
        if 0 <= slot < len(self.loadouts):
            self.current_customization = self.loadouts[slot]
            self._save_customizations()
            print(f"[Customization] Loaded slot {slot + 1}")

    def get_customization(self) -> SnakeCustomization:
        """Get current customization."""
        return self.current_customization


def get_ai_personality_customization(personality_key: str) -> SnakeCustomization:
    """
    Get a unique visual customization for an AI personality.

    Args:
        personality_key: Key identifying the AI personality

    Returns:
        SnakeCustomization themed for that personality
    """
    # AI personality visual themes
    ai_themes = {
        "speed_demon": SnakeCustomization(
            base_color=(255, 50, 50),  # Red
            pattern="stripes",
            pattern_color=(255, 150, 150),
            eye_style="laser",
            eye_color=(255, 0, 0),
            accessory="none",
            accessory_color=(255, 0, 0),
            trail="fire",
        ),
        "coward": SnakeCustomization(
            base_color=(150, 150, 255),  # Light blue
            pattern="none",
            pattern_color=(255, 255, 255),
            eye_style="sleepy",
            eye_color=(200, 200, 255),
            accessory="none",
            accessory_color=(150, 150, 255),
            trail="none",
        ),
        "greedy": SnakeCustomization(
            base_color=(255, 215, 0),  # Gold
            pattern="scales",
            pattern_color=(255, 255, 100),
            eye_style="cute",
            eye_color=(255, 255, 0),
            accessory="crown",
            accessory_color=(255, 215, 0),
            trail="sparkle",
        ),
        "power_hunter": SnakeCustomization(
            base_color=(255, 0, 255),  # Magenta
            pattern="dots",
            pattern_color=(200, 0, 200),
            eye_style="angry",
            eye_color=(255, 0, 255),
            accessory="sunglasses",
            accessory_color=(100, 0, 100),
            trail="rainbow",
        ),
        "drunk": SnakeCustomization(
            base_color=(255, 100, 200),  # Pink
            pattern="zigzag",
            pattern_color=(200, 50, 150),
            eye_style="derp",
            eye_color=(255, 200, 255),
            accessory="hat",
            accessory_color=(200, 50, 100),
            trail="smoke",
        ),
        "optimal": SnakeCustomization(
            base_color=(100, 255, 255),  # Cyan
            pattern="checker",
            pattern_color=(50, 200, 200),
            eye_style="laser",
            eye_color=(0, 255, 255),
            accessory="headphones",
            accessory_color=(50, 200, 200),
            trail="none",
        ),
        "yolo": SnakeCustomization(
            base_color=(255, 140, 0),  # Orange
            pattern="stripes",
            pattern_color=(255, 200, 100),
            eye_style="angry",
            eye_color=(255, 100, 0),
            accessory="sunglasses",
            accessory_color=(200, 100, 0),
            trail="fire",
        ),
        "balanced": SnakeCustomization(
            base_color=(100, 255, 100),  # Green
            pattern="none",
            pattern_color=(150, 255, 150),
            eye_style="cute",
            eye_color=(200, 255, 200),
            accessory="none",
            accessory_color=(100, 255, 100),
            trail="none",
        ),
        "wall_hugger": SnakeCustomization(
            base_color=(139, 69, 19),  # Brown
            pattern="scales",
            pattern_color=(180, 100, 50),
            eye_style="sleepy",
            eye_color=(200, 150, 100),
            accessory="none",
            accessory_color=(139, 69, 19),
            trail="none",
        ),
        "zen_master": SnakeCustomization(
            base_color=(200, 255, 200),  # Light green
            pattern="dots",
            pattern_color=(150, 255, 150),
            eye_style="sleepy",
            eye_color=(255, 255, 255),
            accessory="bowtie",
            accessory_color=(150, 255, 150),
            trail="sparkle",
        ),
        "military_tactician": SnakeCustomization(
            base_color=(75, 100, 75),  # Military green
            pattern="checker",
            pattern_color=(50, 75, 50),
            eye_style="angry",
            eye_color=(200, 255, 200),
            accessory="sunglasses",
            accessory_color=(40, 40, 40),
            trail="smoke",
        ),
    }

    # Return themed customization or default if personality not found
    return ai_themes.get(personality_key, SnakeCustomization())


# Unlock requirement types - used to determine what stat to check
# Format: (requirement_type, requirement_value, description)
UNLOCK_REQUIREMENTS = {
    # ===== COLORS - ALL FREE! =====
    "Classic Green": ("free", 0, "Available from start"),
    "Electric Blue": ("free", 0, "Available from start"),
    "Hot Pink": ("free", 0, "Available from start"),
    "Royal Purple": ("free", 0, "Available from start"),
    "Crimson Red": ("free", 0, "Available from start"),
    "Toxic Green": ("free", 0, "Available from start"),
    "Cyber Cyan": ("free", 0, "Available from start"),
    "Magma Orange": ("free", 0, "Available from start"),
    "Arctic White": ("free", 0, "Available from start"),
    # High-score milestone colors.
    "Golden Shimmer": ("apples_eaten", 1000, "Eat 1000 apples"),
    "Diamond Sparkle": ("wall_rides", 500, "Ride walls 500 times"),
    "Platinum Glow": ("games_played", 100, "Play 100 games"),
    # ===== PATTERNS - ALL FREE! =====
    "none": ("free", 0, "Available from start"),
    "stripes": ("free", 0, "Available from start"),
    "dots": ("free", 0, "Available from start"),
    "scales": ("free", 0, "Available from start"),
    "checker": ("free", 0, "Available from start"),
    "zigzag": ("free", 0, "Available from start"),
    # ===== EYE STYLES - ALL FREE! =====
    "cute": ("free", 0, "Available from start"),
    "angry": ("free", 0, "Available from start"),
    "sleepy": ("free", 0, "Available from start"),
    "derp": ("free", 0, "Available from start"),
    "laser": ("free", 0, "Available from start"),
    # ===== ACCESSORIES - ALL FREE! =====
    "hat": ("free", 0, "Available from start"),
    "sunglasses": ("free", 0, "Available from start"),
    "headphones": ("free", 0, "Available from start"),
    "crown": ("free", 0, "Available from start"),
    "bowtie": ("free", 0, "Available from start"),
    # ===== TRAILS - Unlock with interesting gameplay stats! =====
    "sparkle": ("apples_eaten", 50, "Eat 50 apples"),
    "smoke": ("wall_rides", 25, "Ride walls 25 times"),
    "rainbow": ("highest_combo", 10, "Get 10x combo"),
    "fire": ("games_played", 20, "Play 20 games"),
}

# Preset customization options for UI
COLOR_PRESETS = {
    # Free colors - available to everyone
    "Classic Green": (50, 255, 50),
    "Electric Blue": (0, 191, 255),
    "Hot Pink": (255, 105, 180),
    "Royal Purple": (147, 51, 255),
    "Crimson Red": (220, 20, 60),
    "Toxic Green": (57, 255, 20),
    "Cyber Cyan": (0, 255, 255),
    "Magma Orange": (255, 69, 0),
    "Arctic White": (245, 245, 255),
    # High-score milestone colors.
    "Golden Shimmer": (255, 215, 0),  # Gold metallic effect
    "Diamond Sparkle": (185, 242, 255),  # Diamond white with sparkle
    "Platinum Glow": (229, 228, 226),  # Platinum metallic
}

PATTERN_OPTIONS = ["none", "stripes", "dots", "scales", "checker", "zigzag"]

EYE_STYLES = [
    "cute",  # O_O
    "angry",  # >_<
    "sleepy",  # -_-
    "derp",  # o_O
    "laser",  # X_X (laser eyes)
]

ACCESSORIES = [
    "none",
    "hat",  # Top hat
    "crown",  # Royal crown
    "sunglasses",  # Cool shades
    "headphones",  # Gaming headset
    "bowtie",  # Classy bowtie
]

TRAILS = [
    "none",
    "sparkle",  # Glitter particles
    "smoke",  # Smoke trail
    "rainbow",  # Rainbow gradient
    "fire",  # Fire particles
]
