using Godot;
using VibeSnake.Persistence;
using VibeSnake.Rules;
using RulesDirection = VibeSnake.Rules.Direction;

namespace VibeSnake.Game;

public partial class Main : Node2D
{
    private const float CellSize = 20.0f;
    private const float HudHeight = 60.0f;
    private const ulong SmokeSeed = 20260801UL;
    private const int FeedbackVisibilityTicks = 30;
    private const int MaximumReplayStatusCharacters = 240;
    private const ulong ReplayShutdownDrainMilliseconds = 5_000UL;

    private ScreenState _screenState = ScreenState.Menu;
    private SnakeRun? _run;
    private ulong _nextSeed = 1UL;
    private bool _paused;
    private bool _pausedByFocusLoss;
    private double _rulesStepAccumulatorMilliseconds;
    private string? _feedbackCaption;
    private int _feedbackTicksRemaining;
    private ProceduralCuePlayer? _cuePlayer;
    private ReplayStore? _replayStore;
    private RunReplayRecorder? _replayRecorder;
    private string? _replayStatusCaption;
    private Task<string>? _replayOperation;
    private ReplayOperationKind? _replayOperationKind;
    private Func<string>? _queuedReplaySave;
    private bool _quitAfterReplaySave;
    private ulong? _replayQuitDeadlineMilliseconds;
    private bool _skipReplayShutdownDrain;
    private Window? _window;
    private VirtualViewport _virtualViewport = new(
        VirtualViewport.LogicalWidth,
        VirtualViewport.LogicalHeight);
    private ShellSettings _shellSettings = ShellSettings.CreateDefaults();
    private PreferencesStore? _preferencesStore;
    private LocalDiagnostics? _diagnostics;
    private StructuredLocalLog? _structuredLog;
    private InputBindingsStore? _inputBindingsStore;
    private InputBindingsDocument _keyboardBindings =
        InputBindingsDocument.CreateKeyboardDefaults();
    private readonly ControllerConnectionTracker _controllerConnections = new();
    private string? _controllerCaption;

    private enum ScreenState
    {
        Menu,
        Running,
        Ended,
    }

    private enum ReplayOperationKind
    {
        Inspection,
        Save,
    }

    public override void _Ready()
    {
        GameActions.EnsureDefaults();
        AudioBuses.EnsureRegistered();
        GetTree().AutoAcceptQuit = false;
        _cuePlayer = new ProceduralCuePlayer();
        AddChild(_cuePlayer);
        var userArguments = OS.GetCmdlineUserArgs();
        var smokeTest = userArguments.Contains("--smoke-test", StringComparer.Ordinal);
        var smokeUserDataRoot = GetArgumentValue(
            userArguments,
            "--smoke-user-data-root=");
        if (smokeTest && smokeUserDataRoot is null)
        {
            throw new ArgumentException(
                "The smoke test requires an explicit --smoke-user-data-root path.");
        }

        var userDataRoot = smokeUserDataRoot
            ?? ProjectSettings.GlobalizePath("user://");
        _replayStore = new ReplayStore(userDataRoot);
        _preferencesStore = new PreferencesStore(userDataRoot);
        _diagnostics = new LocalDiagnostics(userDataRoot);
        _structuredLog = new StructuredLocalLog(userDataRoot);
        _inputBindingsStore = new InputBindingsStore(userDataRoot);
        LoadShellSettings();
        AudioBuses.ApplyShellSettings(_shellSettings);
        LoadInputBindings();
        SeedControllerConnections();
        Input.JoyConnectionChanged += OnJoyConnectionChanged;
        _structuredLog.Information(
            "shell",
            smokeTest ? "Headless smoke session started." : "Interactive shell session started.",
            eventCode: smokeTest ? "smoke_session_start" : "session_start");
        _window = GetWindow();
        _window.FilesDropped += OnFilesDropped;
        _window.SizeChanged += OnWindowSizeChanged;
        RefreshVirtualViewport();
        if (smokeTest)
        {
            ExecuteSmokeTest();
            return;
        }

        ApplyWindowModeFromSettings();
        QueueRedraw();
    }

    private void SeedControllerConnections()
    {
        foreach (var deviceId in Input.GetConnectedJoypads())
        {
            _controllerConnections.NoteConnected((int)deviceId, Input.GetJoyName((int)deviceId));
        }
    }

    private void OnJoyConnectionChanged(long device, bool connected)
    {
        var deviceId = (int)device;
        ControllerConnectionEvent? connectionEvent = connected
            ? _controllerConnections.NoteConnected(deviceId, Input.GetJoyName(deviceId))
            : _controllerConnections.NoteDisconnected(deviceId);
        if (connectionEvent is null)
        {
            return;
        }

        _controllerCaption = connectionEvent.Value.Caption;
        _structuredLog?.Information(
            "input",
            connectionEvent.Value.Caption,
            eventCode: connectionEvent.Value.Kind == ControllerConnectionKind.Connected
                ? "controller_connected"
                : "controller_disconnected");

        // Pause a live run when the last controller disconnects so input is not lost.
        if (
            connectionEvent.Value.Kind == ControllerConnectionKind.Disconnected
            && _controllerConnections.ConnectedCount == 0
            && _screenState == ScreenState.Running
            && !_paused)
        {
            _paused = true;
            _pausedByFocusLoss = false;
            PlayCue(AudioCue.Pause);
            _structuredLog?.Warning(
                "input",
                "Paused run after last controller disconnected.",
                eventCode: "controller_disconnect_pause");
        }

        QueueRedraw();
    }

    private void ApplyWindowModeFromSettings()
    {
        if (_window is null)
        {
            return;
        }

        // Headless and packaged smoke stay windowed; interactive sessions honor prefs.
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        if (_shellSettings.Fullscreen)
        {
            _window.Mode = Window.ModeEnum.Fullscreen;
        }
        else if (_window.Mode == Window.ModeEnum.Fullscreen)
        {
            _window.Mode = Window.ModeEnum.Windowed;
        }
    }

    private void OnWindowSizeChanged()
    {
        RefreshVirtualViewport();
        QueueRedraw();
    }

    private void RefreshVirtualViewport()
    {
        var size = _window?.Size ?? new Vector2I(
            (int)VirtualViewport.LogicalWidth,
            (int)VirtualViewport.LogicalHeight);
        var width = Math.Max(size.X, 1);
        var height = Math.Max(size.Y, 1);
        _virtualViewport.Resize(width, height);
    }

    /// <summary>
    /// Maps a window-space pointer into the logical 1280x720 canvas.
    /// </summary>
    private Vector2 MapPointerToLogical(Vector2 windowPoint) =>
        _virtualViewport.WindowToLogical(windowPoint);

    private void LoadShellSettings()
    {
        if (_preferencesStore is null)
        {
            _shellSettings = ShellSettings.CreateDefaults();
            return;
        }

        var loaded = _preferencesStore.Load();
        if (loaded.IsSuccess && loaded.Document is not null)
        {
            _shellSettings = ShellSettings.FromDocument(loaded.Document);
            return;
        }

        _shellSettings = ShellSettings.CreateDefaults();
        if (loaded.Code is PreferencesLoadCode.UnsupportedSchema or PreferencesLoadCode.InvalidJson)
        {
            WriteLocalCrashReport(
                "SettingsLoad",
                new InvalidOperationException(loaded.Message),
                eventCode: "preferences_load_failed");
        }
    }

    /// <summary>
    /// Writes a sanitized offline crash report and a structured log Error line.
    /// Returns null when diagnostics are unavailable (early init edge cases).
    /// </summary>
    private string? WriteLocalCrashReport(
        string screenState,
        Exception exception,
        string? eventCode = "crash_report",
        string? configHash = null,
        string? configHashAlgorithm = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screenState);
        ArgumentNullException.ThrowIfNull(exception);
        _structuredLog?.Error(
            "diagnostics",
            exception.Message,
            eventCode: eventCode);
        if (_diagnostics is null)
        {
            return null;
        }

        return _diagnostics.WriteCrashReport(
            appVersion: ProductIdentity.AppVersion,
            platform: OS.GetName(),
            rulesetId: SnakeRun.RulesetId,
            rulesVersion: SnakeRun.RulesVersion,
            screenState: screenState,
            exception: exception,
            configHash: configHash,
            configHashAlgorithm: configHashAlgorithm);
    }

    private void SaveShellSettings()
    {
        if (_preferencesStore is null)
        {
            return;
        }

        _shellSettings.Clamp();
        _preferencesStore.Save(_shellSettings.ToDocument());
        AudioBuses.ApplyShellSettings(_shellSettings);
    }

    private void ApplyMasterMuteToggle()
    {
        _shellSettings.ToggleMasterMute();
        SaveShellSettings();
        QueueRedraw();
    }

    private void ApplyHighContrastToggle()
    {
        _shellSettings.ToggleHighContrast();
        SaveShellSettings();
        QueueRedraw();
    }

    private void ApplyReducedMotionToggle()
    {
        _shellSettings.ToggleReducedMotion();
        SaveShellSettings();
        QueueRedraw();
    }

    private void ApplyFullscreenToggle()
    {
        _shellSettings.ToggleFullscreen();
        SaveShellSettings();
        ApplyWindowModeFromSettings();
        QueueRedraw();
    }

    private void ApplyMasterVolumeStep(float delta)
    {
        _shellSettings.AdjustMasterVolume(delta);
        SaveShellSettings();
        QueueRedraw();
    }

    private void ApplyTextScaleStep(float delta)
    {
        _shellSettings.AdjustTextScale(delta);
        SaveShellSettings();
        QueueRedraw();
    }

    private void ApplyFlashFreeToggle()
    {
        _shellSettings.ToggleFlashFree();
        SaveShellSettings();
        QueueRedraw();
    }

    private Color CanvasBackgroundColor() =>
        _shellSettings.HighContrast
            ? new Color(0.0f, 0.0f, 0.0f)
            : new Color(0.02f, 0.035f, 0.03f);

    private Color BoardBackgroundColor() =>
        _shellSettings.HighContrast
            ? new Color(0.0f, 0.0f, 0.0f)
            : new Color(0.055f, 0.12f, 0.085f);

    private Color PrimaryTextColor() =>
        _shellSettings.HighContrast
            ? Colors.White
            : new Color(0.45f, 1.0f, 0.68f);

    private Color SecondaryTextColor() =>
        _shellSettings.HighContrast
            ? new Color(0.92f, 0.92f, 0.92f)
            : new Color(0.58f, 0.7f, 0.64f);

    private int ScaledFontSize(int baseSize)
    {
        var scale = Math.Clamp(_shellSettings.TextScale, 0.8f, 1.5f);
        return Math.Max(10, (int)Math.Round(baseSize * scale));
    }

    private void LoadInputBindings()
    {
        if (_inputBindingsStore is null)
        {
            _keyboardBindings = InputBindingsDocument.CreateKeyboardDefaults();
            GameActions.ApplyKeyboardBindings(_keyboardBindings);
            GameActions.ApplyControllerBindings(InputBindingsDocument.CreateControllerDefaults());
            return;
        }

        var loaded = _inputBindingsStore.LoadOrDefault(InputBindingsDocument.KeyboardDeviceClass);
        if (loaded.IsSuccess && loaded.Document is not null)
        {
            _keyboardBindings = loaded.Document;
        }
        else
        {
            _keyboardBindings = InputBindingsDocument.CreateKeyboardDefaults();
            WriteLocalCrashReport(
                "InputBindingsLoad",
                new InvalidOperationException(loaded.Message),
                eventCode: "input_bindings_load_failed");
        }

        GameActions.ApplyKeyboardBindings(_keyboardBindings);

        var controllerLoaded = _inputBindingsStore.LoadOrDefault(
            InputBindingsDocument.ControllerDeviceClass);
        InputBindingsDocument controllerDocument;
        if (controllerLoaded.IsSuccess && controllerLoaded.Document is not null)
        {
            controllerDocument = controllerLoaded.Document;
        }
        else
        {
            controllerDocument = InputBindingsDocument.CreateControllerDefaults();
            if (!controllerLoaded.IsSuccess)
            {
                WriteLocalCrashReport(
                    "InputBindingsLoad",
                    new InvalidOperationException(controllerLoaded.Message),
                    eventCode: "controller_bindings_load_failed");
            }
        }

        GameActions.ApplyControllerBindings(controllerDocument);
    }

    private void SaveInputBindings()
    {
        if (_inputBindingsStore is null)
        {
            return;
        }

        _inputBindingsStore.Save(_keyboardBindings);
    }

    private void RestoreInputBindingDefaults()
    {
        _keyboardBindings = InputBindingsDocument.CreateKeyboardDefaults();
        if (_inputBindingsStore is not null)
        {
            _inputBindingsStore.Save(_keyboardBindings);
            _inputBindingsStore.Save(InputBindingsDocument.CreateControllerDefaults());
        }

        GameActions.ApplyKeyboardBindings(_keyboardBindings);
        GameActions.ApplyControllerBindings(InputBindingsDocument.CreateControllerDefaults());
        ShowReplayStatus("INPUT DEFAULTS RESTORED");
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_screenState != ScreenState.Running || _paused || _run is null)
        {
            return;
        }

        var steps = RulesCadenceClock.DrainSteps(
            ref _rulesStepAccumulatorMilliseconds,
            delta,
            () => _run.EffectiveRulesStepMilliseconds);
        for (var index = 0; index < steps; index++)
        {
            if (_run is null || _run.Status != RunStatus.Running)
            {
                break;
            }

            AdvanceOneRulesStep();
        }

        if (steps > 0)
        {
            QueueRedraw();
        }
    }

    private void AdvanceOneRulesStep()
    {
        if (_run is null)
        {
            return;
        }

        var result = _run.Step();
        if (
            _replayRecorder is { } recorder
            && !recorder.TryCompleteStep(result, _run))
        {
            ShowReplayStatus(
                "REPLAY RECORDING STOPPED: "
                    + (recorder.FailureMessage ?? "UNKNOWN RECORDER FAILURE"));
        }

        AdvanceFeedback(result.OrderedEvents);

        if (_run.Status != RunStatus.Running)
        {
            _screenState = ScreenState.Ended;
            PlayCue(
                _run.Status == RunStatus.Won
                    ? AudioCue.Victory
                    : AudioCue.Death);
            FinalizeAndStoreReplay();
        }
    }

    public override void _Process(double delta)
    {
        _ = delta;
        if (ShouldQuitAfterReplayWork(Time.GetTicksMsec()))
        {
            GetTree().Quit();
        }
    }

    public override void _ExitTree()
    {
        DrainReplaySaveBeforeExit();
        _cuePlayer?.StopAndRelease();
        if (_window is not null && IsInstanceValid(_window))
        {
            _window.FilesDropped -= OnFilesDropped;
            _window.SizeChanged -= OnWindowSizeChanged;
        }

        Input.JoyConnectionChanged -= OnJoyConnectionChanged;
        GameActions.ReleaseRuntimeDefaults();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent.IsActionPressed(GameActions.Quit))
        {
            RequestQuit();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.RestoreDefaults))
        {
            RestoreInputBindingDefaults();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.ToggleMasterMute))
        {
            ApplyMasterMuteToggle();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.ToggleHighContrast))
        {
            ApplyHighContrastToggle();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.ToggleReducedMotion))
        {
            ApplyReducedMotionToggle();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.ToggleFullscreen))
        {
            ApplyFullscreenToggle();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.VolumeUp))
        {
            ApplyMasterVolumeStep(ShellSettings.DefaultVolumeStep);
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.VolumeDown))
        {
            ApplyMasterVolumeStep(-ShellSettings.DefaultVolumeStep);
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.TextScaleUp))
        {
            ApplyTextScaleStep(ShellSettings.DefaultTextScaleStep);
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.TextScaleDown))
        {
            ApplyTextScaleStep(-ShellSettings.DefaultTextScaleStep);
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.ToggleFlashFree))
        {
            ApplyFlashFreeToggle();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.OpenDiagnostics))
        {
            OpenDiagnosticsDirectory();
            if (_screenState is ScreenState.Menu or ScreenState.Ended)
            {
                ShowReplayStatus("DIAGNOSTICS PATH COPIED");
            }

            return;
        }

        if (_screenState is ScreenState.Menu or ScreenState.Ended)
        {
            if (inputEvent.IsActionPressed(GameActions.Replay))
            {
                VerifyLatestReplay();
            }
            else if (inputEvent.IsActionPressed(GameActions.Confirm))
            {
                StartRun();
            }
            else if (inputEvent.IsActionPressed(GameActions.Back))
            {
                if (_screenState == ScreenState.Menu)
                {
                    RequestQuit();
                }
                else
                {
                    ReturnToMenu();
                }
            }

            return;
        }

        if (inputEvent.IsActionPressed(GameActions.Back))
        {
            ReturnToMenu();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.Pause))
        {
            _paused = !_paused;
            _pausedByFocusLoss = false;
            PlayCue(AudioCue.Pause);
            QueueRedraw();
            return;
        }

        if (_paused)
        {
            return;
        }

        var direction = inputEvent switch
        {
            _ when inputEvent.IsActionPressed(GameActions.MoveUp) => RulesDirection.Up,
            _ when inputEvent.IsActionPressed(GameActions.MoveRight) => RulesDirection.Right,
            _ when inputEvent.IsActionPressed(GameActions.MoveDown) => RulesDirection.Down,
            _ when inputEvent.IsActionPressed(GameActions.MoveLeft) => RulesDirection.Left,
            _ => (RulesDirection?)null,
        };

        if (direction is { } requestedDirection)
        {
            if (
                _replayRecorder is { } recorder
                && !recorder.TryRecordCommand(requestedDirection))
            {
                ShowReplayStatus(
                    "REPLAY RECORDING STOPPED: "
                        + (recorder.FailureMessage ?? "UNKNOWN RECORDER FAILURE"));
            }

            _run?.QueueDirection(requestedDirection);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            RequestQuit();
        }
        else if (what == NotificationApplicationFocusOut)
        {
            PauseForFocusLoss();
        }
    }

    public override void _Draw()
    {
        // Window-space letterbox/pillarbox bars, then logical canvas content.
        DrawRect(
            new Rect2(0.0f, 0.0f, _virtualViewport.WindowWidth, _virtualViewport.WindowHeight),
            Colors.Black);
        DrawSetTransform(
            new Vector2(_virtualViewport.OffsetX, _virtualViewport.OffsetY),
            0.0f,
            new Vector2(_virtualViewport.Scale, _virtualViewport.Scale));

        DrawRect(new Rect2(0.0f, 0.0f, 1280.0f, 720.0f), CanvasBackgroundColor());
        DrawRect(new Rect2(0.0f, HudHeight, 1280.0f, 660.0f), BoardBackgroundColor());

        switch (_screenState)
        {
            case ScreenState.Menu:
                DrawLabel("VIBE SNAKE", new Vector2(42.0f, 190.0f), ScaledFontSize(52), PrimaryTextColor());
                DrawLabel("Plan the route. Build the vibe. Recover with style.", new Vector2(46.0f, 238.0f), ScaledFontSize(24), Colors.White);
                DrawLabel("START RUN", new Vector2(46.0f, 300.0f), ScaledFontSize(22), new Color(0.75f, 0.85f, 0.8f));
                DrawLabel("Enter, Space, or Controller South", new Vector2(46.0f, 336.0f), ScaledFontSize(18), SecondaryTextColor());
                DrawLabel("R or Controller North: verify latest replay", new Vector2(46.0f, 378.0f), ScaledFontSize(18), SecondaryTextColor());
                DrawLabel("Drop one replay file here to verify without changing it", new Vector2(46.0f, 410.0f), ScaledFontSize(18), SecondaryTextColor());
                DrawLabel(
                    "F4 flash  F5/F6 text  F7 mute  -/= vol  F9-F11 a11y  F8 binds  F12 logs",
                    new Vector2(46.0f, 442.0f),
                    ScaledFontSize(16),
                    SecondaryTextColor());
                if (_controllerCaption is not null)
                {
                    DrawLabel(
                        _controllerCaption,
                        new Vector2(46.0f, 474.0f),
                        ScaledFontSize(16),
                        new Color(0.75f, 0.9f, 0.55f));
                }

                if (_replayStatusCaption is not null)
                {
                    DrawLabel(_replayStatusCaption, new Vector2(46.0f, 506.0f), ScaledFontSize(16), new Color(0.46f, 0.94f, 0.96f));
                }

                break;
            case ScreenState.Running:
            case ScreenState.Ended:
                DrawRun();
                break;
            default:
                throw new InvalidOperationException("Unknown screen state.");
        }
    }

    private void PlayCue(AudioCue cue)
    {
        // Bus gains (Master + SFX/UI) already apply mute and volume. Keep the
        // stream player at full linear gain so levels are not attenuated twice.
        _cuePlayer?.PlayCue(cue, volumeLinear: 1.0f);
    }

    private void StartRun()
    {
        if (_replayOperation is not null || _queuedReplaySave is not null)
        {
            ShowReplayStatus("RUN START PAUSED: FINISHING THE CURRENT REPLAY OPERATION");
            return;
        }

        _run = _run is { Status: not RunStatus.Running } terminalRun
            ? terminalRun.Restart(_nextSeed++)
            : SnakeRun.Create(_nextSeed++, ProductRunConfig());
        _replayRecorder = new RunReplayRecorder(_run, appVersion: ProductIdentity.AppVersion);
        _screenState = ScreenState.Running;
        _paused = false;
        _pausedByFocusLoss = false;
        _rulesStepAccumulatorMilliseconds = 0.0;
        _feedbackCaption = null;
        _feedbackTicksRemaining = 0;
        _replayStatusCaption = null;
        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    /// <summary>
    /// Product runs enable achievement candidate emission. Shared parity fixtures
    /// keep the flag off until dual-runtime achievement events are regenerated.
    /// </summary>
    private static RunConfig ProductRunConfig() =>
        new(EnableAchievementCandidates: true);

    private void ReturnToMenu()
    {
        _screenState = ScreenState.Menu;
        _run = null;
        _replayRecorder = null;
        _paused = false;
        _pausedByFocusLoss = false;
        _rulesStepAccumulatorMilliseconds = 0.0;
        _feedbackCaption = null;
        _feedbackTicksRemaining = 0;
        PlayCue(AudioCue.Back);
        QueueRedraw();
    }

    private void FinalizeAndStoreReplay()
    {
        var recorder = _replayRecorder;
        _replayRecorder = null;
        if (recorder is null || _run is null || _replayStore is null)
        {
            ShowReplayStatus("REPLAY NOT SAVED: RECORDING SERVICES WERE UNAVAILABLE");
            return;
        }

        var recording = recorder.Finish(_run);
        if (!recording.IsSuccessful || recording.Replay is null)
        {
            _structuredLog?.Warning(
                "replay",
                recording.Message,
                eventCode: "replay_finalize_failed");
            ShowReplayStatus("REPLAY NOT SAVED: " + recording.Message);
            return;
        }

        var store = _replayStore;
        var replay = recording.Replay;
        _structuredLog?.Information(
            "replay",
            "Terminal replay finalized for atomic save.",
            eventCode: "replay_finalized");
        QueueReplaySave(
            () => SaveAndVerifyReplay(store, replay),
            "REPLAY SAVE IN PROGRESS");
    }

    private static string SaveAndVerifyReplay(ReplayStore store, RunReplay replay)
    {
        var save = store.Save(replay);
        if (!save.IsSuccess || save.FileName is null)
        {
            return $"REPLAY NOT SAVED [{save.Code}]: {save.Message}";
        }

        var loaded = store.Load(save.FileName);
        if (!loaded.IsSuccess)
        {
            return $"REPLAY SAVED BUT POST-WRITE VERIFICATION FAILED [{loaded.Code}]: "
                + loaded.Message;
        }

        return FormatReplayLoadResult(loaded, "REPLAY SAVED AND VERIFIED");
    }

    private void VerifyLatestReplay()
    {
        if (_replayStore is null)
        {
            ShowReplayStatus("REPLAY VERIFICATION UNAVAILABLE: STORAGE SERVICE NOT READY");
            return;
        }

        var store = _replayStore;
        if (!TryStartReplayOperation(
            () => FormatReplayLoadResult(
                store.LoadLatest(),
                "LATEST REPLAY VERIFIED"),
            "REPLAY VERIFICATION IN PROGRESS",
            ReplayOperationKind.Inspection))
        {
            ShowReplayStatus("REPLAY OPERATION ALREADY IN PROGRESS");
        }
    }

    private void OnFilesDropped(string[] files)
    {
        if (_screenState == ScreenState.Running)
        {
            ShowReplayStatus("REPLAY IMPORT PAUSED: RETURN TO THE MENU OR FINISH THE RUN");
            return;
        }

        if (files.Length != 1)
        {
            ShowReplayStatus("REPLAY IMPORT REQUIRES EXACTLY ONE FILE");
            return;
        }

        if (_replayStore is null)
        {
            ShowReplayStatus("REPLAY IMPORT UNAVAILABLE: STORAGE SERVICE NOT READY");
            return;
        }

        var store = _replayStore;
        var path = files[0];
        if (!TryStartReplayOperation(
            () => FormatReplayLoadResult(
                store.InspectExternal(path),
                "IMPORTED REPLAY VERIFIED"),
            "REPLAY IMPORT VERIFICATION IN PROGRESS",
            ReplayOperationKind.Inspection))
        {
            ShowReplayStatus("REPLAY OPERATION ALREADY IN PROGRESS");
        }
    }

    private bool TryStartReplayOperation(
        Func<string> operation,
        string progressMessage,
        ReplayOperationKind kind)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (_replayOperation is not null)
        {
            return false;
        }

        _replayOperation = Task.Run(operation);
        _replayOperationKind = kind;
        ShowReplayStatus(progressMessage);
        return true;
    }

    private void QueueReplaySave(Func<string> operation, string progressMessage)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (TryStartReplayOperation(operation, progressMessage, ReplayOperationKind.Save))
        {
            return;
        }

        if (_queuedReplaySave is not null)
        {
            throw new InvalidOperationException("Only one terminal replay save may be queued.");
        }

        _queuedReplaySave = operation;
        ShowReplayStatus("REPLAY SAVE QUEUED: FINISHING THE CURRENT REPLAY OPERATION");
    }

    private bool TryCompleteReplayOperation()
    {
        var operation = _replayOperation;
        if (operation is null || !operation.IsCompleted)
        {
            return false;
        }

        var completedKind = _replayOperationKind
            ?? throw new InvalidOperationException("A replay operation kind was not recorded.");
        _replayOperation = null;
        _replayOperationKind = null;
        try
        {
            ShowReplayStatus(operation.GetAwaiter().GetResult());
        }
        catch (Exception)
        {
            ShowReplayStatus("REPLAY OPERATION FAILED: AN UNEXPECTED LOCAL ERROR OCCURRED");
        }

        if (_queuedReplaySave is { } queuedSave)
        {
            _queuedReplaySave = null;
            if (!TryStartReplayOperation(
                queuedSave,
                "REPLAY SAVE IN PROGRESS",
                ReplayOperationKind.Save))
            {
                throw new InvalidOperationException("The queued replay save could not start.");
            }

            return false;
        }

        if (_quitAfterReplaySave && completedKind == ReplayOperationKind.Save)
        {
            _quitAfterReplaySave = false;
            _replayQuitDeadlineMilliseconds = null;
            return true;
        }

        return false;
    }

    private void RequestQuit()
    {
        if (
            _replayOperationKind == ReplayOperationKind.Save
            || _queuedReplaySave is not null)
        {
            _quitAfterReplaySave = true;
            _replayQuitDeadlineMilliseconds ??= AddSaturating(
                Time.GetTicksMsec(),
                ReplayShutdownDrainMilliseconds);
            ShowReplayStatus("QUIT PAUSED: FINISHING THE REPLAY SAVE");
            return;
        }

        GetTree().Quit();
    }

    private bool ShouldQuitAfterReplayWork(ulong nowMilliseconds)
    {
        if (TryCompleteReplayOperation())
        {
            return true;
        }

        if (
            !_quitAfterReplaySave
            || _replayQuitDeadlineMilliseconds is not { } deadline
            || nowMilliseconds < deadline)
        {
            return false;
        }

        _quitAfterReplaySave = false;
        _replayQuitDeadlineMilliseconds = null;
        _skipReplayShutdownDrain = true;
        return true;
    }

    private static ulong AddSaturating(ulong value, ulong increment) =>
        value > ulong.MaxValue - increment
            ? ulong.MaxValue
            : value + increment;

    private void DrainReplaySaveBeforeExit()
    {
        var operation = _replayOperation;
        if (
            _skipReplayShutdownDrain
            || operation is null
            || _replayOperationKind != ReplayOperationKind.Save)
        {
            return;
        }

        try
        {
            if (!operation.Wait(TimeSpan.FromMilliseconds(ReplayShutdownDrainMilliseconds)))
            {
                GD.PushWarning("The replay save did not finish within the bounded shutdown drain.");
            }
        }
        catch (AggregateException exception)
        {
            GD.PushWarning(
                $"The replay save failed during shutdown with {exception.InnerExceptions.Count} error(s).");
        }
    }

    private void ShowReplayStatus(string message)
    {
        var sanitized = SanitizeReplayStatus(message);
        _replayStatusCaption = sanitized;
        _feedbackCaption = sanitized;
        _feedbackTicksRemaining = FeedbackVisibilityTicks;
        QueueRedraw();
    }

    /// <summary>
    /// Opens the local diagnostics directory in the host file manager and copies
    /// the absolute path to the clipboard for support. No-op open in headless;
    /// clipboard still receives the path when the display server allows it.
    /// </summary>
    private void OpenDiagnosticsDirectory()
    {
        if (_diagnostics is null)
        {
            return;
        }

        var path = _diagnostics.EnsureDiagnosticsDirectory();
        _structuredLog?.EnsureLogsDirectory();
        _structuredLog?.Information(
            "diagnostics",
            "Opened local diagnostics directory for support.",
            eventCode: "open_diagnostics");
        try
        {
            DisplayServer.ClipboardSet(path);
        }
        catch (Exception)
        {
            // Clipboard can fail on locked-down hosts; open still proceeds.
        }

        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        OS.ShellOpen(path);
    }

    private static string SanitizeReplayStatus(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var truncated = message.Length > MaximumReplayStatusCharacters;
        var length = Math.Min(
            message.Length,
            truncated
                ? MaximumReplayStatusCharacters - 3
                : MaximumReplayStatusCharacters);
        if (length > 0 && char.IsHighSurrogate(message[length - 1]))
        {
            length--;
        }

        var characters = message.AsSpan(0, length).ToArray();
        for (var index = 0; index < characters.Length; index++)
        {
            if (char.IsControl(characters[index]))
            {
                characters[index] = ' ';
            }
        }

        return new string(characters) + (truncated ? "..." : string.Empty);
    }

    private static string FormatReplayLoadResult(
        ReplayLoadResult result,
        string successPrefix)
    {
        if (result.IsSuccess && result.Replay is { } replay)
        {
            return $"{successPrefix}: {replay.Outcome.StepCount} STEPS, SCORE {replay.Outcome.Score}";
        }

        if (result.Compatibility is { } compatibility)
        {
            return $"REPLAY REJECTED [{compatibility.Code}]: {compatibility.Message}";
        }

        return $"REPLAY UNAVAILABLE [{result.Code}]: {result.Message}";
    }

    private static string? GetArgumentValue(
        IEnumerable<string> arguments,
        string prefix)
    {
        var matches = arguments
            .Where(value => value.StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length > 1)
        {
            throw new ArgumentException(
                $"Command-line argument was specified more than once: {prefix}",
                nameof(arguments));
        }

        if (matches.Length == 0)
        {
            return null;
        }

        var value = matches[0][prefix.Length..];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"Command-line argument requires a value: {prefix}",
                nameof(arguments));
        }

        return value;
    }

    private void PauseForFocusLoss()
    {
        if (_screenState != ScreenState.Running || _paused)
        {
            return;
        }

        _paused = true;
        _pausedByFocusLoss = true;
        QueueRedraw();
    }

    private void DrawRun()
    {
        if (_run is null)
        {
            return;
        }

        var snapshot = _run.GetSnapshot();
        var hungerSeconds = snapshot.HungerTicksRemaining * RunConfig.RulesTickMilliseconds / 1000.0;
        var hungerWarning = snapshot.HungerTicksRemaining > 0
            && snapshot.HungerTicksRemaining <= RunConfig.DefaultStarvationWarningTicks;
        var statusText = _pausedByFocusLoss
            ? "PAUSED: FOCUS LOST"
            : _paused
                ? "PAUSED"
                : snapshot.Status.ToString().ToUpperInvariant();
        var hudColor = hungerWarning
            ? new Color(1.0f, 0.55f, 0.2f)
            : Colors.White;
        DrawLabel(
            $"SCORE {snapshot.Score:D6}    COMBO {snapshot.ComboMultiplier:0.0}x    HUNGER {hungerSeconds:0.0}s    {statusText}",
            new Vector2(18.0f, 31.0f),
            ScaledFontSize(20),
            hudColor);

        var powerStatus = DescribePowerStatus(snapshot);
        var secondaryStatus = _feedbackCaption is null
            ? powerStatus
            : $"{powerStatus}    {_feedbackCaption}";
        DrawLabel(
            secondaryStatus,
            new Vector2(18.0f, 53.0f),
            15,
            new Color(0.46f, 0.94f, 0.96f));

        if (snapshot.HasDetachedObstacles)
        {
            var hazard = PowerPresentation.SignalColor(PowerKind.SegmentDetach);
            foreach (var obstacle in snapshot.DetachedObstacles)
            {
                DrawCell(obstacle, new Color(0.18f, 0.05f, 0.07f), inset: 2.0f);
                DrawCellOutline(obstacle, hazard, 1.5f, inset: 2.0f);
            }
        }

        if (snapshot.HasBait && snapshot.BaitPosition is { } bait)
        {
            var baitColor = PowerPresentation.SignalColor(PowerKind.Bait);
            DrawCell(bait, new Color(0.16f, 0.12f, 0.02f), inset: 4.0f);
            DrawCellOutline(bait, baitColor, 1.5f, inset: 3.0f);
            DrawLabel(
                "T",
                new Vector2(
                    (bait.X * CellSize) + 5.0f,
                    HudHeight + (bait.Y * CellSize) + 16.0f),
                14,
                baitColor);
        }

        if (snapshot.Food is { } food)
        {
            DrawCell(food, new Color(1.0f, 0.25f, 0.38f), inset: 4.0f);
        }

        if (snapshot.PowerPickup is { } pickup)
        {
            DrawPowerPickup(pickup);
        }

        for (var index = 0; index < snapshot.Body.Count; index++)
        {
            var isHead = index == snapshot.Body.Count - 1;
            var bodyColor = isHead
                ? new Color(0.72f, 1.0f, 0.82f)
                : new Color(0.22f, 0.88f, 0.47f);
            if (snapshot.HasPhaseShift && !isHead)
            {
                bodyColor = new Color(0.55f, 0.42f, 0.88f, 0.72f);
            }
            else if (snapshot.HasGluttony && !isHead)
            {
                bodyColor = new Color(0.88f, 0.58f, 0.22f);
            }

            DrawCell(
                snapshot.Body[index],
                bodyColor,
                inset: isHead ? 1.0f : 2.0f);
        }

        DrawActiveHeadOutlines(snapshot);

        if (_screenState == ScreenState.Ended)
        {
            DrawRect(new Rect2(290.0f, 250.0f, 700.0f, 210.0f), new Color(0.01f, 0.02f, 0.018f, 0.92f));
            var ending = snapshot.Status == RunStatus.Won ? "GRID COMPLETE" : snapshot.DeathCause.ToString().ToUpperInvariant();
            DrawLabel(ending, new Vector2(445.0f, 310.0f), 38, new Color(1.0f, 0.75f, 0.3f));
            DrawLabel("Confirm to coil again", new Vector2(465.0f, 360.0f), 21, Colors.White);
            DrawLabel("R or Controller North: verify latest replay", new Vector2(430.0f, 390.0f), 16, new Color(0.58f, 0.7f, 0.64f));
            if (_run is not null)
            {
                var identity = RunScoreIdentity.FromRun(_run);
                DrawLabel(
                    FormatScoreIdentityCaption(identity),
                    new Vector2(360.0f, 420.0f),
                    14,
                    new Color(0.5f, 0.62f, 0.58f));
            }
        }
    }

    /// <summary>
    /// Compact support caption: ruleset contract, score, and config-hash prefix.
    /// </summary>
    internal static string FormatScoreIdentityCaption(RunScoreIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var hashPrefix = identity.ConfigHash.Length >= 12
            ? identity.ConfigHash[..12]
            : identity.ConfigHash;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{identity.RulesetContractId}  score {identity.Score}  cfg {hashPrefix}");
    }

    private void DrawActiveHeadOutlines(RunSnapshot snapshot)
    {
        if (snapshot.HasShield)
        {
            DrawCellOutline(snapshot.Head, PowerPresentation.SignalColor(PowerKind.Shield), 2.0f);
        }

        if (snapshot.HasPhaseShift)
        {
            DrawCellOutline(snapshot.Head, PowerPresentation.SignalColor(PowerKind.PhaseShift), 2.0f, inset: 2.0f);
        }

        if (snapshot.LastStandHeld || snapshot.HasLastStandRecovery)
        {
            DrawCellOutline(snapshot.Head, PowerPresentation.SignalColor(PowerKind.LastStand), 1.5f, inset: 3.5f);
        }

        if (snapshot.HasMagnet)
        {
            DrawCellOutline(snapshot.Head, PowerPresentation.SignalColor(PowerKind.Magnet), 1.5f, inset: 5.0f);
        }

        if (snapshot.HasSlowMo)
        {
            DrawCellOutline(snapshot.Head, PowerPresentation.SignalColor(PowerKind.SlowMo), 1.5f, inset: -1.0f);
        }

        if (snapshot.HasBoost)
        {
            DrawCellOutline(snapshot.Head, PowerPresentation.SignalColor(PowerKind.Boost), 1.5f, inset: -2.5f);
        }
    }

    private void DrawCell(GridPoint point, Color color, float inset)
    {
        DrawRect(
            new Rect2(
                (point.X * CellSize) + inset,
                HudHeight + (point.Y * CellSize) + inset,
                CellSize - (inset * 2.0f),
                CellSize - (inset * 2.0f)),
            color);
    }

    private void DrawPowerPickup(PowerPickup pickup)
    {
        var signalColor = PowerPresentation.SignalColor(pickup.Kind);
        DrawCell(pickup.Position, new Color(0.025f, 0.13f, 0.14f), inset: 3.0f);
        DrawCellOutline(pickup.Position, signalColor, 2.0f, inset: 3.0f);
        DrawLabel(
            PowerPresentation.Marker(pickup.Kind).ToString(),
            new Vector2(
                (pickup.Position.X * CellSize) + 5.0f,
                HudHeight + (pickup.Position.Y * CellSize) + 16.0f),
            14,
            signalColor);
    }

    private void DrawCellOutline(
        GridPoint point,
        Color color,
        float width,
        float inset = 0.5f)
    {
        DrawRect(
            new Rect2(
                (point.X * CellSize) + inset,
                HudHeight + (point.Y * CellSize) + inset,
                CellSize - (inset * 2.0f),
                CellSize - (inset * 2.0f)),
            color,
            filled: false,
            width: width);
    }

    private static string DescribePowerStatus(RunSnapshot snapshot) =>
        PowerPresentation.DescribeStatus(snapshot);

    private void AdvanceFeedback(IReadOnlyList<RunEventDetail> events)
    {
        if (_feedbackTicksRemaining > 0)
        {
            _feedbackTicksRemaining--;
            if (_feedbackTicksRemaining == 0)
            {
                _feedbackCaption = null;
            }
        }

        var feedback = StepFeedback.Resolve(events);
        if (feedback.Cue is { } cue)
        {
            // Flash-free keeps critical death/victory cues but skips rapid near-miss style tones.
            if (!_shellSettings.FlashFree
                || cue is AudioCue.Death or AudioCue.Victory or AudioCue.Pause or AudioCue.Confirm)
            {
                PlayCue(cue);
            }
        }

        if (feedback.Caption is { } caption)
        {
            _feedbackCaption = _shellSettings.FlashFree
                ? SoftenFlashyCaption(caption)
                : caption;
            // Reduced motion shortens transient captions without hiding critical recovery text.
            // Flash-free lengthens captions so information is not conveyed by brief flashes.
            if (_shellSettings.FlashFree)
            {
                _feedbackTicksRemaining = FeedbackVisibilityTicks + 10;
            }
            else if (_shellSettings.ReducedMotion)
            {
                _feedbackTicksRemaining = Math.Max(8, FeedbackVisibilityTicks / 2);
            }
            else
            {
                _feedbackTicksRemaining = FeedbackVisibilityTicks;
            }
        }
    }

    /// <summary>
    /// Removes high-intensity punctuation so flash-free mode does not rely on
    /// rapid visual emphasis. Critical words remain intact.
    /// </summary>
    internal static string SoftenFlashyCaption(string caption)
    {
        ArgumentNullException.ThrowIfNull(caption);
        return caption.Replace("!", string.Empty, StringComparison.Ordinal).Trim();
    }

    private void DrawLabel(string text, Vector2 position, int fontSize, Color color)
    {
        DrawString(ThemeDB.FallbackFont, position, text, HorizontalAlignment.Left, -1.0f, fontSize, color);
    }

    private async void ExecuteSmokeTest()
    {
        try
        {
            GameActions.AssertDefaultsRegistered();
            AudioBuses.AssertRegistered();
            var boundedStatus = SanitizeReplayStatus(
                "UNTRUSTED\n" + new string('x', MaximumReplayStatusCharacters * 2));
            if (
                boundedStatus.Length != MaximumReplayStatusCharacters
                || boundedStatus.Contains('\n'))
            {
                throw new InvalidOperationException("Replay status text was not bounded and sanitized.");
            }

            foreach (var cue in Enum.GetValues<AudioCue>())
            {
                _cuePlayer?.ValidateCue(cue);
            }
            ExecuteContentServiceSmokeTest();
            ExecuteStepFeedbackSmokeTest();
            ExecuteShellSettingsSmokeTest();
            ExecuteVirtualViewportSmokeTest();
            await ExecutePresentationFrameSamplerSmokeTestAsync();
            ExecuteMenuRunDeathRestartSmokeTest();
            var first = SnakeRun.Create(SmokeSeed);
            var replay = SnakeRun.Create(SmokeSeed);
            if (first.ComputeStateHash() != replay.ComputeStateHash())
            {
                throw new InvalidOperationException("Equal seeds produced different initial states.");
            }

            if (!first.QueueDirection(RulesDirection.Up) || !replay.QueueDirection(RulesDirection.Up))
            {
                throw new InvalidOperationException("A legal direction was rejected.");
            }

            var firstResult = first.Step();
            var replayResult = replay.Step();
            if (firstResult != replayResult || first.Head != new GridPoint(32, 15))
            {
                throw new InvalidOperationException("The deterministic movement smoke contract failed.");
            }

            var canonicalState = first.SerializeCanonicalState();
            var restored = SnakeRun.RestoreCanonicalState(canonicalState);
            if (
                restored.SerializeCanonicalState() != canonicalState
                || restored.ComputeStateHash() != first.ComputeStateHash())
            {
                throw new InvalidOperationException("The canonical restore smoke contract failed.");
            }

            if (
                !first.QueueDirection(RulesDirection.Left)
                || !restored.QueueDirection(RulesDirection.Left)
                || first.Step() != restored.Step())
            {
                throw new InvalidOperationException("The restored continuation smoke contract failed.");
            }

            IReadOnlyList<RulesDirection>[] replayCommands =
            [
                [RulesDirection.Up],
                [RulesDirection.Left],
            ];
            var replayEnvelope = RunReplay.Capture(
                SnakeRun.Create(SmokeSeed),
                replayCommands,
                checkpointInterval: 1);
            var replayRead = RunReplay.Read(replayEnvelope.Serialize());
            if (
                !replayRead.Compatibility.IsCompatible
                || replayRead.Replay is null
                || !replayRead.Replay.Verify().IsValid)
            {
                throw new InvalidOperationException("The replay envelope smoke contract failed.");
            }

            var storedReplayName = ExecuteReplayStorageSmokeTest();

            await ExecuteInputLifecycleSmokeTest();
            await ExecuteReplayOperationLifecycleSmokeTest();

            await SettlePlayedAudio();
            await ReleaseSmokeAudio();
            GD.Print(
                $"VIBESNAKE_GODOT_SMOKE_OK hash={firstResult.StateHash} replay={storedReplayName}");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            await SettlePlayedAudio();
            await ReleaseSmokeAudio();
            GD.PushError($"VIBESNAKE_GODOT_SMOKE_FAILED {exception}");
            GetTree().Quit(1);
        }
    }

    private async Task SettlePlayedAudio()
    {
        using var timer = GetTree().CreateTimer(0.15);
        await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
    }

    private async Task ReleaseSmokeAudio()
    {
        if (_cuePlayer is null)
        {
            return;
        }

        var cuePlayer = _cuePlayer;
        _cuePlayer = null;
        cuePlayer.StopAndDetach();
        using (var timer = GetTree().CreateTimer(0.10))
        {
            await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
        }

        cuePlayer.ReleaseStreams();
        cuePlayer.Free();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private string ExecuteReplayStorageSmokeTest()
    {
        if (_replayStore is null)
        {
            throw new InvalidOperationException("Replay storage was not initialized.");
        }

        var live = SnakeRun.Create(
            SmokeSeed + 1,
            new RunConfig(StarvationTicks: 2));
        var recorder = new RunReplayRecorder(live, checkpointInterval: 1, appVersion: ProductIdentity.AppVersion);
        IReadOnlyList<RulesDirection>[] commandsByStep =
        [
            [RulesDirection.Left, RulesDirection.Up],
            [RulesDirection.Down, RulesDirection.Left],
        ];
        foreach (var commands in commandsByStep)
        {
            foreach (var command in commands)
            {
                if (!recorder.TryRecordCommand(command))
                {
                    throw new InvalidOperationException(
                        "Live replay command capture failed: " + recorder.FailureMessage);
                }

                live.QueueDirection(command);
            }

            var result = live.Step();
            if (!recorder.TryCompleteStep(result, live))
            {
                throw new InvalidOperationException(
                    "Live replay step capture failed: " + recorder.FailureMessage);
            }
        }

        var recording = recorder.Finish(live);
        if (
            !recording.IsSuccessful
            || recording.Replay is null
            || !recording.Replay.Outcome.IsTerminal)
        {
            throw new InvalidOperationException(
                "Live terminal replay did not finalize: " + recording.Message);
        }

        var save = _replayStore.Save(recording.Replay);
        if (!save.IsSuccess || save.FileName is null)
        {
            throw new InvalidOperationException(
                "Atomic replay save failed: " + save.Message);
        }

        var loaded = _replayStore.Load(save.FileName);
        if (
            !loaded.IsSuccess
            || loaded.Replay is null
            || loaded.Replay.Serialize() != recording.Replay.Serialize()
            || !string.Equals(
                loaded.Replay.AppVersion,
                ProductIdentity.AppVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Stored replay verification failed: " + loaded.Message);
        }

        var inspected = _replayStore.InspectExternal(
            Path.Combine(_replayStore.ReplayDirectory, save.FileName));
        if (!inspected.IsSuccess || inspected.Replay?.PayloadHash != recording.Replay.PayloadHash)
        {
            throw new InvalidOperationException(
                "Read-only replay import failed: " + inspected.Message);
        }

        var futurePayload = recording.Replay.Serialize().Replace(
            "\"schemaVersion\":1",
            "\"schemaVersion\":2",
            StringComparison.Ordinal);
        var futureRead = RunReplay.Read(futurePayload);
        var futureResult = new ReplayLoadResult(
            ReplayLoadCode.Incompatible,
            futureRead.Compatibility.Message,
            Compatibility: futureRead.Compatibility);
        var compatibilityMessage = FormatReplayLoadResult(
            futureResult,
            "UNREACHABLE");
        if (
            futureRead.Compatibility.Code != ReplayCompatibilityCode.UnsupportedSchema
            || !compatibilityMessage.Contains(
                nameof(ReplayCompatibilityCode.UnsupportedSchema),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Replay compatibility feedback was not actionable.");
        }

        return save.FileName;
    }

    private async Task ExecuteInputLifecycleSmokeTest()
    {
        _screenState = ScreenState.Menu;
        _run = null;
        _paused = false;
        _pausedByFocusLoss = false;

        DispatchSmokeAction(GameActions.Confirm);
        if (_screenState != ScreenState.Running || _run is null)
        {
            throw new InvalidOperationException("Logical confirm did not start a run.");
        }

        DispatchSmokeAction(GameActions.MoveUp);
        if (_run.PendingDirectionCount != 1)
        {
            throw new InvalidOperationException("Logical movement was not buffered.");
        }

        PauseForFocusLoss();
        DispatchSmokeAction(GameActions.MoveLeft);
        if (!_paused || !_pausedByFocusLoss || _run.PendingDirectionCount != 1)
        {
            throw new InvalidOperationException("Focus-loss pause accepted hidden input.");
        }

        DispatchSmokeAction(GameActions.Pause);
        _PhysicsProcess(1.0 / 20.0);
        if (
            _paused
            || _pausedByFocusLoss
            || _run.Direction != RulesDirection.Up
            || _run.Head != new GridPoint(32, 15))
        {
            throw new InvalidOperationException("Focus-loss recovery did not resume safely.");
        }

        DispatchSmokeAction(GameActions.Back);
        if (_screenState != ScreenState.Menu || _run is not null || _paused)
        {
            throw new InvalidOperationException("Logical back did not return to the menu.");
        }

        DispatchSmokeAction(GameActions.Replay);
        for (var frame = 0; frame < 300 && _replayOperation is not null; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        if (
            _replayOperation is not null
            || _replayStatusCaption is null
            || !_replayStatusCaption.StartsWith(
                "LATEST REPLAY VERIFIED:",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Logical replay verification did not report a verified stored run.");
        }
    }

    private async Task ExecuteReplayOperationLifecycleSmokeTest()
    {
        _screenState = ScreenState.Menu;
        _run = null;
        _replayRecorder = null;
        _queuedReplaySave = null;
        _quitAfterReplaySave = false;
        _replayQuitDeadlineMilliseconds = null;
        _skipReplayShutdownDrain = false;

        var inspectionGate = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryStartReplayOperation(
            () =>
            {
                inspectionGate.Task.GetAwaiter().GetResult();
                return "REPLAY INSPECTION COMPLETED";
            },
            "REPLAY INSPECTION IN PROGRESS",
            ReplayOperationKind.Inspection))
        {
            throw new InvalidOperationException("The replay lifecycle smoke inspection could not start.");
        }

        DispatchSmokeAction(GameActions.Confirm);
        if (
            _screenState != ScreenState.Menu
            || _run is not null
            || _replayRecorder is not null
            || _replayStatusCaption != "RUN START PAUSED: FINISHING THE CURRENT REPLAY OPERATION")
        {
            throw new InvalidOperationException(
                "A run started before the active replay inspection completed.");
        }

        QueueReplaySave(
            () => "QUEUED REPLAY SAVE COMPLETED",
            "REPLAY SAVE IN PROGRESS");
        if (_queuedReplaySave is null)
        {
            throw new InvalidOperationException("A terminal replay save was not retained behind inspection.");
        }

        inspectionGate.SetResult(true);
        for (
            var frame = 0;
            frame < 300 && (_replayOperation is not null || _queuedReplaySave is not null);
            frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        if (
            _replayOperation is not null
            || _queuedReplaySave is not null
            || _replayStatusCaption != "QUEUED REPLAY SAVE COMPLETED")
        {
            throw new InvalidOperationException(
                "The queued terminal replay save did not complete after inspection.");
        }

        DispatchSmokeAction(GameActions.Confirm);
        if (_screenState != ScreenState.Running || _run is null || _replayRecorder is null)
        {
            throw new InvalidOperationException("A run did not start after replay work completed.");
        }

        ReturnToMenu();
        _replayOperation = Task.FromResult("REPLAY SAVE COMPLETED");
        _replayOperationKind = ReplayOperationKind.Save;
        RequestQuit();
        if (
            !_quitAfterReplaySave
            || _replayStatusCaption != "QUIT PAUSED: FINISHING THE REPLAY SAVE")
        {
            throw new InvalidOperationException("Quit did not wait for an active replay save.");
        }

        if (
            !TryCompleteReplayOperation()
            || _quitAfterReplaySave
            || _replayQuitDeadlineMilliseconds is not null
            || _replayOperation is not null)
        {
            throw new InvalidOperationException("Quit was not released after the replay save completed.");
        }

        var blockedSave = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _replayOperation = blockedSave.Task;
        _replayOperationKind = ReplayOperationKind.Save;
        RequestQuit();
        var deadline = _replayQuitDeadlineMilliseconds
            ?? throw new InvalidOperationException("Quit did not establish a replay save deadline.");
        if (
            ShouldQuitAfterReplayWork(deadline - 1UL)
            || !ShouldQuitAfterReplayWork(deadline)
            || !_skipReplayShutdownDrain)
        {
            throw new InvalidOperationException(
                "A noncompleting replay save did not release quit at the bounded deadline.");
        }

        _replayOperation = null;
        _replayOperationKind = null;
        _skipReplayShutdownDrain = false;
    }

    private static void ExecuteContentServiceSmokeTest()
    {
        // Embedded fixture keeps packaged-player smoke independent of the
        // development checkout inventory used only by optional live checks.
        const string blockedPath = "audio/radio/smoke_blocked_track.mp3";
        const string syntheticInventoryJson =
            """
            {
              "schemaVersion": 1,
              "fileCount": 1,
              "assets": [
                {
                  "id": "asset:audio/radio/smoke_blocked_track.mp3",
                  "path": "audio/radio/smoke_blocked_track.mp3",
                  "mediaType": "audio/mpeg",
                  "bytes": 32,
                  "sha256": "0000000000000000000000000000000000000000000000000000000000000000",
                  "exportEligible": false,
                  "shipStatus": "blocked",
                  "rights": { "status": "cleared" }
                }
              ]
            }
            """;

        var content = new ContentService(ContentInventory.Parse(syntheticInventoryJson));
        if (content.FileCount != 1 || content.ExportEligibleCount != 0 || content.TotalBytes != 32)
        {
            throw new InvalidOperationException(
                "Content service smoke expected one blocked synthetic inventory asset.");
        }

        if (content.MayPackage(blockedPath))
        {
            throw new InvalidOperationException(
                "Content service must deny packaging of non-exportEligible assets.");
        }

        var denied = content.ResolveForPackaging(blockedPath);
        if (denied.Code != ContentResolveCode.NotExportEligible || denied.IsReady)
        {
            throw new InvalidOperationException(
                "ResolveForPackaging must return NotExportEligible for blocked assets.");
        }

        var missing = content.ResolveForPackaging("missing/asset.bin");
        if (missing.Code != ContentResolveCode.NotFound)
        {
            throw new InvalidOperationException(
                "ResolveForPackaging must return NotFound for unknown assets.");
        }

        var invalid = content.ResolveForPackaging("../escape.bin");
        if (invalid.Code != ContentResolveCode.InvalidPath)
        {
            throw new InvalidOperationException(
                "ResolveForPackaging must reject path traversal.");
        }

        var budget = content.MeasureBudgets();
        if (budget.ExportEligibleCount != 0
            || budget.InventoryBytes != 32
            || !budget.ExportEligibleWithinCoreCompressedBudget)
        {
            throw new InvalidOperationException(
                "Content budget smoke contract failed for the synthetic inventory.");
        }

        if (content.ListByMediaTypePrefix("audio/").Count != 1
            || content.CountByMediaTypePrefix("audio/") != 1)
        {
            throw new InvalidOperationException(
                "Content service media-type listing smoke contract failed.");
        }

        var timing = ContentTimingReport.FromMeasurements(
            inventoryScanMilliseconds: 10,
            coldStartMilliseconds: 20);
        if (!timing.WithinInventoryScanBudget || !timing.WithinColdStartBudget)
        {
            throw new InvalidOperationException(
                "Content timing smoke must accept sub-ceiling measurements.");
        }

        if (TryResolveCheckoutInventoryPath(out var inventoryPath))
        {
            var live = ContentService.LoadInventoryFile(inventoryPath);
            if (live.ExportEligibleCount != 0)
            {
                throw new InvalidOperationException(
                    "Live content inventory must keep exportEligible at zero until pack approval.");
            }

            if (live.MayPackage("audio/radio/ambient_graceful_laminar.mp3"))
            {
                throw new InvalidOperationException(
                    "Live content inventory must deny packaging of non-exportEligible radio assets.");
            }

            var liveBudget = live.MeasureBudgets();
            if (liveBudget.ExportEligibleCount != 0 || liveBudget.InventoryBytes <= 0)
            {
                throw new InvalidOperationException(
                    "Live content inventory budget report must keep eligibility at zero.");
            }
        }
    }

    private static bool TryResolveCheckoutInventoryPath(out string inventoryPath)
    {
        string[] candidates =
        [
            System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    System.IO.Directory.GetCurrentDirectory(),
                    "config",
                    "content_inventory.json")),
            System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    System.IO.Directory.GetCurrentDirectory(),
                    "..",
                    "config",
                    "content_inventory.json")),
            System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    ProjectSettings.GlobalizePath("res://"),
                    "..",
                    "config",
                    "content_inventory.json")),
        ];

        foreach (var candidate in candidates)
        {
            if (System.IO.File.Exists(candidate))
            {
                inventoryPath = candidate;
                return true;
            }
        }

        inventoryPath = string.Empty;
        return false;
    }

    private void ExecuteShellSettingsSmokeTest()
    {
        var settings = ShellSettings.CreateDefaults();
        settings.MasterVolume = 2.0f;
        settings.TextScale = 9.0f;
        settings.MusicMuted = true;
        settings.Clamp();
        if (settings.MasterVolume != 1.0f || settings.TextScale != 1.5f)
        {
            throw new InvalidOperationException("Shell settings clamp contract failed.");
        }

        if (settings.EffectiveMusicVolume() != 0.0f || settings.EffectiveSfxVolume() <= 0.0f)
        {
            throw new InvalidOperationException("Shell bus mute contract failed.");
        }

        AudioBuses.ApplyShellSettings(settings);
        if (AudioBuses.GetBusLinear(AudioBuses.Music) > 0.0001f)
        {
            throw new InvalidOperationException("Muted music bus must apply zero linear gain.");
        }

        if (AudioBuses.GetBusLinear(AudioBuses.Sfx) <= 0.0f)
        {
            throw new InvalidOperationException("Unmuted SFX bus must keep positive linear gain.");
        }

        settings.MusicMuted = false;
        settings.MusicVolume = 0.5f;
        settings.SfxVolume = 0.25f;
        settings.UiVolume = 0.75f;
        AudioBuses.ApplyShellSettings(settings);
        if (Math.Abs(AudioBuses.GetBusLinear(AudioBuses.Music) - 0.5f) > 0.02f
            || Math.Abs(AudioBuses.GetBusLinear(AudioBuses.Sfx) - 0.25f) > 0.02f
            || Math.Abs(AudioBuses.GetBusLinear(AudioBuses.Ui) - 0.75f) > 0.02f)
        {
            throw new InvalidOperationException("Shell settings bus volume apply contract failed.");
        }

        // Restore defaults so later smoke cues remain audible on host runners.
        AudioBuses.ApplyShellSettings(ShellSettings.CreateDefaults());

        settings.ReducedMotion = true;
        settings.FlashFree = true;
        settings.HighContrast = true;
        settings.ScreenShakeIntensity = 0.0f;
        if (!settings.ReducedMotion || !settings.FlashFree || settings.ScreenShakeIntensity != 0.0f)
        {
            throw new InvalidOperationException("Accessibility placeholder settings failed.");
        }

        settings.MasterMuted = false;
        if (!settings.ToggleMasterMute() || !settings.MasterMuted)
        {
            throw new InvalidOperationException("Master mute toggle on failed.");
        }

        if (settings.ToggleMasterMute() || settings.MasterMuted)
        {
            throw new InvalidOperationException("Master mute toggle off failed.");
        }

        settings.HighContrast = false;
        if (!settings.ToggleHighContrast() || !settings.HighContrast)
        {
            throw new InvalidOperationException("High contrast toggle on failed.");
        }

        if (settings.ToggleHighContrast() || settings.HighContrast)
        {
            throw new InvalidOperationException("High contrast toggle off failed.");
        }

        settings.ReducedMotion = false;
        settings.ScreenShakeIntensity = 1.0f;
        if (!settings.ToggleReducedMotion()
            || !settings.ReducedMotion
            || settings.ScreenShakeIntensity != 0.0f)
        {
            throw new InvalidOperationException("Reduced motion toggle on failed.");
        }

        if (settings.ToggleReducedMotion() || settings.ReducedMotion)
        {
            throw new InvalidOperationException("Reduced motion toggle off failed.");
        }

        settings.Fullscreen = false;
        if (!settings.ToggleFullscreen() || !settings.Fullscreen)
        {
            throw new InvalidOperationException("Fullscreen toggle on failed.");
        }

        if (settings.ToggleFullscreen() || settings.Fullscreen)
        {
            throw new InvalidOperationException("Fullscreen toggle off failed.");
        }

        settings.MasterVolume = 0.5f;
        settings.MasterMuted = true;
        if (Math.Abs(settings.AdjustMasterVolume(ShellSettings.DefaultVolumeStep) - 0.55f) > 0.0001f
            || settings.MasterMuted)
        {
            throw new InvalidOperationException("Master volume step-up unmute contract failed.");
        }

        if (Math.Abs(settings.AdjustMasterVolume(-1.0f) - 0.0f) > 0.0001f
            || Math.Abs(settings.AdjustMasterVolume(2.0f) - 1.0f) > 0.0001f)
        {
            throw new InvalidOperationException("Master volume clamp contract failed.");
        }

        settings.TextScale = 1.0f;
        if (Math.Abs(settings.AdjustTextScale(ShellSettings.DefaultTextScaleStep) - 1.05f) > 0.0001f)
        {
            throw new InvalidOperationException("Text scale step-up contract failed.");
        }

        if (Math.Abs(settings.AdjustTextScale(2.0f) - ShellSettings.MaximumTextScale) > 0.0001f
            || Math.Abs(settings.AdjustTextScale(-2.0f) - ShellSettings.MinimumTextScale) > 0.0001f)
        {
            throw new InvalidOperationException("Text scale clamp contract failed.");
        }

        settings.FlashFree = false;
        if (!settings.ToggleFlashFree() || !settings.FlashFree)
        {
            throw new InvalidOperationException("Flash-free toggle on failed.");
        }

        if (settings.ToggleFlashFree() || settings.FlashFree)
        {
            throw new InvalidOperationException("Flash-free toggle off failed.");
        }

        if (!string.Equals(
                SoftenFlashyCaption("+2 STYLE STREAK!"),
                "+2 STYLE STREAK",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Flash-free caption soften contract failed.");
        }

        settings.ScreenShakeIntensity = 0.8f;
        settings.ReducedMotion = false;
        settings.FlashFree = false;
        if (Math.Abs(settings.EffectiveScreenShakeIntensity() - 0.8f) > 0.0001f)
        {
            throw new InvalidOperationException("Effective screen shake without a11y gates failed.");
        }

        settings.FlashFree = true;
        if (settings.EffectiveScreenShakeIntensity() != 0.0f)
        {
            throw new InvalidOperationException("Flash-free must force effective screen shake to zero.");
        }

        settings.FlashFree = false;
        settings.ReducedMotion = true;
        if (settings.EffectiveScreenShakeIntensity() != 0.0f)
        {
            throw new InvalidOperationException("Reduced motion must force effective screen shake to zero.");
        }

        if (_preferencesStore is null || _diagnostics is null)
        {
            throw new InvalidOperationException("Preferences and diagnostics services were not ready.");
        }

        settings.MusicVolume = 0.33f;
        settings.ReducedMotion = true;
        settings.HighContrast = true;
        _shellSettings = settings;
        SaveShellSettings();
        LoadShellSettings();
        if (Math.Abs(_shellSettings.MusicVolume - 0.33f) > 0.0001f || !_shellSettings.ReducedMotion)
        {
            throw new InvalidOperationException("Preferences schema 2 did not round-trip through the store.");
        }

        var scoreIdentity = RunScoreIdentity.FromRun(SnakeRun.Create(99UL));
        var identityCaption = FormatScoreIdentityCaption(scoreIdentity);
        if (!identityCaption.Contains("vibesnake-core@4", StringComparison.Ordinal)
            || !identityCaption.Contains("cfg ", StringComparison.Ordinal)
            || identityCaption.Length < 20)
        {
            throw new InvalidOperationException("Score identity caption contract failed.");
        }

        // Production apply paths flip each preference and persist without throwing.
        // After load: mute off, high-contrast on, reduced-motion on, fullscreen off.
        ApplyMasterMuteToggle();
        ApplyHighContrastToggle();
        ApplyReducedMotionToggle();
        ApplyFullscreenToggle();
        if (!_shellSettings.MasterMuted
            || _shellSettings.HighContrast
            || _shellSettings.ReducedMotion
            || !_shellSettings.Fullscreen)
        {
            throw new InvalidOperationException(
                "Accessibility apply toggles did not flip persisted shell settings.");
        }

        // Restore quiet defaults for remaining smoke work.
        _shellSettings.MasterMuted = false;
        _shellSettings.HighContrast = false;
        _shellSettings.ReducedMotion = false;
        _shellSettings.Fullscreen = false;
        SaveShellSettings();

        var controllerTracker = new ControllerConnectionTracker();
        var connected = controllerTracker.NoteConnected(0, "Smoke Pad");
        if (connected is null
            || connected.Value.Kind != ControllerConnectionKind.Connected
            || controllerTracker.ConnectedCount != 1)
        {
            throw new InvalidOperationException("Controller connection tracker connect contract failed.");
        }

        var disconnected = controllerTracker.NoteDisconnected(0);
        if (disconnected is null
            || disconnected.Value.Kind != ControllerConnectionKind.Disconnected
            || controllerTracker.ConnectedCount != 0)
        {
            throw new InvalidOperationException("Controller connection tracker disconnect contract failed.");
        }

        var smokeConfigHash = new RunConfig().ComputeConfigHash();
        var reportPath = WriteLocalCrashReport(
            "Smoke",
            new InvalidOperationException("Smoke diagnostics probe under C:\\Users\\example\\x"),
            eventCode: "smoke_crash_probe",
            configHash: smokeConfigHash,
            configHashAlgorithm: RunConfig.ConfigHashAlgorithmId)
            ?? throw new InvalidOperationException("Smoke crash report path was not produced.");
        var reportText = System.IO.File.ReadAllText(reportPath);
        if (!System.IO.File.Exists(reportPath)
            || !reportText.Contains("<path>", StringComparison.Ordinal)
            || !reportText.Contains(smokeConfigHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Diagnostics smoke report was missing, unsanitized, or missing config hash.");
        }

        var diagnosticsDirectory = _diagnostics.EnsureDiagnosticsDirectory();
        if (!System.IO.Directory.Exists(diagnosticsDirectory)
            || !System.IO.Path.IsPathFullyQualified(diagnosticsDirectory)
            || !reportPath.StartsWith(diagnosticsDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "EnsureDiagnosticsDirectory did not return the absolute report parent folder.");
        }

        // Headless no-op path; interactive sessions may open the folder later from UI.
        OpenDiagnosticsDirectory();

        if (_structuredLog is null)
        {
            throw new InvalidOperationException("Structured log was not initialized for smoke.");
        }

        var structuredLogPath = _structuredLog.ActiveLogPath;
        if (!System.IO.File.Exists(structuredLogPath))
        {
            throw new InvalidOperationException(
                "Structured session log was not written under user-data logs.");
        }

        var structuredLogText = System.IO.File.ReadAllText(structuredLogPath);
        if (!structuredLogText.Contains("smoke_session_start", StringComparison.Ordinal)
            || !structuredLogText.Contains("open_diagnostics", StringComparison.Ordinal)
            || !structuredLogText.Contains("smoke_crash_probe", StringComparison.Ordinal)
            || !structuredLogText.Contains("\"level\":\"Error\"", StringComparison.Ordinal)
            || !structuredLogText.Contains("\"kind\":\"structured-log\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Structured log missing required smoke event codes or kind marker.");
        }

        ShellTransitions.EnsureTransition(ShellScreen.Menu, ShellScreen.Running);
        ShellTransitions.EnsureTransition(ShellScreen.Running, ShellScreen.Ended);
        ShellTransitions.EnsureTransition(ShellScreen.Ended, ShellScreen.Running);
        ShellTransitions.EnsureTransition(ShellScreen.Ended, ShellScreen.Menu);
        try
        {
            ShellTransitions.EnsureTransition(ShellScreen.Menu, ShellScreen.Ended);
            throw new InvalidOperationException("Illegal shell transition was accepted.");
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("Illegal shell transition", StringComparison.Ordinal))
        {
            // Expected rejection.
        }

        var bank = SnakeRun.CreateStreamBank(SmokeSeed);
        var run = SnakeRun.Create(SmokeSeed);
        if (run.MasterSeed != SmokeSeed
            || bank.Gameplay.NextUInt() == bank.Ai.NextUInt())
        {
            throw new InvalidOperationException("Master-seed stream bank smoke contract failed.");
        }

        if (_inputBindingsStore is null)
        {
            throw new InvalidOperationException("Input bindings store was not ready.");
        }

        _keyboardBindings = InputBindingsDocument.CreateKeyboardDefaults();
        SaveInputBindings();
        LoadInputBindings();
        if (!_keyboardBindings.ActionToBinding.ContainsKey("confirm")
            || _keyboardBindings.ActionToBinding["confirm"] != "key:enter")
        {
            throw new InvalidOperationException("Input bindings smoke round-trip failed.");
        }

        if (!GameActions.ActionHasKeyboardToken(GameActions.Confirm, "key:enter")
            || !GameActions.ActionHasKeyboardToken(GameActions.MoveUp, "key:up"))
        {
            throw new InvalidOperationException(
                "Default keyboard bindings were not applied to the InputMap.");
        }

        var remappedActions = new Dictionary<string, string>(
            InputBindingsDocument.CreateKeyboardDefaults().ActionToBinding,
            StringComparer.Ordinal)
        {
            ["pause"] = "key:space",
        };
        _keyboardBindings = new InputBindingsDocument(
            InputBindingsDocument.CurrentSchemaVersion,
            InputBindingsDocument.KeyboardDeviceClass,
            remappedActions);
        GameActions.ApplyKeyboardBindings(_keyboardBindings);
        if (!GameActions.ActionHasKeyboardToken(GameActions.Pause, "key:space")
            || GameActions.ActionHasKeyboardToken(GameActions.Pause, "key:p"))
        {
            throw new InvalidOperationException(
                "Keyboard remap did not replace the pause primary key.");
        }

        // Restore defaults for the remainder of the smoke path.
        RestoreInputBindingDefaults();
        if (!GameActions.ActionHasKeyboardToken(GameActions.Pause, "key:p")
            || !GameActions.ActionHasKeyboardToken(GameActions.Confirm, "key:enter"))
        {
            throw new InvalidOperationException(
                "Keyboard defaults could not be restored after remap smoke.");
        }
    }

    private void ExecuteVirtualViewportSmokeTest()
    {
        var viewport = new VirtualViewport(1920.0f, 1080.0f);
        if (Math.Abs(viewport.Scale - 1.5f) > 0.0001f
            || Math.Abs(viewport.OffsetX) > 0.0001f
            || Math.Abs(viewport.OffsetY) > 0.0001f)
        {
            throw new InvalidOperationException("16:9 viewport scale contract failed.");
        }

        var ultrawide = new VirtualViewport(2560.0f, 1080.0f);
        if (ultrawide.OffsetX <= 0.0f || Math.Abs(ultrawide.Scale - 1.5f) > 0.0001f)
        {
            throw new InvalidOperationException("Ultrawide letterbox contract failed.");
        }

        var logical = ultrawide.WindowToLogical(ultrawide.LogicalToWindow(new Vector2(640.0f, 360.0f)));
        if (Math.Abs(logical.X - 640.0f) > 0.05f || Math.Abs(logical.Y - 360.0f) > 0.05f)
        {
            throw new InvalidOperationException("Pointer transform round-trip failed.");
        }

        if (!viewport.ContainsLogicalPoint(new Vector2(0.0f, 0.0f))
            || viewport.ContainsLogicalPoint(new Vector2(1280.0f, 720.0f)))
        {
            throw new InvalidOperationException("Logical bounds contract failed.");
        }

        // Live shell viewport must track the active window and preserve pointer math.
        RefreshVirtualViewport();
        if (_virtualViewport.WindowWidth < VirtualViewport.MinimumWindowWidth
            || _virtualViewport.WindowHeight < VirtualViewport.MinimumWindowHeight
            || _virtualViewport.Scale <= 0.0f)
        {
            throw new InvalidOperationException("Live virtual viewport was not initialized from the window.");
        }

        var mapped = MapPointerToLogical(
            _virtualViewport.LogicalToWindow(new Vector2(100.0f, 200.0f)));
        if (Math.Abs(mapped.X - 100.0f) > 0.05f || Math.Abs(mapped.Y - 200.0f) > 0.05f)
        {
            throw new InvalidOperationException("Live pointer mapping round-trip failed.");
        }

        // Ultrawide resize path: pillarbox offsets must appear without stretching Y.
        _virtualViewport.Resize(2560.0f, 1080.0f);
        if (_virtualViewport.OffsetX <= 0.0f || Math.Abs(_virtualViewport.Scale - 1.5f) > 0.0001f)
        {
            throw new InvalidOperationException("Live ultrawide resize contract failed.");
        }

        RefreshVirtualViewport();
    }

    private async Task ExecutePresentationFrameSamplerSmokeTestAsync()
    {
        var sampler = new PresentationFrameSampler();
        // Synthetic host-independent samples prove percentile math only.
        double[] samples = [8.0, 9.0, 10.0, 11.0, 12.0, 16.0, 20.0, 33.0];
        foreach (var sample in samples)
        {
            sampler.RecordFrameMilliseconds(sample);
        }

        var summary = sampler.Summarize();
        if (summary.SampleCount != samples.Length
            || summary.P50Milliseconds < 10.0
            || summary.P95Milliseconds < 20.0
            || summary.MaxMilliseconds != 33.0)
        {
            throw new InvalidOperationException("Presentation frame sampler summary contract failed.");
        }

        // Capture a short live burst using process frames for host-dependent evidence.
        var live = new PresentationFrameSampler();
        for (var index = 0; index < 32; index++)
        {
            var started = Time.GetTicksUsec();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var elapsedMilliseconds = (Time.GetTicksUsec() - started) / 1000.0;
            live.RecordFrameMilliseconds(Math.Max(0.01, elapsedMilliseconds));
        }

        WritePresentationFrameEvidence(live.Summarize());
    }

    private static void WritePresentationFrameEvidence(PresentationFrameSummary summary)
    {
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "presentation_frames.json");
        var json =
            "{\n"
            + "  \"schemaVersion\": 1,\n"
            + "  \"kind\": \"presentation-frame-evidence-v1\",\n"
            + $"  \"sampleCount\": {summary.SampleCount},\n"
            + $"  \"averageMilliseconds\": {summary.AverageMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)},\n"
            + $"  \"p50Milliseconds\": {summary.P50Milliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)},\n"
            + $"  \"p95Milliseconds\": {summary.P95Milliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)},\n"
            + $"  \"p99Milliseconds\": {summary.P99Milliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)},\n"
            + $"  \"maxMilliseconds\": {summary.MaxMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)},\n"
            + "  \"notes\": [\n"
            + "    \"Host-dependent smoke burst only.\",\n"
            + "    \"Does not claim declared-hardware acceptance.\"\n"
            + "  ]\n"
            + "}\n";
        System.IO.File.WriteAllText(path, json);
    }

    private static string ResolveEvidenceDirectory()
    {
        var configured = System.Environment.GetEnvironmentVariable("VIBESNAKE_EVIDENCE_DIR");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return System.IO.Path.GetFullPath(configured);
        }

        var directory = new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var roadmap = System.IO.Path.Combine(directory.FullName, "ROADMAP.md");
            var solution = System.IO.Path.Combine(directory.FullName, "native", "VibeSnake.slnx");
            if (System.IO.File.Exists(roadmap) && System.IO.File.Exists(solution))
            {
                return System.IO.Path.Combine(directory.FullName, "TestResults", "native");
            }

            directory = directory.Parent;
        }

        return System.IO.Path.GetFullPath(
            System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "TestResults", "native"));
    }

    private void ExecuteMenuRunDeathRestartSmokeTest()
    {
        _screenState = ScreenState.Menu;
        _run = null;
        _paused = false;
        _pausedByFocusLoss = false;
        _replayRecorder = null;
        _rulesStepAccumulatorMilliseconds = 0.0;

        DispatchSmokeAction(GameActions.Confirm);
        if (_screenState != ScreenState.Running || _run is null || _replayRecorder is null)
        {
            throw new InvalidOperationException("Menu confirm did not start a recorded run.");
        }

        // Force a terminal self-collision on a fixed body without depending on long starvation.
        _run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 8,
                Height: 6,
                StarvationTicks: 100,
                PowerSpawnIntervalTicks: 0,
                PowerVisibleTicks: 4),
            [
                new GridPoint(1, 1),
                new GridPoint(1, 2),
                new GridPoint(2, 2),
                new GridPoint(2, 1),
            ],
            RulesDirection.Down,
            food: new GridPoint(6, 4),
            hungerTicksRemaining: 100);
        _replayRecorder = new RunReplayRecorder(_run, appVersion: ProductIdentity.AppVersion);

        var deathResult = _run.Step();
        if (
            _run.Status != RunStatus.Dead
            || _run.DeathCause != DeathCause.SelfCollision
            || !deathResult.Events.HasFlag(RunEvent.Died))
        {
            throw new InvalidOperationException("Forced collision did not end the run.");
        }

        _screenState = ScreenState.Ended;
        FinalizeAndStoreReplay();
        for (var frame = 0; frame < 300 && (_replayOperation is not null || _queuedReplaySave is not null); frame++)
        {
            // Drain single-flight replay work without waiting on process frames.
            TryCompleteReplayOperation();
        }

        if (_replayOperation is not null || _queuedReplaySave is not null)
        {
            throw new InvalidOperationException("Death path did not finish terminal replay save.");
        }

        DispatchSmokeAction(GameActions.Confirm);
        if (
            _screenState != ScreenState.Running
            || _run is null
            || _run.Status != RunStatus.Running
            || _replayRecorder is null)
        {
            throw new InvalidOperationException("Death-screen confirm did not start a fresh run.");
        }

        DispatchSmokeAction(GameActions.Back);
        if (_screenState != ScreenState.Menu || _run is not null)
        {
            throw new InvalidOperationException("Post-death back did not return to the menu.");
        }
    }

    private static void ExecuteStepFeedbackSmokeTest()
    {
        var collisionFeedback = StepFeedback.Resolve(
        [
            new RunEventDetail(
                RunEventKind.PowerConsumed,
                Power: PowerKind.Shield),
            new RunEventDetail(
                RunEventKind.CollisionPrevented,
                Cause: DeathCause.SelfCollision,
                Power: PowerKind.Shield),
        ]);
        if (
            collisionFeedback.Cue != AudioCue.ShieldBreak
            || collisionFeedback.Caption != "SHIELD BROKE: COLLISION BLOCKED")
        {
            throw new InvalidOperationException("Shield collision feedback is not canonical.");
        }

        var activationFeedback = StepFeedback.Resolve(
        [
            new RunEventDetail(
                RunEventKind.PowerCollected,
                Power: PowerKind.Shield),
            new RunEventDetail(
                RunEventKind.PowerActivated,
                Power: PowerKind.Shield),
        ]);
        if (activationFeedback.Cue != AudioCue.ShieldActivate)
        {
            throw new InvalidOperationException("Shield activation feedback is not canonical.");
        }

        foreach (var kind in Enum.GetValues<PowerKind>())
        {
            var marker = PowerPresentation.Marker(kind);
            if (marker is < 'A' or > 'Z')
            {
                throw new InvalidOperationException($"Power marker missing for {kind}.");
            }

            var spawn = StepFeedback.Resolve(
            [
                new RunEventDetail(RunEventKind.PowerSpawned, Power: kind),
            ]);
            if (spawn.Cue is null || spawn.Caption is null
                || !spawn.Caption.Contains(
                    PowerPresentation.ShortName(kind),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Spawn feedback missing for {kind}.");
            }

            var activate = StepFeedback.Resolve(
            [
                new RunEventDetail(RunEventKind.PowerActivated, Power: kind),
            ]);
            if (activate.Cue is null || activate.Caption is null)
            {
                throw new InvalidOperationException($"Activation feedback missing for {kind}.");
            }
        }

        var lastStand = StepFeedback.Resolve(
        [
            new RunEventDetail(
                RunEventKind.PowerConsumed,
                Power: PowerKind.LastStand),
            new RunEventDetail(
                RunEventKind.CollisionPrevented,
                Cause: DeathCause.SelfCollision,
                Power: PowerKind.LastStand),
            new RunEventDetail(
                RunEventKind.PowerActivated,
                Power: PowerKind.LastStand),
        ]);
        if (
            lastStand.Cue != AudioCue.PowerRecovery
            || lastStand.Caption != "LAST STAND: DEATH REVERSED")
        {
            throw new InvalidOperationException("Last Stand recovery feedback is not canonical.");
        }

        var starvationWarning = StepFeedback.Resolve(
        [
            new RunEventDetail(RunEventKind.StarvationWarning, Value: 200),
        ]);
        if (
            starvationWarning.Cue != AudioCue.Pause
            || starvationWarning.Caption != "STARVATION WARNING")
        {
            throw new InvalidOperationException("Starvation warning feedback is not canonical.");
        }

        var accumulated = 0.0;
        var drained = RulesCadenceClock.DrainSteps(
            ref accumulated,
            deltaSeconds: 0.05,
            () => RulesCadenceClock.StepIntervalMilliseconds(1, 2));
        if (drained != 2 || accumulated != 0.0)
        {
            throw new InvalidOperationException("Boost cadence drain did not advance two steps.");
        }

        var slowAccumulated = 0.0;
        var slowDrain = RulesCadenceClock.DrainSteps(
            ref slowAccumulated,
            deltaSeconds: 0.05,
            () => RulesCadenceClock.StepIntervalMilliseconds(2, 1));
        if (slowDrain != 0 || slowAccumulated != 50.0)
        {
            throw new InvalidOperationException("Slow-Mo cadence drain did not hold a half-step.");
        }

        var status = PowerPresentation.DescribeStatus(
            new RunSnapshot(
                Tick: 1,
                Status: RunStatus.Running,
                DeathCause: DeathCause.None,
                Direction: RulesDirection.Right,
                Body: [new GridPoint(1, 1)],
                PendingDirections: [],
                Food: new GridPoint(2, 2),
                Score: 0,
                ComboCount: 0,
                ComboMultiplier: 1.0,
                TicksSinceLastFood: 0,
                HungerTicksRemaining: 100,
                PowerPickup: null,
                PowerSpawnTicksElapsed: 0,
                ShieldTicksRemaining: 40,
                PhaseShiftTicksRemaining: 20,
                LastStandHeld: true,
                LastStandRecoveryTicksRemaining: 0,
                SlowMoTicksRemaining: 10,
                BoostTicksRemaining: 0,
                MagnetTicksRemaining: 0,
                GluttonyTicksRemaining: 0,
                BaitPosition: null,
                DetachedObstacles: [],
                DetachedObstacleTicksRemaining: 0,
                StateHash: "smoke"));
        if (
            !status.Contains("SHIELD", StringComparison.Ordinal)
            || !status.Contains("PHASE", StringComparison.Ordinal)
            || !status.Contains("LAST STAND HELD", StringComparison.Ordinal)
            || !status.Contains("SLOW-MO", StringComparison.Ordinal)
            || !status.Contains("CADENCE 2/1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Composite power status is incomplete.");
        }
    }

    private void DispatchSmokeAction(string action)
    {
        using var inputEvent = new InputEventAction
        {
            Action = action,
            Pressed = true,
        };

        _Input(inputEvent);
    }
}
