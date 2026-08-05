using Godot;
using VibeSnake.Persistence;

namespace VibeSnake.Game;

/// <summary>
/// Logical Godot InputMap actions for the shell. Defaults cover keyboard dual
/// bindings and common controller paths. Schema-1 documents may replace the
/// primary keyboard or controller events without dropping the opposite device.
/// </summary>
internal static class GameActions
{
    private const int AnyJoypadDevice = -1;

    public const string MoveUp = "vibe_move_up";
    public const string MoveRight = "vibe_move_right";
    public const string MoveDown = "vibe_move_down";
    public const string MoveLeft = "vibe_move_left";
    public const string Confirm = "vibe_confirm";
    public const string Back = "vibe_back";
    public const string Pause = "vibe_pause";
    public const string Replay = "vibe_replay";
    public const string Quit = "vibe_quit";
    public const string RestoreDefaults = "vibe_restore_defaults";
    public const string ToggleMasterMute = "vibe_toggle_master_mute";
    public const string ToggleHighContrast = "vibe_toggle_high_contrast";
    public const string ToggleReducedMotion = "vibe_toggle_reduced_motion";
    public const string ToggleFullscreen = "vibe_toggle_fullscreen";
    public const string VolumeUp = "vibe_volume_up";
    public const string VolumeDown = "vibe_volume_down";
    public const string TextScaleUp = "vibe_text_scale_up";
    public const string TextScaleDown = "vibe_text_scale_down";
    public const string ToggleFlashFree = "vibe_toggle_flash_free";
    public const string OpenDiagnostics = "vibe_open_diagnostics";
    public const string BrowseAchievements = "vibe_browse_achievements";
    public const string BrowseBindings = "vibe_browse_bindings";

    private static readonly string[] RequiredActions =
    [
        MoveUp,
        MoveRight,
        MoveDown,
        MoveLeft,
        Confirm,
        Back,
        Pause,
        Replay,
        Quit,
    ];

    private static readonly Dictionary<string, string> LogicalToRuntime =
        new(StringComparer.Ordinal)
        {
            ["move_up"] = MoveUp,
            ["move_down"] = MoveDown,
            ["move_left"] = MoveLeft,
            ["move_right"] = MoveRight,
            ["confirm"] = Confirm,
            ["back"] = Back,
            ["pause"] = Pause,
            ["replay"] = Replay,
            ["quit"] = Quit,
            ["restore_defaults"] = RestoreDefaults,
            ["toggle_master_mute"] = ToggleMasterMute,
            ["toggle_high_contrast"] = ToggleHighContrast,
            ["toggle_reduced_motion"] = ToggleReducedMotion,
            ["toggle_fullscreen"] = ToggleFullscreen,
            ["volume_up"] = VolumeUp,
            ["volume_down"] = VolumeDown,
            ["text_scale_up"] = TextScaleUp,
            ["text_scale_down"] = TextScaleDown,
            ["toggle_flash_free"] = ToggleFlashFree,
            ["open_diagnostics"] = OpenDiagnostics,
            ["browse_achievements"] = BrowseAchievements,
            ["browse_bindings"] = BrowseBindings,
        };

    private static readonly HashSet<string> RuntimeActions = [];

    public static void EnsureDefaults()
    {
        AddAction(
            MoveUp,
            0.5f,
            KeyEvent(Key.Up),
            KeyEvent(Key.W),
            JoyButtonEvent(JoyButton.DpadUp),
            JoyAxisEvent(JoyAxis.LeftY, -1.0f));
        AddAction(
            MoveRight,
            0.5f,
            KeyEvent(Key.Right),
            KeyEvent(Key.D),
            JoyButtonEvent(JoyButton.DpadRight),
            JoyAxisEvent(JoyAxis.LeftX, 1.0f));
        AddAction(
            MoveDown,
            0.5f,
            KeyEvent(Key.Down),
            KeyEvent(Key.S),
            JoyButtonEvent(JoyButton.DpadDown),
            JoyAxisEvent(JoyAxis.LeftY, 1.0f));
        AddAction(
            MoveLeft,
            0.5f,
            KeyEvent(Key.Left),
            KeyEvent(Key.A),
            JoyButtonEvent(JoyButton.DpadLeft),
            JoyAxisEvent(JoyAxis.LeftX, -1.0f));
        AddAction(
            Confirm,
            0.5f,
            KeyEvent(Key.Enter, physical: false),
            KeyEvent(Key.Space, physical: false),
            JoyButtonEvent(JoyButton.A));
        AddAction(
            Back,
            0.5f,
            KeyEvent(Key.Escape, physical: false),
            JoyButtonEvent(JoyButton.B));
        AddAction(
            Pause,
            0.5f,
            KeyEvent(Key.P),
            JoyButtonEvent(JoyButton.Start));
        AddAction(
            Replay,
            0.5f,
            KeyEvent(Key.R),
            JoyButtonEvent(JoyButton.Y));
        AddAction(
            Quit,
            0.5f,
            new InputEventKey
            {
                Keycode = Key.Q,
                CommandOrControlAutoremap = true,
            });
        AddAction(
            RestoreDefaults,
            0.5f,
            KeyEvent(Key.F8, physical: false),
            JoyButtonEvent(JoyButton.Back));
        AddAction(
            ToggleMasterMute,
            0.5f,
            KeyEvent(Key.F7, physical: false));
        AddAction(
            ToggleHighContrast,
            0.5f,
            KeyEvent(Key.F9, physical: false));
        AddAction(
            ToggleReducedMotion,
            0.5f,
            KeyEvent(Key.F10, physical: false));
        AddAction(
            ToggleFullscreen,
            0.5f,
            KeyEvent(Key.F11, physical: false));
        AddAction(
            VolumeUp,
            0.5f,
            KeyEvent(Key.Equal, physical: false),
            KeyEvent(Key.KpAdd, physical: false));
        AddAction(
            VolumeDown,
            0.5f,
            KeyEvent(Key.Minus, physical: false),
            KeyEvent(Key.KpSubtract, physical: false));
        AddAction(
            TextScaleUp,
            0.5f,
            KeyEvent(Key.F6, physical: false));
        AddAction(
            TextScaleDown,
            0.5f,
            KeyEvent(Key.F5, physical: false));
        AddAction(
            ToggleFlashFree,
            0.5f,
            KeyEvent(Key.F4, physical: false));
        AddAction(
            OpenDiagnostics,
            0.5f,
            KeyEvent(Key.F12, physical: false));
        AddAction(
            BrowseAchievements,
            0.5f,
            KeyEvent(Key.U),
            JoyButtonEvent(JoyButton.LeftShoulder));
        AddAction(
            BrowseBindings,
            0.5f,
            KeyEvent(Key.B),
            JoyButtonEvent(JoyButton.RightShoulder));
    }

    /// <summary>
    /// Replaces keyboard events for documented actions while retaining joypad
    /// events so a keyboard remap never disables controller input.
    /// </summary>
    public static void ApplyKeyboardBindings(InputBindingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!string.Equals(
                document.DeviceClass,
                InputBindingsDocument.KeyboardDeviceClass,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Keyboard binding application requires deviceClass keyboard.",
                nameof(document));
        }

        EnsureActionSlotsExist();
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in document.ActionToBinding)
        {
            if (!LogicalToRuntime.TryGetValue(pair.Key, out var action))
            {
                continue;
            }

            if (!InputBindingToken.TryParse(pair.Value, out var parsed)
                || parsed.Kind != InputBindingKind.Key)
            {
                throw new InvalidOperationException(
                    "Keyboard binding token is invalid for action " + pair.Key + ": " + pair.Value);
            }

            usedKeys.Add(parsed.Identifier);
            ReplaceKeyboardEvents(action, pair.Value);
        }

        ApplySecondaryKeyboardFallbacks(usedKeys);
    }

    /// <summary>
    /// Replaces joypad events for documented actions while retaining keyboard
    /// events so a controller remap never disables keyboard input.
    /// </summary>
    public static void ApplyControllerBindings(InputBindingsDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!string.Equals(
                document.DeviceClass,
                InputBindingsDocument.ControllerDeviceClass,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Controller binding application requires deviceClass controller.",
                nameof(document));
        }

        EnsureActionSlotsExist();
        foreach (var pair in document.ActionToBinding)
        {
            if (!LogicalToRuntime.TryGetValue(pair.Key, out var action))
            {
                continue;
            }

            if (!InputBindingToken.TryParse(pair.Value, out var parsed)
                || (parsed.Kind is not (InputBindingKind.Button or InputBindingKind.Axis)))
            {
                throw new InvalidOperationException(
                    "Controller binding token is invalid for action " + pair.Key + ": " + pair.Value);
            }

            ReplaceJoypadEvents(action, pair.Value);
        }

        ApplySecondaryControllerAxes();
    }

    public static bool TryMapLogicalAction(string logicalAction, out string runtimeAction) =>
        LogicalToRuntime.TryGetValue(logicalAction, out runtimeAction!);

    public static void AssertDefaultsRegistered()
    {
        foreach (var action in RequiredActions)
        {
            if (!InputMap.HasAction(action))
            {
                throw new InvalidOperationException(
                    $"Required logical action is not registered: {action}");
            }

            if (InputMap.ActionGetEvents(action).Count == 0)
            {
                throw new InvalidOperationException(
                    $"Required logical action has no default binding: {action}");
            }
        }
    }

    public static bool ActionHasKeyboardToken(string runtimeAction, string token)
    {
        if (!InputMap.HasAction(runtimeAction)
            || !InputBindingToken.TryParse(token, out var parsed)
            || parsed.Kind != InputBindingKind.Key
            || !TryMapKey(parsed.Identifier, out var key, out var physical))
        {
            return false;
        }

        foreach (var inputEvent in InputMap.ActionGetEvents(runtimeAction))
        {
            if (inputEvent is not InputEventKey keyEvent)
            {
                continue;
            }

            if (physical)
            {
                if (keyEvent.PhysicalKeycode == key)
                {
                    return true;
                }
            }
            else if (keyEvent.Keycode == key)
            {
                return true;
            }
        }

        return false;
    }

    public static void ReleaseRuntimeDefaults()
    {
        foreach (var action in RuntimeActions)
        {
            if (InputMap.HasAction(action))
            {
                InputMap.EraseAction(action);
            }
        }

        RuntimeActions.Clear();
    }

    private static void EnsureActionSlotsExist()
    {
        EnsureDefaults();
        if (!InputMap.HasAction(RestoreDefaults))
        {
            AddAction(
                RestoreDefaults,
                0.5f,
                KeyEvent(Key.F8, physical: false),
                JoyButtonEvent(JoyButton.Back));
        }
    }

    private static void ReplaceKeyboardEvents(string action, string token)
    {
        ClearEvents(action, keepJoypad: true, keepKeyboard: false);
        var inputEvent = CreateEventFromToken(token);
        try
        {
            InputMap.ActionAddEvent(action, inputEvent);
        }
        finally
        {
            inputEvent.Dispose();
        }
    }

    private static void ReplaceJoypadEvents(string action, string token)
    {
        ClearEvents(action, keepJoypad: false, keepKeyboard: true);
        var inputEvent = CreateEventFromToken(token);
        try
        {
            InputMap.ActionAddEvent(action, inputEvent);
        }
        finally
        {
            inputEvent.Dispose();
        }
    }

    private static void ClearEvents(string action, bool keepJoypad, bool keepKeyboard)
    {
        if (!InputMap.HasAction(action))
        {
            return;
        }

        var existing = InputMap.ActionGetEvents(action);
        foreach (var inputEvent in existing)
        {
            var isKey = inputEvent is InputEventKey;
            var isJoy = inputEvent is InputEventJoypadButton or InputEventJoypadMotion;
            if ((isKey && !keepKeyboard) || (isJoy && !keepJoypad))
            {
                InputMap.ActionEraseEvent(action, inputEvent);
            }
        }
    }

    private static void ApplySecondaryKeyboardFallbacks(HashSet<string> usedKeys)
    {
        // Convenience dual-binds that match EnsureDefaults, skipped on conflict.
        TryAddSecondaryKey(MoveUp, Key.W, "w", usedKeys);
        TryAddSecondaryKey(MoveDown, Key.S, "s", usedKeys);
        TryAddSecondaryKey(MoveLeft, Key.A, "a", usedKeys);
        TryAddSecondaryKey(MoveRight, Key.D, "d", usedKeys);
        TryAddSecondaryKey(Confirm, Key.Space, "space", usedKeys, physical: false);
        TryAddSecondaryKey(Replay, Key.R, "r", usedKeys);
    }

    private static void TryAddSecondaryKey(
        string action,
        Key key,
        string identifier,
        HashSet<string> usedKeys,
        bool physical = true)
    {
        if (usedKeys.Contains(identifier) || ActionHasKeyboardToken(action, "key:" + identifier))
        {
            return;
        }

        var inputEvent = KeyEvent(key, physical);
        try
        {
            InputMap.ActionAddEvent(action, inputEvent);
        }
        finally
        {
            inputEvent.Dispose();
        }
    }

    private static void ApplySecondaryControllerAxes()
    {
        // Stick axes remain available unless a remap already owns LeftX/LeftY events.
        EnsureAxis(MoveUp, JoyAxis.LeftY, -1.0f);
        EnsureAxis(MoveDown, JoyAxis.LeftY, 1.0f);
        EnsureAxis(MoveLeft, JoyAxis.LeftX, -1.0f);
        EnsureAxis(MoveRight, JoyAxis.LeftX, 1.0f);
    }

    private static void EnsureAxis(string action, JoyAxis axis, float value)
    {
        if (!InputMap.HasAction(action))
        {
            return;
        }

        foreach (var inputEvent in InputMap.ActionGetEvents(action))
        {
            if (inputEvent is InputEventJoypadMotion motion
                && motion.Axis == axis
                && Math.Abs(motion.AxisValue - value) < 0.01f)
            {
                return;
            }
        }

        var axisEvent = JoyAxisEvent(axis, value);
        try
        {
            InputMap.ActionAddEvent(action, axisEvent);
        }
        finally
        {
            axisEvent.Dispose();
        }
    }

    private static InputEvent CreateEventFromToken(string token)
    {
        if (!InputBindingToken.TryParse(token, out var parsed))
        {
            throw new InvalidOperationException("Unsupported binding token: " + token);
        }

        return parsed.Kind switch
        {
            InputBindingKind.Key when TryMapKey(parsed.Identifier, out var key, out var physical)
                => KeyEvent(key, physical),
            InputBindingKind.Button when TryMapButton(parsed.Identifier, out var button)
                => JoyButtonEvent(button),
            InputBindingKind.Axis when TryMapAxis(parsed.Identifier, out var axis)
                => JoyAxisEvent(axis, parsed.AxisValue),
            _ => throw new InvalidOperationException("Unsupported binding token: " + token),
        };
    }

    private static bool TryMapKey(string identifier, out Key key, out bool physical)
    {
        physical = true;
        switch (identifier)
        {
            case "up":
                key = Key.Up;
                return true;
            case "down":
                key = Key.Down;
                return true;
            case "left":
                key = Key.Left;
                return true;
            case "right":
                key = Key.Right;
                return true;
            case "enter":
            case "return":
                key = Key.Enter;
                physical = false;
                return true;
            case "escape":
            case "esc":
                key = Key.Escape;
                physical = false;
                return true;
            case "space":
                key = Key.Space;
                physical = false;
                return true;
            case "p":
                key = Key.P;
                return true;
            case "w":
                key = Key.W;
                return true;
            case "a":
                key = Key.A;
                return true;
            case "s":
                key = Key.S;
                return true;
            case "d":
                key = Key.D;
                return true;
            case "r":
                key = Key.R;
                return true;
            case "q":
                key = Key.Q;
                return true;
            case "f8":
                key = Key.F8;
                physical = false;
                return true;
            case "f1":
                key = Key.F1;
                physical = false;
                return true;
            case "f2":
                key = Key.F2;
                physical = false;
                return true;
            case "f3":
                key = Key.F3;
                physical = false;
                return true;
            case "f4":
                key = Key.F4;
                physical = false;
                return true;
            case "f5":
                key = Key.F5;
                physical = false;
                return true;
            case "f6":
                key = Key.F6;
                physical = false;
                return true;
            case "f7":
                key = Key.F7;
                physical = false;
                return true;
            case "f9":
                key = Key.F9;
                physical = false;
                return true;
            case "f10":
                key = Key.F10;
                physical = false;
                return true;
            case "f11":
                key = Key.F11;
                physical = false;
                return true;
            case "f12":
                key = Key.F12;
                physical = false;
                return true;
            case "tab":
                key = Key.Tab;
                physical = false;
                return true;
            default:
                // Letter and digit identifiers match Key enum names when single-char lowercase.
                if (identifier.Length == 1)
                {
                    var ch = identifier[0];
                    if (ch is >= 'a' and <= 'z')
                    {
                        key = (Key)((int)Key.A + (ch - 'a'));
                        return true;
                    }

                    if (ch is >= '0' and <= '9')
                    {
                        key = (Key)((int)Key.Key0 + (ch - '0'));
                        physical = false;
                        return true;
                    }
                }

                key = Key.None;
                return false;
        }
    }

    private static bool TryMapButton(string identifier, out JoyButton button)
    {
        switch (identifier)
        {
            case "dpad_up":
                button = JoyButton.DpadUp;
                return true;
            case "dpad_down":
                button = JoyButton.DpadDown;
                return true;
            case "dpad_left":
                button = JoyButton.DpadLeft;
                return true;
            case "dpad_right":
                button = JoyButton.DpadRight;
                return true;
            case "south":
            case "a":
                button = JoyButton.A;
                return true;
            case "east":
            case "b":
                button = JoyButton.B;
                return true;
            case "west":
            case "x":
                button = JoyButton.X;
                return true;
            case "north":
            case "y":
                button = JoyButton.Y;
                return true;
            case "start":
                button = JoyButton.Start;
                return true;
            case "select":
            case "back":
                button = JoyButton.Back;
                return true;
            default:
                button = JoyButton.Invalid;
                return false;
        }
    }

    private static bool TryMapAxis(string identifier, out JoyAxis axis)
    {
        switch (identifier)
        {
            case "left_x":
                axis = JoyAxis.LeftX;
                return true;
            case "left_y":
                axis = JoyAxis.LeftY;
                return true;
            case "right_x":
                axis = JoyAxis.RightX;
                return true;
            case "right_y":
                axis = JoyAxis.RightY;
                return true;
            default:
                axis = JoyAxis.Invalid;
                return false;
        }
    }

    private static void AddAction(
        string action,
        float deadzone,
        params InputEvent[] events)
    {
        try
        {
            if (InputMap.HasAction(action))
            {
                return;
            }

            InputMap.AddAction(action, deadzone);
            RuntimeActions.Add(action);
            foreach (var inputEvent in events)
            {
                InputMap.ActionAddEvent(action, inputEvent);
            }
        }
        finally
        {
            foreach (var inputEvent in events)
            {
                inputEvent.Dispose();
            }
        }
    }

    private static InputEventKey KeyEvent(Key key, bool physical = true) =>
        physical
            ? new InputEventKey { PhysicalKeycode = key }
            : new InputEventKey { Keycode = key };

    private static InputEventJoypadButton JoyButtonEvent(JoyButton button) =>
        new()
        {
            Device = AnyJoypadDevice,
            ButtonIndex = button,
        };

    private static InputEventJoypadMotion JoyAxisEvent(
        JoyAxis axis,
        float value) =>
        new()
        {
            Device = AnyJoypadDevice,
            Axis = axis,
            AxisValue = value,
        };

    /// <summary>
    /// Formats a pressed keyboard event as a schema-1 <c>key:</c> token for remapping.
    /// Returns false for modifiers-only and unmapped keys.
    /// </summary>
    public static bool TryFormatKeyboardToken(InputEventKey keyEvent, out string token)
    {
        token = string.Empty;
        ArgumentNullException.ThrowIfNull(keyEvent);
        var key = keyEvent.PhysicalKeycode != Key.None
            ? keyEvent.PhysicalKeycode
            : keyEvent.Keycode;
        if (key is Key.None or Key.Shift or Key.Ctrl or Key.Alt or Key.Meta)
        {
            return false;
        }

        var identifier = key switch
        {
            Key.Up => "up",
            Key.Down => "down",
            Key.Left => "left",
            Key.Right => "right",
            Key.Enter or Key.KpEnter => "enter",
            Key.Escape => "escape",
            Key.Space => "space",
            Key.Tab => "tab",
            Key.Backspace => "backspace",
            Key.Delete => "delete",
            Key.Home => "home",
            Key.End => "end",
            Key.Insert => "insert",
            Key.A => "a",
            Key.B => "b",
            Key.C => "c",
            Key.D => "d",
            Key.E => "e",
            Key.F => "f",
            Key.G => "g",
            Key.H => "h",
            Key.I => "i",
            Key.J => "j",
            Key.K => "k",
            Key.L => "l",
            Key.M => "m",
            Key.N => "n",
            Key.O => "o",
            Key.P => "p",
            Key.Q => "q",
            Key.R => "r",
            Key.S => "s",
            Key.T => "t",
            Key.U => "u",
            Key.V => "v",
            Key.W => "w",
            Key.X => "x",
            Key.Y => "y",
            Key.Z => "z",
            Key.Key0 or Key.Kp0 => "0",
            Key.Key1 or Key.Kp1 => "1",
            Key.Key2 or Key.Kp2 => "2",
            Key.Key3 or Key.Kp3 => "3",
            Key.Key4 or Key.Kp4 => "4",
            Key.Key5 or Key.Kp5 => "5",
            Key.Key6 or Key.Kp6 => "6",
            Key.Key7 or Key.Kp7 => "7",
            Key.Key8 or Key.Kp8 => "8",
            Key.Key9 or Key.Kp9 => "9",
            Key.F1 => "f1",
            Key.F2 => "f2",
            Key.F3 => "f3",
            Key.F4 => "f4",
            Key.F5 => "f5",
            Key.F6 => "f6",
            Key.F7 => "f7",
            Key.F8 => "f8",
            Key.F9 => "f9",
            Key.F10 => "f10",
            Key.F11 => "f11",
            Key.F12 => "f12",
            Key.Minus or Key.KpSubtract => "minus",
            Key.Equal or Key.KpAdd => "equal",
            Key.Comma => "comma",
            Key.Period => "period",
            Key.Slash => "slash",
            Key.Semicolon => "semicolon",
            Key.Apostrophe => "apostrophe",
            _ => null,
        };
        if (identifier is null)
        {
            return false;
        }

        token = "key:" + identifier;
        return InputBindingToken.TryParse(token, out _);
    }
}
