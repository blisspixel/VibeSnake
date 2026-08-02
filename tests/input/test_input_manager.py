"""Tests for keyboard, mouse, and gamepad input routing."""

import pygame
import pytest

from vibesnake.core.enums import Direction
from vibesnake.input.input_manager import InputManager


class FakeJoystick:
    def __init__(self, hat=(0, 0), axes=(0.0, 0.0), buttons=(0, 0, 0, 0)):
        self.hat = hat
        self.axes = axes
        self.buttons = buttons

    def init(self):
        return None

    def get_name(self):
        return "Test Pad"

    def get_numhats(self):
        return 1

    def get_hat(self, _index):
        return self.hat

    def get_numaxes(self):
        return len(self.axes)

    def get_axis(self, index):
        return self.axes[index]

    def get_numbuttons(self):
        return len(self.buttons)

    def get_button(self, index):
        return self.buttons[index]


@pytest.mark.parametrize(
    ("key", "direction"),
    [
        (pygame.K_UP, Direction.UP),
        (pygame.K_DOWN, Direction.DOWN),
        (pygame.K_LEFT, Direction.LEFT),
        (pygame.K_RIGHT, Direction.RIGHT),
        (pygame.K_w, Direction.UP),
        (pygame.K_s, Direction.DOWN),
        (pygame.K_a, Direction.LEFT),
        (pygame.K_d, Direction.RIGHT),
    ],
)
def test_keyboard_direction_mapping(key, direction):
    manager = InputManager()
    event = pygame.event.Event(pygame.KEYDOWN, key=key)
    assert manager.get_direction(event) == direction
    assert manager.active_input_mode == "keyboard"
    assert manager.mouse_disabled_until_click


def test_ignored_keyboard_and_mouse_events(monkeypatch):
    manager = InputManager()
    assert manager.get_direction_from_keyboard(pygame.event.Event(pygame.MOUSEMOTION)) is None
    assert manager.get_direction_from_keyboard(pygame.event.Event(pygame.KEYDOWN, key=pygame.K_SPACE)) is None
    assert manager.get_direction_from_mouse(pygame.event.Event(pygame.MOUSEMOTION), (5, 5), 20) is None

    monkeypatch.setattr(pygame.mouse, "get_pos", lambda: (100, 100))
    click = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1)
    assert manager.get_direction_from_mouse(click, (5, 5), 20) is None
    assert manager.get_direction_from_mouse(pygame.event.Event(pygame.KEYUP, key=0), (0, 0), 20) is None


@pytest.mark.parametrize(
    ("position", "expected"),
    [
        ((200, 100), Direction.RIGHT),
        ((0, 100), Direction.LEFT),
        ((100, 200), Direction.DOWN),
        ((100, 0), Direction.UP),
    ],
)
def test_mouse_direction_mapping(monkeypatch, position, expected):
    manager = InputManager()
    monkeypatch.setattr(pygame.mouse, "get_pos", lambda: position)
    click = pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1)
    assert manager.get_direction(click, (5, 5), 20) == expected
    assert manager.active_input_mode == "mouse"


@pytest.mark.parametrize(
    ("joystick", "expected"),
    [
        (FakeJoystick(hat=(-1, 0)), Direction.LEFT),
        (FakeJoystick(hat=(1, 0)), Direction.RIGHT),
        (FakeJoystick(hat=(0, 1)), Direction.UP),
        (FakeJoystick(hat=(0, -1)), Direction.DOWN),
        (FakeJoystick(axes=(0.8, 0.1)), Direction.RIGHT),
        (FakeJoystick(axes=(-0.8, 0.1)), Direction.LEFT),
        (FakeJoystick(axes=(0.1, 0.8)), Direction.DOWN),
        (FakeJoystick(axes=(0.1, -0.8)), Direction.UP),
        (FakeJoystick(buttons=(0, 0, 0, 1)), Direction.UP),
        (FakeJoystick(buttons=(1, 0, 0, 0)), Direction.DOWN),
        (FakeJoystick(buttons=(0, 0, 1, 0)), Direction.LEFT),
        (FakeJoystick(buttons=(0, 1, 0, 0)), Direction.RIGHT),
    ],
)
def test_gamepad_direction_mapping(joystick, expected):
    manager = InputManager()
    manager.joystick = joystick
    assert manager.get_direction_from_gamepad() == expected
    assert manager.active_input_mode == "gamepad"


def test_no_gamepad_input_and_device_status():
    manager = InputManager()
    assert manager.get_direction_from_gamepad() is None
    assert manager.get_direction(pygame.event.Event(pygame.NOEVENT)) is None
    status = manager.get_input_devices_status()
    assert status == {"keyboard": True, "mouse": True, "gamepad": False, "gamepad_name": None}

    manager.joystick = FakeJoystick()
    assert manager.get_direction_from_gamepad() is None
    assert manager.get_input_devices_status()["gamepad_name"] == "Test Pad"


@pytest.mark.parametrize(
    ("action", "key"),
    [
        ("pause", pygame.K_p),
        ("select", pygame.K_RETURN),
        ("back", pygame.K_q),
        ("toggle_sound", pygame.K_m),
        ("help", pygame.K_h),
    ],
)
def test_keyboard_actions(action, key):
    manager = InputManager()
    assert manager.check_action_button(pygame.event.Event(pygame.KEYDOWN, key=key), action)
    assert not manager.check_action_button(pygame.event.Event(pygame.KEYUP, key=key), action)


@pytest.mark.parametrize(("action", "button"), [("pause", 7), ("select", 0), ("back", 1)])
def test_gamepad_actions(action, button):
    manager = InputManager()
    manager.joystick = FakeJoystick()
    event = pygame.event.Event(pygame.JOYBUTTONDOWN, button=button)
    assert manager.check_action_button(event, action)
