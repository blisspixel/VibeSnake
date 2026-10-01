using Godot;
using VibeSnake.Persistence;

namespace VibeSnake.Game;

public partial class Main
{
    private static readonly IReadOnlySet<string> NavigationPrimaryActions = PrimaryActions(
        GameActions.MoveUp, GameActions.MoveDown, GameActions.MoveLeft, GameActions.MoveRight,
        GameActions.Confirm, GameActions.Back, GameActions.Replay);
    private static readonly IReadOnlySet<string> FormPrimaryActions = PrimaryActions(
        GameActions.MoveUp, GameActions.MoveDown, GameActions.MoveLeft, GameActions.MoveRight,
        GameActions.Confirm, GameActions.Back);
    private static readonly IReadOnlySet<string> DirectionPrimaryActions = PrimaryActions(
        GameActions.MoveUp, GameActions.MoveDown, GameActions.MoveLeft, GameActions.MoveRight,
        GameActions.Back);
    private static readonly IReadOnlySet<string> RunningPrimaryActions = PrimaryActions(
        GameActions.MoveUp, GameActions.MoveDown, GameActions.MoveLeft, GameActions.MoveRight,
        GameActions.Back, GameActions.Pause);
    private static readonly IReadOnlySet<string> PlaybackPrimaryActions = PrimaryActions(
        GameActions.MoveUp, GameActions.MoveDown, GameActions.MoveLeft, GameActions.MoveRight,
        GameActions.Confirm, GameActions.Back, GameActions.Replay, GameActions.Pause);
    private static readonly IReadOnlySet<string> TutorialPrimaryActions = PrimaryActions(
        GameActions.MoveUp, GameActions.MoveDown, GameActions.MoveLeft, GameActions.MoveRight,
        GameActions.Confirm, GameActions.Back, GameActions.Pause);
    private static readonly IReadOnlySet<string> VerticalPrimaryActions = PrimaryActions(
        GameActions.MoveUp, GameActions.MoveDown, GameActions.Confirm, GameActions.Back, GameActions.Replay);
    private static readonly IReadOnlySet<string> ContentPrimaryActions = PrimaryActions(
        GameActions.Confirm, GameActions.Back);
    private static readonly IReadOnlySet<string> BackPrimaryActions = PrimaryActions(GameActions.Back);

    private IReadOnlySet<string> ActivePrimaryActions => _screenState switch
    {
        ScreenState.Running => RunningPrimaryActions,
        ScreenState.Lore => DirectionPrimaryActions,
        ScreenState.Ended or ScreenState.Comparisons => VerticalPrimaryActions,
        ScreenState.ContentPacks => ContentPrimaryActions,
        ScreenState.Settings or ScreenState.Bindings => FormPrimaryActions,
        ScreenState.Spectator => PlaybackPrimaryActions,
        ScreenState.Onboarding when _onboardingSession is not null => TutorialPrimaryActions,
        ScreenState.Onboarding => FormPrimaryActions,
        ScreenState.Replays when _replayPlayback is not null => PlaybackPrimaryActions,
        ScreenState.Replays => VerticalPrimaryActions,
#if AGENT_ARENA_PREVIEW
        ScreenState.AgentWatch => BackPrimaryActions,
        ScreenState.AgentQualification => FormPrimaryActions,
        ScreenState.AgentPassports => VerticalPrimaryActions,
        ScreenState.AgentExhibitions when _agentExhibitionStory is not null => PlaybackPrimaryActions,
        ScreenState.AgentExhibitions => VerticalPrimaryActions,
#endif
        _ => NavigationPrimaryActions,
    };

    private static HashSet<string> PrimaryActions(params string[] actions)
    {
        var result = new HashSet<string>(actions, StringComparer.Ordinal)
        {
            GameActions.RestoreDefaults,
        };
        return result;
    }

    private (string Token, InputPromptFamily Family) ResolveMainMenuHintPrompt(int index)
    {
        var prompt = ResolveActionPrompt(
            _activePromptFamily == InputPromptFamily.Keyboard
                ? MainMenuShortcutActions[index]
                : "confirm");
        return prompt.Token == "unbound" ? ResolveActionPrompt("confirm") : prompt;
    }

    private void ExecutePrimaryInputSmokeTest()
    {
        if (_screenState != ScreenState.Menu || _run is not null)
        {
            throw new InvalidOperationException("Primary input qualification requires an idle menu.");
        }

        var originalKeyboard = _keyboardBindings;
        var originalController = _controllerBindings;
        var originalStore = _inputBindingsStore;
        var originalCursor = _mainMenuCursor;
        var originalPromptFamily = _activePromptFamily;
        var originalBindingsStatus = _bindingsStatusCaption;
        var originalReplayStatus = _replayStatusCaption;
        var originalSequence = _inputSequence;
        var originalMute = _shellSettings.MasterMuted;
        var originalContrast = _shellSettings.HighContrast;
        try
        {
            InputOwnershipQualification.AssertRouting(originalKeyboard, originalController);
            _inputBindingsStore = null;
            _keyboardBindings = InputBindingsDocument.CreateKeyboardDefaults();
            _controllerBindings = InputBindingsDocument.CreateControllerDefaults();
            var remap = new Dictionary<string, string>(_keyboardBindings.ActionToBinding, StringComparer.Ordinal)
            {
                ["confirm"] = "key:b",
                ["move_down"] = "key:r",
                ["move_up"] = "key:f7",
            };
            _keyboardBindings = _keyboardBindings with { ActionToBinding = remap };
            GameActions.ApplyKeyboardBindings(_keyboardBindings);
            GameActions.ApplyControllerBindings(_controllerBindings);
            _mainMenuCursor = (int)MainMenuItem.Start;
            _activePromptFamily = InputPromptFamily.Keyboard;
            AssertPrimaryInput(ResolveActionPrompt("browse_bindings").Token == "unbound",
                "The bindings prompt advertised a shortcut owned by remapped Confirm.");
            var shadowedRowBindings = new Dictionary<string, string>(remap, StringComparer.Ordinal)
            {
                ["move_down"] = "key:c",
            };
            GameActions.ApplyKeyboardBindings(_keyboardBindings with { ActionToBinding = shadowedRowBindings });
            AssertPrimaryInput(ResolveMainMenuHintPrompt((int)MainMenuItem.Customize).Token == "key:b",
                "A shadowed main menu row did not show configured Select input.");
            var utilityBindings = new Dictionary<string, string>(remap, StringComparer.Ordinal)
            {
                ["move_up"] = "key:f11",
                ["confirm"] = "key:j",
            };
            GameActions.ApplyKeyboardBindings(_keyboardBindings with { ActionToBinding = utilityBindings });
            AssertPrimaryInput(MainMenuUtilityHint() == string.Empty,
                "The menu footer advertised occupied keyboard utility shortcuts.");
            _activePromptFamily = InputPromptFamily.Xbox;
            AssertPrimaryInput(!MainMenuUtilityHint().Contains("F11", StringComparison.Ordinal)
                && MainMenuUtilityHint().Contains(ActionPromptLabel("cycle_radio"), StringComparison.Ordinal),
                "The controller footer lost its available radio control or advertised occupied F11.");
            var occupiedControllerUtility = new Dictionary<string, string>(
                _controllerBindings.ActionToBinding, StringComparer.Ordinal)
            {
                ["confirm"] = "button:right_stick",
            };
            GameActions.ApplyControllerBindings(_controllerBindings with { ActionToBinding = occupiedControllerUtility });
            AssertPrimaryInput(MainMenuUtilityHint() == string.Empty,
                "The menu footer advertised an occupied controller radio shortcut.");
            GameActions.ApplyControllerBindings(_controllerBindings);
            _activePromptFamily = InputPromptFamily.Keyboard;
            GameActions.ApplyKeyboardBindings(_keyboardBindings);
            DispatchSmokeKey(Key.R);
            AssertPrimaryInput(_screenState == ScreenState.Menu && _mainMenuCursor == 1,
                "The replay shortcut stole remapped menu Down.");
            DispatchSmokeKey(Key.F7, physical: false);
            AssertPrimaryInput(_screenState == ScreenState.Menu && _mainMenuCursor == 0
                && _shellSettings.MasterMuted == originalMute,
                "The mute shortcut stole remapped menu Up.");
            DispatchSmokeKey(Key.B);
            AssertPrimaryInput(_screenState == ScreenState.Running && _run is not null,
                "The bindings shortcut stole remapped Confirm.");
            DispatchSmokeJoyButton(JoyButton.Start);
            AssertPrimaryInput(_screenState == ScreenState.Running && _paused,
                "Controller Start did not pause the run.");
            ReturnToMenu();
            DispatchSmokeJoyButton(JoyButton.Start);
            AssertPrimaryInput(_screenState == ScreenState.Settings,
                "Controller Start did not open menu settings.");
            ReturnToMenu();

            var controllerRemap = new Dictionary<string, string>(_controllerBindings.ActionToBinding, StringComparer.Ordinal)
            {
                ["confirm"] = "button:right_shoulder",
                ["move_down"] = "button:west",
            };
            _controllerBindings = _controllerBindings with { ActionToBinding = controllerRemap };
            GameActions.ApplyControllerBindings(_controllerBindings);
            _mainMenuCursor = 0;
            DispatchSmokeJoyButton(JoyButton.X);
            AssertPrimaryInput(_screenState == ScreenState.Menu && _mainMenuCursor == 1,
                "The content shortcut stole remapped controller Down.");
            _mainMenuCursor = 0;
            DispatchSmokeJoyButton(JoyButton.RightShoulder);
            AssertPrimaryInput(_screenState == ScreenState.Running && _run is not null,
                "The bindings shortcut stole remapped controller Confirm.");
            ReturnToMenu();

            OpenBindingsBrowse();
            _bindingsDeviceTab = BindingsDeviceTab.Keyboard;
            var actions = ListRemappableActions();
            _bindingsCursor = Array.IndexOf(actions, "pause");
            _bindingsCapturePending = true;
            DispatchSmokeKey(Key.F9, physical: false);
            AssertPrimaryInput(_screenState == ScreenState.Bindings
                && !_bindingsCapturePending
                && _keyboardBindings.ActionToBinding["pause"] == "key:f9"
                && _shellSettings.HighContrast == originalContrast,
                "A fixed shortcut intercepted raw keyboard binding capture.");
        }
        finally
        {
            ReturnToMenu();
            _keyboardBindings = originalKeyboard;
            _controllerBindings = originalController;
            _inputBindingsStore = originalStore;
            GameActions.ApplyKeyboardBindings(originalKeyboard);
            GameActions.ApplyControllerBindings(originalController);
            _mainMenuCursor = originalCursor;
            _activePromptFamily = originalPromptFamily;
            _bindingsStatusCaption = originalBindingsStatus;
            _replayStatusCaption = originalReplayStatus;
            _inputSequence = originalSequence;
        }
    }

    private static void AssertPrimaryInput(bool condition, string failure)
    {
        if (!condition)
        {
            throw new InvalidOperationException(failure);
        }
    }
}
