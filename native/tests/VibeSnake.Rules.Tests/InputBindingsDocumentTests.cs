using VibeSnake.Persistence;
using System.Text;

namespace VibeSnake.Rules.Tests;

public sealed class InputBindingsDocumentTests
{
    [Theory]
    [InlineData("keyboard")]
    [InlineData("controller")]
    public void Initial_future_schema_load_then_remap_preserves_original_device_file(string deviceClass)
    {
        var root = Directory.CreateTempSubdirectory("vibesnake-input-future-");
        try
        {
            var store = new InputBindingsStore(root.FullName);
            Directory.CreateDirectory(store.BindingsDirectory);
            var path = store.PathForDeviceClass(deviceClass);
            var bytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":99,\"deviceClass\":\"" + deviceClass + "\",\"futureSetting\":\"keep\"}\n");
            File.WriteAllBytes(path, bytes);
            Assert.Equal(InputBindingsLoadCode.UnsupportedSchema, store.LoadOrDefault(deviceClass).Code);
            var defaults = deviceClass == InputBindingsDocument.KeyboardDeviceClass
                ? InputBindingsDocument.CreateKeyboardDefaults()
                : InputBindingsDocument.CreateControllerDefaults();
            var remapped = defaults.TrySwapActions("pause", "confirm");
            Assert.True(remapped.IsSuccess);

            Assert.Throws<InvalidOperationException>(() => store.Save(remapped.Document!));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(store.BindingsDirectory, "*.tmp-*"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stale_loaded_bindings_preserve_future_file_published_before_or_during_staging(bool duringStaging)
    {
        var root = Directory.CreateTempSubdirectory("vibesnake-input-stale-");
        try
        {
            var physical = new InputBindingsStore(root.FullName);
            physical.Save(InputBindingsDocument.CreateKeyboardDefaults());
            var loaded = physical.LoadOrDefault("keyboard").Document!;
            var path = physical.PathForDeviceClass("keyboard");
            var bytes = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"schema_version\":99,\"futureSetting\":\"keep\"}\n");
            var operations = new ObservingWriteOperations(afterWrite: _ => File.WriteAllBytes(path, bytes));
            var store = duringStaging ? new InputBindingsStore(root.FullName, operations) : physical;
            if (!duringStaging)
            {
                File.WriteAllBytes(path, bytes);
            }

            Assert.Throws<InvalidOperationException>(() => store.Save(loaded.TryRemapAction("pause", "key:space").Document!));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal(0, operations.MoveCount);
            Assert.Equal(duringStaging ? 1 : 0, operations.StagedPaths.Count);
            Assert.Empty(Directory.GetFiles(store.BindingsDirectory, "*.tmp-*"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("\"schemaVersion\":99,\"schemaVersion\":1")]
    [InlineData("\"schema_version\":99,\"schema_version\":1")]
    [InlineData("\"schema_version\":1,\"schemaVersion\":99")]
    [InlineData("\"schemaVersion\":1,\"schemaVersion\":1")]
    [InlineData("\"schemaVersion\":\"unknown\"")]
    [InlineData("\"schemaVersion\":2147483648")]
    [InlineData("\"schemaVersion\":0")]
    public void Save_preserves_ambiguous_or_unsupported_schema_declarations(string declarations)
    {
        var root = Directory.CreateTempSubdirectory("vibesnake-input-schema-");
        try
        {
            var store = new InputBindingsStore(root.FullName);
            Directory.CreateDirectory(store.BindingsDirectory);
            var path = store.PathForDeviceClass("keyboard");
            var bytes = Encoding.UTF8.GetBytes("{" + declarations + ",\"futureSetting\":\"keep\"}\n");
            File.WriteAllBytes(path, bytes);

            Assert.Throws<InvalidOperationException>(() => store.Save(InputBindingsDocument.CreateKeyboardDefaults()));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Empty(Directory.GetFiles(store.BindingsDirectory, "*.tmp-*"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Interrupted_save_preserves_committed_bindings_and_other_writer_stage(bool partialWrite)
    {
        var root = Directory.CreateTempSubdirectory("vibesnake-input-fault-");
        try
        {
            var physical = new InputBindingsStore(root.FullName);
            var original = InputBindingsDocument.CreateKeyboardDefaults();
            physical.Save(original);
            var path = physical.PathForDeviceClass("keyboard");
            var bytes = File.ReadAllBytes(path);
            var otherStage = path + ".tmp-other-writer";
            File.WriteAllText(otherStage, "unrelated work");
            var operations = new ObservingWriteOperations(failWrite: partialWrite, failMove: !partialWrite);
            var store = new InputBindingsStore(root.FullName, operations);

            Assert.Throws<IOException>(() => store.Save(original.TryRemapAction("pause", "key:space").Document!));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal("unrelated work", File.ReadAllText(otherStage));
            Assert.Single(operations.StagedPaths);
            Assert.False(File.Exists(operations.StagedPaths[0]));
            Assert.Equal(new[] { otherStage }, Directory.GetFiles(store.BindingsDirectory, "*.tmp-*"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Interleaved_binding_saves_publish_only_their_own_complete_payloads()
    {
        var root = Directory.CreateTempSubdirectory("vibesnake-input-writers-");
        try
        {
            var first = InputBindingsDocument.CreateKeyboardDefaults();
            var second = first.TryRemapAction("pause", "key:space").Document!;
            var secondOperations = new ObservingWriteOperations();
            var secondStore = new InputBindingsStore(root.FullName, secondOperations);
            var firstOperations = new ObservingWriteOperations(afterWrite: stage =>
            {
                secondStore.Save(second);
                Assert.Equal(second.SerializeCanonical(), File.ReadAllText(secondStore.PathForDeviceClass("keyboard")));
                Assert.Equal(first.SerializeCanonical(), File.ReadAllText(stage));
            });
            var firstStore = new InputBindingsStore(root.FullName, firstOperations);

            firstStore.Save(first);

            Assert.Equal(first.SerializeCanonical(), File.ReadAllText(firstStore.PathForDeviceClass("keyboard")));
            Assert.NotEqual(Assert.Single(firstOperations.StagedPaths), Assert.Single(secondOperations.StagedPaths));
            Assert.Empty(Directory.GetFiles(firstStore.BindingsDirectory, "*.tmp-*"));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"schemaVersion\":1,\"schema_version\":1}")]
    public void Explicit_binding_recovery_accepts_corruption_or_matching_supported_aliases(string existing)
    {
        var root = Directory.CreateTempSubdirectory("vibesnake-input-recovery-");
        try
        {
            var store = new InputBindingsStore(root.FullName);
            Directory.CreateDirectory(store.BindingsDirectory);
            var path = store.PathForDeviceClass("keyboard");
            File.WriteAllText(path, existing);
            var defaults = InputBindingsDocument.CreateKeyboardDefaults();

            store.Save(defaults);

            Assert.Equal(defaults.SerializeCanonical(), File.ReadAllText(path));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private sealed class ObservingWriteOperations(
        Action<string>? afterWrite = null,
        bool failWrite = false,
        bool failMove = false) : IPreferencesWriteOperations
    {
        public List<string> StagedPaths { get; } = [];

        public int MoveCount { get; private set; }

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public void WriteAllText(string path, string contents, Encoding encoding)
        {
            StagedPaths.Add(path);
            File.WriteAllText(path, failWrite ? contents[..(contents.Length / 2)] : contents, encoding);
            if (failWrite)
            {
                throw new IOException("Injected partial controls write.");
            }
            afterWrite?.Invoke(path);
        }

        public void Move(string sourcePath, string destinationPath, bool overwrite)
        {
            MoveCount++;
            if (failMove)
            {
                throw new IOException("Injected interrupted controls replacement.");
            }
            File.Move(sourcePath, destinationPath, overwrite);
        }

        public void Delete(string path) => File.Delete(path);
    }

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
        Assert.Equal("confirm", result.ConflictingAction);
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
    public void Store_rejects_invalid_publicly_constructed_documents()
    {
        var root = Path.Combine(Path.GetTempPath(), "vibesnake-input-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var actions = new Dictionary<string, string>(
                InputBindingsDocument.CreateKeyboardDefaults().ActionToBinding,
                StringComparer.Ordinal)
            {
                ["pause"] = "button:west",
            };
            var invalid = new InputBindingsDocument(
                InputBindingsDocument.CurrentSchemaVersion,
                InputBindingsDocument.KeyboardDeviceClass,
                actions);

            var store = new InputBindingsStore(root);
            Assert.Throws<InvalidOperationException>(() => store.Save(invalid));
            Assert.False(File.Exists(store.PathForDeviceClass(invalid.DeviceClass)));
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
        Assert.Equal("confirm", conflict.ConflictingAction);
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
    public void TryRemapAction_rejects_overlapping_axis_thresholds()
    {
        var controller = InputBindingsDocument.CreateControllerDefaults();
        var first = controller.TryRemapAction("move_up", "axis:left_y:-0.5");
        Assert.True(first.IsSuccess);

        var conflict = first.Document!.TryRemapAction("pause", "axis:left_y:-1");
        Assert.Equal(InputBindingsLoadCode.Conflict, conflict.Code);
        Assert.Equal("move_up", conflict.ConflictingAction);
        Assert.Contains("move_up", conflict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRemapAction_rejects_cross_device_and_unsupported_tokens()
    {
        var keyboard = InputBindingsDocument.CreateKeyboardDefaults();
        var controller = InputBindingsDocument.CreateControllerDefaults();

        Assert.Equal(
            InputBindingsLoadCode.InvalidField,
            keyboard.TryRemapAction("pause", "button:west").Code);
        Assert.Equal(
            InputBindingsLoadCode.InvalidField,
            controller.TryRemapAction("pause", "key:space").Code);
        Assert.Equal(
            InputBindingsLoadCode.InvalidField,
            controller.TryRemapAction("pause", "button:unknown").Code);
        Assert.Equal(
            InputBindingsLoadCode.Conflict,
            controller.TryRemapAction("pause", "button:a").Code);
    }

    [Fact]
    public void Read_rejects_unsupported_device_classes_and_binding_vocabularies()
    {
        var unsupportedDevice = InputBindingsDocument.Read(
            InputBindingsDocument.CreateKeyboardDefaults()
                .SerializeCanonical()
                .Replace("\"keyboard\"", "\"mouse\"", StringComparison.Ordinal));
        Assert.Equal(InputBindingsLoadCode.InvalidField, unsupportedDevice.Code);

        var wrongBindingKind = InputBindingsDocument.Read(
            InputBindingsDocument.CreateKeyboardDefaults()
                .SerializeCanonical()
                .Replace("\"key:p\"", "\"button:west\"", StringComparison.Ordinal));
        Assert.Equal(InputBindingsLoadCode.InvalidField, wrongBindingKind.Code);
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

    [Fact]
    public void Read_rejects_malformed_root_schema_device_and_actions_fields()
    {
        Assert.Equal(
            InputBindingsLoadCode.InvalidField,
            InputBindingsDocument.Read("[]").Code);

        foreach (var json in new[]
        {
            """{ "deviceClass": "keyboard", "actions": {} }""",
            """{ "schemaVersion": "1", "deviceClass": "keyboard", "actions": {} }""",
            """{ "schemaVersion": 1.5, "deviceClass": "keyboard", "actions": {} }""",
            """{ "schemaVersion": 1, "actions": {} }""",
            """{ "schemaVersion": 1, "deviceClass": 1, "actions": {} }""",
            """{ "schemaVersion": 1, "deviceClass": " ", "actions": {} }""",
            """{ "schemaVersion": 1, "deviceClass": "keyboard" }""",
            """{ "schemaVersion": 1, "deviceClass": "keyboard", "actions": [] }""",
            """{ "schemaVersion": 1, "deviceClass": "keyboard", "actions": { "confirm": 1 } }""",
            """{ "schemaVersion": 1, "deviceClass": "keyboard", "actions": { "confirm": " " } }""",
        })
        {
            Assert.Equal(InputBindingsLoadCode.InvalidField, InputBindingsDocument.Read(json).Code);
        }
    }

    [Fact]
    public void Read_rejects_duplicate_action_names_before_binding_conflicts()
    {
        var json = InputBindingsDocument.CreateKeyboardDefaults()
            .SerializeCanonical()
            .Replace(
                "\"confirm\":\"key:enter\"",
                "\"confirm\":\"key:enter\",\"confirm\":\"key:space\"",
                StringComparison.Ordinal);

        Assert.Equal(InputBindingsLoadCode.InvalidField, InputBindingsDocument.Read(json).Code);
    }

    [Fact]
    public void Swap_handles_identical_actions_and_duplicate_public_binding_state()
    {
        var defaults = InputBindingsDocument.CreateKeyboardDefaults();
        var identical = defaults.TrySwapActions("confirm", "confirm");
        Assert.True(identical.IsSuccess);
        Assert.Same(defaults, identical.Document);

        var duplicateActions = new Dictionary<string, string>(
            defaults.ActionToBinding,
            StringComparer.Ordinal)
        {
            ["back"] = "key:enter",
        };
        var duplicateDocument = defaults with { ActionToBinding = duplicateActions };
        Assert.Equal(
            InputBindingsLoadCode.Conflict,
            duplicateDocument.TrySwapActions("confirm", "back").Code);
    }

    [Fact]
    public void Store_sanitizes_device_file_names_and_uses_keyboard_fallback_defaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "vibesnake-input-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new InputBindingsStore(root);
            Assert.EndsWith(
                "mouse__.input_bindings.json",
                store.PathForDeviceClass("mouse?!"),
                StringComparison.Ordinal);
            Assert.Equal(
                InputBindingsDocument.KeyboardDeviceClass,
                store.LoadOrDefault("mouse").Document!.DeviceClass);

            var unsupportedDocument = InputBindingsDocument.CreateKeyboardDefaults() with
            {
                DeviceClass = "mouse",
            };
            Assert.Equal(
                InputBindingsLoadCode.InvalidField,
                unsupportedDocument.TryRemapAction("pause", "key:space").Code);
            Assert.False(new InputBindingsLoadResult(InputBindingsLoadCode.Success, "missing").IsSuccess);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
