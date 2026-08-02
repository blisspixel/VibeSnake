"""
Universal input manager supporting keyboard, mouse, and gamepad.

Design philosophy: Multiple input methods should work simultaneously.
Players can seamlessly switch between keyboard arrows, WASD, mouse clicks,
or gamepad without changing settings.
"""

import pygame
from typing import Optional, Tuple
from vibesnake.core.enums import Direction
from vibesnake.utils.logger import get_logger

logger = get_logger(__name__)


class InputManager:
    """
    Manages input from multiple sources: keyboard, mouse, gamepad.

    Supports:
    - Keyboard: Arrow keys, WASD
    - Mouse: Click ahead of snake to set direction
    - Gamepad: D-pad, left analog stick, face buttons

    All input methods can be used simultaneously - the game responds
    to whichever input is most recent.
    """

    def __init__(self):
        """Initialize input manager and detect available input devices."""
        self.last_direction: Optional[Direction] = None
        self.mouse_pos: Optional[Tuple[int, int]] = None

        # Track which input method was last used to prevent mouse from taking over
        self.active_input_mode: str = "keyboard"  # 'keyboard', 'mouse', or 'gamepad'
        self.mouse_disabled_until_click: bool = True  # Require explicit click to enable mouse

        # Gamepad detection
        pygame.joystick.init()
        self.joystick: Optional[pygame.joystick.Joystick] = None

        if pygame.joystick.get_count() > 0:
            self.joystick = pygame.joystick.Joystick(0)
            self.joystick.init()
            logger.info("Gamepad detected: %s", self.joystick.get_name())
        else:
            logger.info("No gamepad detected - keyboard and mouse only")

        # Deadzone for analog sticks (prevent drift)
        self.analog_deadzone = 0.3

        logger.info("InputManager initialized - keyboard, mouse, gamepad support active")

    def get_direction_from_keyboard(self, event: pygame.event.Event) -> Optional[Direction]:
        """
        Get direction from keyboard input.

        Supports both arrow keys and WASD.

        Args:
            event: Pygame keyboard event

        Returns:
            Direction if valid key pressed, None otherwise
        """
        if event.type != pygame.KEYDOWN:
            return None

        direction = None

        # Arrow keys
        if event.key == pygame.K_UP:
            direction = Direction.UP
        elif event.key == pygame.K_DOWN:
            direction = Direction.DOWN
        elif event.key == pygame.K_LEFT:
            direction = Direction.LEFT
        elif event.key == pygame.K_RIGHT:
            direction = Direction.RIGHT

        # WASD keys
        elif event.key == pygame.K_w:
            direction = Direction.UP
        elif event.key == pygame.K_s:
            direction = Direction.DOWN
        elif event.key == pygame.K_a:
            direction = Direction.LEFT
        elif event.key == pygame.K_d:
            direction = Direction.RIGHT

        # If keyboard input detected, switch to keyboard mode
        if direction:
            self.active_input_mode = "keyboard"
            self.mouse_disabled_until_click = True

        return direction

    def get_direction_from_mouse(
        self, event: pygame.event.Event, snake_head_pos: Tuple[int, int], cell_size: int
    ) -> Optional[Direction]:
        """
        Get direction from mouse click/position.

        Clicking ahead of the snake sets direction toward that point.
        Uses relative position to snake head to determine direction.

        Mouse input requires an explicit click to activate to prevent
        accidental input when multitasking or using keyboard/gamepad.

        Args:
            event: Pygame mouse event
            snake_head_pos: Current snake head position (grid coords)
            cell_size: Size of each grid cell in pixels

        Returns:
            Direction toward mouse position, None if invalid
        """
        # Enable mouse mode only on explicit click
        if event.type == pygame.MOUSEBUTTONDOWN:
            self.mouse_disabled_until_click = False
            self.active_input_mode = "mouse"

        # If not in mouse mode, ignore mouse movement
        if self.mouse_disabled_until_click or self.active_input_mode != "mouse":
            return None

        if event.type not in (pygame.MOUSEBUTTONDOWN, pygame.MOUSEMOTION):
            return None

        # Get mouse position in grid coordinates
        mouse_x, mouse_y = pygame.mouse.get_pos()
        grid_x = mouse_x // cell_size
        grid_y = mouse_y // cell_size

        head_x, head_y = snake_head_pos

        # Calculate direction based on relative position
        # Prioritize the axis with greater distance
        dx = grid_x - head_x
        dy = grid_y - head_y

        if abs(dx) == 0 and abs(dy) == 0:
            return None  # Mouse on snake head

        # Choose primary axis (larger delta)
        if abs(dx) > abs(dy):
            return Direction.RIGHT if dx > 0 else Direction.LEFT
        else:
            return Direction.DOWN if dy > 0 else Direction.UP

    def get_direction_from_gamepad(self) -> Optional[Direction]:
        """
        Get direction from gamepad input.

        Supports:
        - D-pad (digital input)
        - Left analog stick (analog input with deadzone)
        - Face buttons (A/B/X/Y or Cross/Circle/Square/Triangle)

        Returns:
            Direction from gamepad, None if no input
        """
        if not self.joystick:
            return None

        direction = None

        # D-pad (hat) input
        if self.joystick.get_numhats() > 0:
            hat = self.joystick.get_hat(0)
            if hat[0] == -1:  # Left
                direction = Direction.LEFT
            elif hat[0] == 1:  # Right
                direction = Direction.RIGHT
            elif hat[1] == 1:  # Up
                direction = Direction.UP
            elif hat[1] == -1:  # Down
                direction = Direction.DOWN

        # Left analog stick
        if not direction and self.joystick.get_numaxes() >= 2:
            x_axis = self.joystick.get_axis(0)  # Horizontal
            y_axis = self.joystick.get_axis(1)  # Vertical

            # Apply deadzone
            if abs(x_axis) > self.analog_deadzone or abs(y_axis) > self.analog_deadzone:
                # Prioritize axis with greater magnitude
                if abs(x_axis) > abs(y_axis):
                    direction = Direction.RIGHT if x_axis > 0 else Direction.LEFT
                else:
                    direction = Direction.DOWN if y_axis > 0 else Direction.UP

        # Face buttons (alternative control scheme)
        # Button 0 = A/Cross, 1 = B/Circle, 2 = X/Square, 3 = Y/Triangle
        if not direction and self.joystick.get_numbuttons() >= 4:
            if self.joystick.get_button(3):  # Y/Triangle = Up
                direction = Direction.UP
            elif self.joystick.get_button(0):  # A/Cross = Down
                direction = Direction.DOWN
            elif self.joystick.get_button(2):  # X/Square = Left
                direction = Direction.LEFT
            elif self.joystick.get_button(1):  # B/Circle = Right
                direction = Direction.RIGHT

        # If gamepad input detected, switch to gamepad mode
        if direction:
            self.active_input_mode = "gamepad"
            self.mouse_disabled_until_click = True

        return direction

    def get_direction(
        self, event: pygame.event.Event, snake_head_pos: Optional[Tuple[int, int]] = None, cell_size: int = 20
    ) -> Optional[Direction]:
        """
        Get direction from any input source.

        Checks all input methods and returns the first valid direction found.
        Priority: keyboard > mouse > gamepad (fastest to slowest response time).

        Args:
            event: Pygame event
            snake_head_pos: Snake head position for mouse input (optional)
            cell_size: Grid cell size for mouse input (optional)

        Returns:
            Direction from any input source, None if no input
        """
        # Try keyboard first (most responsive)
        direction = self.get_direction_from_keyboard(event)
        if direction:
            self.last_direction = direction
            return direction

        # Try mouse (if position provided)
        if snake_head_pos:
            direction = self.get_direction_from_mouse(event, snake_head_pos, cell_size)
            if direction:
                self.last_direction = direction
                return direction

        # Try gamepad
        direction = self.get_direction_from_gamepad()
        if direction:
            self.last_direction = direction
            return direction

        return None

    def check_action_button(self, event: pygame.event.Event, action: str) -> bool:
        """
        Check if an action button was pressed (pause, menu select, etc.).

        Supports keyboard and gamepad buttons for common actions.

        Args:
            event: Pygame event
            action: Action name ('pause', 'select', 'back', 'toggle_sound')

        Returns:
            True if action button pressed
        """
        if event.type == pygame.KEYDOWN:
            if action == "pause" and event.key == pygame.K_p:
                return True
            elif action == "select" and event.key == pygame.K_RETURN:
                return True
            elif action == "back" and event.key == pygame.K_q:
                return True
            elif action == "toggle_sound" and event.key == pygame.K_m:
                return True
            elif action == "help" and event.key == pygame.K_h:
                return True

        # Gamepad button support
        if event.type == pygame.JOYBUTTONDOWN and self.joystick:
            # Standard mapping (may vary by controller)
            if action == "pause" and event.button == 7:  # Start button
                return True
            elif action == "select" and event.button == 0:  # A/Cross
                return True
            elif action == "back" and event.button == 1:  # B/Circle
                return True

        return False

    def get_input_devices_status(self) -> dict:
        """
        Get status of all input devices.

        Returns:
            Dictionary with device availability and info
        """
        return {
            "keyboard": True,  # Always available in pygame
            "mouse": True,  # Always available in pygame
            "gamepad": self.joystick is not None,
            "gamepad_name": self.joystick.get_name() if self.joystick else None,
        }
