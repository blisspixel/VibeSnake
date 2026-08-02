"""Bounded presentation effects for gameplay and menu events.

Effects communicate state changes but do not own scored rules. Callers select
motion, duration, and salience; accessibility settings may reduce or suppress
decorative output without changing deterministic outcomes.
"""

import pygame
import math
import random
from typing import List, Tuple, Dict
from dataclasses import dataclass

from vibesnake.data import settings
from vibesnake.utils.logger import get_logger


logger = get_logger(__name__)


@dataclass
class Particle:
    """Single particle for effects."""

    x: float
    y: float
    vx: float
    vy: float
    lifetime: float
    max_lifetime: float
    color: Tuple[int, int, int]
    size: float
    fade: bool = True


@dataclass
class TextPopup:
    """Floating text popup for score bonuses and messages."""

    x: float
    y: float
    text: str
    color: Tuple[int, int, int]
    lifetime: float
    max_lifetime: float
    vy: float = -50.0  # Float upward speed


class VisualEffectsManager:
    """Manage bounded particles, popups, flashes, shake, and effect indicators.

    The manager owns presentation state only. Gameplay systems supply event
    importance and deterministic outcomes. Accessibility settings may reduce
    output without modifying those outcomes.
    """

    def __init__(self):
        """Initialize the visual effects manager."""
        self.particles: List[Particle] = []
        self.text_popups: List[TextPopup] = []
        self.screen_flash_color = None
        self.screen_flash_alpha = 0

        # Screen shake state.
        self.screen_shake_intensity = 0.0
        self.screen_shake_duration = 0.0
        self.shake_offset_x = 0
        self.shake_offset_y = 0

        # Power-up auras
        self.active_auras: List[Dict] = []  # {type, color, intensity, duration}

        # Active power-up indicator stack.
        self.stacked_powerups: List[Dict] = []  # {name, color, timer, max_duration, icon_char}

        # Hitstop/time freeze (fighting game style)
        self.hitstop_duration = 0.0
        self.hitstop_active = False

    def add_burst(
        self,
        x: float,
        y: float,
        color: Tuple[int, int, int],
        count: int = 20,
        speed: float = 150.0,
        lifetime: float = 0.8,
    ):
        """
        Add a burst of particles radiating from a point.

        Args:
            x, y: Center position in pixels
            color: RGB color tuple
            count: Number of particles
            speed: Particle speed in pixels/second
            lifetime: Particle lifetime in seconds
        """
        for _ in range(count):
            angle = random.uniform(0, 2 * math.pi)
            particle_speed = random.uniform(speed * 0.5, speed)
            vx = math.cos(angle) * particle_speed
            vy = math.sin(angle) * particle_speed

            self.particles.append(
                Particle(
                    x=x,
                    y=y,
                    vx=vx,
                    vy=vy,
                    lifetime=lifetime,
                    max_lifetime=lifetime,
                    color=color,
                    size=random.uniform(2, 6),
                )
            )

    def add_powerup_activation_effect(self, screen_width: int, screen_height: int, color: Tuple[int, int, int]):
        """
        Add screen-edge particle sweep for power-up activation.

        Args:
            screen_width: Width of screen
            screen_height: Height of screen
            color: RGB color of power-up
        """
        # Create particles along screen edges
        edge_particle_count = 30

        # Top edge
        for i in range(edge_particle_count):
            x = (i / edge_particle_count) * screen_width
            self.particles.append(
                Particle(
                    x=x,
                    y=0,
                    vx=random.uniform(-20, 20),
                    vy=random.uniform(100, 200),
                    lifetime=1.0,
                    max_lifetime=1.0,
                    color=color,
                    size=random.uniform(3, 8),
                )
            )

        # Screen flash
        self.screen_flash_color = color
        self.screen_flash_alpha = 80

    def add_food_collection_sparkle(self, x: float, y: float, combo_multiplier: float = 1.0):
        """
        Add sparkle effect for food collection.

        Intensity increases with combo multiplier.
        - Combo 1.0x: Small gold sparkles
        - Combo 2.0x: More particles, faster
        - Combo 3.0x+: Explosion effect with screen shake
        - Combo 5.0x+: MEGA explosion with rainbow colors

        Args:
            x, y: Position in pixels
            combo_multiplier: Current combo multiplier (affects intensity)
        """
        # Scale particle count with combo (10 base, +10 per multiplier level)
        particle_count = int(10 + (combo_multiplier * 10))

        # Color palette changes with combo tier
        if combo_multiplier >= 5.0:
            # MEGA combo - rainbow explosion
            colors = [
                (255, 0, 0),  # Red
                (255, 127, 0),  # Orange
                (255, 215, 0),  # Gold
                (0, 255, 0),  # Green
                (0, 127, 255),  # Blue
                (255, 0, 255),  # Magenta
                (255, 255, 255),  # White
            ]
        elif combo_multiplier >= 3.0:
            # High combo - golden with fire
            colors = [
                (255, 215, 0),  # Gold
                (255, 140, 0),  # Orange
                (255, 69, 0),  # Red-orange (fire)
                (255, 255, 255),  # White
            ]
        elif combo_multiplier >= 2.0:
            # Medium combo - enhanced gold
            colors = [
                (255, 215, 0),  # Gold
                (255, 255, 100),  # Yellow
                (255, 255, 255),  # White
            ]
        else:
            # Base combo - simple gold
            colors = [
                (255, 215, 0),  # Gold
                (255, 255, 100),  # Yellow
            ]

        # Speed scales with combo
        base_speed = 50
        max_speed = 150 + (combo_multiplier * 50)

        for _ in range(particle_count):
            color = random.choice(colors)
            angle = random.uniform(0, 2 * math.pi)
            speed = random.uniform(base_speed, max_speed)

            # Size increases with combo
            size = random.uniform(2, 4 + combo_multiplier)

            self.particles.append(
                Particle(
                    x=x,
                    y=y,
                    vx=math.cos(angle) * speed,
                    vy=math.sin(angle) * speed - 50,  # Slight upward bias
                    lifetime=0.6 + (combo_multiplier * 0.1),  # Longer for big combos
                    max_lifetime=0.6 + (combo_multiplier * 0.1),
                    color=color,
                    size=size,
                )
            )

        # Screen shake and hitstop for high combos
        if combo_multiplier >= 5.0:
            # MEGA combo - strong shake + hitstop
            shake_importance = min(10, int(3 + combo_multiplier))
            self.trigger_shake(shake_importance)
            self.trigger_hitstop(0.08)  # Brief freeze for impact
        elif combo_multiplier >= 3.0:
            # High combo - shake only
            shake_importance = min(10, int(3 + combo_multiplier))
            self.trigger_shake(shake_importance)

    def add_screen_shake(self, intensity: float, duration: float):
        """Preserve the strongest requested shake until its duration elapses.

        Args:
            intensity: Requested pixel offset bound.
            duration: Requested duration in seconds.
        """
        self.screen_shake_intensity = max(self.screen_shake_intensity, intensity)
        self.screen_shake_duration = max(self.screen_shake_duration, duration)

    def trigger_shake(self, importance: int = 5):
        """
        Trigger screen shake that scales with importance (1-10).

        Importance levels:
        - 1-3: Subtle (food collection, minor events)
        - 4-6: Medium (power-up collection, combo milestones)
        - 7-8: Strong (death, achievement unlock)
        - 9-10: Extreme (rare events, huge combos)

        Args:
            importance: Event importance (1-10)
        """
        importance = max(1, min(10, importance))  # Clamp to 1-10

        # Scale intensity and duration based on importance
        intensity = 2.0 + (importance * 2.0)  # 4px to 22px
        duration = 0.1 + (importance * 0.05)  # 0.15s to 0.6s

        self.add_screen_shake(intensity, duration)

    def add_power_up_aura(self, color: Tuple[int, int, int], duration: float):
        """
        Add glowing aura effect for active power-up.

        Args:
            color: RGB color of the aura
            duration: How long the aura lasts
        """
        self.active_auras.append({"color": color, "intensity": 1.0, "duration": duration, "max_duration": duration})

    def add_stacked_powerup(self, name: str, color: Tuple[int, int, int], duration: float, icon_char: str = "P"):
        """
        Add or refresh an active power-up indicator.

        Shows active power-ups with timers stacked vertically.

        Args:
            name: Power-up name (e.g., "Shield", "Magnet")
            color: RGB color for the icon
            duration: Duration in seconds
            icon_char: Single character icon (e.g., "S" for Shield, "M" for Magnet)
        """
        self.stacked_powerups.append(
            {"name": name, "color": color, "timer": duration, "max_duration": duration, "icon_char": icon_char}
        )

    def remove_stacked_powerup(self, name: str):
        """
        Remove specific power-up from stack by name.

        Args:
            name: Power-up name to remove
        """
        self.stacked_powerups = [p for p in self.stacked_powerups if p["name"] != name]

    def add_score_popup(
        self, x: float, y: float, text: str, color: Tuple[int, int, int] = (255, 255, 255), lifetime: float = 1.5
    ):
        """
        Add a floating text popup (for near-miss bonuses, scores, etc.).

        Args:
            x, y: Position in pixels
            text: Text to display (e.g., "+50 CLUTCH!")
            color: RGB color tuple
            lifetime: Duration in seconds
        """
        self.text_popups.append(TextPopup(x=x, y=y, text=text, color=color, lifetime=lifetime, max_lifetime=lifetime))

    def trigger_hitstop(self, duration: float = 0.1):
        """
        Trigger time freeze/hitstop effect (fighting game style).

        Brief pause in game time for impact on big moments:
        - Death: 0.15s
        - High combo food collection (5x+): 0.08s
        - Power-up collection: 0.05s
        - Achievement unlock: 0.2s

        Args:
            duration: Duration of time freeze in seconds
        """
        self.hitstop_duration = max(self.hitstop_duration, duration)
        self.hitstop_active = True

    def is_hitstop_active(self) -> bool:
        """Check if hitstop is currently active."""
        return self.hitstop_active and self.hitstop_duration > 0

    def get_hitstop_time_scale(self) -> float:
        """
        Get time scale factor during hitstop.

        Returns:
            0.0 during hitstop (frozen), 1.0 normally
        """
        return 0.0 if self.is_hitstop_active() else 1.0

    def update(self, dt: float):
        """
        Update all particles and effects.

        Args:
            dt: Delta time in seconds
        """
        # Update hitstop timer (always ticks down, even during freeze)
        if self.hitstop_duration > 0:
            self.hitstop_duration -= dt
            if self.hitstop_duration <= 0:
                self.hitstop_duration = 0
                self.hitstop_active = False

        # Update particles (always update for smooth visuals)
        for particle in self.particles[:]:
            particle.x += particle.vx * dt
            particle.y += particle.vy * dt
            particle.vy += 200 * dt  # Gravity
            particle.lifetime -= dt

            if particle.lifetime <= 0:
                self.particles.remove(particle)

        # Fade screen flash
        if self.screen_flash_alpha > 0:
            self.screen_flash_alpha -= 200 * dt  # Fade speed
            if self.screen_flash_alpha < 0:
                self.screen_flash_alpha = 0
                self.screen_flash_color = None

        # Update screen shake
        if self.screen_shake_duration > 0:
            self.screen_shake_duration -= dt
            # Random shake offset
            self.shake_offset_x = random.uniform(-self.screen_shake_intensity, self.screen_shake_intensity)
            self.shake_offset_y = random.uniform(-self.screen_shake_intensity, self.screen_shake_intensity)
        else:
            self.shake_offset_x = 0
            self.shake_offset_y = 0
            self.screen_shake_intensity = 0

        # Update auras
        for aura in self.active_auras[:]:
            aura["duration"] -= dt
            aura["intensity"] = aura["duration"] / aura["max_duration"]
            if aura["duration"] <= 0:
                self.active_auras.remove(aura)

        # Update stacked power-ups
        for powerup in self.stacked_powerups[:]:
            powerup["timer"] -= dt
            if powerup["timer"] <= 0:
                self.stacked_powerups.remove(powerup)

        # Update text popups
        for popup in self.text_popups[:]:
            popup.y += popup.vy * dt  # Float upward
            popup.lifetime -= dt
            if popup.lifetime <= 0:
                self.text_popups.remove(popup)

    def draw(self, surface: pygame.Surface):
        """
        Draw all visual effects.

        Args:
            surface: Pygame surface to draw on
        """
        # Draw particles
        for particle in self.particles:
            if particle.fade:
                # Calculate alpha based on lifetime
                alpha = int(255 * (particle.lifetime / particle.max_lifetime))
                alpha = max(0, min(255, alpha))
            else:
                alpha = 255

            # Create particle surface with alpha
            particle_surface = pygame.Surface((int(particle.size * 2), int(particle.size * 2)), pygame.SRCALPHA)
            pygame.draw.circle(
                particle_surface, (*particle.color, alpha), (int(particle.size), int(particle.size)), int(particle.size)
            )

            surface.blit(particle_surface, (int(particle.x - particle.size), int(particle.y - particle.size)))

        # Draw power-up auras (glowing edges around screen)
        for aura in self.active_auras:
            alpha = int(100 * aura["intensity"])
            aura_surface = pygame.Surface(surface.get_size(), pygame.SRCALPHA)

            # Draw pulsing border
            border_thickness = int(10 + math.sin(pygame.time.get_ticks() / 200) * 5)
            pygame.draw.rect(
                aura_surface,
                (*aura["color"], alpha),
                (0, 0, surface.get_width(), surface.get_height()),
                border_thickness,
            )
            surface.blit(aura_surface, (0, 0))

        # Draw screen flash
        if self.screen_flash_alpha > 0 and self.screen_flash_color:
            flash_surface = pygame.Surface(surface.get_size(), pygame.SRCALPHA)
            flash_surface.fill((*self.screen_flash_color, int(self.screen_flash_alpha)))
            surface.blit(flash_surface, (0, 0))

        # Draw active power-up indicators.
        if self.stacked_powerups:
            self._draw_powerup_stack(surface)

        # Draw text popups
        for popup in self.text_popups:
            # Fade out as lifetime decreases
            alpha = int(255 * (popup.lifetime / popup.max_lifetime))
            alpha = max(0, min(255, alpha))

            try:
                font = settings.create_font(20, bold=True)
                text_surface = font.render(popup.text, True, popup.color)

                # Add semi-transparent background for readability
                text_rect = text_surface.get_rect(center=(int(popup.x), int(popup.y)))
                bg_rect = text_rect.inflate(10, 5)
                bg_surface = pygame.Surface(bg_rect.size, pygame.SRCALPHA)
                bg_surface.fill((0, 0, 0, min(150, alpha)))
                surface.blit(bg_surface, bg_rect.topleft)

                # Draw text with alpha
                text_surface.set_alpha(alpha)
                surface.blit(text_surface, text_rect)
            except (pygame.error, TypeError, ValueError) as error:
                logger.debug("Unable to render text popup: %s", error)

    def _draw_powerup_stack(self, surface: pygame.Surface):
        """
        Draw active power-up indicators with remaining timers.

        Shows active power-ups in HUD bar:
        - Icon with power-up color
        - Circular timer showing remaining duration
        - Compact horizontal layout in HUD
        """
        # Position in HUD bar (right side, horizontal layout)
        from vibesnake.data import settings

        margin = 10
        icon_size = 35
        spacing = 45  # Horizontal spacing
        start_y = 12  # Center vertically in HUD (60px tall)
        # Start from right edge, moving left
        start_x = settings.WIDTH - margin - icon_size

        # Draw from right to left
        for i, powerup in enumerate(self.stacked_powerups):
            x_pos = start_x - (i * spacing)

            # Icon circle (compact HUD style, no background box)
            icon_center = (x_pos + icon_size // 2, start_y + icon_size // 2)
            pygame.draw.circle(surface, powerup["color"], icon_center, icon_size // 2)

            # Icon character
            try:
                font = settings.create_font(18, bold=True)
                icon_text = font.render(powerup["icon_char"], True, (255, 255, 255))
                icon_text_rect = icon_text.get_rect(center=icon_center)
                surface.blit(icon_text, icon_text_rect)
            except (pygame.error, TypeError, ValueError) as error:
                logger.debug("Unable to render stacked power-up icon: %s", error)

            # Timer ring (circular progress)
            timer_percent = powerup["timer"] / powerup["max_duration"]
            if timer_percent > 0:
                # Draw arc showing remaining time
                angle_start = -90  # Start at top
                angle_sweep = int(360 * timer_percent)
                if angle_sweep > 0:
                    pygame.draw.arc(
                        surface,
                        (255, 255, 255),
                        (x_pos, start_y, icon_size, icon_size),
                        math.radians(angle_start),
                        math.radians(angle_start + angle_sweep),
                        3,
                    )

    def get_shake_offset(self) -> Tuple[int, int]:
        """Get current screen shake offset for camera."""
        return (int(self.shake_offset_x), int(self.shake_offset_y))

    def clear(self):
        """Clear all effects."""
        self.particles.clear()
        self.text_popups.clear()
        self.screen_flash_alpha = 0
        self.screen_flash_color = None
        self.active_auras.clear()
        self.stacked_powerups.clear()
        self.screen_shake_duration = 0
        self.hitstop_duration = 0
        self.hitstop_active = False


@dataclass
class BackgroundElement:
    """Single decorative element for snake-themed backgrounds."""

    x: float
    y: float
    element_type: str  # 'grass', 'flower', 'rock', 'vine', 'crystal', etc.
    size: float
    color: Tuple[int, int, int]
    animation_offset: float
    depth: float  # For parallax (0.5-1.5)


class BackgroundRenderer:
    """
    Procedurally generated backgrounds that progress with score.

    Visual progression through score-banded environments. The current thresholds
    are presentation parameters and must not independently infer game intensity.

    Score bands select Garden (0-99), Cliffs (100-299), Rainforest
    (300-599), Geothermal (600-999), or Temple (1000 and above). These are
    presentation labels only and do not alter gameplay rules.
    """

    def __init__(self, width: int, height: int):
        """
        Initialize background renderer.

        Args:
            width: Screen width in pixels
            height: Screen height in pixels
        """
        self.width = width
        self.height = height
        self.time = 0.0

        # Current environment (vibe progression)
        self.current_environment = "garden"
        self.score = 0

        # Background elements (vibe-specific decorations)
        self.elements: List[BackgroundElement] = []
        self._generate_environment_elements()

        # Grid settings
        self.grid_enabled = True
        self.grid_alpha = 20  # Subtle
        self.grid_pulse_speed = 0.5

        # Base colors per environment (vibe palettes)
        self.environment_colors = {
            "garden": (40, 80, 40),  # Chill green vibe
            "cliffs": (90, 70, 50),  # Warm confident stone
            "rainforest": (20, 60, 30),  # Dense jungle atmosphere
            "geothermal": (40, 30, 50),  # Mysterious purple glow
            "temple": (60, 50, 40),  # Ancient transcendent stone
        }

        # Color shifting
        self.base_color = self.environment_colors["garden"]
        self.target_color = self.base_color
        self.current_color = list(self.base_color)
        self.color_transition_speed = 0.3

        # CRT scanlines
        self.scanlines_enabled = True
        self.scanline_alpha = 15
        self.scanline_spacing = 4

    def _generate_environment_elements(self):
        """Generate decorative elements based on current environment."""
        self.elements.clear()
        count = 50  # Number of decorative elements

        for _ in range(count):
            element = self._create_element_for_environment()
            if element:
                self.elements.append(element)

    def _create_element_for_environment(self) -> BackgroundElement:
        """Create a random decorative element for current environment."""
        from vibesnake.data import settings

        x = random.uniform(0, self.width)
        y = random.uniform(settings.HUD_HEIGHT, self.height)
        depth = random.uniform(0.5, 1.5)
        animation_offset = random.uniform(0, math.pi * 2)

        if self.current_environment == "garden":
            # Garden - Chill starter vibe
            element_types = ["grass", "flower", "rock"]
            element_type = random.choice(element_types)
            if element_type == "grass":
                color = (60, random.randint(120, 150), 60)
                size = random.uniform(3, 8)
            elif element_type == "flower":
                colors = [(255, 200, 50), (255, 100, 150), (200, 150, 255)]
                color = random.choice(colors)
                size = random.uniform(2, 5)
            else:  # rock
                color = (100, 100, 100)
                size = random.uniform(4, 10)

        elif self.current_environment == "cliffs":
            # Cliffs - Elevated energy vibe
            element_types = ["stone", "boulder", "rock_formation"]
            element_type = random.choice(element_types)
            if element_type == "stone":
                color = (150, 120, 70)
                size = random.uniform(15, 30)
            elif element_type == "boulder":
                color = (120, 90, 60)
                size = random.uniform(8, 15)
            else:  # rock_formation
                color = (140, 100, 70)
                size = random.uniform(10, 25)

        elif self.current_environment == "rainforest":
            # Rainforest - Dense humid vibe
            element_types = ["leaf", "vine", "flower"]
            element_type = random.choice(element_types)
            if element_type == "leaf":
                color = (40, random.randint(100, 140), 40)
                size = random.uniform(10, 20)
            elif element_type == "vine":
                color = (30, 90, 30)
                size = random.uniform(5, 15)
            else:  # flower
                colors = [(255, 50, 50), (255, 200, 50), (200, 50, 255)]
                color = random.choice(colors)
                size = random.uniform(4, 8)

        elif self.current_environment == "geothermal":
            # Geothermal - Mysterious glow vibe
            element_types = ["glow", "crystal", "stone"]
            element_type = random.choice(element_types)
            if element_type == "glow":
                color = (100, 80, 120)
                size = random.uniform(8, 20)
            elif element_type == "crystal":
                colors = [(255, 150, 100), (255, 100, 150), (200, 100, 200)]
                color = random.choice(colors)
                size = random.uniform(4, 10)
            else:  # stone
                color = (80, 60, 90)
                size = random.uniform(10, 25)

        else:  # temple
            # Temple - Ancient transcendent vibe
            element_types = ["tile", "rune", "pillar"]
            element_type = random.choice(element_types)
            if element_type == "tile":
                color = (110, 90, 70)
                size = random.uniform(15, 25)
            elif element_type == "rune":
                colors = [(200, 180, 100), (180, 200, 255), (255, 200, 150)]
                color = random.choice(colors)
                size = random.uniform(5, 12)
            else:  # pillar
                color = (100, 85, 65)
                size = random.uniform(20, 40)

        return BackgroundElement(
            x=x, y=y, element_type=element_type, size=size, color=color, animation_offset=animation_offset, depth=depth
        )

    def set_score(self, score: int):
        """
        Update score and check if environment should change.

        Vibe progression - each score threshold unlocks a new aesthetic.

        Args:
            score: Current game score
        """
        self.score = score

        # Determine environment based on score (vibe progression)
        new_environment = "garden"
        if score >= 1000:
            new_environment = "temple"
        elif score >= 600:
            new_environment = "geothermal"
        elif score >= 300:
            new_environment = "rainforest"
        elif score >= 100:
            new_environment = "cliffs"

        # Transition to new environment if changed
        if new_environment != self.current_environment:
            self.current_environment = new_environment
            self.target_color = self.environment_colors[new_environment]
            self._generate_environment_elements()

    def update(self, dt: float):
        """
        Update background animations.

        Args:
            dt: Delta time in seconds
        """
        self.time += dt

        # No element position updates - they stay static
        # Only animation time advances for pulsing/swaying effects

        # Smooth color transition to target
        for i in range(3):  # RGB channels
            diff = self.target_color[i] - self.current_color[i]
            self.current_color[i] += diff * self.color_transition_speed * dt

    def draw(self, surface: pygame.Surface):
        """
        Draw the animated background.

        Args:
            surface: Pygame surface to draw on
        """
        # Fill with current base color
        bg_color = tuple(int(c) for c in self.current_color)
        surface.fill(bg_color)

        # Draw environment elements
        self._draw_environment_elements(surface)

        # Draw grid if enabled
        if self.grid_enabled:
            self._draw_grid(surface)

        # Draw scanlines if enabled
        if self.scanlines_enabled:
            self._draw_scanlines(surface)

    def _draw_environment_elements(self, surface: pygame.Surface):
        """Draw decorative environment elements with pixel art style."""
        # Sort by depth for proper layering (back to front)
        sorted_elements = sorted(self.elements, key=lambda e: e.depth)

        for element in sorted_elements:
            # Subtle animation (sway, pulse, glow)
            animation = math.sin(self.time * 2 + element.animation_offset) * 0.1 + 0.9

            # Adjust alpha based on depth (further = more transparent)
            # Clamp alpha to valid range 0-255
            alpha = min(255, max(0, int(180 * element.depth)))

            # Draw based on element type
            if element.element_type in ["grass", "leaf", "vine"]:
                # Small vertical lines for grass/foliage
                size = int(element.size * animation)
                if size > 0:
                    elem_surface = pygame.Surface((max(1, size), max(1, size * 2)), pygame.SRCALPHA)
                    pygame.draw.line(
                        elem_surface, (*element.color, alpha), (size // 2, 0), (size // 2, size * 2), max(1, size // 3)
                    )
                    surface.blit(elem_surface, (int(element.x), int(element.y)))

            elif element.element_type in ["flower", "crystal", "rune", "glow"]:
                # Small glowing circles for flowers/crystals/glows
                size = int(element.size * animation)
                if size > 0:
                    glow_alpha = min(255, max(0, int(alpha * animation)))
                    elem_surface = pygame.Surface((max(1, size * 2), max(1, size * 2)), pygame.SRCALPHA)
                    pygame.draw.circle(elem_surface, (*element.color, glow_alpha), (size, size), size)
                    surface.blit(elem_surface, (int(element.x - size), int(element.y - size)))

            else:  # rocks, stone, boulder, tile, pillar, rock_formation
                # Rectangles for solid objects
                size = int(element.size)
                if size > 0:
                    elem_surface = pygame.Surface((max(1, size), max(1, size)), pygame.SRCALPHA)
                    pygame.draw.rect(
                        elem_surface, (*element.color, alpha), (0, 0, size, size), border_radius=max(1, size // 4)
                    )
                    surface.blit(elem_surface, (int(element.x), int(element.y)))

    def _draw_grid(self, surface: pygame.Surface):
        """Draw subtle animated grid lines."""
        from vibesnake.data import settings

        # Pulse effect
        pulse = math.sin(self.time * self.grid_pulse_speed) * 0.3 + 0.7
        alpha = int(self.grid_alpha * pulse)

        # Grid color (slightly lighter than background)
        grid_color = tuple(min(255, int(c * 1.5)) for c in self.current_color)

        # Create grid surface
        grid_surface = pygame.Surface(surface.get_size(), pygame.SRCALPHA)

        # Draw vertical lines
        for x in range(0, self.width, settings.CELL_SIZE):
            pygame.draw.line(grid_surface, (*grid_color, alpha), (x, settings.HUD_HEIGHT), (x, self.height), 1)

        # Draw horizontal lines (skip HUD area)
        for y in range(settings.HUD_HEIGHT, self.height, settings.CELL_SIZE):
            pygame.draw.line(grid_surface, (*grid_color, alpha), (0, y), (self.width, y), 1)

        surface.blit(grid_surface, (0, 0))

    def _draw_scanlines(self, surface: pygame.Surface):
        """Draw CRT-style scanlines for retro aesthetic."""
        scanline_surface = pygame.Surface(surface.get_size(), pygame.SRCALPHA)

        # Draw horizontal lines
        for y in range(0, self.height, self.scanline_spacing):
            pygame.draw.line(scanline_surface, (0, 0, 0, self.scanline_alpha), (0, y), (self.width, y), 1)

        surface.blit(scanline_surface, (0, 0))
