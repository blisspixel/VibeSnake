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
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
