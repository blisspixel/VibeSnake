"""Render navigation screens and modal overlays.

Menus use a retro-modern arcade language: hard-edged panels, pixel-scale type,
and limited neon accents. Color is never the only action label. Layout, contrast,
focus, text scale, and reduced-motion behavior remain release gates and must be
measured from rendered output rather than inferred from design intent.
"""

import os

import pygame

from vibesnake.data import settings
from vibesnake.rendering import theme


class Menu:
    def __init__(self, screen: pygame.Surface):
        self.screen = screen
        self.font = settings.create_font(24)
        self.large_font = settings.create_font(50)

        self.logo = None
        logo_path = settings.LOGO_PATH
        if os.path.exists(logo_path):
            try:
                self.logo = pygame.image.load(logo_path).convert_alpha()
                # Nearest-neighbor keeps the handcrafted mark crisp.
                self.logo = pygame.transform.scale(self.logo, (192, 192))
            except Exception as e:
                print(f"[Menu] Failed to load logo: {e}")
        else:
            print(f"[Menu] logo.png not found at {logo_path}")

    def _hsv_to_rgb(self, h: float, s: float, v: float):
        """Convert HSV to RGB color (h: 0-360, s/v: 0-1)."""
        h = h / 60.0
        c = v * s
        x = c * (1 - abs(h % 2 - 1))
        m = v - c

        if h < 1:
            r, g, b = c, x, 0
        elif h < 2:
            r, g, b = x, c, 0
        elif h < 3:
            r, g, b = 0, c, x
        elif h < 4:
            r, g, b = 0, x, c
        elif h < 5:
            r, g, b = x, 0, c
        else:
            r, g, b = c, 0, x

        return (int((r + m) * 255), int((g + m) * 255), int((b + m) * 255))

    def draw_title_screen(self):
        """Draw the main menu as a retro-modern arcade title card."""
        self.screen.fill(theme.PALETTE["void"])
        theme.draw_pixel_grid(self.screen, step=20, color=(16, 12, 34))

        # Outer cabinet frame
        frame = pygame.Rect(24, 20, settings.WIDTH - 48, settings.HEIGHT - 40)
        theme.draw_panel(
            self.screen, frame, fill=theme.PALETTE["panel"], border=theme.PALETTE["accent"], border_width=3
        )

        center_x = settings.WIDTH // 2
        y_offset = 48

        if self.logo:
            logo = pygame.transform.scale(self.logo, (200, 200))
            logo_rect = logo.get_rect(center=(center_x, y_offset + 100))
            # Pixel bezel around the brand mark
            bezel = logo_rect.inflate(16, 16)
            theme.draw_panel(
                self.screen,
                bezel,
                fill=theme.PALETTE["ink"],
                border=theme.PALETTE["accent_gold"],
                border_width=3,
                shadow=False,
            )
            self.screen.blit(logo, logo_rect)
            y_offset += 230
        else:
            title = theme.render_pixel_text("VIBE SNAKE", color=theme.PALETTE["accent_gold"], scale=3, bold=True)
            self.screen.blit(title, title.get_rect(center=(center_x, y_offset + 24)))
            y_offset += 70

        tag = theme.render_pixel_text("RETRO CORE  //  MODERN FLOW", color=theme.PALETTE["muted"], scale=2)
        self.screen.blit(tag, tag.get_rect(center=(center_x, y_offset)))
        y_offset += 36

        menu_items = [
            ("ENTER", "START RUN", theme.PALETTE["accent"]),
            ("C", "CUSTOMIZE", theme.PALETTE["accent_gold"]),
            ("V", "HIGH SCORES", theme.PALETTE["accent_sky"]),
            ("A", "ACHIEVEMENTS", theme.PALETTE["accent_hot"]),
            ("L", "AI CHANNELS", (255, 120, 170)),
            ("S", "SETTINGS", theme.PALETTE["accent_sky"]),
            ("H", "HELP", theme.PALETTE["text"]),
            ("Q", "QUIT", theme.PALETTE["danger"]),
        ]

        row_w = min(520, settings.WIDTH - 120)
        menu_x = center_x - row_w // 2
        row_h = 40

        for key, label, accent in menu_items:
            row = pygame.Rect(menu_x, y_offset, row_w, row_h)
            theme.draw_panel(self.screen, row, fill=theme.PALETTE["panel_hi"], border=accent, border_width=2)

            key_box = pygame.Rect(row.x + 8, row.y + 6, 92, row_h - 12)
            pygame.draw.rect(self.screen, theme.PALETTE["ink"], key_box)
            pygame.draw.rect(self.screen, accent, key_box, 2)
            key_surface = theme.render_pixel_text(key, color=theme.PALETTE["text"], scale=2, bold=True, base_px=11)
            self.screen.blit(key_surface, key_surface.get_rect(center=key_box.center))

            label_surface = theme.render_pixel_text(label, color=theme.PALETTE["text"], scale=2, bold=True, base_px=12)
            self.screen.blit(label_surface, (key_box.right + 18, row.y + (row_h - label_surface.get_height()) // 2))
            y_offset += row_h + 8

        footer = theme.render_pixel_text(
            "9 POWERS  ·  25 ACHIEVEMENTS  ·  RESIZE WINDOW ANYTIME",
            color=theme.PALETTE["dim"],
            scale=1,
            base_px=12,
        )
        self.screen.blit(footer, footer.get_rect(center=(center_x, settings.HEIGHT - 42)))

    def draw_pause_overlay(self):
        overlay = pygame.Surface((settings.WIDTH, settings.HEIGHT), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 160))
        self.screen.blit(overlay, (0, 0))

        pause_msg = self.large_font.render("PAUSED", True, settings.YELLOW)
        hint = self.font.render("Press P to resume or Q to quit", True, settings.WHITE)

        self.screen.blit(pause_msg, pause_msg.get_rect(center=(settings.WIDTH // 2, settings.HEIGHT // 3)))
        self.screen.blit(hint, hint.get_rect(center=(settings.WIDTH // 2, settings.HEIGHT // 2)))
        # Presentation is owned by AdaptiveDisplay.present() in the game loop.

    def choose_game_over_message(self, score: int, high_score: int, is_new_high_score: bool) -> str:
        """
        Choose a game over message based on score (called once when game ends).

        Args:
            score: Final score
            high_score: Current high score
            is_new_high_score: Whether this beat the high score

        Returns:
            The chosen message string
        """
        import random

        if is_new_high_score:
            motivational = [
                "Built different fr fr",
                "They could never",
                "Unironically talented",
                "Literally HIM",
                "That's what I'm talking about",
                "Got that dog in you",
            ]
            return random.choice(motivational)
        else:
            # Roast messages based on score brackets
            if score < 10:
                roasts = [
                    "Bro literally just started and gave up",
                    "My grandma plays better and she's dead",
                    "Were you even trying?",
                    "Skill issue detected",
                    "Maybe try an easier game?",
                    "Have you considered not being bad?",
                ]
            elif score < 50:
                roasts = [
                    "Mid performance, mid results",
                    "Not everyone is built for greatness",
                    "I'm sure your mom thinks you did well",
                    "Better luck next time... maybe",
                    "That was certainly an attempt",
                    "POV: You're not HIM",
                ]
            elif score < 100:
                roasts = [
                    "Decent, but no one cares about second place",
                    "Close, but no cigar",
                    "You peaked in high school vibes",
                    "Not bad... not good either",
                    "Mediocrity is still a vibe I guess",
                ]
            elif score < high_score * 0.9:
                roasts = [
                    "So close, yet so far away",
                    "Imagine being that close and still losing",
                    "Skill ceiling found",
                    "You'll get 'em next time (maybe)",
                    "That's tough buddy",
                ]
            else:
                # Very close to high score
                roasts = [
                    "You were THIS close to glory",
                    "Literally one more apple away",
                    "Felt the high score and fumbled",
                    "Choking at the finish line",
                    "Built different but not different enough",
                ]

            return random.choice(roasts)

    def draw_game_over_overlay(
        self,
        score: int,
        high_score: int,
        is_new_high_score: bool,
        message: str = "",
        is_ai_mode: bool = False,
        ai_restart_time: float = 0.0,
    ):
        """
        Draw game over screen with Gen Z humor and epic high score celebration.

        Args:
            score: Final score
            high_score: Current high score
            is_new_high_score: Whether this score set the current local record
            message: Pre-selected game over message (to avoid flashing)
            is_ai_mode: Whether this is AI Let's Play mode (changes button labels)
            ai_restart_time: Time remaining until AI auto-restart (0 if not AI mode)
        """
        import math

        # Overlay darkness depends on high score status
        overlay = pygame.Surface((settings.WIDTH, settings.HEIGHT), pygame.SRCALPHA)
        if is_new_high_score:
            # Pulsing gold overlay for champions
            pulse = abs(math.sin(pygame.time.get_ticks() * 0.005))
            gold_alpha = int(100 + pulse * 50)
            overlay.fill((255, 215, 0, gold_alpha))
        else:
            overlay.fill((0, 0, 0, 200))
        self.screen.blit(overlay, (0, 0))

        y_offset = 80

        if is_new_high_score:
            # Distinct record-state treatment.
            # Giant animated text
            scale_pulse = 1.0 + abs(math.sin(pygame.time.get_ticks() * 0.008)) * 0.3

            # Multiple congratulatory messages with rainbow colors
            congrats_messages = ["NEW HIGH SCORE!", "ABSOLUTE LEGEND", "MAIN CHARACTER ENERGY"]

            colors = [
                (255, 50, 50),  # Red
                (255, 140, 0),  # Orange
                (255, 215, 0),  # Gold
                (50, 255, 50),  # Green
                (50, 150, 255),  # Blue
                (200, 50, 255),  # Purple
            ]

            for i, msg_text in enumerate(congrats_messages):
                color_index = (i + int(pygame.time.get_ticks() * 0.003)) % len(colors)
                color = colors[color_index]

                # Scale gets bigger for each message
                size_mult = 1.0 + (i * 0.1)
                font_size = int(48 * scale_pulse * size_mult)
                celebration_font = pygame.font.Font(None, font_size)

                msg = celebration_font.render(msg_text, True, color)
                msg_rect = msg.get_rect(center=(settings.WIDTH // 2, y_offset))
                self.screen.blit(msg, msg_rect)
                y_offset += 60

            # Score with glow effect
            y_offset += 20
            score_text = self.large_font.render(f"SCORE: {score}", True, (255, 255, 255))

            # Draw glow by rendering same text multiple times slightly offset
            for offset_x in [-2, -1, 1, 2]:
                for offset_y in [-2, -1, 1, 2]:
                    glow = self.large_font.render(f"SCORE: {score}", True, (255, 215, 0))
                    glow_rect = glow.get_rect(center=(settings.WIDTH // 2 + offset_x, y_offset + offset_y))
                    self.screen.blit(glow, glow_rect)

            score_rect = score_text.get_rect(center=(settings.WIDTH // 2, y_offset))
            self.screen.blit(score_text, score_rect)
            y_offset += 60

            # Motivational message (use provided message to avoid flashing)
            if not message:
                message = "Literally HIM"  # Fallback
            motivation_text = self.font.render(message, True, (255, 255, 100))
            motivation_rect = motivation_text.get_rect(center=(settings.WIDTH // 2, y_offset))
            self.screen.blit(motivation_text, motivation_rect)

        else:
            # Regular game over - Enhanced visual presentation
            # Title with shadow effect
            header_font = settings.create_font(64, bold=True)
            header_text = "GAME OVER"

            # Shadow
            shadow = header_font.render(header_text, True, (0, 0, 0))
            shadow_rect = shadow.get_rect(center=(settings.WIDTH // 2 + 3, y_offset + 3))
            self.screen.blit(shadow, shadow_rect)

            # Main text with pulsing red
            pulse = abs(math.sin(pygame.time.get_ticks() * 0.003))
            red_intensity = int(200 + pulse * 55)
            header = header_font.render(header_text, True, (red_intensity, 60, 60))
            self.screen.blit(header, header.get_rect(center=(settings.WIDTH // 2, y_offset)))
            y_offset += 80

            # Score box with border
            score_box_rect = pygame.Rect(settings.WIDTH // 2 - 150, y_offset - 10, 300, 60)
            pygame.draw.rect(self.screen, (40, 40, 50), score_box_rect)
            pygame.draw.rect(self.screen, (150, 200, 255), score_box_rect, 3)

            # Score display
            score_font = settings.create_font(42, bold=True)
            score_text = score_font.render(f"Score: {score}", True, (150, 255, 150))
            self.screen.blit(score_text, score_text.get_rect(center=(settings.WIDTH // 2, y_offset + 20)))
            y_offset += 80

            # High score reference (if not a new high score)
            if score < high_score:
                high_score_font = settings.create_font(22)
                high_score_text = high_score_font.render(f"High Score: {high_score}", True, (200, 200, 200))
                self.screen.blit(high_score_text, high_score_text.get_rect(center=(settings.WIDTH // 2, y_offset)))
                y_offset += 40

            # Roast message (use provided message to avoid flashing)
            if not message:
                message = "POV: You're not HIM"  # Fallback
            roast_font = settings.create_font(24)
            roast_text = roast_font.render(message, True, (255, 200, 100))
            self.screen.blit(roast_text, roast_text.get_rect(center=(settings.WIDTH // 2, y_offset)))
            y_offset += 60

        # Action buttons with visual boxes
        y_offset += 20
        button_y = settings.HEIGHT - 100

        # AI auto-restart countdown (if AI mode)
        if is_ai_mode and ai_restart_time > 0:
            countdown_font = settings.create_font(28, bold=True)
            countdown_text = f"Restarting in {ai_restart_time:.1f}s..."
            countdown_surface = countdown_font.render(countdown_text, True, (255, 200, 100))
            countdown_rect = countdown_surface.get_rect(center=(settings.WIDTH // 2, button_y - 50))
            self.screen.blit(countdown_surface, countdown_rect)

        # Retry button
        retry_box = pygame.Rect(settings.WIDTH // 2 - 280, button_y, 250, 50)
        pygame.draw.rect(self.screen, (40, 60, 40), retry_box)
        pygame.draw.rect(self.screen, (150, 255, 150), retry_box, 3)

        retry_font = settings.create_font(24, bold=True)
        retry_label = "C - Watch Again" if is_ai_mode else "C - Play Again"
        retry_text = retry_font.render(retry_label, True, (150, 255, 150))
        retry_rect = retry_text.get_rect(center=retry_box.center)
        self.screen.blit(retry_text, retry_rect)

        # Quit/Back button
        quit_box = pygame.Rect(settings.WIDTH // 2 + 30, button_y, 250, 50)
        pygame.draw.rect(self.screen, (60, 40, 40), quit_box)
        pygame.draw.rect(self.screen, (255, 100, 100), quit_box, 3)

        quit_label = "Q - Back to Menu" if is_ai_mode else "Q - Quit"
        quit_text = retry_font.render(quit_label, True, (255, 150, 150))
        quit_rect = quit_text.get_rect(center=quit_box.center)
        self.screen.blit(quit_text, quit_rect)

        # Note: Don't call pygame.display.flip() here - let the main draw loop handle it

    def draw_help_overlay(self):
        """Draw comprehensive help overlay with all controls and power-ups."""
        # Dark overlay
        overlay = pygame.Surface((settings.WIDTH, settings.HEIGHT), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 220))
        self.screen.blit(overlay, (0, 0))

        y = 30

        # Smaller fonts for fitting more content
        small_font = settings.create_font(16)
        section_font = settings.create_font(18, bold=True)

        # Title
        title = self.large_font.render("HELP & POWER-UPS", True, (100, 200, 255))
        self.screen.blit(title, title.get_rect(center=(settings.WIDTH // 2, y)))
        y += 45

        # Controls section
        controls_title = section_font.render("CONTROLS:", True, (255, 255, 100))
        self.screen.blit(controls_title, (60, y))
        y += 25

        controls = [
            "Arrow Keys / WASD - Move snake",
            "Mouse Click - Move toward position",
            "P - Pause  |  H - This Help  |  ESC - Exit  |  F11 - Fullscreen",
            "S - Settings  |  C - Customization  |  V - High Scores  |  A - Achievements",
            "L - Browse Let's Play channels (watch AI streamers)",
            "",
            "RADIO CONTROLS:",
            "M - Mute/OFF  |  R - Next station  |  [ ] - Prev/Next  |  1-9 - Direct select",
        ]
        for control in controls:
            # Slight indent for radio controls subsection
            indent = 100 if control.startswith("M -") else 80
            text = small_font.render(control, True, (200, 200, 200))
            self.screen.blit(text, (indent, y))
            y += 20

        y += 5

        # Power-ups section
        powerups_title = section_font.render("POWER-UPS (ALL 9):", True, (255, 255, 100))
        self.screen.blit(powerups_title, (60, y))
        y += 25

        # All 9 power-ups with colors
        powerups = [
            ((0, 255, 255), None, "SHIELD", "Absorbs the next crash for up to 5 seconds."),
            ((255, 255, 0), None, "SLOW-MO", "Slows movement for 6 seconds."),
            ((255, 20, 147), (255, 215, 0), "MAGNET", "Pulls food toward your head for 6 seconds."),
            ((255, 140, 0), None, "BOOST", "Doubles movement speed for 4 seconds."),
            ((200, 0, 255), None, "PHASE SHIFT", "Crosses your body and detached walls for 5 seconds."),
            ((255, 50, 50), (255, 215, 0), "GLUTTONY", "Scores from food without growing for 5 seconds."),
            ((128, 128, 0), (255, 215, 0), "BAIT", "Pulls the next food spawn toward this location."),
            ((255, 80, 0), None, "LAST STAND", "Survives one death, halves length, grants recovery."),
            ((70, 130, 180), None, "SEGMENT DETACH", "Turns up to five tail cells into 10-second walls."),
        ]

        for fill_color, outline_color, name, desc in powerups:
            # Draw color square (smaller)
            pygame.draw.rect(self.screen, fill_color, (80, y, 14, 14))
            if outline_color:
                pygame.draw.rect(self.screen, outline_color, (80, y, 14, 14), width=2)

            # Draw name and description
            text = small_font.render(f"{name}: {desc}", True, settings.WHITE)
            self.screen.blit(text, (100, y - 1))
            y += 20

        y += 5

        # Scoring info
        scoring_title = section_font.render("SCORING:", True, (255, 255, 100))
        self.screen.blit(scoring_title, (60, y))
        y += 25

        scoring_info = [
            "Chain food for COMBO multipliers: 2x, 3x, 5x, 10x!",
            "Speed bonus (+50%) for eating within 1.5 seconds",
            "Near-miss bonuses: Close Call (+1), Threading Needle (+2)",
            "WARNING: You starve after 30 seconds without food!",
        ]
        for info in scoring_info:
            text = small_font.render(info, True, (200, 200, 200))
            self.screen.blit(text, (80, y))
            y += 20

        y += 5

        # Progression section
        progression_title = section_font.render("PROGRESSION:", True, (255, 255, 100))
        self.screen.blit(progression_title, (60, y))
        y += 25

        progression_info = [
            "Unlock milestone colors and trails by eating food, wrapping edges, and building combos",
            "Earn 25 achievements (common, rare, epic, legendary)",
        ]
        for info in progression_info:
            text = small_font.render(info, True, (200, 200, 200))
            self.screen.blit(text, (80, y))
            y += 20

        y += 10

        # Footer
        footer = small_font.render("Press H or ESC to close | Press ENTER to start playing!", True, (150, 200, 255))
        self.screen.blit(footer, footer.get_rect(center=(settings.WIDTH // 2, y)))

    def draw_lets_play_overlay(self, ai_name: str, ai_description: str, score: int, combo: int):
        """
        Draw overlay showing AI streamer and stats during Let's Play mode.

        Args:
            ai_name: Name of the AI streamer
            ai_description: Funny description of their play style
            score: Current score
            combo: Current combo count
        """
        # Semi-transparent top banner with streaming vibe
        banner_height = 100
        banner = pygame.Surface((settings.WIDTH, banner_height), pygame.SRCALPHA)
        banner.fill((30, 10, 40, 220))  # Slightly purple tint for streaming aesthetic
        self.screen.blit(banner, (0, 0))

        # Streamer name with "NOW STREAMING" indicator
        streaming_text = self.font.render("NOW STREAMING:", True, (255, 100, 200))
        name_text = self.large_font.render(ai_name, True, (255, 200, 255))
        desc_text = self.font.render(ai_description, True, (200, 180, 255))

        self.screen.blit(streaming_text, streaming_text.get_rect(center=(settings.WIDTH // 2, 15)))
        self.screen.blit(name_text, name_text.get_rect(center=(settings.WIDTH // 2, 40)))
        self.screen.blit(desc_text, desc_text.get_rect(center=(settings.WIDTH // 2, 68)))

        # Controls hint - bright white with black shadow for readability
        hint_font = settings.create_font(18, bold=True)
        hint_str = "ESC/L - Stop Watching | ENTER - New Streamer | R - Change Station"

        # Draw shadow first
        shadow_text = hint_font.render(hint_str, True, (0, 0, 0))
        shadow_rect = shadow_text.get_rect(center=(settings.WIDTH // 2 + 1, 89))
        self.screen.blit(shadow_text, shadow_rect)

        # Draw main text in bright white
        hint_text = hint_font.render(hint_str, True, (255, 255, 255))
        self.screen.blit(hint_text, hint_text.get_rect(center=(settings.WIDTH // 2, 88)))

    def draw_channel_browser(self, personalities_list, selected_index: int):
        """
        Draw Twitch-style channel browser to pick which AI streamer to watch.

        Args:
            personalities_list: List of (key, AIPersonality) tuples
            selected_index: Currently selected channel index
        """
        import math

        # Dark semi-transparent background
        overlay = pygame.Surface((settings.WIDTH, settings.HEIGHT), pygame.SRCALPHA)
        overlay.fill((10, 10, 20, 240))
        self.screen.blit(overlay, (0, 0))

        # Header
        header_text = self.large_font.render("BROWSE CHANNELS", True, (255, 100, 200))
        header_rect = header_text.get_rect(center=(settings.WIDTH // 2, 30))
        self.screen.blit(header_text, header_rect)

        subtitle = self.font.render("Pick an AI Streamer to Watch", True, (150, 150, 150))
        subtitle_rect = subtitle.get_rect(center=(settings.WIDTH // 2, 60))
        self.screen.blit(subtitle, subtitle_rect)

        # Channel list area
        list_start_y = 100
        list_item_height = 50
        visible_channels = 8

        # Calculate scroll offset to keep selected item visible
        if selected_index >= visible_channels:
            scroll_offset = selected_index - visible_channels + 1
        else:
            scroll_offset = 0

        # Draw channels
        for i in range(visible_channels):
            channel_index = i + scroll_offset
            if channel_index >= len(personalities_list):
                break

            key, personality = personalities_list[channel_index]
            y_pos = list_start_y + (i * list_item_height)

            # Highlight selected channel
            if channel_index == selected_index:
                # Selected background with pulsing effect
                pulse = abs(math.sin(pygame.time.get_ticks() * 0.003))
                highlight_alpha = int(150 + pulse * 50)
                highlight = pygame.Surface((settings.WIDTH - 40, list_item_height - 4), pygame.SRCALPHA)
                highlight.fill((100, 50, 150, highlight_alpha))
                self.screen.blit(highlight, (20, y_pos))

                # Selected indicator
                arrow = self.large_font.render(">", True, (255, 200, 255))
                self.screen.blit(arrow, (25, y_pos + 8))

            # Channel color indicator (small square)
            color_size = 20
            pygame.draw.rect(self.screen, personality.color, (60, y_pos + 10, color_size, color_size), border_radius=3)

            # Channel name
            name_color = (255, 255, 255) if channel_index == selected_index else (200, 200, 200)
            name_text = self.font.render(personality.name, True, name_color)
            self.screen.blit(name_text, (90, y_pos + 8))

            # Channel description (small, gray)
            desc_color = (180, 180, 180) if channel_index == selected_index else (120, 120, 120)
            desc_text = pygame.font.Font(None, 18).render(personality.description[:50] + "...", True, desc_color)
            self.screen.blit(desc_text, (90, y_pos + 30))

        # Controls footer
        footer_y = settings.HEIGHT - 80
        controls_bg = pygame.Surface((settings.WIDTH, 80), pygame.SRCALPHA)
        controls_bg.fill((20, 20, 30, 220))
        self.screen.blit(controls_bg, (0, footer_y))

        controls = [
            "UP/DOWN: Browse Channels",
            "ENTER: Watch Selected Stream",
            "R: Random Channel",
            "ESC: Back to Menu",
        ]

        for i, control in enumerate(controls):
            control_text = pygame.font.Font(None, 20).render(control, True, (150, 150, 150))
            x_offset = (settings.WIDTH // 4) * i + 20
            self.screen.blit(control_text, (x_offset, footer_y + 30))

    def draw_name_entry_screen(self, current_name: str, score: int, cursor_blink: bool):
        """
        Draw name entry screen for high score or first-time setup.

        Args:
            current_name: Current entered name (max 12 chars)
            score: Score achieved (0 for first-time setup)
            cursor_blink: Whether to show cursor (blinks)
        """
        # Semi-transparent overlay with cyberpunk vibe
        overlay = pygame.Surface((settings.WIDTH, settings.HEIGHT), pygame.SRCALPHA)
        overlay.fill((0, 0, 0, 220))
        self.screen.blit(overlay, (0, 0))

        y_offset = 80

        # Show logo for first-time setup
        if score == 0 and self.logo:
            logo_rect = self.logo.get_rect(center=(settings.WIDTH // 2, y_offset + 100))
            self.screen.blit(self.logo, logo_rect)
            y_offset += 220

        # Title - different for first-time vs high score - Retro pixel font style
        title_font = pygame.font.Font(None, 72)  # Pygame default monospace
        if score > 0:
            title_color = (255, 215, 0)  # Gold
            title_text = "NEW HIGH SCORE!"
        else:
            title_color = (100, 255, 100)  # Matrix green
            title_text = "WELCOME TO VIBE SNAKE!"

        # Render title with slight glow effect (transparency)
        title_glow = title_font.render(title_text, True, title_color)
        title_glow.set_alpha(80)
        title_glow_rect = title_glow.get_rect(center=(settings.WIDTH // 2 + 2, y_offset + 2))
        self.screen.blit(title_glow, title_glow_rect)

        title = title_font.render(title_text, True, title_color)
        title_rect = title.get_rect(center=(settings.WIDTH // 2, y_offset))
        self.screen.blit(title, title_rect)

        y_offset += 80

        # Score display (only if high score entry)
        if score > 0:
            score_font = pygame.font.Font(None, 48)
            score_text_str = f"SCORE: {score}"
            score_glow = score_font.render(score_text_str, True, (255, 255, 255))
            score_glow.set_alpha(60)
            score_glow_rect = score_glow.get_rect(center=(settings.WIDTH // 2 + 1, y_offset + 1))
            self.screen.blit(score_glow, score_glow_rect)

            score_text = score_font.render(score_text_str, True, (255, 255, 255))
            score_rect = score_text.get_rect(center=(settings.WIDTH // 2, y_offset))
            self.screen.blit(score_text, score_rect)
            y_offset += 80

        prompt_y = y_offset

        # Prompt - Retro cyberpunk style
        prompt_font = pygame.font.Font(None, 40)
        prompt_text = "ENTER YOUR NAME:"
        prompt_glow = prompt_font.render(prompt_text, True, (0, 200, 200))
        prompt_glow.set_alpha(70)
        prompt_glow_rect = prompt_glow.get_rect(center=(settings.WIDTH // 2 + 1, prompt_y + 1))
        self.screen.blit(prompt_glow, prompt_glow_rect)

        prompt = prompt_font.render(prompt_text, True, (0, 255, 255))  # Cyan
        prompt_rect = prompt.get_rect(center=(settings.WIDTH // 2, prompt_y))
        self.screen.blit(prompt, prompt_rect)

        # Name input box - Cyberpunk style
        box_width = 400
        box_height = 60
        box_x = (settings.WIDTH - box_width) // 2
        box_y = prompt_y + 60

        # Draw box with neon glow effect
        glow_box = pygame.Surface((box_width + 6, box_height + 6), pygame.SRCALPHA)
        glow_box.fill((0, 255, 255, 30))  # Cyan glow
        self.screen.blit(glow_box, (box_x - 3, box_y - 3))

        pygame.draw.rect(self.screen, (20, 20, 40), (box_x, box_y, box_width, box_height))
        pygame.draw.rect(self.screen, (0, 255, 255), (box_x, box_y, box_width, box_height), 2)

        # Draw name text with cursor - Monospace retro style
        name_font = pygame.font.Font(None, 48)
        display_name = current_name if current_name else ""
        if cursor_blink:
            display_name += "|"

        # Render with glow
        name_glow = name_font.render(display_name, True, (100, 255, 100))
        name_glow.set_alpha(60)
        name_glow_rect = name_glow.get_rect(center=(settings.WIDTH // 2 + 1, box_y + box_height // 2 + 1))
        self.screen.blit(name_glow, name_glow_rect)

        name_surface = name_font.render(display_name, True, (0, 255, 0))  # Matrix green
        name_rect = name_surface.get_rect(center=(settings.WIDTH // 2, box_y + box_height // 2))
        self.screen.blit(name_surface, name_rect)

        # Instructions - Retro monospace
        instructions = [
            "TYPE YOUR NAME WITH KEYBOARD (MAX 12 CHARS)",
            "ENTER/A: CONFIRM  |  BACKSPACE: DELETE  |  ESC/B: QUICK PLAY",
        ]

        inst_font = pygame.font.Font(None, 24)
        for i, instruction in enumerate(instructions):
            inst_glow = inst_font.render(instruction, True, (100, 100, 100))
            inst_glow.set_alpha(50)
            inst_glow_rect = inst_glow.get_rect(center=(settings.WIDTH // 2 + 1, prompt_y + 170 + i * 25 + 1))
            self.screen.blit(inst_glow, inst_glow_rect)

            inst_surface = inst_font.render(instruction, True, (150, 150, 150))
            inst_rect = inst_surface.get_rect(center=(settings.WIDTH // 2, prompt_y + 170 + i * 25))
            self.screen.blit(inst_surface, inst_rect)

    def draw_customization_menu(
        self,
        customization,
        selected_category: int,
        selected_option: int,
        options_list: list,
        player_profile=None,
        notification=None,
    ):
        """
        Draw snake customization menu with live preview and unlock status.

        Args:
            customization: Current SnakeCustomization object
            selected_category: Index of selected category (0-4)
            selected_option: Index of selected option within category
            options_list: List of available options for current category
            player_profile: PlayerProfile object for checking unlocks (optional)
            notification: Optional notification message to display (optional)
        """
        from vibesnake.core.customization import UNLOCK_REQUIREMENTS

        # Background
        self.screen.fill((20, 20, 30))

        # Title
        title_font = settings.create_font(42, bold=True)
        title = title_font.render("CUSTOMIZE YOUR SNAKE", True, (255, 215, 0))
        title_rect = title.get_rect(center=(settings.WIDTH // 2, 50))
        self.screen.blit(title, title_rect)

        # Categories (left side)
        categories = ["Color", "Pattern", "Eyes", "Accessory", "Trail"]
        category_x = 50
        category_y_start = 150

        cat_font = settings.create_font(24, bold=True)
        for i, category in enumerate(categories):
            y_pos = category_y_start + i * 60
            color = (255, 255, 255) if i == selected_category else (100, 100, 100)

            # Highlight selected category
            if i == selected_category:
                highlight_rect = pygame.Rect(category_x - 10, y_pos - 5, 150, 40)
                pygame.draw.rect(self.screen, (50, 50, 80), highlight_rect)
                pygame.draw.rect(self.screen, (100, 150, 255), highlight_rect, 2)

            cat_text = cat_font.render(category, True, color)
            self.screen.blit(cat_text, (category_x, y_pos))

        # Options (middle section)
        options_x = 250
        options_y_start = 150

        # Color category: Show as visual grid
        if selected_category == 0:  # Color
            grid_x = options_x
            grid_y = options_y_start
            swatch_size = 50
            spacing = 10
            cols = 3  # 3 columns

            for i, option in enumerate(options_list):
                row = i // cols
                col = i % cols
                x = grid_x + col * (swatch_size + spacing)
                y = grid_y + row * (swatch_size + spacing)

                # Check if unlocked
                option_name = option[0] if isinstance(option, tuple) else option
                requirement = UNLOCK_REQUIREMENTS.get(option_name, 0)
                is_unlocked = player_profile.check_unlocked(option_name, requirement) if player_profile else True

                # Draw swatch - ALWAYS show actual color for preview!
                swatch_rect = pygame.Rect(x, y, swatch_size, swatch_size)
                color_value = option[1]
                pygame.draw.rect(self.screen, color_value, swatch_rect)

                # If locked, add semi-transparent dark overlay
                if not is_unlocked:
                    dark_overlay = pygame.Surface((swatch_size, swatch_size))
                    dark_overlay.set_alpha(160)  # Semi-transparent
                    dark_overlay.fill((20, 20, 20))
                    self.screen.blit(dark_overlay, (x, y))

                    # Show lock icon (use text "LOCK" instead of emoji for compatibility)
                    try:
                        lock_font = settings.create_font(16, bold=True)
                        lock_text = lock_font.render("LOCK", True, (255, 255, 255))
                        lock_rect = lock_text.get_rect(center=swatch_rect.center)
                        self.screen.blit(lock_text, lock_rect)
                    except Exception as e:
                        print(f"[Customization] Lock icon error: {e}")

                # Border - highlight if selected
                if i == selected_option:
                    if is_unlocked:
                        pygame.draw.rect(self.screen, (0, 255, 255), swatch_rect, 4)  # Cyan border
                    else:
                        pygame.draw.rect(self.screen, (255, 200, 0), swatch_rect, 4)  # Gold border for locked
                else:
                    pygame.draw.rect(self.screen, (100, 100, 100), swatch_rect, 2)

            # Show selected color name and unlock requirement below grid
            if selected_option < len(options_list):
                selected_item = options_list[selected_option]
                color_name = selected_item[0]
                requirement = UNLOCK_REQUIREMENTS.get(color_name, 0)
                is_unlocked = player_profile.check_unlocked(color_name, requirement) if player_profile else True

                name_font = pygame.font.Font(None, 32)
                name_text = name_font.render(color_name, True, (255, 255, 255))
                name_rect = name_text.get_rect(center=(grid_x + (cols * (swatch_size + spacing)) // 2, grid_y + 300))
                self.screen.blit(name_text, name_rect)

                if not is_unlocked:
                    req_font = pygame.font.Font(None, 24)
                    # Handle new tuple format: (requirement_type, value, description)
                    if isinstance(requirement, tuple) and len(requirement) >= 3:
                        req_text = requirement[2]  # Use the description
                    else:
                        req_text = "Unlock requirement"
                    req_surface = req_font.render(req_text, True, (255, 100, 100))
                    req_rect = req_surface.get_rect(
                        center=(grid_x + (cols * (swatch_size + spacing)) // 2, grid_y + 330)
                    )
                    self.screen.blit(req_surface, req_rect)

        else:  # Other categories: Show as list
            opt_font = pygame.font.Font(None, 28)  # Monospace
            lock_font = pygame.font.Font(None, 20)
            for i, option in enumerate(options_list):
                y_pos = options_y_start + i * 45

                # Don't draw if outside visible area
                if y_pos > settings.HEIGHT - 100:
                    break

                # Check if this option is unlocked
                option_name = option[0] if isinstance(option, tuple) else option
                requirement = UNLOCK_REQUIREMENTS.get(option_name, 0)
                is_unlocked = player_profile.check_unlocked(option_name, requirement) if player_profile else True

                # Color based on unlock status
                if not is_unlocked:
                    color = (80, 80, 80)  # Dim for locked
                elif i == selected_option:
                    color = (0, 255, 255)  # Cyan for selected
                else:
                    color = (150, 150, 150)  # Gray for available

                # Highlight selected option
                if i == selected_option:
                    highlight_rect = pygame.Rect(options_x - 10, y_pos - 5, 400, 35)
                    if is_unlocked:
                        pygame.draw.rect(self.screen, (0, 50, 50), highlight_rect)
                        pygame.draw.rect(self.screen, (0, 255, 255), highlight_rect, 2)
                    else:
                        pygame.draw.rect(self.screen, (50, 20, 20), highlight_rect)
                        pygame.draw.rect(self.screen, (150, 50, 50), highlight_rect, 2)

                # Display option name
                display_text = f"{'[LOCKED] ' if not is_unlocked else ''}{option.title()}"
                opt_text = opt_font.render(display_text, True, color)
                self.screen.blit(opt_text, (options_x, y_pos))

                # Show unlock requirement for locked items (when selected)
                if not is_unlocked and i == selected_option:
                    if isinstance(requirement, tuple) and len(requirement) >= 3:
                        req_text = f"UNLOCK: {requirement[2]}"
                    elif isinstance(requirement, (int, float)) and requirement > 0:
                        req_text = f"UNLOCK: Play {requirement} games"
                    else:
                        req_text = "UNLOCK: Play more"
                    req_surface = lock_font.render(req_text, True, (255, 100, 100))
                    self.screen.blit(req_surface, (options_x, y_pos + 25))

        # Preview (right side) - Animated snake preview
        preview_x = 550
        preview_y = 200
        preview_size = 200

        # Preview background
        preview_bg = pygame.Rect(preview_x - 10, preview_y - 10, preview_size + 20, preview_size + 20)
        pygame.draw.rect(self.screen, (40, 40, 50), preview_bg)
        pygame.draw.rect(self.screen, (100, 100, 100), preview_bg, 2)

        # Preview label
        preview_label = settings.create_font(18).render("LIVE PREVIEW", True, (200, 200, 200))
        self.screen.blit(preview_label, (preview_x + 50, preview_y - 35))

        # Animated preview using pygame.time
        import math

        anim_time = pygame.time.get_ticks() / 1000.0  # Time in seconds

        # Create snake segments like actual gameplay - square segments in a line
        num_segments = 6
        segment_size = 20  # Square size like in-game
        segment_positions = []

        # Horizontal snake with slight movement animation
        offset_x = math.sin(anim_time * 2) * 10  # Gentle horizontal sway
        for i in range(num_segments):
            x = preview_x + 40 + i * segment_size + offset_x
            y = preview_y + 100
            segment_positions.append((x, y))

        # Draw trail effect preview if trail is set
        if customization.trail != "none":
            trail_name = customization.trail
            # Draw a few trail particles behind tail
            for j in range(5):
                fade = 1.0 - j / 5.0
                alpha = int(255 * fade)
                trail_x = segment_positions[0][0] - j * 5
                trail_y = segment_positions[0][1] + math.sin(anim_time * 3 + j * 0.3) * 3

                if trail_name == "sparkle":
                    trail_surf = pygame.Surface((6, 6), pygame.SRCALPHA)
                    pygame.draw.circle(trail_surf, (255, 255, 200, alpha), (3, 3), 3)
                    self.screen.blit(trail_surf, (int(trail_x - 3), int(trail_y - 3)))
                elif trail_name == "smoke":
                    size = int(4 + j)
                    trail_surf = pygame.Surface((size * 2, size * 2), pygame.SRCALPHA)
                    pygame.draw.circle(trail_surf, (150, 150, 150, alpha // 2), (size, size), size)
                    self.screen.blit(trail_surf, (int(trail_x - size), int(trail_y - size)))
                elif trail_name == "rainbow":
                    hue = (anim_time * 100 + j * 40) % 360
                    color = self._hsv_to_rgb(hue, 1.0, 1.0)
                    trail_surf = pygame.Surface((6, 6), pygame.SRCALPHA)
                    pygame.draw.circle(trail_surf, (*color, alpha), (3, 3), 3)
                    self.screen.blit(trail_surf, (int(trail_x - 3), int(trail_y - 3)))
                elif trail_name == "fire":
                    colors = [(255, 255, 200), (255, 200, 0), (255, 100, 0), (200, 50, 0)]
                    color = colors[min(j, 3)]
                    trail_surf = pygame.Surface((6, 6), pygame.SRCALPHA)
                    pygame.draw.circle(trail_surf, (*color, alpha), (3, 3), 3)
                    self.screen.blit(trail_surf, (int(trail_x - 3), int(trail_y - 3)))

        # Draw snake segments with customization
        for i, pos in enumerate(segment_positions):
            is_head = i == num_segments - 1

            # Use current customization colors with gradient
            segment_progress = i / max(num_segments - 1, 1)
            if customization.secondary_color:
                # Blend between base and secondary color
                r = int(
                    customization.base_color[0]
                    + (customization.secondary_color[0] - customization.base_color[0]) * segment_progress
                )
                g = int(
                    customization.base_color[1]
                    + (customization.secondary_color[1] - customization.base_color[1]) * segment_progress
                )
                b = int(
                    customization.base_color[2]
                    + (customization.secondary_color[2] - customization.base_color[2]) * segment_progress
                )
                color = (r, g, b)
            else:
                color = customization.base_color

            # Draw segment as SQUARE with rounded corners (like actual gameplay)
            # Draw segment as rounded rectangle like in-game
            segment_rect = pygame.Rect(
                int(pos[0] - segment_size / 2), int(pos[1] - segment_size / 2), segment_size, segment_size
            )
            pygame.draw.rect(self.screen, color, segment_rect, border_radius=int(segment_size * 0.2))

            # Draw pattern overlay on segments
            if customization.pattern != "none":
                if customization.pattern == "stripes":
                    # Horizontal stripes on square
                    stripe_y1 = segment_rect.top + segment_size * 0.33
                    stripe_y2 = segment_rect.top + segment_size * 0.66
                    pygame.draw.line(
                        self.screen,
                        customization.pattern_color,
                        (segment_rect.left, int(stripe_y1)),
                        (segment_rect.right, int(stripe_y1)),
                        2,
                    )
                    pygame.draw.line(
                        self.screen,
                        customization.pattern_color,
                        (segment_rect.left, int(stripe_y2)),
                        (segment_rect.right, int(stripe_y2)),
                        2,
                    )
                elif customization.pattern == "dots":
                    # Grid of dots
                    dot_size = 2
                    for dx in [-segment_size * 0.25, segment_size * 0.25]:
                        for dy in [-segment_size * 0.25, segment_size * 0.25]:
                            pygame.draw.circle(
                                self.screen, customization.pattern_color, (int(pos[0] + dx), int(pos[1] + dy)), dot_size
                            )
                elif customization.pattern == "scales":
                    # Arc pattern
                    pygame.draw.arc(self.screen, customization.pattern_color, segment_rect, 0, math.pi, 2)

            # Draw eyes and accessories on head
            if is_head:
                eye_offset = 6
                eye_size = 3
                eye_y_offset = -3

                # Draw eyes based on style
                if customization.eye_style == "cute":
                    pygame.draw.circle(
                        self.screen,
                        customization.eye_color,
                        (int(pos[0] - eye_offset), int(pos[1] + eye_y_offset)),
                        eye_size,
                    )
                    pygame.draw.circle(
                        self.screen,
                        customization.eye_color,
                        (int(pos[0] + eye_offset), int(pos[1] + eye_y_offset)),
                        eye_size,
                    )
                elif customization.eye_style == "angry":
                    pygame.draw.line(
                        self.screen,
                        customization.eye_color,
                        (int(pos[0] - eye_offset - 3), int(pos[1] + eye_y_offset - 2)),
                        (int(pos[0] - eye_offset + 3), int(pos[1] + eye_y_offset + 2)),
                        2,
                    )
                    pygame.draw.line(
                        self.screen,
                        customization.eye_color,
                        (int(pos[0] + eye_offset - 3), int(pos[1] + eye_y_offset + 2)),
                        (int(pos[0] + eye_offset + 3), int(pos[1] + eye_y_offset - 2)),
                        2,
                    )
                elif customization.eye_style == "sleepy":
                    pygame.draw.line(
                        self.screen,
                        customization.eye_color,
                        (int(pos[0] - eye_offset - 3), int(pos[1] + eye_y_offset)),
                        (int(pos[0] - eye_offset + 3), int(pos[1] + eye_y_offset)),
                        2,
                    )
                    pygame.draw.line(
                        self.screen,
                        customization.eye_color,
                        (int(pos[0] + eye_offset - 3), int(pos[1] + eye_y_offset)),
                        (int(pos[0] + eye_offset + 3), int(pos[1] + eye_y_offset)),
                        2,
                    )
                elif customization.eye_style == "derp":
                    pygame.draw.circle(
                        self.screen, customization.eye_color, (int(pos[0] - eye_offset), int(pos[1] + eye_y_offset)), 2
                    )
                    pygame.draw.circle(
                        self.screen, customization.eye_color, (int(pos[0] + eye_offset), int(pos[1] + eye_y_offset)), 5
                    )
                elif customization.eye_style == "laser":
                    # Red X eyes
                    pygame.draw.line(
                        self.screen,
                        (255, 0, 0),
                        (int(pos[0] - eye_offset - 2), int(pos[1] + eye_y_offset - 2)),
                        (int(pos[0] - eye_offset + 2), int(pos[1] + eye_y_offset + 2)),
                        2,
                    )
                    pygame.draw.line(
                        self.screen,
                        (255, 0, 0),
                        (int(pos[0] - eye_offset - 2), int(pos[1] + eye_y_offset + 2)),
                        (int(pos[0] - eye_offset + 2), int(pos[1] + eye_y_offset - 2)),
                        2,
                    )
                    pygame.draw.line(
                        self.screen,
                        (255, 0, 0),
                        (int(pos[0] + eye_offset - 2), int(pos[1] + eye_y_offset - 2)),
                        (int(pos[0] + eye_offset + 2), int(pos[1] + eye_y_offset + 2)),
                        2,
                    )
                    pygame.draw.line(
                        self.screen,
                        (255, 0, 0),
                        (int(pos[0] + eye_offset - 2), int(pos[1] + eye_y_offset + 2)),
                        (int(pos[0] + eye_offset + 2), int(pos[1] + eye_y_offset - 2)),
                        2,
                    )

                # Draw accessory
                if customization.accessory == "hat":
                    hat_y = pos[1] - segment_size - 8
                    pygame.draw.rect(self.screen, customization.accessory_color, (int(pos[0] - 8), int(hat_y), 16, 8))
                    pygame.draw.rect(
                        self.screen, customization.accessory_color, (int(pos[0] - 10), int(hat_y + 6), 20, 3)
                    )
                elif customization.accessory == "crown":
                    crown_y = pos[1] - segment_size - 6
                    points = [
                        (pos[0] - 10, crown_y + 6),
                        (pos[0] - 8, crown_y),
                        (pos[0] - 3, crown_y + 4),
                        (pos[0], crown_y - 2),
                        (pos[0] + 3, crown_y + 4),
                        (pos[0] + 8, crown_y),
                        (pos[0] + 10, crown_y + 6),
                    ]
                    pygame.draw.polygon(
                        self.screen, customization.accessory_color, [(int(p[0]), int(p[1])) for p in points]
                    )
                elif customization.accessory == "sunglasses":
                    pygame.draw.ellipse(self.screen, (20, 20, 20), (int(pos[0] - 10), int(pos[1] - 4), 6, 4))
                    pygame.draw.ellipse(self.screen, (20, 20, 20), (int(pos[0] + 4), int(pos[1] - 4), 6, 4))
                    pygame.draw.ellipse(
                        self.screen, customization.accessory_color, (int(pos[0] - 10), int(pos[1] - 4), 6, 4), 1
                    )
                    pygame.draw.ellipse(
                        self.screen, customization.accessory_color, (int(pos[0] + 4), int(pos[1] - 4), 6, 4), 1
                    )
                elif customization.accessory == "headphones":
                    # Ear cups
                    pygame.draw.circle(self.screen, (50, 50, 50), (int(pos[0] - 12), int(pos[1])), 4)
                    pygame.draw.circle(
                        self.screen, customization.accessory_color, (int(pos[0] - 12), int(pos[1])), 4, 1
                    )
                    pygame.draw.circle(self.screen, (50, 50, 50), (int(pos[0] + 12), int(pos[1])), 4)
                    pygame.draw.circle(
                        self.screen, customization.accessory_color, (int(pos[0] + 12), int(pos[1])), 4, 1
                    )
                    # Headband
                    arc_rect = pygame.Rect(int(pos[0] - 12), int(pos[1] - 16), 24, 24)
                    pygame.draw.arc(self.screen, customization.accessory_color, arc_rect, 0, math.pi, 2)
                elif customization.accessory == "bowtie":
                    bow_y = pos[1] + segment_size + 4
                    points = [
                        (pos[0] - 8, bow_y),
                        (pos[0] - 2, bow_y - 2),
                        (pos[0] + 2, bow_y - 2),
                        (pos[0] + 8, bow_y),
                        (pos[0] + 2, bow_y + 2),
                        (pos[0] - 2, bow_y + 2),
                    ]
                    pygame.draw.polygon(
                        self.screen, customization.accessory_color, [(int(p[0]), int(p[1])) for p in points]
                    )
                    pygame.draw.circle(self.screen, customization.accessory_color, (int(pos[0]), int(bow_y)), 2)

        # Instructions (bottom)
        instructions = [
            "UP/DOWN: Navigate  |  LEFT/RIGHT: Change Category",
            "ENTER: Apply  |  ESC: Cancel",
            "1/2/3: Save to Slot  |  4/5/6: Load from Slot",
        ]

        inst_y = settings.HEIGHT - 80
        inst_font = settings.create_font(18)
        for i, instruction in enumerate(instructions):
            inst_surface = inst_font.render(instruction, True, (150, 150, 150))
            inst_rect = inst_surface.get_rect(center=(settings.WIDTH // 2, inst_y + i * 25))
            self.screen.blit(inst_surface, inst_rect)

        # Draw notification if present (save/load confirmation)
        if notification:
            notif_font = settings.create_font(32, bold=True)
            notif_surface = notif_font.render(notification, True, (0, 255, 0))
            notif_rect = notif_surface.get_rect(center=(settings.WIDTH // 2, settings.HEIGHT // 2 - 100))
            # Add background for better visibility
            padding = 20
            bg_rect = pygame.Rect(
                notif_rect.left - padding,
                notif_rect.top - padding,
                notif_rect.width + padding * 2,
                notif_rect.height + padding * 2,
            )
            pygame.draw.rect(self.screen, (20, 40, 20), bg_rect)
            pygame.draw.rect(self.screen, (0, 255, 0), bg_rect, 3)
            self.screen.blit(notif_surface, notif_rect)

    def draw_achievement_notification(self, achievement, timer: float):
        """
        Draw achievement unlock notification (toast-style popup in top-right).

        Args:
            achievement: Achievement object that was unlocked
            timer: Time remaining for display (used for fade animation)
        """
        # Rarity colors
        rarity_colors = {
            "common": (200, 200, 200),  # Gray
            "rare": (100, 150, 255),  # Blue
            "epic": (200, 100, 255),  # Purple
            "legendary": (255, 215, 0),  # Gold
        }

        border_color = rarity_colors.get(achievement.rarity, (200, 200, 200))

        # Calculate fade-in/fade-out alpha
        if timer > 3.5:  # Fade in during first 0.5s
            alpha = int((4.0 - timer) * 2 * 255)
        elif timer < 0.5:  # Fade out during last 0.5s
            alpha = int(timer * 2 * 255)
        else:
            alpha = 255

        # Position in top-right corner
        notif_width = 350
        notif_height = 100
        notif_x = settings.WIDTH - notif_width - 20
        notif_y = 20

        # Create semi-transparent background surface
        notif_surface = pygame.Surface((notif_width, notif_height))
        notif_surface.set_alpha(alpha)
        notif_surface.fill((30, 30, 40))

        # Draw border
        pygame.draw.rect(notif_surface, border_color, pygame.Rect(0, 0, notif_width, notif_height), 3)

        # Draw achievement text badge
        icon_font = pygame.font.Font(None, 48)
        icon_surface = icon_font.render(achievement.icon, True, (255, 255, 255))
        notif_surface.blit(icon_surface, (15, 25))

        # Draw "ACHIEVEMENT UNLOCKED" text
        header_font = settings.create_font(14, bold=True)
        header_surface = header_font.render("ACHIEVEMENT UNLOCKED", True, border_color)
        notif_surface.blit(header_surface, (70, 15))

        # Draw achievement name
        name_font = settings.create_font(20, bold=True)
        name_surface = name_font.render(achievement.name, True, (255, 255, 255))
        notif_surface.blit(name_surface, (70, 35))

        # Draw achievement description
        desc_font = settings.create_font(14)
        desc_surface = desc_font.render(achievement.description, True, (180, 180, 180))
        notif_surface.blit(desc_surface, (70, 60))

        # Blit to screen
        self.screen.blit(notif_surface, (notif_x, notif_y))

    def draw_settings_menu(self, selected_option: int, sound_on: bool, volume: float):
        """
        Draw settings menu.

        Args:
            selected_option: Currently selected menu option
            sound_on: Whether sound is enabled
            volume: Current volume (0.0 to 1.0)
        """
        # Background
        self.screen.fill((20, 20, 30))

        # Title
        title_font = settings.create_font(48, bold=True)
        title_surface = title_font.render("SETTINGS", True, (150, 200, 255))
        title_rect = title_surface.get_rect(center=(settings.WIDTH // 2, 60))
        self.screen.blit(title_surface, title_rect)

        # Settings options
        options_y_start = 150
        option_height = 60

        settings_options = [
            ("Sound", f"{'ON' if sound_on else 'OFF'}"),
            ("Volume", f"{int(volume * 100)}%"),
            ("Back to Menu", ""),
        ]

        for i, (label, value) in enumerate(settings_options):
            y = options_y_start + i * option_height

            # Highlight selected option
            if i == selected_option:
                highlight_rect = pygame.Rect(200, y - 10, 400, 50)
                pygame.draw.rect(self.screen, (50, 50, 70), highlight_rect)
                pygame.draw.rect(self.screen, (100, 150, 255), highlight_rect, 3)

            # Draw label
            label_font = settings.create_font(28, bold=True)
            label_surface = label_font.render(label, True, (255, 255, 255))
            self.screen.blit(label_surface, (220, y))

            # Draw value
            if value:
                value_font = settings.create_font(24)
                value_surface = value_font.render(value, True, (150, 200, 255))
                self.screen.blit(value_surface, (450, y + 2))

        # Instructions
        instructions = ["UP/DOWN: Navigate  |  LEFT/RIGHT: Adjust  |  ENTER: Select  |  ESC: Back"]
        inst_y = settings.HEIGHT - 60
        inst_font = settings.create_font(18)
        for i, instruction in enumerate(instructions):
            inst_surface = inst_font.render(instruction, True, (150, 150, 150))
            inst_rect = inst_surface.get_rect(center=(settings.WIDTH // 2, inst_y + i * 25))
            self.screen.blit(inst_surface, inst_rect)

    def draw_high_scores(self, high_score_table):
        """
        Draw high score screen showing top 10 scores.

        Args:
            high_score_table: HighScoreTable instance with top scores
        """
        # Dark gradient background
        self.screen.fill((20, 20, 35))

        # Draw subtle grid pattern
        grid_color = (30, 30, 50)
        for x in range(0, settings.WIDTH, 40):
            pygame.draw.line(self.screen, grid_color, (x, 0), (x, settings.HEIGHT), 1)
        for y in range(0, settings.HEIGHT, 40):
            pygame.draw.line(self.screen, grid_color, (0, y), (settings.WIDTH, y), 1)

        # Title
        title_font = settings.create_font(56, bold=True)
        title_text = "HIGH SCORES"

        # Shadow
        shadow = title_font.render(title_text, True, (0, 0, 0))
        shadow_rect = shadow.get_rect(center=(settings.WIDTH // 2 + 3, 63))
        self.screen.blit(shadow, shadow_rect)

        # Main title
        title = title_font.render(title_text, True, (255, 215, 0))
        title_rect = title.get_rect(center=(settings.WIDTH // 2, 60))
        self.screen.blit(title, title_rect)

        # Header row
        header_y = 140
        header_font = settings.create_font(24, bold=True)
        rank_header = header_font.render("RANK", True, (150, 200, 255))
        name_header = header_font.render("NAME", True, (150, 200, 255))
        score_header = header_font.render("SCORE", True, (150, 200, 255))
        date_header = header_font.render("DATE", True, (150, 200, 255))

        self.screen.blit(rank_header, (100, header_y))
        self.screen.blit(name_header, (200, header_y))
        self.screen.blit(score_header, (400, header_y))
        self.screen.blit(date_header, (550, header_y))

        # Header underline
        pygame.draw.line(self.screen, (100, 150, 255), (80, header_y + 35), (720, header_y + 35), 2)

        # Get top scores
        top_scores = high_score_table.get_top_scores()

        if not top_scores:
            # No scores yet
            no_scores_font = settings.create_font(32)
            no_scores_text = no_scores_font.render("No high scores yet!", True, (150, 150, 150))
            no_scores_rect = no_scores_text.get_rect(center=(settings.WIDTH // 2, 300))
            self.screen.blit(no_scores_text, no_scores_rect)

            hint_font = settings.create_font(20)
            hint_text = hint_font.render("Play the game to set your first record!", True, (120, 120, 120))
            hint_rect = hint_text.get_rect(center=(settings.WIDTH // 2, 340))
            self.screen.blit(hint_text, hint_rect)
        else:
            # Draw scores
            entry_y_start = header_y + 50
            entry_height = 40
            rank_font = settings.create_font(28, bold=True)
            entry_font = settings.create_font(24)
            date_font = settings.create_font(18)

            for i, entry in enumerate(top_scores):
                y = entry_y_start + i * entry_height

                # Rank colors (gold, silver, bronze for top 3)
                if i == 0:
                    rank_color = (255, 215, 0)  # Gold
                    entry_bg_color = (50, 45, 20)
                elif i == 1:
                    rank_color = (192, 192, 192)  # Silver
                    entry_bg_color = (40, 40, 45)
                elif i == 2:
                    rank_color = (205, 127, 50)  # Bronze
                    entry_bg_color = (45, 35, 25)
                else:
                    rank_color = (150, 200, 255)
                    entry_bg_color = (30, 30, 40)

                # Background highlight for entry
                entry_rect = pygame.Rect(80, y - 5, 640, 35)
                pygame.draw.rect(self.screen, entry_bg_color, entry_rect)
                if i < 3:
                    pygame.draw.rect(self.screen, rank_color, entry_rect, 2)

                # Rank
                rank_text = rank_font.render(f"#{i + 1}", True, rank_color)
                self.screen.blit(rank_text, (100, y))

                # Name
                name_text = entry_font.render(entry.name, True, (255, 255, 255))
                self.screen.blit(name_text, (200, y + 2))

                # Score
                score_text = entry_font.render(str(entry.score), True, (150, 255, 150))
                self.screen.blit(score_text, (400, y + 2))

                # Date (formatted as MM/DD/YYYY)
                try:
                    from datetime import datetime

                    dt = datetime.fromisoformat(entry.timestamp)
                    date_str = dt.strftime("%m/%d/%Y")
                except (TypeError, ValueError):
                    date_str = "Unknown"
                date_text = date_font.render(date_str, True, (180, 180, 180))
                self.screen.blit(date_text, (550, y + 5))

        # Instructions at bottom
        inst_y = settings.HEIGHT - 50
        inst_font = settings.create_font(20)
        inst_text = inst_font.render("Press ESC or ENTER to return to menu", True, (150, 150, 150))
        inst_rect = inst_text.get_rect(center=(settings.WIDTH // 2, inst_y))
        self.screen.blit(inst_text, inst_rect)

    def draw_achievements_menu(self, achievements: dict, scroll_offset: int = 0):
        """
        Draw achievements menu showing all achievements and their progress.

        Args:
            achievements: Dictionary of Achievement objects
            scroll_offset: Vertical scroll offset for the list
        """
        # Background
        self.screen.fill((20, 20, 30))

        # Title
        title_font = settings.create_font(48, bold=True)
        title_surface = title_font.render("ACHIEVEMENTS", True, (255, 215, 0))
        title_rect = title_surface.get_rect(center=(settings.WIDTH // 2, 60))
        self.screen.blit(title_surface, title_rect)

        # Calculate stats
        total_achievements = len(achievements)
        unlocked_count = sum(1 for ach in achievements.values() if ach.unlocked)
        completion_pct = (unlocked_count / total_achievements * 100) if total_achievements > 0 else 0

        # Progress bar
        bar_width = 600
        bar_height = 30
        bar_x = settings.WIDTH // 2 - bar_width // 2
        bar_y = 110

        # Background bar
        pygame.draw.rect(self.screen, (50, 50, 60), pygame.Rect(bar_x, bar_y, bar_width, bar_height))
        # Progress fill
        fill_width = int(bar_width * (completion_pct / 100))
        pygame.draw.rect(self.screen, (255, 215, 0), pygame.Rect(bar_x, bar_y, fill_width, bar_height))
        # Border
        pygame.draw.rect(self.screen, (255, 255, 255), pygame.Rect(bar_x, bar_y, bar_width, bar_height), 2)

        # Progress text
        progress_font = settings.create_font(20)
        progress_text = f"{unlocked_count}/{total_achievements} ({completion_pct:.0f}%)"
        progress_surface = progress_font.render(progress_text, True, (255, 255, 255))
        progress_rect = progress_surface.get_rect(center=(settings.WIDTH // 2, bar_y + bar_height // 2))
        self.screen.blit(progress_surface, progress_rect)

        # Achievement list
        list_y_start = 170
        item_height = 80
        # Rarity colors
        rarity_colors = {
            "common": (200, 200, 200),
            "rare": (100, 150, 255),
            "epic": (200, 100, 255),
            "legendary": (255, 215, 0),
        }

        # Draw achievements
        achievement_list = list(achievements.values())
        for i, achievement in enumerate(achievement_list):
            item_y = list_y_start + i * item_height - scroll_offset

            # Skip if not visible
            if item_y < list_y_start - item_height or item_y > settings.HEIGHT - 100:
                continue

            # Background for achievement item
            item_bg_color = (40, 40, 50) if achievement.unlocked else (30, 30, 35)
            item_rect = pygame.Rect(80, item_y, settings.WIDTH - 160, item_height - 10)
            pygame.draw.rect(self.screen, item_bg_color, item_rect)

            # Border with rarity color
            border_color = rarity_colors.get(achievement.rarity, (200, 200, 200))
            if achievement.unlocked:
                pygame.draw.rect(self.screen, border_color, item_rect, 3)
            else:
                pygame.draw.rect(self.screen, (80, 80, 90), item_rect, 2)

            # Text badge
            icon_font = pygame.font.Font(None, 48)
            icon_color = (255, 255, 255) if achievement.unlocked else (100, 100, 100)
            icon_surface = icon_font.render(achievement.icon, True, icon_color)
            self.screen.blit(icon_surface, (95, item_y + 15))

            # Achievement name
            name_font = settings.create_font(22, bold=True)
            name_color = (255, 255, 255) if achievement.unlocked else (120, 120, 120)
            name_surface = name_font.render(achievement.name, True, name_color)
            self.screen.blit(name_surface, (160, item_y + 10))

            # Achievement description
            desc_font = settings.create_font(16)
            desc_color = (180, 180, 180) if achievement.unlocked else (100, 100, 100)
            desc_surface = desc_font.render(achievement.description, True, desc_color)
            self.screen.blit(desc_surface, (160, item_y + 38))

            # Rarity badge
            rarity_font = settings.create_font(14, bold=True)
            rarity_text = achievement.rarity.upper()
            rarity_surface = rarity_font.render(rarity_text, True, border_color)
            rarity_x = settings.WIDTH - 180
            self.screen.blit(rarity_surface, (rarity_x, item_y + 15))

            # Lock label if not unlocked
            if not achievement.unlocked:
                lock_font = pygame.font.Font(None, 24)
                lock_surface = lock_font.render("LOCK", True, (150, 150, 150))
                self.screen.blit(lock_surface, (rarity_x, item_y + 40))

        # Instructions
        instructions = ["UP/DOWN: Scroll  |  ESC: Back to Menu"]
        inst_y = settings.HEIGHT - 60
        inst_font = settings.create_font(18)
        for i, instruction in enumerate(instructions):
            inst_surface = inst_font.render(instruction, True, (150, 150, 150))
            inst_rect = inst_surface.get_rect(center=(settings.WIDTH // 2, inst_y + i * 25))
            self.screen.blit(inst_surface, inst_rect)
