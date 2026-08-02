using Godot;

namespace VibeSnake.Game;

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
    }

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
}
