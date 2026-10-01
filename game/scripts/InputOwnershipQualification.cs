using Godot;
using VibeSnake.Persistence;

namespace VibeSnake.Game;

internal static class InputOwnershipQualification
{
    private static readonly IReadOnlySet<string> MenuActions = new HashSet<string>(StringComparer.Ordinal)
    {
        GameActions.MoveUp,
        GameActions.MoveDown,
        GameActions.MoveLeft,
        GameActions.MoveRight,
        GameActions.Confirm,
        GameActions.Back,
        GameActions.RestoreDefaults,
    };

    public static void AssertRouting(
        InputBindingsDocument restoreKeyboard,
        InputBindingsDocument restoreController)
    {
        try
        {
            var keyboardDefaults = InputBindingsDocument.CreateKeyboardDefaults();
            var keyboard = new Dictionary<string, string>(keyboardDefaults.ActionToBinding, StringComparer.Ordinal)
            {
                ["confirm"] = "key:b",
                ["move_down"] = "key:r",
                ["move_up"] = "key:f7",
            };
            GameActions.ApplyKeyboardBindings(keyboardDefaults with { ActionToBinding = keyboard });
            AssertOwned(KeyInput(Key.B), GameActions.Confirm, GameActions.BrowseBindings);
            AssertOwned(KeyInput(Key.R), GameActions.MoveDown, GameActions.Replay);
            AssertOwned(KeyInput(Key.F7), GameActions.MoveUp, GameActions.ToggleMasterMute);
            if (GameActions.IsFixedPromptAvailable("browse_bindings", controller: false, MenuActions)
                || !GameActions.IsFixedPromptAvailable("browse_content_packs", controller: false, MenuActions))
            {
                throw new InvalidOperationException("Fixed shortcut prompts do not reflect primary input ownership.");
            }

            using (var released = KeyInput(Key.B))
            {
                released.Pressed = false;
                AssertUnchanged(released);
            }

            using (var echo = KeyInput(Key.B))
            {
                echo.Echo = true;
                AssertUnchanged(echo);
            }

            using (var modified = KeyInput(Key.B))
            {
                modified.CtrlPressed = true;
                AssertUnchanged(modified);
            }

            using (var fallback = KeyInput(Key.W))
            {
                AssertUnchanged(fallback);
                if (!fallback.IsActionPressed(GameActions.MoveUp))
                {
                    throw new InvalidOperationException("Input ownership removed the keyboard movement fallback.");
                }
            }

            var controllerDefaults = InputBindingsDocument.CreateControllerDefaults();
            GameActions.ApplyControllerBindings(controllerDefaults);
            using (var start = ButtonInput(JoyButton.Start))
            {
                AssertUnchanged(start);
                if (!start.IsActionPressed(GameActions.BrowseSettings))
                {
                    throw new InvalidOperationException("Default controller Start no longer opens menu settings.");
                }

                using var pause = GameActions.NormalizePrimaryInput(
                    start,
                    new HashSet<string>(StringComparer.Ordinal) { GameActions.Pause });
                if (pause is null
                    || !pause.IsActionPressed(GameActions.Pause)
                    || pause.IsActionPressed(GameActions.BrowseSettings))
                {
                    throw new InvalidOperationException("Default controller Start does not belong to gameplay Pause.");
                }
            }

            var controller = new Dictionary<string, string>(controllerDefaults.ActionToBinding, StringComparer.Ordinal)
            {
                ["confirm"] = "button:right_shoulder",
                ["move_down"] = "button:west",
                ["move_up"] = "axis:left_y:-1",
            };
            GameActions.ApplyControllerBindings(controllerDefaults with { ActionToBinding = controller });
            AssertOwned(ButtonInput(JoyButton.RightShoulder), GameActions.Confirm, GameActions.BrowseBindings);
            AssertOwned(ButtonInput(JoyButton.X), GameActions.MoveDown, GameActions.BrowseContentPacks);
            using (var axis = new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -1.0f })
            {
                using var owned = GameActions.NormalizePrimaryInput(axis, MenuActions);
                if (owned is null
                    || !owned.IsActionPressed(GameActions.MoveUp)
                    || owned.IsActionPressed(GameActions.MoveDown)
                    || owned.Strength != axis.GetActionStrength(GameActions.MoveUp, exactMatch: true))
                {
                    throw new InvalidOperationException("Input ownership changed controller axis direction or strength.");
                }

                axis.AxisValue = 0.0f;
                AssertUnchanged(axis);
                axis.AxisValue = -0.05f;
                AssertUnchanged(axis);
            }

            using var synthetic = new InputEventAction { Action = GameActions.Confirm, Pressed = true };
            AssertUnchanged(synthetic);
        }
        finally
        {
            GameActions.ApplyKeyboardBindings(restoreKeyboard);
            GameActions.ApplyControllerBindings(restoreController);
        }
    }

    private static InputEventKey KeyInput(Key key) => new()
    {
        Keycode = key,
        PhysicalKeycode = key,
        Pressed = true,
    };

    private static InputEventJoypadButton ButtonInput(JoyButton button) => new()
    {
        ButtonIndex = button,
        Pressed = true,
    };

    private static void AssertOwned(InputEvent rawEvent, string expectedAction, string suppressedShortcut)
    {
        using (rawEvent)
        {
            if (!rawEvent.IsActionPressed(suppressedShortcut))
            {
                throw new InvalidOperationException("Input ownership qualification did not exercise the fixed shortcut conflict.");
            }

            using var normalized = GameActions.NormalizePrimaryInput(rawEvent, MenuActions);
            if (normalized is null
                || !normalized.IsActionPressed(expectedAction)
                || normalized.IsActionPressed(suppressedShortcut))
            {
                throw new InvalidOperationException("A fixed shortcut stole configured input action " + expectedAction + ".");
            }
        }
    }

    private static void AssertUnchanged(InputEvent rawEvent)
    {
        using var normalized = GameActions.NormalizePrimaryInput(rawEvent, MenuActions);
        if (normalized is not null)
        {
            throw new InvalidOperationException("Input ownership consumed an inactive, modified, fallback, or unpressed event.");
        }
    }
}
