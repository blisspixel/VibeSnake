using VibeSnake.Persistence;

namespace VibeSnake.Rules.Tests;

public sealed class InputBindingsDocumentTests
{
    [Fact]
    public void Defaults_include_required_escape_hatch_actions()
    {
        var keyboard = InputBindingsDocument.CreateKeyboardDefaults();
        foreach (var action in InputBindingsDocument.RequiredActions)
        {
            Assert.True(keyboard.ActionToBinding.ContainsKey(action));
        }

        Assert.Equal("key:enter", keyboard.ActionToBinding["confirm"]);
        Assert.Equal("key:escape", keyboard.ActionToBinding["back"]);
    }

    [Fact]
    public void Rejects_duplicate_bindings()
    {
        var result = InputBindingsDocument.Read(
            """
            {
              "schemaVersion": 1,
              "deviceClass": "keyboard",
              "actions": {
                "move_up": "key:up",
                "move_down": "key:down",
                "move_left": "key:left",
                "move_right": "key:right",
                "confirm": "key:enter",
                "back": "key:enter",
                "pause": "key:p"
              }
            }
            """);

        Assert.Equal(InputBindingsLoadCode.Conflict, result.Code);
    }

    [Fact]
    public void Round_trips_through_atomic_store()
    {
        var root = Path.Combine(Path.GetTempPath(), "vibesnake-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new InputBindingsStore(root);
            var document = InputBindingsDocument.CreateKeyboardDefaults();
            store.Save(document);
            var loaded = store.LoadOrDefault(InputBindingsDocument.KeyboardDeviceClass);
            Assert.True(loaded.IsSuccess);
            Assert.Equal(document.SerializeCanonical(), loaded.Document!.SerializeCanonical());

            var controllerDefaults = store.LoadOrDefault(InputBindingsDocument.ControllerDeviceClass);
            Assert.True(controllerDefaults.IsSuccess);
            Assert.Equal(
                InputBindingsDocument.ControllerDeviceClass,
                controllerDefaults.Document!.DeviceClass);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Rejects_missing_required_actions_and_empty_payloads()
    {
        Assert.Equal(InputBindingsLoadCode.Empty, InputBindingsDocument.Read("").Code);
        Assert.Equal(InputBindingsLoadCode.InvalidJson, InputBindingsDocument.Read("{").Code);

        var missing = InputBindingsDocument.Read(
            """
            {
              "schemaVersion": 1,
              "deviceClass": "keyboard",
              "actions": {
                "confirm": "key:enter",
                "back": "key:escape"
              }
            }
            """);
        Assert.Equal(InputBindingsLoadCode.MissingRequiredAction, missing.Code);
    }

    [Fact]
    public void Rejects_unsupported_schema_and_relative_store_roots()
    {
        var future = InputBindingsDocument.Read(
            """
            {
              "schemaVersion": 9,
              "deviceClass": "keyboard",
              "actions": {}
            }
            """);
        Assert.Equal(InputBindingsLoadCode.UnsupportedSchema, future.Code);
        Assert.Throws<ArgumentException>(() => new InputBindingsStore("not/absolute"));
    }

    [Fact]
    public void Serializes_actions_in_stable_order()
    {
        var document = InputBindingsDocument.CreateKeyboardDefaults();
        var first = document.SerializeCanonical();
        var second = document.SerializeCanonical();
        Assert.Equal(first, second);
        Assert.Contains("\"confirm\":", first, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRemapAction_moves_pause_to_a_free_key()
    {
        var original = InputBindingsDocument.CreateKeyboardDefaults();
        var remapped = original.TryRemapAction("pause", "key:space");

        Assert.True(remapped.IsSuccess);
        Assert.Equal("key:space", remapped.Document!.ActionToBinding["pause"]);
        Assert.Equal("key:p", original.ActionToBinding["pause"]);
        Assert.Equal("key:enter", remapped.Document.ActionToBinding["confirm"]);
    }

    [Fact]
    public void TryRemapAction_rejects_binding_owned_by_another_action()
    {
        var original = InputBindingsDocument.CreateKeyboardDefaults();
        var conflict = original.TryRemapAction("pause", "key:enter");

        Assert.Equal(InputBindingsLoadCode.Conflict, conflict.Code);
        Assert.Null(conflict.Document);
        Assert.Contains("confirm", conflict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRemapAction_is_idempotent_for_the_same_binding()
    {
        var original = InputBindingsDocument.CreateKeyboardDefaults();
        var same = original.TryRemapAction("confirm", "key:enter");

        Assert.True(same.IsSuccess);
        Assert.Same(original, same.Document);
    }

    [Fact]
    public void TryRemapAction_rejects_unknown_actions_and_invalid_tokens()
    {
        var original = InputBindingsDocument.CreateKeyboardDefaults();

        Assert.Equal(
            InputBindingsLoadCode.InvalidField,
            original.TryRemapAction("jump", "key:j").Code);
        Assert.Equal(
            InputBindingsLoadCode.InvalidField,
            original.TryRemapAction("pause", "not-a-token").Code);
        Assert.Equal(
            InputBindingsLoadCode.InvalidField,
            original.TryRemapAction(" ", "key:p").Code);
    }

    [Fact]
    public void TryRemapAction_normalizes_key_identifier_case()
    {
        var original = InputBindingsDocument.CreateKeyboardDefaults();
        var remapped = original.TryRemapAction("pause", "key:Space");

        Assert.True(remapped.IsSuccess);
        Assert.Equal("key:space", remapped.Document!.ActionToBinding["pause"]);
    }

    [Fact]
    public void TryRemapAction_preserves_fractional_axis_thresholds()
    {
        var original = InputBindingsDocument.CreateControllerDefaults();
        var remapped = original.TryRemapAction("move_up", "axis:left_y:-0.5");

        Assert.True(remapped.IsSuccess);
        Assert.Equal("axis:left_y:-0.5", remapped.Document!.ActionToBinding["move_up"]);
    }

    [Fact]
    public void TrySwapActions_exchanges_bindings_without_conflict()
    {
        var original = InputBindingsDocument.CreateKeyboardDefaults();
        var swapped = original.TrySwapActions("pause", "confirm");

        Assert.True(swapped.IsSuccess);
        Assert.Equal("key:enter", swapped.Document!.ActionToBinding["pause"]);
        Assert.Equal("key:p", swapped.Document.ActionToBinding["confirm"]);
        Assert.Equal("key:p", original.ActionToBinding["pause"]);
    }

    [Fact]
    public void TrySwapActions_rejects_unknown_or_blank_actions()
    {
        var original = InputBindingsDocument.CreateKeyboardDefaults();
        Assert.Equal(
            InputBindingsLoadCode.InvalidField,
            original.TrySwapActions("pause", "jump").Code);
        Assert.Equal(
            InputBindingsLoadCode.InvalidField,
            original.TrySwapActions(" ", "confirm").Code);
    }
}
