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
        _window = GetWindow();
        _window.FilesDropped += OnFilesDropped;
        if (smokeTest)
        {
            ExecuteSmokeTest();
            return;
        }

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
        }

        GameActions.ReleaseRuntimeDefaults();
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent.IsActionPressed(GameActions.Quit))
        {
            RequestQuit();
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
        DrawRect(new Rect2(0.0f, 0.0f, 1280.0f, 720.0f), new Color(0.02f, 0.035f, 0.03f));
        DrawRect(new Rect2(0.0f, HudHeight, 1280.0f, 660.0f), new Color(0.055f, 0.12f, 0.085f));

        switch (_screenState)
        {
            case ScreenState.Menu:
                DrawLabel("VIBE SNAKE", new Vector2(42.0f, 190.0f), 52, new Color(0.45f, 1.0f, 0.68f));
                DrawLabel("Plan the route. Build the vibe. Recover with style.", new Vector2(46.0f, 238.0f), 24, Colors.White);
                DrawLabel("START RUN", new Vector2(46.0f, 300.0f), 22, new Color(0.75f, 0.85f, 0.8f));
                DrawLabel("Enter, Space, or Controller South", new Vector2(46.0f, 336.0f), 18, new Color(0.58f, 0.7f, 0.64f));
                DrawLabel("R or Controller North: verify latest replay", new Vector2(46.0f, 378.0f), 18, new Color(0.58f, 0.7f, 0.64f));
                DrawLabel("Drop one replay file here to verify without changing it", new Vector2(46.0f, 410.0f), 18, new Color(0.58f, 0.7f, 0.64f));
                if (_replayStatusCaption is not null)
                {
                    DrawLabel(_replayStatusCaption, new Vector2(46.0f, 458.0f), 16, new Color(0.46f, 0.94f, 0.96f));
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
        _cuePlayer?.PlayCue(cue);
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
            : SnakeRun.Create(_nextSeed++);
        _replayRecorder = new RunReplayRecorder(_run);
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
            ShowReplayStatus("REPLAY NOT SAVED: " + recording.Message);
            return;
        }

        var store = _replayStore;
        var replay = recording.Replay;
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
        var statusText = _pausedByFocusLoss
            ? "PAUSED: FOCUS LOST"
            : _paused
                ? "PAUSED"
                : snapshot.Status.ToString().ToUpperInvariant();
        DrawLabel(
            $"SCORE {snapshot.Score:D6}    COMBO {snapshot.ComboMultiplier:0.0}x    HUNGER {hungerSeconds:0.0}s    {statusText}",
            new Vector2(18.0f, 31.0f),
            20,
            Colors.White);

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
            DrawRect(new Rect2(290.0f, 260.0f, 700.0f, 170.0f), new Color(0.01f, 0.02f, 0.018f, 0.92f));
            var ending = snapshot.Status == RunStatus.Won ? "GRID COMPLETE" : snapshot.DeathCause.ToString().ToUpperInvariant();
            DrawLabel(ending, new Vector2(445.0f, 330.0f), 38, new Color(1.0f, 0.75f, 0.3f));
            DrawLabel("Confirm to coil again", new Vector2(465.0f, 380.0f), 21, Colors.White);
            DrawLabel("R or Controller North: verify latest replay", new Vector2(430.0f, 410.0f), 16, new Color(0.58f, 0.7f, 0.64f));
        }
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
            PlayCue(cue);
        }

        if (feedback.Caption is { } caption)
        {
            _feedbackCaption = caption;
            _feedbackTicksRemaining = FeedbackVisibilityTicks;
        }
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
        var recorder = new RunReplayRecorder(live, checkpointInterval: 1);
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
            || loaded.Replay.Serialize() != recording.Replay.Serialize())
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
        if (content.FileCount != 1 || content.ExportEligibleCount != 0)
        {
            throw new InvalidOperationException(
                "Content service smoke expected one blocked synthetic inventory asset.");
        }

        if (content.MayPackage(blockedPath))
        {
            throw new InvalidOperationException(
                "Content service must deny packaging of non-exportEligible assets.");
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
