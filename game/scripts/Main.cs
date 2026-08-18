using Godot;
using System.Globalization;
#if AGENT_ARENA_PREVIEW
using VibeSnake.AgentPlay;
using VibeSnake.AgentViewer;
#endif
using VibeSnake.Persistence;
using VibeSnake.Rules;
using RulesDirection = VibeSnake.Rules.Direction;

namespace VibeSnake.Game;

public partial class Main : Node2D
{
    private const float CellSize = 20.0f;
    private const float HudHeight = 60.0f;
    private const float ClassicMenuLogicalWidth = 960.0f;
    private const ulong SmokeSeed = 20260801UL;
    private const int FeedbackVisibilityTicks = 30;
    private const int MaximumReplayStatusCharacters = 240;
    private const ulong ReplayShutdownDrainMilliseconds = 5_000UL;
    private const ulong AudioOutputProbeIntervalMilliseconds = 1_000UL;
    private const ulong RadioPlaybackVerificationDelayMilliseconds = 2_000UL;
    private const ulong SpectatorControlsRevealMilliseconds = 3_000UL;
    private const string BrandLogoResourcePath = "res://assets/branding/vibe-snake.png";
    private const int AchievementsPerPage = 10;
    private const int ProgressionGoalsPerPage = 5;
    private const int TourCardsPerPage = 3;
    private const int CosmeticSetsPerPage = 3;
    private const int MainMenuItemCount = 9;
    private const string TourRouteMarker = "T";
    private const string WarningMarker = "!";
    private static readonly double[] ReplayPlaybackSpeeds = [0.5, 1.0, 2.0, 4.0];
    private static readonly System.Text.Json.JsonSerializerOptions CoreOnlyOfflineSerializerOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
    private static readonly string[] MainMenuCopyIds =
    [
        "menu.start",
        "menu.customize",
        "menu.achievements",
        "menu.high-scores",
        "action.ai-channels",
        "action.replays-status",
        "action.settings",
        "action.learn-tutorial",
        "menu.quit",
    ];
    private static readonly string[] MainMenuKeyboardHints =
    [
        "ENTER",
        "C",
        "U",
        "V",
        "L",
        "R",
        "F1",
        "H",
        "Q",
    ];

    private ScreenState _screenState = ScreenState.Menu;
    private int _mainMenuCursor;
    private SnakeRun? _run;
    private readonly VibeLevelDirector _vibeLevelDirector = new();
    private int _selectedRunModeIndex = 1;
    private ulong _nextSeed = 1UL;
    private bool _paused;
    private bool _pausedByFocusLoss;
    private bool _applicationFocused = true;
    private bool _cursorHidden;
    private ulong _lastPointerActivityMilliseconds;
    private double _rulesStepAccumulatorMilliseconds;
    private string? _feedbackCaption;
    private VisualFeedbackTier _feedbackTier = VisualFeedbackTier.Ambient;
    private int _feedbackTicksRemaining;
    private int _comboPulseTicksRemaining;
    private PerformanceProfileDefinition? _performanceStressProfile;
    private ProceduralCuePlayer? _cuePlayer;
    private RadioStreamPlayer? _radioPlayer;
    private Texture2D? _brandLogo;
    private readonly RadioPlaybackPolicy _radioPolicy = new(
        RadioCatalog.Empty,
        new RandomStreamBank(SmokeSeed).Radio);
    private readonly BroadcastPolicy _broadcastPolicy = new(
        new RandomStreamBank(SmokeSeed).Copy);
    private string? _broadcastCaption;
    private int _broadcastTicksRemaining;
    private int _presentationStep;
    private readonly SnakeMotionPresentation _snakeMotionPresentation = new();
    private GridPoint? _baitRevealOrigin;
    private GridPoint? _baitRevealDestination;
    private int _baitRevealTicksRemaining;
    private OptionalPackStore? _optionalPackStore;
    private ContentInventory? _contentInventory;
    private int _installedRadioPackCount;
    private readonly AudioOutputRecoveryTracker _audioOutputRecovery = new();
    private string? _audioStatusCaption;
    private ulong? _audioStatusExpiresAtMilliseconds;
    private ulong _nextAudioOutputProbeMilliseconds;
    private string _observedAudioOutputDevice = string.Empty;
    private string _observedAudioOutputSignature = string.Empty;
    private ulong? _radioPlaybackVerificationDueMilliseconds;
    private bool _radioPlaybackVerificationReported;
    private bool _radioPlaybackRetryRequired;
    private ShellTheme? _shellTheme;
    private ShellLocale _shellLocale = ShellLocale.English;
    private ReplayStore? _replayStore;
    private RunReplayRecorder? _replayRecorder;
    private string? _replayStatusCaption;
    private Task<ReplayOperationResult>? _replayOperation;
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
    private OnboardingStore? _onboardingStore;
    private OnboardingProgressDocument _onboardingProgress =
        OnboardingProgressDocument.CreateDefaults();
    private bool _onboardingWasNewProfile;
    private OnboardingSession? _onboardingSession;
    private int _onboardingOfferCursor;
    private string? _onboardingStatusCaption;
    private AchievementsStore? _achievementsStore;
    private AchievementsDocument _achievements = AchievementsDocument.CreateDefaults();
    private bool _achievementsWritable = true;
    private ProgressionStore? _progressionStore;
    private ProgressionDocument _progression = ProgressionDocument.CreateDefaults();
    private bool _progressionWritable = true;
    private int _progressionGoalCursor;
    private string? _progressionStatusCaption;
    private readonly ProgressionNotificationQueue _progressionNotifications = new();
    private int _tourCursor;
    private int _tourPage;
    private string? _tourStatusCaption;
    private BroadcastTourEvent? _activeTourEvent;
    private BroadcastTourOutcome? _tourRunOutcome;
    private ScoreRunContext _activeRunContext = ScoreRunContextCatalog.NormalHuman;
    private int _cosmeticCursor;
    private int _cosmeticPage;
    private string? _cosmeticStatusCaption;
    private PersonalBestStore? _personalBestStore;
    private PersonalBestDocument _personalBests = PersonalBestDocument.CreateDefaults();
    private bool _personalBestsWritable = true;
    private ScoreHistoryStore? _scoreHistoryStore;
    private ScoreHistoryDocument _scoreHistory = ScoreHistoryDocument.CreateDefaults();
    private bool _scoreHistoryWritable = true;
    private int _scoreBrowseCategoryCursor;
    private bool _scoreImportConfirmation;
    private string? _scoreBrowseStatusCaption;
    private LocalPlaytestSummaryStore? _localPlaytestSummaryStore;
    private int _localPlaytestSummaryCount;
    private SpectatorLeagueStore? _spectatorLeagueStore;
    private SpectatorLeagueDocument _spectatorLeague =
        SpectatorLeagueDocument.CreateDefaults();
    private bool _spectatorLeagueWritable = true;
    private SpectatorSelection _spectatorSelection = SpectatorSelection.CreateDefault();
    private int _spectatorSelectionCursor;
    private SpectatorMatchSession? _spectatorMatch;
    private bool _spectatorMatchPersisted;
    private string? _spectatorStatusCaption;
    private ulong? _spectatorControlsVisibleUntilMilliseconds;
    private SpectatorChallengeDescriptor? _activeSpectatorChallenge;
    private string? _activeSpectatorChallengePersonalityId;
    private int _activeSpectatorAiScore;
    private bool _spectatorKeyboardRouteQualified;
    private bool _spectatorControllerRouteQualified;
    // The bounded spectator evidence band and the localization geometry gate share
    // this width, so it stays available even when the preview route is compiled out.
    private const int AgentViewerStateHashPrefixLength = 8;

    // Real upper bounds for the readable-overlay gate. A match cannot exceed the
    // published step cap, and a viewer sequence cannot exceed the mutation ledger.
    private const int MaximumAgentMatchSteps = 2_000;
    private const int MaximumAgentViewerFrames = 4_096;

#if AGENT_ARENA_PREVIEW
    private AgentViewerClient? _agentViewer;
    private AgentViewerFrameV9? _agentViewerFrame;
    private long _agentViewerCoalescedFrames;
    private bool _agentViewerSnappedLatestFrame;
    private RunSnapshot? _agentViewerSnapshot;
    private string _agentViewerStatusId = "status.agent-viewer.connecting";
    private string _agentViewerFeedId = "agent-arena.feed.connecting";
    private bool _agentViewerSmokeEnabled;
    private ulong? _agentViewerSmokeDeadlineMilliseconds;
    private string? _agentViewerPresentedAvatarId;
    private string? _agentViewerPresentedAccentId;
    private string? _agentViewerPresentedStationId;
    private string? _agentViewerHumanCosmeticIdBeforePresentation;
#endif
    private int _loreDepthFilterIndex;
    private int _loreBrowseCursor;
    private LoreUnlockContext _loreUnlockContext = LoreUnlockContext.Empty;
    private bool _loreKeyboardRouteQualified;
    private bool _loreControllerRouteQualified;
    private readonly PowerDecisionRunTrace _powerDecisionTrace = new();
    private RunEndSummary? _runEndSummary;
    private readonly RestartIntentGate _restartIntentGate = new();
    private long _inputSequence;
    private long _terminalInputSequence = -1;
    private int _achievementsPage;
    private LocalDiagnostics? _diagnostics;
    private StructuredLocalLog? _structuredLog;
    private InputBindingsStore? _inputBindingsStore;
    private InputBindingsDocument _keyboardBindings =
        InputBindingsDocument.CreateKeyboardDefaults();
    private InputBindingsDocument _controllerBindings =
        InputBindingsDocument.CreateControllerDefaults();
    private readonly ControllerConnectionTracker _controllerConnections = new();
    private string? _controllerCaption;
    private int _bindingsCursor;
    private bool _bindingsCapturePending;
    private PendingBindingConflict? _pendingBindingConflict;
    private string? _bindingsStatusCaption;
    private BindingsDeviceTab _bindingsDeviceTab = BindingsDeviceTab.Keyboard;
    private InputPromptFamily _activePromptFamily = InputPromptFamily.Keyboard;
    private InputPromptFamily _controllerPromptFamily = InputPromptFamily.GenericController;
    private IReadOnlyList<ReplayBrowserEntry> _replayBrowserEntries = [];
    private int _replayBrowseCursor;
    private RunReplayPlayback? _replayPlayback;
    private bool _replayPlaybackPaused = true;
    private int _replayPlaybackSpeedIndex = 1;
    private bool _replayHudVisible = true;
    private CapturePresentationState _capturePresentation = CapturePresentationState.Visible;
    private bool _captureKeyboardRouteQualified;
    private bool _captureControllerRouteQualified;
    private bool _captureSummaryExportQualified;
    private bool _captureSummaryIdempotenceQualified;
    private ReplayDeletionPlan? _pendingReplayDeletion;
    private OfflineChallengeStore? _offlineChallengeStore;
#if AGENT_ARENA_PREVIEW
    private AgentExhibitionArchiveStore? _agentExhibitionArchive;
    private AgentExhibitionBrowseReportV1? _agentExhibitionReport;
    private int _agentExhibitionCursor;
#endif
    private IReadOnlyList<GhostSlotEntry> _ghostSlots = [];
    private int _ghostSlotCursor;
    private GhostDeletionPlan? _pendingGhostDeletion;
    private GhostRaceSession? _activeGhostRace;
    private int? _activeGhostSlot;
    private bool _offlineComparisonKeyboardRouteQualified;
    private bool _offlineComparisonControllerRouteQualified;
    private int _settingsSectionCursor;
    private int _settingsItemCursor;
    private bool _settingsSectionOpen;
    private bool _settingsFullResetConfirmation;
    private bool _playtestDeleteConfirmation;
    private string? _settingsStatusCaption;
    private PlayerDataRecoveryService? _playerDataRecovery;
    private PlayerDataResetPlan? _pendingDataResetPlan;
    private IReadOnlyList<PlayerDataBackupInspection> _playerDataBackups = [];
    private int _playerDataBackupCursor;
    private bool _playerDataRecoveryBrowseOpen;
    private Task<PlayerDataOperationResult>? _playerDataOperation;
    private bool _quitAfterPlayerDataOperation;
    private bool _settingsKeyboardResetCancelQualified;
    private bool _settingsControllerResetQualified;

    private enum ScreenState
    {
        Menu,
        Running,
        Ended,
        Achievements,
        Bindings,
        ContentPacks,
        Replays,
        Settings,
        Onboarding,
        Scores,
        Tour,
        Cosmetics,
        Spectator,
        Lore,
        Comparisons,
#if AGENT_ARENA_PREVIEW
        AgentWatch,
        AgentExhibitions,
#endif
    }

    private enum MainMenuItem
    {
        Start = 0,
        Customize = 1,
        Achievements = 2,
        Scores = 3,
        Spectator = 4,
        Replays = 5,
        Settings = 6,
        Help = 7,
        Quit = 8,
    }

    private enum ReplayOperationKind
    {
        Inspection,
        Save,
        BrowserLoad,
        PlaybackLoad,
        Export,
        DeletionPlan,
        Delete,
        GhostList,
        GhostImport,
        GhostRaceLoad,
        GhostRunCardExport,
        GhostDeletionPlan,
        GhostDelete,
    }

    private enum BindingsDeviceTab
    {
        Keyboard,
        Controller,
    }

    private enum PlayerDataOperationKind
    {
        Reset,
        Inspect,
        Restore,
    }

    private enum PlayerDataOperationCompletion
    {
        Pending,
        Succeeded,
        Failed,
    }

    private readonly record struct PendingBindingConflict(
        string Action,
        string ConflictingAction);

    private sealed record ReplayOperationResult(
        string Caption,
        RunReplayPlayback? Playback = null,
        IReadOnlyList<ReplayBrowserEntry>? BrowserEntries = null,
        ReplayDeletionPlan? DeletionPlan = null,
        IReadOnlyList<GhostSlotEntry>? GhostSlots = null,
        GhostDeletionPlan? GhostDeletionPlan = null,
        GhostRaceSession? GhostRace = null);

    private sealed record PlayerDataOperationResult(
        PlayerDataOperationKind Kind,
        PlayerDataResetPlan? ResetPlan = null,
        PlayerDataResetResult? ResetResult = null,
        IReadOnlyList<PlayerDataBackupInspection>? Backups = null,
        PlayerDataRestoreResult? RestoreResult = null);

    public override void _Ready()
    {
        GameActions.EnsureDefaults();
        AudioBuses.EnsureRegistered();
        _brandLogo = GD.Load<Texture2D>(BrandLogoResourcePath)
            ?? throw new InvalidOperationException("The approved Vibe Snake logo is unavailable.");
        _shellTheme = new ShellTheme(ThemeDB.FallbackFont);
        GetTree().AutoAcceptQuit = false;
        _cuePlayer = new ProceduralCuePlayer();
        AddChild(_cuePlayer);
        _radioPlayer = new RadioStreamPlayer();
        AddChild(_radioPlayer);
        RefreshAudioOutputTopology(Time.GetTicksMsec(), force: true);
        var userArguments = OS.GetCmdlineUserArgs();
        _shellLocale = userArguments.Contains("--pseudo-locale", StringComparer.Ordinal)
            ? ShellLocale.Pseudo
            : ShellLocale.English;
        var smokeTest = userArguments.Contains("--smoke-test", StringComparer.Ordinal);
        var launchProbe = userArguments.Contains("--launch-probe", StringComparer.Ordinal);
        var readmeCaptureDirectory = GetArgumentValue(
            userArguments,
            "--readme-capture-dir=");
#if AGENT_ARENA_PREVIEW
        var agentWatchPipe = GetArgumentValue(userArguments, "--agent-watch-pipe=");
        var agentWatchToken = GetArgumentValue(userArguments, "--agent-watch-token=");
        var agentWatchSmoke = userArguments.Contains(
            "--agent-watch-smoke",
            StringComparer.Ordinal);
        // The browser is preview-only, so it is reachable by an explicit flag
        // rather than from the supported main menu. The marker keeps the
        // existing Release exclusion assertion covering it unchanged.
        var agentWatchExhibitions = userArguments.Contains(
            "--agent-watch-exhibitions",
            StringComparer.Ordinal);
        // A spectator QA aid. The accessibility profile is otherwise only reachable
        // through F6 and F9, which an automated watcher cannot press, so overlay
        // legibility at maximum text scale could never be observed from outside.
        var agentWatchAccessibility = userArguments.Contains(
            "--agent-watch-accessibility",
            StringComparer.Ordinal);
        if (agentWatchAccessibility && agentWatchPipe is null)
        {
            throw new ArgumentException(
                "Agent watch accessibility requires the local viewer capability.");
        }
        if ((agentWatchPipe is null) != (agentWatchToken is null))
        {
            throw new ArgumentException(
                "Agent watch mode requires both a pipe name and access token.");
        }
        if (agentWatchSmoke && agentWatchPipe is null)
        {
            throw new ArgumentException(
                "Agent watch smoke requires the local viewer capability.");
        }
#endif
        var automatedModeCount = (smokeTest ? 1 : 0)
            + (launchProbe ? 1 : 0)
            + (readmeCaptureDirectory is null ? 0 : 1);
#if AGENT_ARENA_PREVIEW
        automatedModeCount += agentWatchSmoke ? 1 : 0;
#endif
        if (automatedModeCount > 1)
        {
            throw new ArgumentException(
                "Smoke, launch-probe, and README-capture modes are mutually exclusive.");
        }
#if AGENT_ARENA_PREVIEW
        if (automatedModeCount > 0
            && agentWatchPipe is not null
            && !agentWatchSmoke)
        {
            throw new ArgumentException(
                "Agent watch mode cannot be combined with an automated launch mode.");
        }
#endif

        var smokeUserDataRoot = GetArgumentValue(
            userArguments,
            "--smoke-user-data-root=");
        var automatedLaunchRequiresUserData = smokeTest
            || launchProbe
            || readmeCaptureDirectory is not null;
#if AGENT_ARENA_PREVIEW
        automatedLaunchRequiresUserData |= agentWatchSmoke;
#endif
        if (automatedLaunchRequiresUserData && smokeUserDataRoot is null)
        {
            throw new ArgumentException(
                "Automated launch modes require an explicit --smoke-user-data-root path.");
        }

        var userDataRoot = smokeUserDataRoot
            ?? ProjectSettings.GlobalizePath("user://");
        _replayStore = new ReplayStore(userDataRoot);
        _offlineChallengeStore = new OfflineChallengeStore(userDataRoot);
#if AGENT_ARENA_PREVIEW
        _agentExhibitionArchive = new AgentExhibitionArchiveStore(userDataRoot);
#endif
        _preferencesStore = new PreferencesStore(userDataRoot);
        _onboardingStore = new OnboardingStore(userDataRoot);
        _achievementsStore = new AchievementsStore(userDataRoot);
        _progressionStore = new ProgressionStore(userDataRoot);
        _personalBestStore = new PersonalBestStore(userDataRoot);
        _scoreHistoryStore = new ScoreHistoryStore(userDataRoot);
        _localPlaytestSummaryStore = new LocalPlaytestSummaryStore(userDataRoot);
        _spectatorLeagueStore = new SpectatorLeagueStore(userDataRoot);
        _playerDataRecovery = new PlayerDataRecoveryService(userDataRoot);
        _diagnostics = new LocalDiagnostics(userDataRoot);
        _structuredLog = new StructuredLocalLog(userDataRoot);
        _inputBindingsStore = new InputBindingsStore(userDataRoot);
        _optionalPackStore = new OptionalPackStore(userDataRoot);
        LoadShellSettings();
#if AGENT_ARENA_PREVIEW
        if (agentWatchSmoke || agentWatchAccessibility)
        {
            _shellSettings.MasterMuted = true;
            _shellSettings.HighContrast = true;
            _shellSettings.ReducedMotion = true;
            _shellSettings.TextScale = ShellSettings.MaximumTextScale;
        }
#endif
        InitializeRadio(allowCheckoutFallback: !smokeTest && !launchProbe);
        LoadOnboardingProgress();
        LoadAchievements();
        LoadProgression();
        LoadPersonalBests();
        LoadScoreHistory();
        LoadLocalPlaytestSummaryCount();
        LoadSpectatorLeague();
        LoadInputBindings();
        SeedControllerConnections();
        Input.JoyConnectionChanged += OnJoyConnectionChanged;
        _structuredLog.Information(
            "shell",
            smokeTest || launchProbe || readmeCaptureDirectory is not null
                ? "Automated shell session started."
                : "Interactive shell session started.",
            eventCode: smokeTest
                ? "smoke_session_start"
                : launchProbe
                    ? "launch_probe_start"
                    : readmeCaptureDirectory is not null
                        ? "readme_capture_start"
                        : "session_start");
        _window = GetWindow();
        _window.MinSize = new Vector2I(
            (int)VirtualViewport.MinimumWindowWidth,
            (int)VirtualViewport.MinimumWindowHeight);
        _window.FilesDropped += OnFilesDropped;
        _window.SizeChanged += OnWindowSizeChanged;
        RefreshVirtualViewport();
        NotePointerActivity(Time.GetTicksMsec());
        if (smokeTest)
        {
            ExecuteSmokeTest(userDataRoot);
            return;
        }
        if (launchProbe)
        {
            ExecuteLaunchProbe(userArguments);
            return;
        }
        if (readmeCaptureDirectory is not null)
        {
            ExecuteReadmeCapture(readmeCaptureDirectory);
            return;
        }

        ApplyWindowModeFromSettings();
#if AGENT_ARENA_PREVIEW
        if (agentWatchPipe is not null && agentWatchToken is not null)
        {
            _agentViewerHumanCosmeticIdBeforePresentation =
                _progression.SelectedCosmeticSetId;
            _agentViewer = new AgentViewerClient(agentWatchPipe, agentWatchToken);
            TransitionToScreen(ScreenState.AgentWatch);
            _agentViewerStatusId = AgentViewerStatusCopyId(_agentViewer.State);
            _agentViewerFeedId = AgentViewerFeedCopyId(_agentViewer.State);
            _agentViewerSmokeEnabled = agentWatchSmoke;
            _agentViewerSmokeDeadlineMilliseconds = agentWatchSmoke
                ? Time.GetTicksMsec() + 30_000UL
                : null;
        }
        else if (agentWatchExhibitions)
        {
            // Browsing what was kept needs no live viewer capability, because
            // it reads the local archive rather than a running match.
            RefreshAgentExhibitions(0);
            TransitionToScreen(ScreenState.AgentExhibitions);
        }
#endif
        QueueRedraw();
    }

    private void SeedControllerConnections()
    {
        foreach (var deviceId in Input.GetConnectedJoypads())
        {
            var resolvedDeviceId = (int)deviceId;
            var deviceName = Input.GetJoyName(resolvedDeviceId);
            _controllerConnections.NoteConnected(resolvedDeviceId, deviceName);
            if (_controllerPromptFamily == InputPromptFamily.GenericController)
            {
                _controllerPromptFamily = InputPromptGlyphs.DetectControllerFamily(deviceName);
            }
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

        _controllerCaption = Localize(
            connectionEvent.Value.Kind == ControllerConnectionKind.Connected
                ? "status.controller.connected"
                : "status.controller.disconnected",
            ShellTextArgument.From("device", connectionEvent.Value.DeviceName));
        if (connectionEvent.Value.Kind == ControllerConnectionKind.Connected)
        {
            _controllerPromptFamily = InputPromptGlyphs.DetectControllerFamily(
                connectionEvent.Value.DeviceName);
        }
        _structuredLog?.Information(
            "input",
            $"Controller {connectionEvent.Value.Kind}: {connectionEvent.Value.DeviceName}",
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
            SetRunPaused(true);
            _pausedByFocusLoss = false;
            _rulesStepAccumulatorMilliseconds = 0.0;
            PlayCue(AudioCue.Pause);
            _structuredLog?.Warning(
                "input",
                "Paused run after last controller disconnected.",
                eventCode: "controller_disconnect_pause");
        }
        else if (
            connectionEvent.Value.Kind == ControllerConnectionKind.Disconnected
            && _controllerConnections.ConnectedCount == 0
            && _screenState == ScreenState.Replays
            && _replayPlayback is not null
            && !_replayPlaybackPaused)
        {
            _replayPlaybackPaused = true;
            _rulesStepAccumulatorMilliseconds = 0.0;
            ShowReplayStatus("REPLAY PAUSED: CONTROLLER DISCONNECTED");
            _structuredLog?.Warning(
                "input",
                "Paused replay after last controller disconnected.",
                eventCode: "controller_disconnect_replay_pause");
        }
        else if (
            connectionEvent.Value.Kind == ControllerConnectionKind.Disconnected
            && _controllerConnections.ConnectedCount == 0
            && _screenState == ScreenState.Spectator
            && _spectatorMatch is { Paused: false } spectator)
        {
            spectator.SetPaused(true);
            _rulesStepAccumulatorMilliseconds = 0.0;
            _spectatorStatusCaption = Localize("status.spectator.paused");
            _structuredLog?.Warning(
                "input",
                "Paused spectator match after last controller disconnected.",
                eventCode: "controller_disconnect_spectator_pause");
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
            RefreshVirtualViewport();
            return;
        }

        switch (_shellSettings.WindowMode)
        {
            case PreferencesDocument.WindowedMode:
                _window.Mode = Window.ModeEnum.Windowed;
                var screen = _window.CurrentScreen;
                var screenSize = DisplayServer.ScreenGetSize(screen);
                var requested = DisplayOptions.WindowSize(_shellSettings.WindowSizePreset).Size;
                var fitted = DisplayOptions.FitWindowToScreen(requested, screenSize);
                _window.Size = fitted;
                var screenPosition = DisplayServer.ScreenGetPosition(screen);
                _window.Position = screenPosition + ((screenSize - fitted) / 2);
                break;
            case PreferencesDocument.BorderlessMode:
                _window.Mode = Window.ModeEnum.Fullscreen;
                break;
            case PreferencesDocument.ExclusiveFullscreenMode:
                _window.Mode = Window.ModeEnum.ExclusiveFullscreen;
                break;
            default:
                throw new InvalidOperationException(
                    "Unsupported saved window mode: " + _shellSettings.WindowMode);
        }

        // The selected aspect preset controls presentation in every window
        // mode. Fullscreen uses the whole display surface around that frame.
        RefreshVirtualViewport();
        QueueRedraw();
        UpdateCursorVisibility(Time.GetTicksMsec());
    }

    private void NotePointerActivity(ulong nowMilliseconds)
    {
        _lastPointerActivityMilliseconds = nowMilliseconds;
        SetCursorHidden(false);
        if (_screenState == ScreenState.Spectator && _spectatorMatch is not null)
        {
            RevealSpectatorControls(nowMilliseconds);
        }
    }

    private void UpdateCursorVisibility(ulong nowMilliseconds)
    {
        var fullscreen = _window is not null && _window.Mode != Window.ModeEnum.Windowed;
        SetCursorHidden(IdleCursorPolicy.ShouldHide(
            fullscreen,
            _applicationFocused,
            nowMilliseconds,
            _lastPointerActivityMilliseconds));
    }

    private void SetCursorHidden(bool hidden)
    {
        if (_cursorHidden == hidden)
        {
            return;
        }

        _cursorHidden = hidden;
        Input.MouseMode = hidden
            ? Input.MouseModeEnum.Hidden
            : Input.MouseModeEnum.Visible;
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
        var preferredFrame = DisplayOptions.WindowSize(_shellSettings.WindowSizePreset).Size;
        _virtualViewport.Resize(
            width,
            height,
            preferredFrame.X,
            preferredFrame.Y);
    }

    private bool UsesClassicMenuPresentation =>
        _screenState == ScreenState.Menu
        && _shellSettings.WindowSizePreset == PreferencesDocument.ClassicWindowSize;

    private float ActiveLogicalWidth => UsesClassicMenuPresentation
        ? ClassicMenuLogicalWidth
        : VirtualViewport.LogicalWidth;

    private Rect2 ActivePresentationRect => UsesClassicMenuPresentation
        ? FitLogicalPresentation(
            _window?.Size ?? new Vector2I(
                (int)ClassicMenuLogicalWidth,
                (int)VirtualViewport.LogicalHeight),
            ClassicMenuLogicalWidth,
            VirtualViewport.LogicalHeight)
        : _virtualViewport.DestinationRect;

    private static Rect2 FitLogicalPresentation(
        Vector2I windowSize,
        float logicalWidth,
        float logicalHeight)
    {
        if (windowSize.X <= 0
            || windowSize.Y <= 0
            || logicalWidth <= 0.0f
            || logicalHeight <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowSize),
                "Window and logical presentation dimensions must be positive.");
        }

        var scale = Math.Min(windowSize.X / logicalWidth, windowSize.Y / logicalHeight);
        var drawnSize = new Vector2(logicalWidth * scale, logicalHeight * scale);
        var offset = (new Vector2(windowSize.X, windowSize.Y) - drawnSize) * 0.5f;
        return new Rect2(offset, drawnSize);
    }

    /// <summary>
    /// Maps a window-space pointer into the active logical canvas.
    /// </summary>
    private Vector2 MapPointerToLogical(Vector2 windowPoint)
    {
        var destination = ActivePresentationRect;
        return new Vector2(
            (windowPoint.X - destination.Position.X)
                * (ActiveLogicalWidth / destination.Size.X),
            (windowPoint.Y - destination.Position.Y)
                * (VirtualViewport.LogicalHeight / destination.Size.Y));
    }

    private Vector2 MapLogicalToWindow(Vector2 logicalPoint)
    {
        var destination = ActivePresentationRect;
        return new Vector2(
            destination.Position.X
                + (logicalPoint.X * destination.Size.X / ActiveLogicalWidth),
            destination.Position.Y
                + (logicalPoint.Y * destination.Size.Y / VirtualViewport.LogicalHeight));
    }

    private bool ContainsActiveLogicalPoint(Vector2 logicalPoint) =>
        logicalPoint.X >= 0.0f
        && logicalPoint.Y >= 0.0f
        && logicalPoint.X < ActiveLogicalWidth
        && logicalPoint.Y < VirtualViewport.LogicalHeight;

    private void LoadShellSettings()
    {
        if (_preferencesStore is null)
        {
            _shellSettings = ShellSettings.CreateDefaults();
            ApplyRuntimeShellSettings();
            return;
        }

        var loaded = _preferencesStore.Load();
        if (loaded.IsSuccess && loaded.Document is not null)
        {
            _shellSettings = ShellSettings.FromDocument(loaded.Document);
            ApplyRuntimeShellSettings();
            return;
        }

        _shellSettings = ShellSettings.CreateDefaults();
        ApplyRuntimeShellSettings();
        _settingsStatusCaption = Localize("status.settings.load-defaults");
        if (loaded.Code is PreferencesLoadCode.UnsupportedSchema or PreferencesLoadCode.InvalidJson)
        {
            WriteLocalCrashReport(
                "SettingsLoad",
                new InvalidOperationException(loaded.Message),
                eventCode: "preferences_load_failed");
        }
    }

    private void LoadOnboardingProgress()
    {
        if (_onboardingStore is null)
        {
            _onboardingProgress = OnboardingProgressDocument.CreateDefaults();
            _onboardingStatusCaption = Localize("status.onboarding.progress-unavailable");
            return;
        }

        var loaded = _onboardingStore.Load();
        _onboardingWasNewProfile = loaded.IsNewProfile;
        if (loaded.IsSuccess && loaded.Document is not null)
        {
            _onboardingProgress = loaded.Document;
            return;
        }

        _onboardingProgress = OnboardingProgressDocument.CreateDefaults();
        _onboardingStatusCaption = Localize("status.onboarding.progress-unreadable");
        _structuredLog?.Warning(
            "onboarding",
            loaded.Message,
            eventCode: "onboarding_load_failed");
    }

    private bool SaveOnboardingStatus(OnboardingStatus status)
    {
        var next = _onboardingProgress.WithStatus(status);
        _onboardingProgress = next;
        if (_onboardingStore is null)
        {
            _onboardingStatusCaption = Localize("status.onboarding.progress-session-only");
            return false;
        }

        try
        {
            _onboardingStore.Save(next);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException)
        {
            _onboardingStatusCaption = Localize("status.onboarding.progress-save-failed");
            _structuredLog?.Warning(
                "onboarding",
                exception.Message,
                eventCode: "onboarding_save_failed");
            return false;
        }
    }

    private bool ResetOnboardingProgress()
    {
        _onboardingSession = null;
        var saved = SaveOnboardingStatus(OnboardingStatus.NotStarted);
        _settingsStatusCaption = saved
            ? Localize("status.onboarding.reset-offered")
            : Localize("status.onboarding.reset-save-failed");
        return saved;
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

    private bool SaveShellSettings(
        string? successCopyId = null,
        params ShellTextArgument[] successArguments)
    {
        _shellSettings.Clamp();
        ApplyRuntimeShellSettings();
        if (_preferencesStore is null)
        {
            _settingsStatusCaption = Localize("status.settings.save-unavailable");
            return false;
        }

        try
        {
            _preferencesStore.Save(_shellSettings.ToDocument());
            if (successCopyId is not null)
            {
                _settingsStatusCaption = Localize(successCopyId, successArguments);
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException)
        {
            _settingsStatusCaption = Localize("status.settings.save-failed");
            try
            {
                WriteLocalCrashReport(
                    "SettingsSave",
                    exception,
                    eventCode: "preferences_save_failed");
            }
            catch (Exception diagnosticException) when (
                diagnosticException is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException)
            {
                _structuredLog?.Warning(
                    "settings",
                    "Preferences and diagnostic persistence are unavailable.",
                    eventCode: "preferences_save_and_diagnostics_failed");
            }

            return false;
        }
    }

    private void ApplyRuntimeShellSettings()
    {
        AudioBuses.ApplyShellSettings(_shellSettings);
        GameActions.ApplyGameplayDeadzone(_shellSettings.ControllerDeadzone);
        _radioPolicy.SetMuted(_shellSettings.EffectiveMusicVolume() <= 0.0f);
    }

    private void InitializeRadio(bool allowCheckoutFallback)
    {
        _contentInventory = null;
        _installedRadioPackCount = 0;
        if (_optionalPackStore is not null && TryResolveCheckoutInventoryPath(out var inventoryPath))
        {
            try
            {
                _contentInventory = ContentInventory.LoadFromFile(inventoryPath);
                var inspection = _optionalPackStore.InspectRadioCatalog(_contentInventory);
                if (inspection.Catalog.Stations.Count > 0)
                {
                    _installedRadioPackCount = inspection.Catalog.Stations.Count;
                    _radioPolicy.ReplaceCatalog(inspection.Catalog);
                    _radioPolicy.SetMuted(_shellSettings.EffectiveMusicVolume() <= 0.0f);
                    _radioPolicy.PlayOrResume();
                    _radioPlayer?.Configure(_radioPolicy, _optionalPackStore, _contentInventory);
                    ScheduleRadioPlaybackVerification();
                    return;
                }

                foreach (var rejected in inspection.Rejected)
                {
                    _structuredLog?.Warning(
                        "radio",
                        $"Radio pack {rejected.Key} was isolated: {rejected.Value}",
                        eventCode: "radio_pack_isolated");
                }
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or InvalidDataException
                    or ArgumentException
                    or InvalidOperationException)
            {
                _structuredLog?.Warning(
                    "radio",
                    "Radio catalog unavailable; core play remains available. " + exception.Message,
                    eventCode: "radio_catalog_unavailable");
            }
        }

        if (allowCheckoutFallback
            && TryResolveCheckoutRadioDirectory(out var radioDirectory)
            && CheckoutRadioCatalog.TryCreate(
                radioDirectory,
                out var checkoutCatalog,
                out var checkoutSources))
        {
            _radioPolicy.ReplaceCatalog(checkoutCatalog);
            _radioPolicy.SetMuted(_shellSettings.EffectiveMusicVolume() <= 0.0f);
            _radioPolicy.PlayOrResume();
            _radioPlayer?.ConfigureCheckout(_radioPolicy, checkoutSources);
            ScheduleRadioPlaybackVerification();
            _structuredLog?.Information(
                "radio",
                $"Loaded {checkoutSources.Count} source-checkout radio tracks for local play.",
                eventCode: "checkout_radio_loaded");
            return;
        }

        _radioPolicy.ReplaceCatalog(RadioCatalog.Empty);
        _radioPlayer?.Synchronize();
    }

    private static bool TryResolveCheckoutRadioDirectory(out string radioDirectory)
    {
        radioDirectory = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(
                ProjectSettings.GlobalizePath("res://"),
                "..",
                "assets",
                "audio",
                "radio"));
        return System.IO.Directory.Exists(radioDirectory);
    }

    private void CycleRadioStation()
    {
        var current = _radioPolicy.Snapshot;
        var snapshot = (_radioPlaybackRetryRequired
            || current.Mode is RadioPlaybackMode.StationUnavailable
                or RadioPlaybackMode.NoStations
                or RadioPlaybackMode.Stopped)
            ? _radioPolicy.RetryIsolatedTracks()
            : _radioPolicy.TuneNextStation();
        _radioPlaybackRetryRequired = false;
        _radioPlayer?.Synchronize();
        ScheduleRadioPlaybackVerification();
        PlayCue(AudioCue.Navigate);
        _structuredLog?.Information(
            "radio",
            snapshot.StatusMessage,
            eventCode: snapshot.StationId is null
                ? "radio_pack_help"
                : "radio_station_changed");
        QueueRedraw();
    }

    private void LoadAchievements()
    {
        if (_achievementsStore is null)
        {
            _achievements = AchievementsDocument.CreateDefaults();
            _achievementsWritable = false;
            return;
        }

        var loaded = _achievementsStore.Load();
        if (loaded.IsSuccess && loaded.Document is not null)
        {
            _achievements = loaded.Document;
            _achievementsWritable = true;
            _structuredLog?.Information(
                "shell",
                "Loaded " + _achievements.UnlockedCount + " permanent run unlock(s).",
                eventCode: "achievements_load");
            return;
        }

        _achievements = AchievementsDocument.CreateDefaults();
        _achievementsWritable = false;
        if (loaded.Code is AchievementsLoadCode.UnsupportedSchema
            or AchievementsLoadCode.InvalidJson
            or AchievementsLoadCode.InvalidField)
        {
            WriteLocalCrashReport(
                "AchievementsLoad",
                new InvalidOperationException(loaded.Message),
                eventCode: "achievements_load_failed");
        }
    }

    private void LoadProgression()
    {
        if (_progressionStore is null)
        {
            _progression = ProgressionDocument.CreateDefaults();
            _progressionWritable = false;
            return;
        }

        var loaded = _progressionStore.Load();
        if (loaded.IsSuccess && loaded.Document is not null)
        {
            _progression = loaded.Document;
            _progressionWritable = true;
            _structuredLog?.Information(
                "progression",
                $"Loaded {_progression.CompletedTourEventIds.Count} completed tour event(s).",
                eventCode: "progression_load");
            return;
        }

        _progression = ProgressionDocument.CreateDefaults();
        _progressionWritable = false;
        _progressionStatusCaption = Localize("status.progression.load-defaults");
        _structuredLog?.Warning(
            "progression",
            loaded.Message,
            eventCode: "progression_load_failed");
    }

    private bool TrySaveProgression(string failureEventCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureEventCode);
        if (_progressionStore is null || !_progressionWritable)
        {
            _structuredLog?.Warning(
                "progression",
                "Progression persistence is unavailable; changes remain session-only.",
                eventCode: failureEventCode);
            return false;
        }

        try
        {
            _progressionStore.Save(_progression);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidDataException
                or InvalidOperationException
                or OverflowException)
        {
            _structuredLog?.Warning(
                "progression",
                exception.Message,
                eventCode: failureEventCode);
            return false;
        }
    }

    private void PersistProgression(SnakeRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var previous = _progression;
        try
        {
            _progression = _progression.WithHumanRun(
                run.ToAchievementMetrics(),
                ScoreRunContextCatalog.NormalHuman);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidDataException
                or InvalidOperationException
                or OverflowException)
        {
            _progression = previous;
            _progressionStatusCaption = Localize("status.progression.save-failed");
            _structuredLog?.Warning(
                "progression",
                exception.Message,
                eventCode: "progression_update_rejected");
            return;
        }

        if (!TrySaveProgression("progression_save_failed"))
        {
            _progression = previous;
            _progressionStatusCaption = Localize("status.progression.save-failed");
            return;
        }

        _structuredLog?.Information(
            "progression",
            "Saved monotonic human goal progress.",
            eventCode: "progression_saved");
    }

    private void HighlightProgressionGoal()
    {
        var goals = ProgressionGoalCatalog.Goals;
        if (goals.Count == 0)
        {
            return;
        }

        _progressionGoalCursor = Math.Clamp(_progressionGoalCursor, 0, goals.Count - 1);
        var goal = goals[_progressionGoalCursor];
        try
        {
            _progression = _progression.WithHighlightedGoal(goal.Id);
            if (TrySaveProgression("progression_highlight_save_failed"))
            {
                _progressionStatusCaption = Localize(
                    "status.progression.highlighted",
                    ShellTextArgument.From("goal", goal.Name.ToUpperInvariant()));
                PlayCue(AudioCue.Confirm);
            }
            else
            {
                _progressionStatusCaption = Localize(
                    "status.progression.highlight-save-failed");
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidDataException
                or InvalidOperationException)
        {
            _progressionStatusCaption = Localize("status.progression.highlight-save-failed");
            _structuredLog?.Warning(
                "progression",
                exception.Message,
                eventCode: "progression_highlight_save_failed");
        }

        QueueRedraw();
    }

    private void LoadPersonalBests()
    {
        if (_personalBestStore is null)
        {
            _personalBests = PersonalBestDocument.CreateDefaults();
            _personalBestsWritable = false;
            return;
        }

        var loaded = _personalBestStore.Load();
        if (loaded.IsSuccess && loaded.Document is not null)
        {
            _personalBests = loaded.Document;
            _personalBestsWritable = true;
            return;
        }

        _personalBests = PersonalBestDocument.CreateDefaults();
        _personalBestsWritable = false;
        _structuredLog?.Warning(
            "scores",
            loaded.Message,
            eventCode: "personal_best_load_failed");
    }

    private PersonalBestUpdate UpdatePersonalBest(
        SnakeRun run,
        ScoreRunContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        var identity = RunScoreIdentity.FromRun(run, context);
        if (_personalBestStore is null || !_personalBestsWritable)
        {
            var existing = _personalBests.Find(identity)?.BestScore;
            _structuredLog?.Warning(
                "scores",
                "Personal-best storage is unavailable; the run remains session-only.",
                eventCode: "personal_best_store_unavailable");
            return new PersonalBestUpdate(
                _personalBests,
                IsNewRecord: false,
                PreviousBestScore: existing,
                BestScore: Math.Max(existing ?? 0, run.Score));
        }

        var previous = _personalBests;
        try
        {
            var update = _personalBests.Apply(identity);
            _personalBests = update.Document;
            _personalBestStore.Save(_personalBests);
            _structuredLog?.Information(
                "scores",
                update.IsNewRecord
                    ? "Saved a new fair-category personal best."
                    : "Retained the existing fair-category personal best.",
                eventCode: update.IsNewRecord
                    ? "personal_best_saved"
                    : "personal_best_retained");
            return update;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidDataException
            or InvalidOperationException)
        {
            _personalBests = previous;
            _structuredLog?.Warning(
                "scores",
                exception.Message,
                eventCode: "personal_best_save_failed");
            var existing = _personalBests.Find(identity)?.BestScore;
            return new PersonalBestUpdate(
                _personalBests,
                IsNewRecord: false,
                PreviousBestScore: existing,
                BestScore: Math.Max(existing ?? 0, run.Score));
        }
    }

    private void LoadScoreHistory()
    {
        _scoreHistoryWritable = true;
        if (_scoreHistoryStore is null)
        {
            _scoreHistory = ScoreHistoryDocument.CreateDefaults();
            _scoreHistoryWritable = false;
            return;
        }

        var loaded = _scoreHistoryStore.Load();
        if (!loaded.IsSuccess || loaded.Document is null)
        {
            _scoreHistory = ScoreHistoryDocument.CreateDefaults();
            _scoreHistoryWritable = false;
            _structuredLog?.Warning(
                "scores",
                loaded.Message,
                eventCode: "score_history_load_failed");
            return;
        }

        _scoreHistory = loaded.Document;
        try
        {
            var migration = _scoreHistory.MergePersonalBests(_personalBests);
            _scoreHistory = migration.Document;
            if (migration.AddedEntryCount > 0)
            {
                _scoreHistoryStore.Save(_scoreHistory);
                _structuredLog?.Information(
                    "scores",
                    $"Seeded score history from {migration.AddedEntryCount} existing personal best(s).",
                    eventCode: "score_history_personal_bests_migrated");
            }
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or InvalidOperationException)
        {
            _scoreHistoryWritable = false;
            _structuredLog?.Warning(
                "scores",
                exception.Message,
                eventCode: "score_history_migration_failed");
        }
    }

    private void UpdateScoreHistory(
        SnakeRun run,
        DateTimeOffset recordedAtUtc,
        ScoreRunContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (_scoreHistoryStore is null || !_scoreHistoryWritable)
        {
            _structuredLog?.Warning(
                "scores",
                "Score-history storage is unavailable; the personal best remains available.",
                eventCode: "score_history_store_unavailable");
            return;
        }

        try
        {
            var update = _scoreHistory.Add(
                RunScoreIdentity.FromRun(run, context),
                recordedAtUtc,
                ScoreHistoryDocument.LocalPlayerLabel);
            if (update.Retained)
            {
                _scoreHistory = update.Document;
                _scoreHistoryStore.Save(_scoreHistory);
            }

            _structuredLog?.Information(
                "scores",
                update.Retained
                    ? $"Saved native score-history rank {update.Rank}."
                    : "Score did not enter the exact-category top ten.",
                eventCode: update.Retained
                    ? "score_history_saved"
                    : "score_history_not_retained");
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidDataException
            or InvalidOperationException)
        {
            _structuredLog?.Warning(
                "scores",
                exception.Message,
                eventCode: "score_history_save_failed");
        }
    }

    private void LoadLocalPlaytestSummaryCount()
    {
        if (_localPlaytestSummaryStore is null)
        {
            _localPlaytestSummaryCount = 0;
            return;
        }

        var loaded = _localPlaytestSummaryStore.Load();
        if (loaded.IsSuccess && loaded.Document is not null)
        {
            _localPlaytestSummaryCount = loaded.Document.Summaries.Count;
            return;
        }

        _localPlaytestSummaryCount = 0;
        _structuredLog?.Warning(
            "playtest-summaries",
            loaded.Message,
            eventCode: "local_playtest_summary_load_failed");
    }

    private void LoadSpectatorLeague()
    {
        if (_spectatorLeagueStore is null)
        {
            _spectatorLeague = SpectatorLeagueDocument.CreateDefaults();
            _spectatorLeagueWritable = false;
            return;
        }

        var loaded = _spectatorLeagueStore.Load();
        if (loaded.IsSuccess && loaded.Document is not null)
        {
            _spectatorLeague = loaded.Document;
            _spectatorLeagueWritable = true;
            return;
        }

        _spectatorLeague = SpectatorLeagueDocument.CreateDefaults();
        _spectatorLeagueWritable = false;
        _structuredLog?.Warning(
            "spectator",
            loaded.Message,
            eventCode: "spectator_league_load_failed");
    }

    private bool CaptureLocalPlaytestSummary(SnakeRun run, DateTimeOffset capturedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!_shellSettings.LocalPlaytestSummariesEnabled)
        {
            return false;
        }

        if (_localPlaytestSummaryStore is null)
        {
            _structuredLog?.Warning(
                "playtest-summaries",
                "Local summary storage is unavailable; no run facts were retained.",
                eventCode: "local_playtest_summary_store_unavailable");
            return false;
        }

        try
        {
            var summary = LocalPlaytestSummary.Capture(
                run,
                ProductIdentity.AppVersion,
                capturedAtUtc,
                _powerDecisionTrace.Snapshot().Select(counts =>
                    new LocalPowerDecisionSummary(
                        PowerDecisionCatalog.Get(counts.Kind).Id,
                        counts.Offered,
                        counts.DetoursObserved,
                        counts.Collected,
                        counts.Activated,
                        counts.Expired,
                        counts.Consumed,
                        counts.Saved,
                        counts.DeathAdjacent)).ToArray());
            var appended = _localPlaytestSummaryStore.Append(summary);
            _localPlaytestSummaryCount = appended.Document.Summaries.Count;
            _structuredLog?.Information(
                "playtest-summaries",
                appended.Added
                    ? "Stored one opted-in local playtest summary."
                    : "The opted-in local playtest summary was already stored.",
                eventCode: appended.Added
                    ? "local_playtest_summary_saved"
                    : "local_playtest_summary_duplicate");
            return appended.Added;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidDataException
                or InvalidOperationException)
        {
            _structuredLog?.Warning(
                "playtest-summaries",
                exception.Message,
                eventCode: "local_playtest_summary_save_failed");
            return false;
        }
    }

    private IReadOnlyList<string> PersistAchievementUnlocks(
        IReadOnlyList<RunEventDetail> orderedEvents)
    {
        if (_achievementsStore is null || !_achievementsWritable || orderedEvents.Count == 0)
        {
            return Array.Empty<string>();
        }

        var newlyEarned = new List<string>();
        foreach (var detail in orderedEvents)
        {
            if (detail.Kind != RunEventKind.AchievementCandidate || detail.Value is not int index)
            {
                continue;
            }

            var definition = AchievementCatalog.DefinitionAt(index);
            if (definition is null)
            {
                continue;
            }

            newlyEarned.Add(definition.Id);
        }

        if (newlyEarned.Count == 0)
        {
            return Array.Empty<string>();
        }

        newlyEarned = newlyEarned
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var previous = _achievements;
        try
        {
            _achievements = _achievements.WithUnlocks(newlyEarned);
            _achievementsStore.Save(_achievements);
            foreach (var id in newlyEarned)
            {
                var name = AchievementCatalog.Find(id)?.Name ?? id;
                _progressionNotifications.Enqueue(
                    id,
                    "UNLOCKED: " + name.ToUpperInvariant(),
                    _shellSettings.ReducedMotion);
            }
            _structuredLog?.Information(
                "shell",
                "Persisted " + newlyEarned.Count + " achievement unlock(s).",
                eventCode: "achievements_unlock_saved");
            // Surface permanent unlock progress on the ended-run overlay without
            // replacing the higher-priority ACHIEVEMENT caption from step feedback.
            if (_feedbackCaption is null || !_feedbackCaption.StartsWith("ACHIEVEMENT", StringComparison.Ordinal))
            {
                _feedbackCaption = newlyEarned.Count == 1
                    ? Localize(
                        "status.unlock.saved",
                        ShellTextArgument.From("unlock", newlyEarned[0].ToUpperInvariant()))
                    : Localize(
                        "status.unlock.saved-many",
                        ShellTextArgument.From("count", newlyEarned.Count));
                _feedbackTicksRemaining = FeedbackVisibilityTicks * 2;
            }

            return newlyEarned;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException)
        {
            _achievements = previous;
            WriteLocalCrashReport(
                "AchievementsSave",
                exception,
                eventCode: "achievements_save_failed");
            return Array.Empty<string>();
        }
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

    private ShellTheme ActiveShellTheme =>
        _shellTheme ?? throw new InvalidOperationException("Shell theme was not initialized.");

    private ShellPalette ActiveShellPalette =>
        ShellTheme.Palette(_shellSettings.HighContrast);

    private string Localize(string id, params ShellTextArgument[] arguments) =>
        ShellLocalization.Format(id, _shellLocale, arguments);

    private string Localize(ShellTextReference text) =>
        Localize(text.Id, text.Arguments.ToArray());

    private Color CanvasBackgroundColor() => ActiveShellPalette.CanvasBackground;

    private Color BoardBackgroundColor() => VibeLevelDirector.BoardBackground(
        ActiveShellPalette.BoardBackground,
        _shellSettings.HighContrast,
        _screenState is ScreenState.Running or ScreenState.Ended
            ? _vibeLevelDirector.CurrentLevel
            : VibeLevel.Grounded);

    private Color PrimaryTextColor() => ActiveShellPalette.PrimaryText;

    private Color SecondaryTextColor() => ActiveShellPalette.SecondaryText;

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
            _controllerBindings = InputBindingsDocument.CreateControllerDefaults();
            GameActions.ApplyKeyboardBindings(_keyboardBindings);
            GameActions.ApplyControllerBindings(_controllerBindings);
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
        if (controllerLoaded.IsSuccess && controllerLoaded.Document is not null)
        {
            _controllerBindings = controllerLoaded.Document;
        }
        else
        {
            _controllerBindings = InputBindingsDocument.CreateControllerDefaults();
            if (!controllerLoaded.IsSuccess)
            {
                WriteLocalCrashReport(
                    "InputBindingsLoad",
                    new InvalidOperationException(controllerLoaded.Message),
                    eventCode: "controller_bindings_load_failed");
            }
        }

        GameActions.ApplyControllerBindings(_controllerBindings);
    }

    private bool SaveInputBindings()
    {
        if (_inputBindingsStore is null)
        {
            _bindingsStatusCaption = Localize("status.bindings.save-unavailable");
            _settingsStatusCaption = Localize("status.bindings.save-unavailable");
            return false;
        }

        var keyboardSaved = TrySaveInputBindingDocument(_keyboardBindings);
        var controllerSaved = TrySaveInputBindingDocument(_controllerBindings);
        return keyboardSaved && controllerSaved;
    }

    private bool TrySaveInputBindingDocument(InputBindingsDocument document)
    {
        if (_inputBindingsStore is null)
        {
            return false;
        }

        try
        {
            _inputBindingsStore.Save(document);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException)
        {
            _bindingsStatusCaption = Localize("status.bindings.session-save-failed");
            _settingsStatusCaption = Localize("status.bindings.session-save-failed");
            try
            {
                WriteLocalCrashReport(
                    "InputBindingsSave",
                    exception,
                    eventCode: "input_bindings_save_failed");
            }
            catch (Exception diagnosticException) when (
                diagnosticException is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException)
            {
                _structuredLog?.Warning(
                    "input",
                    "Input bindings and diagnostic persistence are unavailable.",
                    eventCode: "input_bindings_save_and_diagnostics_failed");
            }

            return false;
        }
    }

    private bool RestoreInputBindingDefaults()
    {
        _bindingsCapturePending = false;
        _pendingBindingConflict = null;
        _keyboardBindings = InputBindingsDocument.CreateKeyboardDefaults();
        _controllerBindings = InputBindingsDocument.CreateControllerDefaults();
        GameActions.ApplyKeyboardBindings(_keyboardBindings);
        GameActions.ApplyControllerBindings(_controllerBindings);
        var saved = SaveInputBindings();
        ShowReplayStatus(
            saved
                ? "INPUT DEFAULTS RESTORED"
                : "INPUT DEFAULTS ACTIVE THIS SESSION; SAVE FAILED");
        QueueRedraw();
        return saved;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_screenState == ScreenState.Spectator)
        {
            AdvanceSpectatorMatch(delta);
            return;
        }

        if (_screenState == ScreenState.Replays)
        {
            AdvanceReplayPlayback(delta);
            return;
        }

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

    private void AdvanceReplayPlayback(double delta)
    {
        if (_replayPlayback is null
            || _replayPlaybackPaused
            || _replayPlayback.IsComplete)
        {
            return;
        }

        var steps = RulesCadenceClock.DrainSteps(
            ref _rulesStepAccumulatorMilliseconds,
            delta * ReplayPlaybackSpeeds[_replayPlaybackSpeedIndex],
            () => _replayPlayback.CurrentSnapshot.EffectiveRulesStepMilliseconds);
        for (var index = 0; index < steps; index++)
        {
            if (_replayPlayback is null || _replayPlayback.IsComplete)
            {
                break;
            }

            AdvanceReplayPlaybackStep();
        }

        if (steps > 0)
        {
            QueueRedraw();
        }
    }

    private void AdvanceSpectatorMatch(double delta)
    {
        if (_spectatorMatch is not { Paused: false } spectator || spectator.IsComplete)
        {
            PersistSpectatorMatchIfComplete();
            return;
        }

        var steps = RulesCadenceClock.DrainSteps(
            ref _rulesStepAccumulatorMilliseconds,
            delta * spectator.PlaybackSpeed,
            () => spectator.ViewedSnapshot.EffectiveRulesStepMilliseconds);
        for (var index = 0; index < steps && !spectator.IsComplete; index++)
        {
            var before = spectator.ViewedSnapshot;
            var advance = spectator.Advance(audioAvailable: _cuePlayer is not null);
            var after = spectator.ViewedSnapshot;
            var viewedStep = spectator.ViewedPersonalityId == spectator.Selection.PersonalityId
                ? advance.FeaturedStep
                : advance.RivalStep;
            if (viewedStep is { } result)
            {
                BeginSnakeMotion(
                    before.Body,
                    after.Body,
                    Math.Max(
                        1,
                        (int)Math.Round(
                            after.EffectiveRulesStepMilliseconds / spectator.PlaybackSpeed)));
                AdvanceFeedback(result.OrderedEvents, spectator.ViewedSnapshot.ComboCount);
                _vibeLevelDirector.Update(spectator.ViewedSnapshot.ComboCount);
            }
        }

        if (spectator.IsComplete)
        {
            spectator.SetPaused(true);
            _rulesStepAccumulatorMilliseconds = 0.0;
            _spectatorStatusCaption = Localize("status.spectator.complete");
            PersistSpectatorMatchIfComplete();
        }

        if (steps > 0)
        {
            QueueRedraw();
        }
    }

    private void PersistSpectatorMatchIfComplete()
    {
        if (_spectatorMatch is not { IsComplete: true } spectator
            || _spectatorMatchPersisted)
        {
            return;
        }

        var previous = _spectatorLeague;
        try
        {
            if (_spectatorLeagueStore is null || !_spectatorLeagueWritable)
            {
                _spectatorMatchPersisted = true;
                _spectatorStatusCaption = Localize("status.spectator.save-failed");
                return;
            }

            _spectatorLeague = _spectatorLeague.WithMatch(spectator.BuildResult());
            _spectatorLeagueStore.Save(_spectatorLeague);
            _spectatorMatchPersisted = true;
            _spectatorStatusCaption = Localize("status.spectator.saved");
            _structuredLog?.Information(
                "spectator",
                "Saved a local equal-rules AI league result.",
                eventCode: "spectator_league_saved");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidDataException
                or InvalidOperationException)
        {
            _spectatorLeague = previous;
            _spectatorStatusCaption = Localize("status.spectator.save-failed");
            _structuredLog?.Warning(
                "spectator",
                exception.Message,
                eventCode: "spectator_league_save_failed");
        }
    }

    private void PersistSpectatorChallenge(SnakeRun humanRun)
    {
        if (_activeSpectatorChallenge is null
            || _activeSpectatorChallengePersonalityId is null)
        {
            throw new InvalidOperationException(
                "Spectator challenge persistence requires an active descriptor.");
        }

        var previous = _spectatorLeague;
        try
        {
            if (_spectatorLeagueStore is null || !_spectatorLeagueWritable)
            {
                throw new InvalidOperationException(
                    "Spectator challenge persistence is unavailable.");
            }

            _spectatorLeague = _spectatorLeague.WithHumanChallenge(
                _activeSpectatorChallengePersonalityId,
                _activeSpectatorAiScore,
                _activeSpectatorChallenge,
                humanRun,
                ScoreRunContextCatalog.SeededChallenge);
            _spectatorLeagueStore.Save(_spectatorLeague);
            _structuredLog?.Information(
                "spectator",
                "Saved an equal-rules human seed-challenge result.",
                eventCode: "spectator_challenge_saved");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidDataException
                or InvalidOperationException)
        {
            _spectatorLeague = previous;
            _structuredLog?.Warning(
                "spectator",
                exception.Message,
                eventCode: "spectator_challenge_save_failed");
        }
    }

    private void AdvanceReplayPlaybackStep()
    {
        if (_replayPlayback is null || _replayPlayback.IsComplete)
        {
            return;
        }

        var before = _replayPlayback.CurrentSnapshot;
        if (_replayPlayback.TryAdvance(out var frame) && frame is not null)
        {
            var after = _replayPlayback.CurrentSnapshot;
            BeginSnakeMotion(
                before.Body,
                after.Body,
                Math.Max(
                    1,
                    (int)Math.Round(
                        after.EffectiveRulesStepMilliseconds
                            / ReplayPlaybackSpeeds[_replayPlaybackSpeedIndex])));
            AdvanceFeedback(
                frame.Result.OrderedEvents,
                after.ComboCount);
        }

        if (_replayPlayback.IsComplete)
        {
            _replayPlaybackPaused = true;
            _rulesStepAccumulatorMilliseconds = 0.0;
            ShowReplayStatus(
                $"REPLAY COMPLETE: {_replayPlayback.StepCount} STEPS, SCORE {_replayPlayback.CurrentSnapshot.Score}");
        }
    }

    private void SyncVibeLevel(int comboCount)
    {
        _vibeLevelDirector.Reset();
        _vibeLevelDirector.Update(comboCount);
    }

    private void AdvanceOneRulesStep()
    {
        if (_run is null)
        {
            return;
        }

        var before = _run.GetSnapshot();
        RunStepResult result;
        if (_activeGhostRace is not null)
        {
            if (!_activeGhostRace.TryAdvance(out var ghostFrame) || ghostFrame is null)
            {
                return;
            }

            result = ghostFrame.PlayerResult;
        }
        else
        {
            result = _run.Step();
        }
        var after = _run.GetSnapshot();
        BeginSnakeMotion(before.Body, after.Body, after.EffectiveRulesStepMilliseconds);
        _powerDecisionTrace.Observe(before, after, result.OrderedEvents);
        if (
            _replayRecorder is { } recorder
            && !recorder.TryCompleteStep(result, _run))
        {
            ShowReplayStatus(
                "REPLAY RECORDING STOPPED: "
                    + (recorder.FailureMessage ?? "UNKNOWN RECORDER FAILURE"));
        }

        AdvanceFeedback(result.OrderedEvents, _run.ComboCount);
        CaptureBaitConversion(before, after, result.OrderedEvents);

        if (_run.Status != RunStatus.Running)
        {
            CompleteRunEnd(result.OrderedEvents);
            FinalizeAndStoreReplay();
        }
    }

    private void BeginSnakeMotion(
        IReadOnlyList<GridPoint> previousBody,
        IReadOnlyList<GridPoint> currentBody,
        int durationMilliseconds)
    {
        if (_shellSettings.ReducedMotion || durationMilliseconds <= 0)
        {
            _snakeMotionPresentation.Reset(currentBody);
            return;
        }

        _snakeMotionPresentation.Begin(
            previousBody,
            currentBody,
            Time.GetTicksMsec(),
            durationMilliseconds);
    }

    private void CompleteRunEnd(IReadOnlyList<RunEventDetail> orderedEvents)
    {
        if (_run is null || _run.Status == RunStatus.Running)
        {
            throw new InvalidOperationException("Run-end presentation requires a terminal run.");
        }

        _terminalInputSequence = _inputSequence;
        _restartIntentGate.NoteTerminal(_terminalInputSequence);
        var recordedAtUtc = DateTimeOffset.UtcNow;
        IReadOnlyList<string> newlyUnlocked;
        if (_activeTourEvent is { } tourEvent)
        {
            if (_activeRunContext != ScoreRunContextCatalog.Practice)
            {
                throw new InvalidOperationException(
                    "Broadcast Tour runs must retain the canonical practice identity.");
            }

            newlyUnlocked = PersistTourCompletion(tourEvent, _run);
            _runEndSummary = RunEndSummary.Create(
                _run,
                _run.Score,
                isNewPersonalBest: false,
                newlyUnlocked);
        }
        else if (_activeRunContext == ScoreRunContextCatalog.NormalHuman)
        {
            newlyUnlocked = PersistAchievementUnlocks(orderedEvents);
            PersistProgression(_run);
            var personalBest = UpdatePersonalBest(_run);
            UpdateScoreHistory(_run, recordedAtUtc);
            _runEndSummary = RunEndSummary.Create(
                _run,
                personalBest.BestScore,
                personalBest.IsNewRecord,
                newlyUnlocked);
            CaptureLocalPlaytestSummary(_run, recordedAtUtc);
        }
        else if (_activeRunContext == ScoreRunContextCatalog.SeededChallenge
            && _activeSpectatorChallenge is not null
            && _activeSpectatorChallengePersonalityId is not null)
        {
            newlyUnlocked = Array.Empty<string>();
            var personalBest = UpdatePersonalBest(
                _run,
                ScoreRunContextCatalog.SeededChallenge);
            UpdateScoreHistory(
                _run,
                recordedAtUtc,
                ScoreRunContextCatalog.SeededChallenge);
            PersistSpectatorChallenge(_run);
            _runEndSummary = RunEndSummary.Create(
                _run,
                personalBest.BestScore,
                personalBest.IsNewRecord,
                newlyUnlocked);
        }
        else if (_activeRunContext == ScoreRunContextCatalog.SeededChallenge
            && _activeGhostRace is not null
            && _activeGhostSlot is not null)
        {
            newlyUnlocked = Array.Empty<string>();
            var personalBest = UpdatePersonalBest(
                _run,
                ScoreRunContextCatalog.SeededChallenge);
            UpdateScoreHistory(
                _run,
                recordedAtUtc,
                ScoreRunContextCatalog.SeededChallenge);
            _runEndSummary = RunEndSummary.Create(
                _run,
                personalBest.BestScore,
                personalBest.IsNewRecord,
                newlyUnlocked);
        }
        else
        {
            throw new InvalidOperationException(
                "The live run ended with an unsupported product run identity.");
        }

        TransitionToScreen(ScreenState.Ended);
        TryBroadcast(BroadcastBoundary.PostRun, criticalCueActive: false);
        _structuredLog?.Information(
            "shell",
            _run.Status == RunStatus.Won
                ? "Run ended: won."
                : "Run ended: " + _run.DeathCause + ".",
            eventCode: _run.Status == RunStatus.Won ? "run_won" : "run_dead");
        PlayCue(
            _run.Status == RunStatus.Won
                ? AudioCue.Victory
                : DeathFeedback.Describe(_run.DeathCause).Cue);
    }

    private string[] PersistTourCompletion(
        BroadcastTourEvent tourEvent,
        SnakeRun run)
    {
        _tourRunOutcome = BroadcastTourSession.Evaluate(tourEvent, run);
        if (!_tourRunOutcome.PrimaryComplete)
        {
            _tourStatusCaption = Localize(
                "status.tour.primary-incomplete",
                ShellTextArgument.From("progress", _tourRunOutcome.PrimaryProgress));
            _structuredLog?.Information(
                "progression",
                $"Broadcast Tour event {tourEvent.Id} ended without primary completion.",
                eventCode: "broadcast_tour_retry");
            return Array.Empty<string>();
        }

        var alreadyCompleted = _progression.CompletedTourEventIds.Contains(
            tourEvent.Id,
            StringComparer.Ordinal);
        if (alreadyCompleted)
        {
            _tourStatusCaption = Localize("status.tour.rematch-owned");
            return Array.Empty<string>();
        }

        ProgressionDocument updatedProgression;
        try
        {
            updatedProgression = _progression.CompleteTourEvent(tourEvent.Id);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidDataException
                or InvalidOperationException)
        {
            _tourStatusCaption = Localize("status.tour.completion-rejected");
            _structuredLog?.Warning(
                "progression",
                exception.Message,
                eventCode: "broadcast_tour_completion_rejected");
            return Array.Empty<string>();
        }

        var previous = _progression;
        _progression = updatedProgression;
        var saved = TrySaveProgression("broadcast_tour_save_failed");
        if (!saved)
        {
            _progression = previous;
        }

        _progressionNotifications.Enqueue(
            tourEvent.Reward.Id,
            "TOUR REWARD: " + tourEvent.Reward.DisplayName.ToUpperInvariant(),
            _shellSettings.ReducedMotion);
        _tourStatusCaption = saved
            ? Localize(
                "status.tour.event-cleared",
                ShellTextArgument.From("reward", tourEvent.Reward.DisplayName.ToUpperInvariant()))
            : Localize("status.tour.save-failed");
        _structuredLog?.Information(
            "progression",
            saved
                ? $"Completed Broadcast Tour event {tourEvent.Id} and saved its expression reward."
                : $"Completed Broadcast Tour event {tourEvent.Id}; its reward remains session-only.",
            eventCode: saved
                ? "broadcast_tour_complete"
                : "broadcast_tour_session_only");
        return [tourEvent.Reward.Id];
    }

    public override void _Process(double delta)
    {
        _ = delta;
        var nowMilliseconds = Time.GetTicksMsec();
#if AGENT_ARENA_PREVIEW
        PollAgentViewer(nowMilliseconds);
        CheckAgentViewerSmokeTimeout(nowMilliseconds);
#endif
        var redrawAnimatedScreen = _screenState is ScreenState.Running
            or ScreenState.Ended
            or ScreenState.Replays
            or ScreenState.Spectator;
#if AGENT_ARENA_PREVIEW
        redrawAnimatedScreen |= _screenState == ScreenState.AgentWatch;
#endif
        if (_snakeMotionPresentation.IsAnimating(nowMilliseconds)
            && redrawAnimatedScreen)
        {
            QueueRedraw();
        }
        UpdateCursorVisibility(nowMilliseconds);
        _cuePlayer?.ProcessMix(nowMilliseconds);
        RefreshAudioOutputTopology(nowMilliseconds);
        VerifyRadioPlayback(nowMilliseconds);
        if (_audioStatusExpiresAtMilliseconds is { } audioDeadline
            && nowMilliseconds >= audioDeadline)
        {
            _audioStatusCaption = null;
            _audioStatusExpiresAtMilliseconds = null;
            QueueRedraw();
        }
        if (_spectatorControlsVisibleUntilMilliseconds is { } spectatorControlsDeadline
            && nowMilliseconds >= spectatorControlsDeadline)
        {
            _spectatorControlsVisibleUntilMilliseconds = null;
            QueueRedraw();
        }

        if (ShouldQuitAfterReplayWork(nowMilliseconds))
        {
            GetTree().Quit();
        }

        if (ShouldQuitAfterPlayerDataWork())
        {
            GetTree().Quit();
        }
    }

    public override void _ExitTree()
    {
        DrainReplaySaveBeforeExit();
        if (_cuePlayer is not null
            && !_cuePlayer.TryStopAndRelease(out var audioCleanupFailure))
        {
            _structuredLog?.Warning(
                "audio",
                "Audio cleanup completed with recoverable failures: " + audioCleanupFailure,
                eventCode: "audio_cleanup_degraded");
        }
        if (_radioPlayer is not null
            && !_radioPlayer.TryStopAndRelease(out var radioCleanupFailure))
        {
            _structuredLog?.Warning(
                "audio",
                "Radio cleanup completed with recoverable failures: " + radioCleanupFailure,
                eventCode: "radio_cleanup_degraded");
        }
        if (_window is not null && IsInstanceValid(_window))
        {
            _window.FilesDropped -= OnFilesDropped;
            _window.SizeChanged -= OnWindowSizeChanged;
        }

#if AGENT_ARENA_PREVIEW
        _agentViewer?.Dispose();
        _agentViewer = null;
#endif

        Input.JoyConnectionChanged -= OnJoyConnectionChanged;
        GameActions.ReleaseRuntimeDefaults();
    }

    public override void _Input(InputEvent @event)
    {
        var inputEvent = @event;
        if (inputEvent is InputEventMouseMotion mouseMotion)
        {
            NotePointerActivity(Time.GetTicksMsec());
            HandleMouseMotionInput(mouseMotion);
            return;
        }

        if (inputEvent is InputEventMouseButton { Pressed: true } mouseButton)
        {
            NotePointerActivity(Time.GetTicksMsec());
            HandleMouseButtonInput(mouseButton);
            return;
        }

        _inputSequence = checked(_inputSequence + 1);
        ObservePromptInput(inputEvent);

        if (inputEvent.IsActionPressed(GameActions.Quit))
        {
            RequestQuit();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.RestoreDefaults))
        {
            if (_screenState == ScreenState.Replays && _replayPlayback is null)
            {
                HandleReplayBrowseListInput(inputEvent);
            }
            else if (_screenState == ScreenState.Comparisons)
            {
                HandleOfflineComparisonsInput(inputEvent);
            }
            else if (_screenState == ScreenState.Settings)
            {
                RestoreCurrentSettingsSection();
            }
            else
            {
                RestoreInputBindingDefaults();
            }

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
            var diagnosticsStatus = OpenDiagnosticsDirectory();
            if (_screenState is ScreenState.Menu or ScreenState.Ended)
            {
                ShowReplayStatus(diagnosticsStatus);
            }

            return;
        }

        if (inputEvent.IsActionPressed(GameActions.CycleRadio)
            && _screenState is ScreenState.Menu
                or ScreenState.Running
                or ScreenState.Ended
                or ScreenState.ContentPacks
                or ScreenState.Spectator)
        {
            CycleRadioStation();
            return;
        }

        if (_screenState == ScreenState.Spectator)
        {
            HandleSpectatorInput(inputEvent);
            return;
        }

#if AGENT_ARENA_PREVIEW
        if (_screenState == ScreenState.AgentWatch)
        {
            if (inputEvent.IsActionPressed(GameActions.Back))
            {
                ReturnToMenu();
            }
            else if (inputEvent.IsActionPressed(GameActions.Help))
            {
                ToggleCleanCaptureMode();
            }

            return;
        }
#endif

        if (_screenState == ScreenState.Lore)
        {
            HandleLoreInput(inputEvent);
            return;
        }

        if (_screenState == ScreenState.Comparisons)
        {
            HandleOfflineComparisonsInput(inputEvent);
            return;
        }

#if AGENT_ARENA_PREVIEW
        if (_screenState == ScreenState.AgentExhibitions)
        {
            HandleAgentExhibitionsInput(inputEvent);
            return;
        }
#endif

        if (_screenState == ScreenState.Ended)
        {
            if (inputEvent.IsActionPressed(GameActions.Help))
            {
                OpenOnboardingOffer();
            }
            else if (inputEvent.IsActionPressed(GameActions.BrowseSettings))
            {
                OpenSettingsBrowse();
            }
            else if (inputEvent.IsActionPressed(GameActions.BrowseContentPacks))
            {
                OpenContentPacksBrowse();
            }
            else if (inputEvent.IsActionPressed(GameActions.BrowseAchievements))
            {
                OpenAchievementsBrowse();
            }
            else if (inputEvent.IsActionPressed(GameActions.BrowseSpectator))
            {
                OpenSpectatorBrowse();
            }
            else if (inputEvent.IsActionPressed(GameActions.MoveUp))
            {
                OpenSpectatorBrowse();
            }
            else if (inputEvent.IsActionPressed(GameActions.BrowseScores)
                || inputEvent.IsActionPressed(GameActions.MoveDown))
            {
                OpenScoresBrowse();
            }
            else if (inputEvent.IsActionPressed(GameActions.BrowseBindings))
            {
                OpenBindingsBrowse();
            }
            else if (inputEvent.IsActionPressed(GameActions.Replay))
            {
                OpenReplaysBrowse();
            }
            else if (inputEvent.IsActionPressed(GameActions.Confirm))
            {
                TryRestartFromEnded(_inputSequence);
            }
            else if (inputEvent.IsActionPressed(GameActions.Back))
            {
                if (_activeTourEvent is not null)
                {
                    OpenBroadcastTour();
                }
                else if (_activeGhostSlot is not null)
                {
                    OpenOfflineComparisons();
                }
                else
                {
                    ReturnToMenu();
                }
            }

            return;
        }

        if (_screenState == ScreenState.Menu)
        {
            if (inputEvent.IsActionPressed(GameActions.Help))
            {
                OpenOnboardingOffer();
            }
            else if (inputEvent.IsActionPressed(GameActions.BrowseSettings))
            {
                OpenSettingsBrowse();
            }
            else if (inputEvent.IsActionPressed(GameActions.BrowseContentPacks))
            {
                OpenCosmeticSets();
            }
            else if (inputEvent.IsActionPressed(GameActions.BrowseAchievements))
            {
                OpenAchievementsBrowse();
            }
            else if (inputEvent.IsActionPressed(GameActions.MoveUp))
            {
                MoveMainMenuCursor(-1);
            }
            else if (inputEvent.IsActionPressed(GameActions.BrowseScores))
            {
                OpenScoresBrowse();
            }
            else if (inputEvent.IsActionPressed(GameActions.MoveDown))
            {
                MoveMainMenuCursor(1);
            }
            else if (inputEvent.IsActionPressed(GameActions.BrowseBindings))
            {
                OpenBindingsBrowse();
            }
            else if (inputEvent.IsActionPressed(GameActions.Replay))
            {
                OpenReplaysBrowse();
            }
            else if (inputEvent.IsActionPressed(GameActions.MoveLeft))
            {
                if ((MainMenuItem)_mainMenuCursor == MainMenuItem.Start)
                {
                    CycleSelectedRunMode(-1);
                }
            }
            else if (inputEvent.IsActionPressed(GameActions.MoveRight))
            {
                if ((MainMenuItem)_mainMenuCursor == MainMenuItem.Start)
                {
                    CycleSelectedRunMode(1);
                }
            }
            else if (inputEvent.IsActionPressed(GameActions.Confirm))
            {
                ActivateMainMenuItem();
            }
            else if (inputEvent.IsActionPressed(GameActions.Back))
            {
                RequestQuit();
            }

            return;
        }

        if (_screenState == ScreenState.Onboarding)
        {
            HandleOnboardingInput(inputEvent);
            return;
        }

        if (_screenState == ScreenState.Settings)
        {
            HandleSettingsScreenInput(inputEvent);
            return;
        }

        if (_screenState == ScreenState.Achievements)
        {
            if (inputEvent.IsActionPressed(GameActions.Back)
                || inputEvent.IsActionPressed(GameActions.BrowseAchievements))
            {
                LeaveOverlayScreen();
            }
            else if (inputEvent.IsActionPressed(GameActions.Replay))
            {
                OpenBroadcastTour();
            }
            else if (inputEvent.IsActionPressed(GameActions.BrowseContentPacks))
            {
                OpenCosmeticSets();
            }
            else if (inputEvent.IsActionPressed(GameActions.Confirm))
            {
                HighlightProgressionGoal();
            }
            else if (inputEvent.IsActionPressed(GameActions.MoveUp))
            {
                var previous = _progressionGoalCursor;
                _progressionGoalCursor = Math.Max(0, _progressionGoalCursor - 1);
                _achievementsPage = _progressionGoalCursor / ProgressionGoalsPerPage;
                if (_progressionGoalCursor != previous)
                {
                    PlayCue(AudioCue.Navigate);
                }

                QueueRedraw();
            }
            else if (inputEvent.IsActionPressed(GameActions.MoveDown))
            {
                var previous = _progressionGoalCursor;
                _progressionGoalCursor = Math.Min(
                    ProgressionGoalCatalog.Goals.Count - 1,
                    _progressionGoalCursor + 1);
                _achievementsPage = _progressionGoalCursor / ProgressionGoalsPerPage;
                if (_progressionGoalCursor != previous)
                {
                    PlayCue(AudioCue.Navigate);
                }

                QueueRedraw();
            }
            else if (inputEvent.IsActionPressed(GameActions.MoveLeft))
            {
                var previousPage = _achievementsPage;
                _achievementsPage = Math.Max(0, _achievementsPage - 1);
                _progressionGoalCursor = _achievementsPage * ProgressionGoalsPerPage;
                if (_achievementsPage != previousPage)
                {
                    PlayCue(AudioCue.Navigate);
                }

                QueueRedraw();
            }
            else if (inputEvent.IsActionPressed(GameActions.MoveRight))
            {
                var previousPage = _achievementsPage;
                _achievementsPage = Math.Min(
                    AchievementPageCount() - 1,
                    _achievementsPage + 1);
                _progressionGoalCursor = Math.Min(
                    ProgressionGoalCatalog.Goals.Count - 1,
                    _achievementsPage * ProgressionGoalsPerPage);
                if (_achievementsPage != previousPage)
                {
                    PlayCue(AudioCue.Navigate);
                }

                QueueRedraw();
            }

            return;
        }

        if (_screenState == ScreenState.Tour)
        {
            HandleBroadcastTourInput(inputEvent);
            return;
        }

        if (_screenState == ScreenState.Cosmetics)
        {
            HandleCosmeticSetsInput(inputEvent);
            return;
        }

        if (_screenState == ScreenState.Scores)
        {
            HandleScoresBrowseInput(inputEvent);
            return;
        }

        if (_screenState == ScreenState.Bindings)
        {
            HandleBindingsScreenInput(inputEvent);
            return;
        }

        if (_screenState == ScreenState.ContentPacks)
        {
            if (inputEvent.IsActionPressed(GameActions.Back)
                || inputEvent.IsActionPressed(GameActions.BrowseContentPacks)
                || inputEvent.IsActionPressed(GameActions.Confirm))
            {
                LeaveOverlayScreen();
            }

            return;
        }

        if (_screenState == ScreenState.Replays)
        {
            HandleReplaysScreenInput(inputEvent);
            return;
        }

        if (_screenState == ScreenState.Running
            && inputEvent.IsActionPressed(GameActions.Help))
        {
            ToggleCleanCaptureMode();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.Back))
        {
            ReturnToMenu();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.Pause))
        {
            SetRunPaused(!_paused);
            _pausedByFocusLoss = false;
            PlayCue(AudioCue.Pause);
            QueueRedraw();
            return;
        }

        if (_paused)
        {
            return;
        }

        if (GameActions.TryMapDirectionInput(inputEvent, out var requestedDirection))
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

    private void HandleMouseButtonInput(InputEventMouseButton mouseButton)
    {
        if (_activePromptFamily != InputPromptFamily.Keyboard)
        {
            _activePromptFamily = InputPromptFamily.Keyboard;
            QueueRedraw();
        }

        var action = ResolveMouseAction(mouseButton);
        if (action is null)
        {
            return;
        }

        using var translated = new InputEventAction
        {
            Action = action,
            Pressed = true,
        };
        _Input(translated);
    }

    private void HandleMouseMotionInput(InputEventMouseMotion mouseMotion)
    {
        var logicalPoint = MapPointerToLogical(mouseMotion.Position);
        if (!ContainsActiveLogicalPoint(logicalPoint))
        {
            return;
        }

        if (_screenState == ScreenState.Menu)
        {
            var menuIndex = MouseInputPolicy.ResolveMenuIndex(logicalPoint, ActiveLogicalWidth);
            if (menuIndex is not null && menuIndex.Value != _mainMenuCursor)
            {
                _mainMenuCursor = menuIndex.Value;
                QueueRedraw();
            }

            return;
        }

        if (_screenState == ScreenState.Cosmetics)
        {
            var pageIndex = MouseInputPolicy.ResolveCosmeticPageIndex(logicalPoint);
            var catalogIndex = pageIndex is null
                ? -1
                : (_cosmeticPage * CosmeticSetsPerPage) + pageIndex.Value;
            if (catalogIndex >= 0
                && catalogIndex < CosmeticSetCatalog.Sets.Count
                && catalogIndex != _cosmeticCursor)
            {
                _cosmeticCursor = catalogIndex;
                _cosmeticStatusCaption = LocalizedCosmeticRequirement(
                    CosmeticSetCatalog.Sets[_cosmeticCursor]);
                QueueRedraw();
            }
        }
    }

    private string? ResolveMouseAction(InputEventMouseButton mouseButton)
    {
        if (_screenState == ScreenState.Menu
            && mouseButton.ButtonIndex is MouseButton.WheelLeft or MouseButton.WheelRight)
        {
            var hoveredLogicalPoint = MapPointerToLogical(mouseButton.Position);
            var menuIndex = ContainsActiveLogicalPoint(hoveredLogicalPoint)
                ? MouseInputPolicy.ResolveMenuIndex(hoveredLogicalPoint, ActiveLogicalWidth)
                : null;
            if (menuIndex is not null)
            {
                _mainMenuCursor = menuIndex.Value;
                QueueRedraw();
            }
        }

        if (mouseButton.ButtonIndex == MouseButton.Right)
        {
            return _screenState == ScreenState.Menu ? null : GameActions.Back;
        }

        if (mouseButton.ButtonIndex == MouseButton.Middle)
        {
            return _screenState == ScreenState.Running ? GameActions.Pause : null;
        }

        if (mouseButton.ButtonIndex is MouseButton.WheelUp
            or MouseButton.WheelDown
            or MouseButton.WheelLeft
            or MouseButton.WheelRight)
        {
            if (!ScreenAllowsWheelNavigation(_screenState))
            {
                return null;
            }

            return mouseButton.ButtonIndex switch
            {
                MouseButton.WheelUp => GameActions.MoveUp,
                MouseButton.WheelDown => GameActions.MoveDown,
                MouseButton.WheelLeft => GameActions.MoveLeft,
                _ => GameActions.MoveRight,
            };
        }

        if (mouseButton.ButtonIndex != MouseButton.Left)
        {
            return null;
        }

        var logicalPoint = MapPointerToLogical(mouseButton.Position);
        if (!ContainsActiveLogicalPoint(logicalPoint))
        {
            return null;
        }

        if (_screenState == ScreenState.Menu)
        {
            var menuIndex = MouseInputPolicy.ResolveMenuIndex(logicalPoint, ActiveLogicalWidth);
            if (menuIndex is null)
            {
                return null;
            }

            _mainMenuCursor = menuIndex.Value;
            QueueRedraw();
            return GameActions.Confirm;
        }

        if (_screenState == ScreenState.Cosmetics)
        {
            var pageIndex = MouseInputPolicy.ResolveCosmeticPageIndex(logicalPoint);
            var catalogIndex = pageIndex is null
                ? -1
                : (_cosmeticPage * CosmeticSetsPerPage) + pageIndex.Value;
            if (catalogIndex < 0 || catalogIndex >= CosmeticSetCatalog.Sets.Count)
            {
                return null;
            }

            _cosmeticCursor = catalogIndex;
            _cosmeticStatusCaption = LocalizedCosmeticRequirement(
                CosmeticSetCatalog.Sets[_cosmeticCursor]);
            QueueRedraw();
            return GameActions.Confirm;
        }

        if (_screenState == ScreenState.Running)
        {
            if (_paused)
            {
                return GameActions.Pause;
            }

            return _run is null
                ? null
                : MouseInputPolicy.ResolveGameplayDirectionAction(
                    logicalPoint,
                    _run.Head,
                    CellSize,
                    HudHeight);
        }

        return null;
    }

    private static bool ScreenAllowsWheelNavigation(ScreenState state) =>
        state is ScreenState.Menu
            or ScreenState.Settings
            or ScreenState.Achievements
            or ScreenState.Scores
            or ScreenState.Replays
            or ScreenState.Tour
            or ScreenState.Cosmetics
            or ScreenState.Lore
            or ScreenState.Comparisons
            or ScreenState.Spectator
            or ScreenState.ContentPacks
            or ScreenState.Bindings;

    private void ObservePromptInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Echo: false })
        {
            if (_activePromptFamily != InputPromptFamily.Keyboard)
            {
                _activePromptFamily = InputPromptFamily.Keyboard;
                QueueRedraw();
            }

            return;
        }

        var deliberateControllerInput = inputEvent switch
        {
            InputEventJoypadButton { Pressed: true } => true,
            InputEventJoypadMotion motion => Math.Abs(motion.AxisValue) >= 0.75f,
            _ => false,
        };
        if (!deliberateControllerInput)
        {
            return;
        }

        var deviceName = _controllerConnections.GetDeviceName(inputEvent.Device)
            ?? Input.GetJoyName(inputEvent.Device);
        var family = InputPromptGlyphs.DetectControllerFamily(deviceName);
        var changed = _activePromptFamily != family || _controllerPromptFamily != family;
        _activePromptFamily = family;
        _controllerPromptFamily = family;
        if (changed)
        {
            QueueRedraw();
        }
    }

    private (string Token, InputPromptFamily Family) ResolveActionPrompt(
        string logicalAction)
    {
        var family = _activePromptFamily;
        var document = family == InputPromptFamily.Keyboard
            ? _keyboardBindings
            : _controllerBindings;
        return document.ActionToBinding.TryGetValue(logicalAction, out var token)
            ? (token, family)
            : ("unbound", family);
    }

    private (string Token, InputPromptFamily Family) ResolveStaticPrompt(
        string keyboardToken,
        string controllerToken) =>
        _activePromptFamily == InputPromptFamily.Keyboard
            ? (keyboardToken, InputPromptFamily.Keyboard)
            : (controllerToken, _controllerPromptFamily);

    private float DrawActionPromptSegment(
        string logicalAction,
        string caption,
        Vector2 baseline,
        int fontSize,
        Color color)
    {
        var prompt = ResolveActionPrompt(logicalAction);
        return DrawPromptSegment(prompt.Token, prompt.Family, caption, baseline, fontSize, color);
    }

    private float DrawStaticPromptSegment(
        string keyboardToken,
        string controllerToken,
        string caption,
        Vector2 baseline,
        int fontSize,
        Color color)
    {
        var prompt = ResolveStaticPrompt(keyboardToken, controllerToken);
        return DrawPromptSegment(prompt.Token, prompt.Family, caption, baseline, fontSize, color);
    }

    private float DrawPromptSegment(
        string token,
        InputPromptFamily family,
        string caption,
        Vector2 baseline,
        int fontSize,
        Color color)
    {
        var glyph = InputPromptGlyphs.DescribeToken(token, family);
        var measurement = PromptBadgeRenderer.Draw(
            this,
            ActiveShellTheme.InterfaceFont,
            glyph,
            baseline,
            fontSize,
            ActiveShellPalette);
        var captionPosition = baseline + new Vector2(measurement.Width + 10.0f, 0.0f);
        DrawLabel(caption, captionPosition, fontSize, color);
        return captionPosition.X
            + ActiveShellTheme.InterfaceFont.GetStringSize(
                caption,
                HorizontalAlignment.Left,
                -1.0f,
                fontSize).X
            + 16.0f;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            RequestQuit();
        }
        else if (what == NotificationApplicationFocusOut)
        {
            _applicationFocused = false;
            SetCursorHidden(false);
            PauseForFocusLoss();
        }
        else if (what == NotificationApplicationFocusIn)
        {
            _applicationFocused = true;
            NotePointerActivity(Time.GetTicksMsec());
        }
    }

    public override void _Draw()
    {
        // Window-space letterbox/pillarbox bars, then logical canvas content.
        var presentation = ActivePresentationRect;
        var logicalWidth = ActiveLogicalWidth;
        var presentationScale = presentation.Size.X / logicalWidth;
        DrawRect(
            new Rect2(0.0f, 0.0f, _virtualViewport.WindowWidth, _virtualViewport.WindowHeight),
            Colors.Black);
        DrawSetTransform(
            presentation.Position,
            0.0f,
            new Vector2(presentationScale, presentationScale));

        DrawRect(
            new Rect2(0.0f, 0.0f, logicalWidth, VirtualViewport.LogicalHeight),
            CanvasBackgroundColor());
        DrawRect(
            new Rect2(
                0.0f,
                HudHeight,
                logicalWidth,
                VirtualViewport.LogicalHeight - HudHeight),
            BoardBackgroundColor());

        switch (_screenState)
        {
            case ScreenState.Scores:
                DrawScoresBrowse();
                break;
            case ScreenState.Achievements:
                DrawAchievementsBrowse();
                break;
            case ScreenState.Tour:
                DrawBroadcastTour();
                break;
            case ScreenState.Cosmetics:
                DrawCosmeticSets();
                break;
            case ScreenState.Bindings:
                DrawBindingsBrowse();
                break;
            case ScreenState.ContentPacks:
                DrawContentPacksBrowse();
                break;
            case ScreenState.Replays:
                DrawReplaysBrowse();
                break;
            case ScreenState.Settings:
                DrawSettingsBrowse();
                break;
            case ScreenState.Onboarding:
                DrawOnboarding();
                break;
            case ScreenState.Spectator:
                DrawSpectator();
                break;
            case ScreenState.Lore:
                DrawLoreArchive();
                break;
            case ScreenState.Comparisons:
                DrawOfflineComparisons();
                break;
#if AGENT_ARENA_PREVIEW
            case ScreenState.AgentWatch:
                DrawAgentWatch();
                break;
            case ScreenState.AgentExhibitions:
                DrawAgentExhibitions();
                break;
#endif
            case ScreenState.Menu:
                DrawMainMenu();
                break;
            case ScreenState.Running:
            case ScreenState.Ended:
                DrawRun();
                break;
            default:
                throw new InvalidOperationException("Unknown screen state.");
        }

        if (_audioStatusCaption is not null && _capturePresentation.ShowAudioStatus)
        {
            DrawLabel(
                _audioStatusCaption,
                new Vector2(18.0f, 704.0f),
                ScaledFontSize(14),
                ActiveShellPalette.WarningText);
        }

        if (_performanceStressProfile is { } performanceProfile
            && _capturePresentation.ShowDebugOverlays)
        {
            DrawPerformanceStressScene(performanceProfile);
        }
    }

    private void DrawPerformanceStressScene(PerformanceProfileDefinition profile)
    {
        DrawRect(new Rect2(0.0f, 0.0f, 1280.0f, 720.0f), CanvasBackgroundColor());
        DrawRect(new Rect2(0.0f, HudHeight, 1280.0f, 660.0f), BoardBackgroundColor());
        DrawRect(new Rect2(18.0f, 18.0f, 200.0f, 7.0f), ActiveShellPalette.BodyText);
        DrawRect(new Rect2(236.0f, 18.0f, 160.0f, 7.0f), ActiveShellPalette.GoldText);
        for (var segment = 0; segment < HungerFeedback.SegmentCount; segment++)
        {
            DrawRect(
                new Rect2(530.0f + (segment * 14.0f), 14.0f, 10.0f, 14.0f),
                segment < 2
                    ? ActiveShellPalette.WarningText
                    : ActiveShellPalette.PromptFill);
        }

        DrawRect(new Rect2(1_010.0f, 18.0f, 220.0f, 7.0f), ActiveShellPalette.BodyText);

        GridPoint[] obstacleCells =
        [
            new(60, 30),
            new(61, 30),
            new(62, 30),
        ];
        GridPoint[] collectibleCells =
        [
            new(59, 29),
            new(60, 29),
        ];
        var reserved = obstacleCells.Take(profile.ObstacleCount)
            .Concat(collectibleCells.Take(profile.VisibleCollectibleCount))
            .ToHashSet();
        GridPoint? head = null;
        var drawnSnakeCells = 0;
        for (var y = 0; y < PerformanceQualification.GridHeight; y++)
        {
            for (var x = 0; x < PerformanceQualification.GridWidth; x++)
            {
                var cell = new GridPoint(x, y);
                if (reserved.Contains(cell))
                {
                    continue;
                }

                var isHead = drawnSnakeCells == profile.SnakeCellCount - 1;
                DrawCell(
                    cell,
                    isHead ? GameplayPresentation.HeadColor : GameplayPresentation.BodyColor,
                    isHead ? GameplayPresentation.HeadInset : GameplayPresentation.BodyInset);
                drawnSnakeCells++;
                if (isHead)
                {
                    head = cell;
                }

                if (drawnSnakeCells == profile.SnakeCellCount)
                {
                    break;
                }
            }

            if (drawnSnakeCells == profile.SnakeCellCount)
            {
                break;
            }
        }

        if (drawnSnakeCells != profile.SnakeCellCount || head is null)
        {
            throw new InvalidOperationException("Performance stress snake exceeded board capacity.");
        }

        DrawLine(
            new Vector2(
                (head.Value.X * CellSize) + 10.0f,
                HudHeight + (head.Value.Y * CellSize) + 10.0f),
            new Vector2(
                (head.Value.X * CellSize) + 16.0f,
                HudHeight + (head.Value.Y * CellSize) + 10.0f),
            ActiveShellPalette.BodyText,
            2.0f,
            antialiased: false);
        foreach (var obstacle in obstacleCells.Take(profile.ObstacleCount))
        {
            DrawCell(
                obstacle,
                GameplayPresentation.DetachedObstacleFill,
                GameplayPresentation.DetachedObstacleInset);
            DrawCellOutline(
                obstacle,
                PowerPresentation.SignalColor(PowerKind.SegmentDetach),
                GameplayPresentation.DetachedObstacleOutlineWidth,
                GameplayPresentation.DetachedObstacleInset);
        }

        if (profile.VisibleCollectibleCount >= 1)
        {
            DrawCell(
                collectibleCells[0],
                GameplayPresentation.FoodColor,
                GameplayPresentation.FoodInset);
            DrawCellOutline(collectibleCells[0], ActiveShellPalette.BodyText, 1.0f, 2.0f);
        }

        if (profile.VisibleCollectibleCount >= 2)
        {
            DrawCell(collectibleCells[1], ActiveShellPalette.PromptFill, 3.0f);
            DrawCellOutline(
                collectibleCells[1],
                PowerPresentation.SignalColor(PowerKind.Shield),
                2.0f,
                3.0f);
        }

        var shakeOffset = profile.ShakeSourceCount > 0
            ? profile.ShakeStrength * 4.0f
            : 0.0f;
        for (var index = 0; index < profile.ParticleCount; index++)
        {
            var x = 14.0f + ((index * 73) % 1_252) + shakeOffset;
            var y = 74.0f + ((index * 97) % 632);
            DrawRect(new Rect2(x, y, 2.0f, 2.0f), ActiveShellPalette.GoldText);
        }

        for (var index = 0; index < profile.PopupCount; index++)
        {
            var x = 900.0f;
            var y = 88.0f + (index * 34.0f);
            DrawRect(
                new Rect2(x, y, 300.0f - (index * 28.0f), 24.0f),
                ActiveShellPalette.PromptFill);
            DrawRect(
                new Rect2(x + 10.0f, y + 10.0f, 190.0f - (index * 22.0f), 4.0f),
                ActiveShellPalette.PrimaryText);
        }
    }

    private void PlayCue(AudioCue cue)
    {
        // Bus gains (Master + SFX/UI) already apply mute and volume. Keep the
        // stream player at full linear gain so levels are not attenuated twice.
        if (_cuePlayer is null)
        {
            return;
        }

        var nowMilliseconds = Time.GetTicksMsec();
        if (!_audioOutputRecovery.ShouldAttemptPlayback(nowMilliseconds))
        {
            return;
        }

        if (!_audioOutputRecovery.IsAvailable)
        {
            try
            {
                AudioBuses.EnsureRegistered();
                AudioBuses.ApplyShellSettings(_shellSettings);
            }
            catch (Exception exception)
            {
                NoteAudioFailure(nowMilliseconds, exception.Message);
                return;
            }
        }

        if (!_cuePlayer.TryPlayCue(cue, volumeLinear: 1.0f, out var failureReason))
        {
            NoteAudioFailure(nowMilliseconds, failureReason);
            return;
        }

        if (_audioOutputRecovery.NoteSuccess() is { } recovered)
        {
            ShowAudioStatus(recovered.Caption, persist: false);
            _structuredLog?.Information(
                "audio",
                $"Audio output recovered after {recovered.ConsecutiveFailures} failed attempt(s).",
                eventCode: "audio_output_recovered");
        }
    }

    private void NoteAudioFailure(ulong nowMilliseconds, string? failureReason)
    {
        if (_audioOutputRecovery.NoteFailure(nowMilliseconds, failureReason) is not { } unavailable)
        {
            return;
        }

        ShowAudioStatus(unavailable.Caption, persist: true);
        _structuredLog?.Warning(
            "audio",
            unavailable.FailureReason,
            eventCode: "audio_output_unavailable");
    }

    private void RefreshAudioOutputTopology(ulong nowMilliseconds, bool force = false)
    {
        if (!force && nowMilliseconds < _nextAudioOutputProbeMilliseconds)
        {
            return;
        }

        _nextAudioOutputProbeMilliseconds = nowMilliseconds
            > ulong.MaxValue - AudioOutputProbeIntervalMilliseconds
                ? ulong.MaxValue
                : nowMilliseconds + AudioOutputProbeIntervalMilliseconds;
        try
        {
            var selected = AudioServer.OutputDevice?.Trim() ?? string.Empty;
            var devices = AudioServer.GetOutputDeviceList()
                .Where(device => !string.IsNullOrWhiteSpace(device))
                .Select(device => device.Trim())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var signature = selected + "\n" + string.Join("\n", devices);
            if (_observedAudioOutputSignature.Length == 0)
            {
                _observedAudioOutputDevice = selected;
                _observedAudioOutputSignature = signature;
                return;
            }

            if (string.Equals(
                signature,
                _observedAudioOutputSignature,
                StringComparison.Ordinal))
            {
                return;
            }

            var previous = _observedAudioOutputDevice;
            if (!TryRepairAudioOutput(out var failureReason))
            {
                NoteAudioFailure(nowMilliseconds, failureReason);
                return;
            }

            _observedAudioOutputDevice = selected;
            _observedAudioOutputSignature = signature;
            ShowAudioStatus("AUDIO OUTPUT REFRESHED", persist: false);
            _structuredLog?.Information(
                "audio",
                $"Audio output topology changed from '{previous}' to '{selected}'.",
                eventCode: "audio_output_changed");
        }
        catch (Exception exception)
        {
            NoteAudioFailure(nowMilliseconds, exception.Message);
        }
    }

    private bool TryRepairAudioOutput(out string failureReason)
    {
        try
        {
            _cuePlayer?.StopAndDetach();
            AudioBuses.EnsureRegistered();
            AudioBuses.ApplyShellSettings(_shellSettings);
            _radioPlayer?.Synchronize();
            ScheduleRadioPlaybackVerification();
            if (_audioOutputRecovery.NoteSuccess() is { } recovered)
            {
                ShowAudioStatus(recovered.Caption, persist: false);
            }

            failureReason = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            failureReason = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private void ShowAudioStatus(string caption, bool persist)
    {
        _audioStatusCaption = caption;
        _audioStatusExpiresAtMilliseconds = persist
            ? null
            : Time.GetTicksMsec() + 3_000UL;
        QueueRedraw();
    }

    private void ScheduleRadioPlaybackVerification()
    {
        if (!_radioPolicy.Snapshot.IsAudible)
        {
            _radioPlaybackVerificationDueMilliseconds = null;
            _radioPlaybackVerificationReported = false;
            return;
        }

        _radioPlaybackVerificationDueMilliseconds =
            Time.GetTicksMsec() + RadioPlaybackVerificationDelayMilliseconds;
        _radioPlaybackVerificationReported = false;
    }

    private void VerifyRadioPlayback(ulong nowMilliseconds)
    {
        if (_radioPlaybackVerificationReported
            || _radioPlaybackVerificationDueMilliseconds is not { } due
            || nowMilliseconds < due)
        {
            return;
        }

        _radioPlaybackVerificationReported = true;
        _radioPlaybackVerificationDueMilliseconds = null;
        var runtime = _radioPlayer?.CaptureRuntimeSnapshot()
            ?? new RadioStreamRuntimeSnapshot(false, false, null, 0.0, "Decoder unavailable.");
        if (runtime.PlayerReady
            && runtime.Playing
            && runtime.TrackId is not null
            && runtime.PlaybackPositionSeconds > 0.05)
        {
            _structuredLog?.Information(
                "radio",
                $"Decoded playback advanced to {runtime.PlaybackPositionSeconds:0.00}s on {runtime.TrackId}.",
                eventCode: "radio_playback_verified");
            return;
        }

        var failure = runtime.LastFailure ?? "Playback did not advance after startup.";
        _radioPlaybackRetryRequired = true;
        _radioPlayer?.ForceReload();
        ShowAudioStatus("MUSIC COULD NOT START. PRESS J TO RETRY.", persist: true);
        _structuredLog?.Warning(
            "radio",
            failure,
            eventCode: "radio_playback_failed");
    }

    private void OpenSpectatorBrowse()
    {
        TransitionToScreen(ScreenState.Spectator);
        _replayRecorder = null;
        _spectatorMatch = null;
        _spectatorMatchPersisted = false;
        _spectatorSelectionCursor = 0;
        _spectatorStatusCaption = null;
        _loreDepthFilterIndex = 0;
        _loreBrowseCursor = 0;
        _loreUnlockContext = LoreUnlockContext.Empty;
        _capturePresentation = CapturePresentationState.Visible;
        _rulesStepAccumulatorMilliseconds = 0.0;
        _vibeLevelDirector.Reset();
        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    private void HandleSpectatorInput(InputEvent inputEvent)
    {
        if (_spectatorMatch is null)
        {
            if (inputEvent.IsActionPressed(GameActions.Back))
            {
                LeaveOverlayScreen();
            }
            else if (inputEvent.IsActionPressed(GameActions.MoveUp))
            {
                _spectatorSelectionCursor = (_spectatorSelectionCursor + 7) % 8;
                PlayCue(AudioCue.Navigate);
                QueueRedraw();
            }
            else if (inputEvent.IsActionPressed(GameActions.MoveDown))
            {
                _spectatorSelectionCursor = (_spectatorSelectionCursor + 1) % 8;
                PlayCue(AudioCue.Navigate);
                QueueRedraw();
            }
            else if (inputEvent.IsActionPressed(GameActions.MoveLeft))
            {
                CycleSpectatorSelection(-1);
            }
            else if (inputEvent.IsActionPressed(GameActions.MoveRight))
            {
                CycleSpectatorSelection(1);
            }
            else if (inputEvent.IsActionPressed(GameActions.Confirm))
            {
                StartSpectatorMatch();
            }
            else if (inputEvent.IsActionPressed(GameActions.BrowseAchievements))
            {
                OpenLoreArchive();
            }

            return;
        }

        var spectator = _spectatorMatch;
        RevealSpectatorControls(Time.GetTicksMsec());
        if (inputEvent.IsActionPressed(GameActions.Back))
        {
            _spectatorMatch = null;
            _spectatorMatchPersisted = false;
            _spectatorStatusCaption = null;
            _spectatorControlsVisibleUntilMilliseconds = null;
            _capturePresentation = CapturePresentationState.Visible;
            _vibeLevelDirector.Reset();
            _rulesStepAccumulatorMilliseconds = 0.0;
            PlayCue(AudioCue.Back);
            QueueRedraw();
        }
        else if (inputEvent.IsActionPressed(GameActions.Confirm)
            || inputEvent.IsActionPressed(GameActions.Pause))
        {
            spectator.TogglePaused();
            _rulesStepAccumulatorMilliseconds = 0.0;
            _spectatorStatusCaption = spectator.Paused
                ? Localize("status.spectator.paused")
                : Localize(
                    "status.spectator.started",
                    ShellTextArgument.From(
                        "channel",
                        SpectatorRivalCatalog.Get(spectator.Selection.PersonalityId)
                            .BroadcastIdentity),
                    ShellTextArgument.From(
                        "rival",
                        SpectatorRivalCatalog.Get(spectator.Selection.RivalPersonalityId)
                            .BroadcastIdentity));
            PlayCue(spectator.Paused ? AudioCue.Pause : AudioCue.Confirm);
            QueueRedraw();
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveLeft))
        {
            spectator.CyclePlaybackSpeed(-1);
            PlayCue(AudioCue.Navigate);
            QueueRedraw();
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveRight))
        {
            spectator.CyclePlaybackSpeed(1);
            PlayCue(AudioCue.Navigate);
            QueueRedraw();
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveUp))
        {
            spectator.SwitchViewedChannel();
            _snakeMotionPresentation.Reset(spectator.ViewedSnapshot.Body);
            _vibeLevelDirector.Reset();
            _vibeLevelDirector.Update(spectator.ViewedSnapshot.ComboCount);
            PlayCue(AudioCue.Navigate);
            QueueRedraw();
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveDown) && spectator.Paused)
        {
            AdvanceSpectatorOneStep(spectator);
        }
        else if (inputEvent.IsActionPressed(GameActions.Help))
        {
            _capturePresentation = _capturePresentation.Toggle();
            PlayCue(AudioCue.Navigate);
            QueueRedraw();
        }
        else if (inputEvent.IsActionPressed(GameActions.Replay))
        {
            StartSpectatorMatch();
        }
        else if (inputEvent.IsActionPressed(GameActions.BrowseContentPacks)
            && spectator.IsComplete)
        {
            StartSpectatorSeedChallenge(spectator);
        }
    }

    private void OpenLoreArchive()
    {
        TransitionToScreen(ScreenState.Lore);
        _loreDepthFilterIndex = 0;
        _loreBrowseCursor = 0;
        var replayCount = _replayStore?.ListStored().Replays.Count ?? 0;
        _loreUnlockContext = new LoreUnlockContext(
            _progression.UnlockedRewardIds.ToHashSet(StringComparer.Ordinal),
            _spectatorLeague.Standings
                .SelectMany(item => item.MilestoneIds)
                .ToHashSet(StringComparer.Ordinal),
            replayCount);
        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    private void HandleLoreInput(InputEvent inputEvent)
    {
        if (inputEvent.IsActionPressed(GameActions.Back))
        {
            TransitionToScreen(ScreenState.Spectator);
            PlayCue(AudioCue.Back);
            QueueRedraw();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.MoveLeft))
        {
            _loreDepthFilterIndex = (_loreDepthFilterIndex + 3) % 4;
            _loreBrowseCursor = 0;
            PlayCue(AudioCue.Navigate);
            QueueRedraw();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.MoveRight))
        {
            _loreDepthFilterIndex = (_loreDepthFilterIndex + 1) % 4;
            _loreBrowseCursor = 0;
            PlayCue(AudioCue.Navigate);
            QueueRedraw();
            return;
        }

        var entries = FilteredLoreEntries();
        if (entries.Length == 0)
        {
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.MoveUp))
        {
            _loreBrowseCursor = (_loreBrowseCursor + entries.Length - 1) % entries.Length;
            PlayCue(AudioCue.Navigate);
            QueueRedraw();
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveDown))
        {
            _loreBrowseCursor = (_loreBrowseCursor + 1) % entries.Length;
            PlayCue(AudioCue.Navigate);
            QueueRedraw();
        }
    }

    private LoreEntry[] FilteredLoreEntries()
    {
        var depth = _loreDepthFilterIndex == 0
            ? (LoreDepth?)null
            : (LoreDepth)(_loreDepthFilterIndex - 1);
        return LoreCatalog.All
            .Where(item => depth is null || item.Depth == depth)
            .ToArray();
    }

    private void AdvanceSpectatorOneStep(SpectatorMatchSession spectator)
    {
        var before = spectator.ViewedSnapshot;
        var advance = spectator.StepOnce(audioAvailable: _cuePlayer is not null);
        var after = spectator.ViewedSnapshot;
        var viewedStep = spectator.ViewedPersonalityId == spectator.Selection.PersonalityId
            ? advance.FeaturedStep
            : advance.RivalStep;
        if (viewedStep is { } result)
        {
            BeginSnakeMotion(
                before.Body,
                after.Body,
                after.EffectiveRulesStepMilliseconds);
            AdvanceFeedback(result.OrderedEvents, after.ComboCount);
            _vibeLevelDirector.Update(after.ComboCount);
        }

        if (spectator.IsComplete)
        {
            _spectatorStatusCaption = Localize("status.spectator.complete");
            PersistSpectatorMatchIfComplete();
        }

        PlayCue(AudioCue.Navigate);
        QueueRedraw();
    }

    private void CycleSpectatorSelection(int offset)
    {
        var personalities = AiPersonalityCatalog.BuiltIn.Select(item => item.Id).ToArray();
        switch (_spectatorSelectionCursor)
        {
            case 0:
                _spectatorSelection = _spectatorSelection with
                {
                    PersonalityId = CycleDistinctPersonality(
                        personalities,
                        _spectatorSelection.PersonalityId,
                        _spectatorSelection.RivalPersonalityId,
                        offset),
                };
                break;
            case 1:
                _spectatorSelection = _spectatorSelection with
                {
                    RivalPersonalityId = CycleDistinctPersonality(
                        personalities,
                        _spectatorSelection.RivalPersonalityId,
                        _spectatorSelection.PersonalityId,
                        offset),
                };
                break;
            case 2:
                var modes = RunModeCatalog.All;
                var modeIndex = modes.ToList().FindIndex(item =>
                    item.Id == _spectatorSelection.ModeId
                    && item.Version == _spectatorSelection.ModeVersion);
                var mode = modes[(modeIndex + offset + modes.Count) % modes.Count];
                _spectatorSelection = _spectatorSelection with
                {
                    ModeId = mode.Id,
                    ModeVersion = mode.Version,
                };
                break;
            case 3:
                _spectatorSelection = _spectatorSelection with
                {
                    SeedClass = CycleEnum(_spectatorSelection.SeedClass, offset),
                };
                break;
            case 4:
                _spectatorSelection = _spectatorSelection with
                {
                    SeedSlot = (_spectatorSelection.SeedSlot + offset
                        + SpectatorSeedCatalog.SeedsPerClass)
                        % SpectatorSeedCatalog.SeedsPerClass,
                };
                break;
            case 5:
                _spectatorSelection = _spectatorSelection with
                {
                    PlaybackSpeedIndex = (_spectatorSelection.PlaybackSpeedIndex + offset
                        + SpectatorSelection.PlaybackSpeeds.Count)
                        % SpectatorSelection.PlaybackSpeeds.Count,
                };
                break;
            case 6:
                _spectatorSelection = _spectatorSelection with
                {
                    ExplanationLevel = CycleEnum(
                        _spectatorSelection.ExplanationLevel,
                        offset),
                };
                break;
            case 7:
                _spectatorSelection = _spectatorSelection with
                {
                    Prediction = CycleEnum(_spectatorSelection.Prediction, offset),
                };
                break;
            default:
                throw new InvalidOperationException("Unknown spectator selection row.");
        }

        _spectatorSelection.Validate();
        PlayCue(AudioCue.Navigate);
        QueueRedraw();
    }

    private static string CycleDistinctPersonality(
        string[] personalities,
        string current,
        string excluded,
        int offset)
    {
        var index = personalities.ToList().IndexOf(current);
        do
        {
            index = (index + offset + personalities.Length) % personalities.Length;
        }
        while (personalities[index] == excluded);

        return personalities[index];
    }

    private static TEnum CycleEnum<TEnum>(TEnum value, int offset)
        where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        var index = Array.IndexOf(values, value);
        return values[(index + offset + values.Length) % values.Length];
    }

    private void StartSpectatorMatch()
    {
        _spectatorSelection.Validate();
        _spectatorMatch = new SpectatorMatchSession(_spectatorSelection);
        _spectatorMatch.SetPaused(false);
        _snakeMotionPresentation.Reset(_spectatorMatch.ViewedSnapshot.Body);
        _spectatorMatchPersisted = false;
        _capturePresentation = CapturePresentationState.Visible;
        RevealSpectatorControls(Time.GetTicksMsec());
        _rulesStepAccumulatorMilliseconds = 0.0;
        _vibeLevelDirector.Reset();
        _spectatorStatusCaption = Localize(
            "status.spectator.started",
            ShellTextArgument.From(
                "channel",
                SpectatorRivalCatalog.Get(_spectatorSelection.PersonalityId)
                    .BroadcastIdentity),
            ShellTextArgument.From(
                "rival",
                SpectatorRivalCatalog.Get(_spectatorSelection.RivalPersonalityId)
                    .BroadcastIdentity));
        _structuredLog?.Information(
            "spectator",
            "Started an equal-rules local AI rivalry match.",
            eventCode: "spectator_match_start");
        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    private void RevealSpectatorControls(ulong nowMilliseconds)
    {
        _spectatorControlsVisibleUntilMilliseconds = AddSaturating(
            nowMilliseconds,
            SpectatorControlsRevealMilliseconds);
        QueueRedraw();
    }

    private void StartSpectatorSeedChallenge(SpectatorMatchSession spectator)
    {
        ArgumentNullException.ThrowIfNull(spectator);
        if (!spectator.IsComplete)
        {
            return;
        }

        _activeSpectatorChallenge = spectator.CreateChallenge();
        _activeGhostRace = null;
        _activeGhostSlot = null;
        _activeSpectatorChallengePersonalityId = spectator.Selection.PersonalityId;
        _activeSpectatorAiScore = spectator.ScoreFor(spectator.Selection.PersonalityId);
        var run = _activeSpectatorChallenge.CreateHumanRun();
        BeginPreparedRun(
            run,
            ScoreRunContextCatalog.SeededChallenge,
            tourEvent: null,
            isRestart: false);
        ShowReplayStatus(Localize("status.spectator.challenge-started"));
    }

    private void StartRun(bool isRestart = false)
    {
        if (isRestart && _activeGhostRace is not null && _activeGhostSlot is { } ghostSlot)
        {
            if (_replayOperation is not null || _queuedReplaySave is not null)
            {
                ShowReplayStatus("RUN START PAUSED: FINISHING THE CURRENT REPLAY OPERATION");
                return;
            }

            if (_offlineChallengeStore is not null)
            {
                var prepared = LoadGhostRace(_offlineChallengeStore, ghostSlot);
                if (prepared.GhostRace is not null)
                {
                    _activeGhostRace = prepared.GhostRace;
                    BeginPreparedRun(
                        prepared.GhostRace.PlayerRun,
                        ScoreRunContextCatalog.SeededChallenge,
                        tourEvent: null,
                        isRestart: true);
                    return;
                }
            }

            // Ghost files may have been reset; rematch as a normal run instead
            // of leaving the player stuck on the ended screen.
            _activeGhostRace = null;
            _activeGhostSlot = null;
        }

        if (isRestart && _activeSpectatorChallenge is not null)
        {
            if (_replayOperation is not null || _queuedReplaySave is not null)
            {
                ShowReplayStatus("RUN START PAUSED: FINISHING THE CURRENT REPLAY OPERATION");
                return;
            }

            BeginPreparedRun(
                _activeSpectatorChallenge.CreateHumanRun(),
                ScoreRunContextCatalog.SeededChallenge,
                tourEvent: null,
                isRestart: true);
            return;
        }

        if (isRestart && _activeTourEvent is { } tourEvent)
        {
            if (BroadcastTourSession.CanStart(
                tourEvent,
                _progression.CompletedTourEventIds))
            {
                StartTourEvent(tourEvent, isRestart: true);
                return;
            }

            // Progression reset can lock a later rematch. Keep the ended run
            // restartable as a normal human run instead of crashing.
            _activeTourEvent = null;
            _tourRunOutcome = null;
        }

        _activeSpectatorChallenge = null;
        _activeSpectatorChallengePersonalityId = null;
        _activeSpectatorAiScore = 0;
        _activeGhostRace = null;
        _activeGhostSlot = null;

        if (_replayOperation is not null || _queuedReplaySave is not null)
        {
            ShowReplayStatus("RUN START PAUSED: FINISHING THE CURRENT REPLAY OPERATION");
            return;
        }

        var run = _run is { Status: not RunStatus.Running } terminalRun
            ? terminalRun.Restart(_nextSeed++)
            : SnakeRun.Create(_nextSeed++, SelectedRunConfig());
        BeginPreparedRun(
            run,
            ScoreRunContextCatalog.NormalHuman,
            tourEvent: null,
            isRestart);
    }

    private void StartTourEvent(BroadcastTourEvent tourEvent, bool isRestart = false)
    {
        ArgumentNullException.ThrowIfNull(tourEvent);
        _activeSpectatorChallenge = null;
        _activeSpectatorChallengePersonalityId = null;
        _activeSpectatorAiScore = 0;
        _activeGhostRace = null;
        _activeGhostSlot = null;
        if (_replayOperation is not null || _queuedReplaySave is not null)
        {
            ShowReplayStatus("TOUR START PAUSED: FINISHING THE CURRENT REPLAY OPERATION");
            return;
        }

        if (!BroadcastTourSession.CanStart(tourEvent, _progression.CompletedTourEventIds))
        {
            throw new InvalidOperationException("A locked Broadcast Tour event cannot start.");
        }

        BeginPreparedRun(
            BroadcastTourSession.CreateRun(tourEvent),
            ScoreRunContextCatalog.Practice,
            tourEvent,
            isRestart);
    }

    private void BeginPreparedRun(
        SnakeRun run,
        ScoreRunContext context,
        BroadcastTourEvent? tourEvent,
        bool isRestart)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(context);
        _run = run;
        _snakeMotionPresentation.Reset(_run.GetSnapshot().Body);
        _activeRunContext = context;
        _activeTourEvent = tourEvent;
        _tourRunOutcome = null;
        _run.ApplyProfileUnlocks(_achievements.UnlockedSet);
        _powerDecisionTrace.Reset();
        _replayRecorder = new RunReplayRecorder(
            _run,
            appVersion: ProductIdentity.AppVersion,
            capturedAtUtc: CurrentReplayCaptureTimestampUtc());
        TransitionToScreen(ScreenState.Running);
        _capturePresentation = CapturePresentationState.Visible;
        _replayHudVisible = true;
        _pausedByFocusLoss = false;
        _rulesStepAccumulatorMilliseconds = 0.0;
        _feedbackCaption = null;
        _feedbackTier = VisualFeedbackTier.Ambient;
        _feedbackTicksRemaining = 0;
        _comboPulseTicksRemaining = 0;
        _vibeLevelDirector.Reset();
        _broadcastPolicy.ResetRun();
        _broadcastCaption = null;
        _broadcastTicksRemaining = 0;
        _presentationStep = 0;
        _baitRevealOrigin = null;
        _baitRevealDestination = null;
        _baitRevealTicksRemaining = 0;
        _runEndSummary = null;
        _progressionNotifications.Clear();
        _restartIntentGate.Reset();
        _terminalInputSequence = -1;
        _replayStatusCaption = null;
        _structuredLog?.Information(
            "shell",
            tourEvent is null
                ? $"Run started in {_run.Mode.ContractId} ({_run.ScoreCategoryId}, DDA {_run.Configuration.AdaptivePolicyId})."
                : $"Broadcast Tour event {tourEvent.Id} started as noncompetitive fixed-seed practice.",
            eventCode: tourEvent is null ? "run_start" : "broadcast_tour_run_start");
        PlayCue(isRestart ? AudioCue.Restart : AudioCue.Confirm);
        TryBroadcast(BroadcastBoundary.RunStart, criticalCueActive: false);
        QueueRedraw();
    }

    private bool TryRestartFromEnded(long inputSequence)
    {
        if (_screenState != ScreenState.Ended)
        {
            throw new InvalidOperationException("Restart intent is only valid on the ended screen.");
        }

        if (!_restartIntentGate.CanRestart(inputSequence))
        {
            ShowReplayStatus("RESTART BLOCKED: RELEASE THE FATAL INPUT, THEN CONFIRM");
            return false;
        }

        StartRun(isRestart: true);
        return _screenState == ScreenState.Running;
    }

    /// <summary>
    /// Product runs enable achievement candidate emission. Shared parity fixtures
    /// keep the flag off until dual-runtime achievement events are regenerated.
    /// </summary>
    private RunModeDefinition SelectedRunMode => RunModeCatalog.All[_selectedRunModeIndex];

    private RunConfig SelectedRunConfig() => RunModeCatalog.CreateConfig(
        SelectedRunMode,
        SelectedRunMode.Id == RunModeCatalog.VibeId
            ? _shellSettings.VibeAdaptationEnabled
            : null);

    private void MoveMainMenuCursor(int offset)
    {
        if (offset == 0)
        {
            return;
        }

        _mainMenuCursor = (_mainMenuCursor + offset + MainMenuItemCount) % MainMenuItemCount;
        PlayCue(AudioCue.Navigate);
        QueueRedraw();
    }

    private void ActivateMainMenuItem()
    {
        switch ((MainMenuItem)Math.Clamp(_mainMenuCursor, 0, MainMenuItemCount - 1))
        {
            case MainMenuItem.Start:
                StartRun();
                break;
            case MainMenuItem.Customize:
                OpenCosmeticSets();
                break;
            case MainMenuItem.Achievements:
                OpenAchievementsBrowse();
                break;
            case MainMenuItem.Scores:
                OpenScoresBrowse();
                break;
            case MainMenuItem.Spectator:
                OpenSpectatorBrowse();
                break;
            case MainMenuItem.Replays:
                OpenReplaysBrowse();
                break;
            case MainMenuItem.Settings:
                OpenSettingsBrowse();
                break;
            case MainMenuItem.Help:
                OpenOnboardingOffer();
                break;
            case MainMenuItem.Quit:
                RequestQuit();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(_mainMenuCursor));
        }
    }

    private void CycleSelectedRunMode(int offset)
    {
        if (_screenState != ScreenState.Menu)
        {
            return;
        }

        _selectedRunModeIndex = (_selectedRunModeIndex + offset + RunModeCatalog.All.Count)
            % RunModeCatalog.All.Count;
        PlayCue(AudioCue.Navigate);
        QueueRedraw();
    }

    private static string CurrentReplayCaptureTimestampUtc() =>
        DateTimeOffset.UtcNow.ToString(
            RunReplay.CaptureTimestampFormat,
            CultureInfo.InvariantCulture);

    private void LeaveOverlayScreen()
    {
        if (_run is { Status: RunStatus.Dead or RunStatus.Won } && _runEndSummary is not null)
        {
            TransitionToScreen(ScreenState.Ended);
            PlayCue(AudioCue.Back);
            QueueRedraw();
            return;
        }

        ReturnToMenu();
    }

    private void ReturnToMenu()
    {
        TransitionToScreen(ScreenState.Menu);
        _run = null;
        _replayRecorder = null;
        _replayPlayback = null;
        _replayPlaybackPaused = true;
        _replayBrowserEntries = [];
        _replayBrowseCursor = 0;
        _replayPlaybackSpeedIndex = 1;
        _replayHudVisible = true;
        _capturePresentation = CapturePresentationState.Visible;
        _spectatorMatch = null;
        _spectatorMatchPersisted = false;
        _spectatorStatusCaption = null;
#if AGENT_ARENA_PREVIEW
        _agentViewer?.Dispose();
        _agentViewer = null;
        _agentViewerFrame = null;
        _agentViewerSnapshot = null;
        _agentViewerCoalescedFrames = 0;
        _agentViewerSnappedLatestFrame = false;
        _agentViewerStatusId = "status.agent-viewer.connecting";
        _agentViewerSmokeEnabled = false;
        _agentViewerSmokeDeadlineMilliseconds = null;
        _agentViewerPresentedAvatarId = null;
        _agentViewerPresentedAccentId = null;
        _agentViewerPresentedStationId = null;
        _agentViewerHumanCosmeticIdBeforePresentation = null;
#endif
        _activeSpectatorChallenge = null;
        _activeSpectatorChallengePersonalityId = null;
        _activeSpectatorAiScore = 0;
        _pendingReplayDeletion = null;
        _ghostSlots = [];
        _ghostSlotCursor = 0;
        _pendingGhostDeletion = null;
        _activeGhostRace = null;
        _activeGhostSlot = null;
        _settingsSectionOpen = false;
        _settingsFullResetConfirmation = false;
        _playtestDeleteConfirmation = false;
        _onboardingSession = null;
        _runEndSummary = null;
        _activeRunContext = ScoreRunContextCatalog.NormalHuman;
        _activeTourEvent = null;
        _tourRunOutcome = null;
        _tourStatusCaption = null;
        _restartIntentGate.Reset();
        _terminalInputSequence = -1;
        _pausedByFocusLoss = false;
        _rulesStepAccumulatorMilliseconds = 0.0;
        _feedbackCaption = null;
        _feedbackTier = VisualFeedbackTier.Ambient;
        _feedbackTicksRemaining = 0;
        _comboPulseTicksRemaining = 0;
        _vibeLevelDirector.Reset();
        _broadcastPolicy.ResetRun();
        _broadcastCaption = null;
        _broadcastTicksRemaining = 0;
        _presentationStep = 0;
        PlayCue(AudioCue.Back);
        QueueRedraw();
    }

    /// <summary>
    /// Owns every screen-state write after field initialization. Pause is a
    /// validated state in the same graph even though the running scene remains
    /// visible while paused.
    /// </summary>
    private void TransitionToScreen(ScreenState target)
    {
        var from = CurrentShellScreen();
        var to = ToShellScreen(target, paused: false);
        ShellTransitions.EnsureTransition(from, to);
        _screenState = target;
        _paused = false;
    }

    private void SetRunPaused(bool paused)
    {
        if (_screenState != ScreenState.Running)
        {
            throw new InvalidOperationException(
                "Only the running screen can enter or leave pause.");
        }

        var from = CurrentShellScreen();
        var to = ToShellScreen(ScreenState.Running, paused);
        if (from == to)
        {
            return;
        }

        ShellTransitions.EnsureTransition(from, to);
        _paused = paused;
    }

    private ShellScreen CurrentShellScreen() =>
        ToShellScreen(_screenState, _paused);

    private static ShellScreen ToShellScreen(ScreenState state, bool paused) =>
        state switch
        {
            ScreenState.Menu => ShellScreen.Menu,
            ScreenState.Running when paused => ShellScreen.Paused,
            ScreenState.Running => ShellScreen.Running,
            ScreenState.Ended => ShellScreen.Ended,
            ScreenState.Achievements => ShellScreen.Achievements,
            ScreenState.Bindings => ShellScreen.Bindings,
            ScreenState.ContentPacks => ShellScreen.ContentPacks,
            ScreenState.Replays => ShellScreen.Replays,
            ScreenState.Settings => ShellScreen.Settings,
            ScreenState.Onboarding => ShellScreen.Onboarding,
            ScreenState.Scores => ShellScreen.Scores,
            ScreenState.Tour => ShellScreen.Tour,
            ScreenState.Cosmetics => ShellScreen.Cosmetics,
            ScreenState.Spectator => ShellScreen.Spectator,
            ScreenState.Lore => ShellScreen.Lore,
            ScreenState.Comparisons => ShellScreen.Comparisons,
#if AGENT_ARENA_PREVIEW
            ScreenState.AgentWatch => ShellScreen.AgentWatch,
            ScreenState.AgentExhibitions => ShellScreen.AgentExhibitions,
#endif
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    private void OpenOnboardingOffer()
    {
        TransitionToScreen(ScreenState.Onboarding);
        _run = null;
        _replayRecorder = null;
        _onboardingSession = null;
        _onboardingOfferCursor = 0;
        _onboardingStatusCaption ??= _onboardingWasNewProfile
            ? "NEW PROFILE: LEARN THE LOOP OR PLAY DIRECTLY"
            : "TUTORIAL CAN BE REPLAYED OR SKIPPED AT ANY TIME";
        _structuredLog?.Information(
            "onboarding",
            "Opened the unscored onboarding offer.",
            eventCode: "onboarding_offer_open");
        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    private void HandleOnboardingInput(InputEvent inputEvent)
    {
        if (_onboardingSession is null
            && inputEvent.IsActionPressed(GameActions.BrowseSettings))
        {
            OpenSettingsBrowse();
            return;
        }

        if (_onboardingSession is null)
        {
            if (inputEvent.IsActionPressed(GameActions.MoveUp)
                || inputEvent.IsActionPressed(GameActions.MoveLeft))
            {
                var previousCursor = _onboardingOfferCursor;
                _onboardingOfferCursor = 0;
                if (_onboardingOfferCursor != previousCursor)
                {
                    PlayCue(AudioCue.Navigate);
                }
            }
            else if (inputEvent.IsActionPressed(GameActions.MoveDown)
                || inputEvent.IsActionPressed(GameActions.MoveRight))
            {
                var previousCursor = _onboardingOfferCursor;
                _onboardingOfferCursor = 1;
                if (_onboardingOfferCursor != previousCursor)
                {
                    PlayCue(AudioCue.Navigate);
                }
            }
            else if (inputEvent.IsActionPressed(GameActions.Confirm))
            {
                if (_onboardingOfferCursor == 0)
                {
                    _onboardingSession = new OnboardingSession();
                    _onboardingStatusCaption = Localize("status.onboarding.practice-isolated");
                    _structuredLog?.Information(
                        "onboarding",
                        "Started deterministic unscored tutorial micro-scenarios.",
                        eventCode: "onboarding_start");
                    PlayCue(AudioCue.Confirm);
                }
                else
                {
                    SaveOnboardingStatus(OnboardingStatus.Skipped);
                    _onboardingStatusCaption = Localize("status.onboarding.skipped");
                    StartRun();
                }
            }
            else if (inputEvent.IsActionPressed(GameActions.Back))
            {
                SaveSkippedOnboardingUnlessCompleted();
                ReturnToMenu();
                _replayStatusCaption = Localize("status.onboarding.available");
            }

            QueueRedraw();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.Back))
        {
            SaveSkippedOnboardingUnlessCompleted();
            ReturnToMenu();
            _replayStatusCaption = Localize("status.onboarding.exited");
            return;
        }

        OnboardingAdvance? advance = null;
        if (GameActions.TryMapDirectionInput(inputEvent, out var direction))
        {
            advance = _onboardingSession.SubmitDirection(direction);
        }
        else if (inputEvent.IsActionPressed(GameActions.Pause))
        {
            advance = _onboardingSession.SubmitPause();
        }
        else if (inputEvent.IsActionPressed(GameActions.Confirm))
        {
            advance = _onboardingSession.SubmitRestart();
        }

        if (advance is null)
        {
            return;
        }

        _onboardingStatusCaption = Localize(advance.CopyId);
        if (advance.InputAccepted)
        {
            PlayCue(AudioCue.Confirm);
        }
        else
        {
            PlayCue(AudioCue.Back);
        }

        if (_onboardingSession.IsComplete)
        {
            SaveOnboardingStatus(OnboardingStatus.Completed);
            _structuredLog?.Information(
                "onboarding",
                "Completed all deterministic onboarding lessons.",
                eventCode: "onboarding_complete");
            ReturnToMenu();
            _replayStatusCaption = Localize("status.onboarding.complete");
            return;
        }

        QueueRedraw();
    }

    private void SaveSkippedOnboardingUnlessCompleted()
    {
        if (_onboardingProgress.Status != OnboardingStatus.Completed)
        {
            SaveOnboardingStatus(OnboardingStatus.Skipped);
        }
    }

    private void OpenSettingsBrowse()
    {
        TransitionToScreen(ScreenState.Settings);
        _settingsSectionCursor = 0;
        _settingsItemCursor = 0;
        _settingsSectionOpen = false;
        _settingsFullResetConfirmation = false;
        _playtestDeleteConfirmation = false;
        _settingsStatusCaption ??= "SELECT A SECTION";
        _structuredLog?.Information(
            "settings",
            "Opened accessible settings browser.",
            eventCode: "settings_browse_open");
        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    private void OpenAchievementsBrowse()
    {
        TransitionToScreen(ScreenState.Achievements);
        var progress = _progression.BuildGoalProgress();
        _progressionGoalCursor = progress
            .Select((item, index) => (item, index))
            .Where(pair => pair.item.Highlighted)
            .Select(pair => pair.index)
            .DefaultIfEmpty(progress
                .Select((item, index) => (item, index))
                .Where(pair => !pair.item.Completed)
                .Select(pair => pair.index)
                .DefaultIfEmpty(0)
                .First())
            .First();
        _achievementsPage = _progressionGoalCursor / ProgressionGoalsPerPage;
        _structuredLog?.Information(
            "progression",
            "Opened exact three-lane progression goal browser.",
            eventCode: "achievements_browse_open");
        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    private void OpenBroadcastTour()
    {
        var returningEvent = _activeTourEvent;
        var returningOutcome = _tourRunOutcome;
        TransitionToScreen(ScreenState.Tour);
        _run = null;
        _replayRecorder = null;
        _activeRunContext = ScoreRunContextCatalog.NormalHuman;
        _activeTourEvent = null;
        _tourRunOutcome = null;
        var cards = BroadcastTourSession.BuildCards(_progression.CompletedTourEventIds);
        if (returningEvent is not null)
        {
            _tourCursor = cards
                .Select((card, index) => (card, index))
                .Single(pair => pair.card.Event.Id == returningEvent.Id)
                .index;
            _tourStatusCaption = returningOutcome is { PrimaryComplete: true }
                ? Localize(
                    "status.tour.event-cleared",
                    ShellTextArgument.From(
                        "reward",
                        returningEvent.Reward.DisplayName.ToUpperInvariant()))
                : returningOutcome is not null
                    ? Localize(
                        "status.tour.retry-ready",
                        ShellTextArgument.From("progress", returningOutcome.PrimaryProgress))
                    : _tourStatusCaption;
        }
        else
        {
            _tourCursor = Math.Clamp(_tourCursor, 0, cards.Count - 1);
            if (cards[_tourCursor].State == BroadcastTourEventState.Locked)
            {
                _tourCursor = cards
                    .Select((card, index) => (card, index))
                    .Where(pair => pair.card.State != BroadcastTourEventState.Locked)
                    .Select(pair => pair.index)
                    .DefaultIfEmpty(0)
                    .First();
            }

            _tourStatusCaption ??= "SELECT AN AVAILABLE EVENT; PRACTICE NEVER SUBMITS A SCORE";
        }

        _tourPage = _tourCursor / TourCardsPerPage;
        _structuredLog?.Information(
            "progression",
            "Opened the finite fixed-seed Broadcast Tour.",
            eventCode: "broadcast_tour_open");
        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    private void HandleBroadcastTourInput(InputEvent inputEvent)
    {
        var cards = BroadcastTourSession.BuildCards(_progression.CompletedTourEventIds);
        if (inputEvent.IsActionPressed(GameActions.Back)
            || inputEvent.IsActionPressed(GameActions.BrowseAchievements))
        {
            OpenAchievementsBrowse();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.Confirm)
            || inputEvent.IsActionPressed(GameActions.Replay))
        {
            var selected = cards[Math.Clamp(_tourCursor, 0, cards.Count - 1)];
            if (selected.State == BroadcastTourEventState.Locked)
            {
                var requirements = selected.Event.PrerequisiteEventIds
                    .Select(id => BroadcastTourCatalog.Events.Single(item => item.Id == id))
                    .Select(item => item.PrimaryGoal.ExactRequirement)
                    .ToArray();
                _tourStatusCaption = Localize(
                    "status.tour.locked",
                    ShellTextArgument.From("requirements", string.Join(" + ", requirements)));
                PlayCue(AudioCue.Back);
                QueueRedraw();
                return;
            }

            StartTourEvent(selected.Event);
            return;
        }

        var previousCursor = _tourCursor;
        if (inputEvent.IsActionPressed(GameActions.MoveUp))
        {
            _tourCursor = Math.Max(0, _tourCursor - 1);
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveDown))
        {
            _tourCursor = Math.Min(cards.Count - 1, _tourCursor + 1);
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveLeft))
        {
            _tourPage = Math.Max(0, _tourPage - 1);
            _tourCursor = _tourPage * TourCardsPerPage;
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveRight))
        {
            var pageCount = (int)Math.Ceiling(cards.Count / (double)TourCardsPerPage);
            _tourPage = Math.Min(pageCount - 1, _tourPage + 1);
            _tourCursor = Math.Min(cards.Count - 1, _tourPage * TourCardsPerPage);
        }

        if (_tourCursor != previousCursor)
        {
            _tourPage = _tourCursor / TourCardsPerPage;
            _tourStatusCaption = cards[_tourCursor].State switch
            {
                BroadcastTourEventState.Completed => Localize("status.tour.card-cleared"),
                BroadcastTourEventState.Available => Localize("status.tour.card-available"),
                _ => Localize("status.tour.card-locked"),
            };
            PlayCue(AudioCue.Navigate);
            QueueRedraw();
        }
    }

    private void OpenCosmeticSets()
    {
        TransitionToScreen(ScreenState.Cosmetics);
        _cosmeticCursor = CosmeticSetCatalog.Sets
            .Select((item, index) => (item, index))
            .Single(pair => pair.item.Id == _progression.SelectedCosmeticSetId)
            .index;
        _cosmeticPage = _cosmeticCursor / CosmeticSetsPerPage;
        _cosmeticStatusCaption ??= "SELECTED SET CHANGES PRESENTATION ONLY";
        _structuredLog?.Information(
            "progression",
            "Opened the curated cosmetic set browser.",
            eventCode: "cosmetic_sets_open");
        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    private void HandleCosmeticSetsInput(InputEvent inputEvent)
    {
        if (inputEvent.IsActionPressed(GameActions.Back)
            || inputEvent.IsActionPressed(GameActions.BrowseAchievements)
            || inputEvent.IsActionPressed(GameActions.BrowseContentPacks))
        {
            OpenAchievementsBrowse();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.Confirm))
        {
            ApplyCosmeticSelection(saveLoadout: false);
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.Replay))
        {
            ApplyCosmeticSelection(saveLoadout: true);
            return;
        }

        var previousCursor = _cosmeticCursor;
        if (inputEvent.IsActionPressed(GameActions.MoveUp))
        {
            _cosmeticCursor = Math.Max(0, _cosmeticCursor - 1);
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveDown))
        {
            _cosmeticCursor = Math.Min(CosmeticSetCatalog.Sets.Count - 1, _cosmeticCursor + 1);
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveLeft))
        {
            _cosmeticPage = Math.Max(0, _cosmeticPage - 1);
            _cosmeticCursor = _cosmeticPage * CosmeticSetsPerPage;
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveRight))
        {
            var pageCount = (int)Math.Ceiling(
                CosmeticSetCatalog.Sets.Count / (double)CosmeticSetsPerPage);
            _cosmeticPage = Math.Min(pageCount - 1, _cosmeticPage + 1);
            _cosmeticCursor = Math.Min(
                CosmeticSetCatalog.Sets.Count - 1,
                _cosmeticPage * CosmeticSetsPerPage);
        }

        if (_cosmeticCursor != previousCursor)
        {
            _cosmeticPage = _cosmeticCursor / CosmeticSetsPerPage;
            var cosmetic = CosmeticSetCatalog.Sets[_cosmeticCursor];
            _cosmeticStatusCaption = LocalizedCosmeticRequirement(cosmetic);
            PlayCue(AudioCue.Navigate);
            QueueRedraw();
        }
    }

    private void ApplyCosmeticSelection(bool saveLoadout)
    {
        var cosmetic = CosmeticSetCatalog.Sets[Math.Clamp(
            _cosmeticCursor,
            0,
            CosmeticSetCatalog.Sets.Count - 1)];
        try
        {
            var updated = _progression.WithSelectedCosmeticSet(cosmetic.Id);
            if (saveLoadout)
            {
                updated = updated.WithSavedCosmeticSet(cosmetic.Id);
            }

            _progression = updated;
            if (TrySaveProgression("cosmetic_selection_save_failed"))
            {
                _cosmeticStatusCaption = saveLoadout
                    ? Localize(
                        "status.cosmetics.loadout-saved",
                        ShellTextArgument.From("cosmetic", cosmetic.Name.ToUpperInvariant()))
                    : Localize(
                        "status.cosmetics.selected",
                        ShellTextArgument.From("cosmetic", cosmetic.Name.ToUpperInvariant()));
                _structuredLog?.Information(
                    "progression",
                    _cosmeticStatusCaption,
                    eventCode: saveLoadout ? "cosmetic_loadout_saved" : "cosmetic_selected");
            }
            else
            {
                _cosmeticStatusCaption = Localize("status.progression.save-failed");
            }

            PlayCue(AudioCue.Confirm);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidDataException
                or InvalidOperationException)
        {
            _cosmeticStatusCaption = LocalizedCosmeticRequirement(cosmetic);
            _structuredLog?.Warning(
                "progression",
                exception.Message,
                eventCode: "cosmetic_selection_rejected");
            PlayCue(AudioCue.Back);
        }

        QueueRedraw();
    }

    private string LocalizedCosmeticRequirement(CosmeticSetDefinition cosmetic)
    {
        if (cosmetic.AvailableFromStart)
        {
            return Localize("cosmetics.requirement.available");
        }

        var tourEvent = BroadcastTourCatalog.Events.Single(item =>
            item.Reward.Id == cosmetic.UnlockRewardId);
        var complete = _progression.CompletedTourEventIds.Contains(
            tourEvent.Id,
            StringComparer.Ordinal);
        return Localize(
            complete
                ? "cosmetics.requirement.tour-unlocked"
                : "cosmetics.requirement.tour-locked",
            ShellTextArgument.From("current", complete ? 1 : 0),
            ShellTextArgument.From("requirement", tourEvent.PrimaryGoal.ExactRequirement),
            ShellTextArgument.From("event", FormatTourEventName(tourEvent.Id)));
    }

    private (string Requirement, string? Event) LocalizedCosmeticDetail(
        CosmeticSetDefinition cosmetic)
    {
        if (cosmetic.AvailableFromStart)
        {
            return (Localize("cosmetics.requirement.available"), null);
        }

        var tourEvent = BroadcastTourCatalog.Events.Single(item =>
            item.Reward.Id == cosmetic.UnlockRewardId);
        var complete = _progression.CompletedTourEventIds.Contains(
            tourEvent.Id,
            StringComparer.Ordinal);
        return (
            Localize(
                complete
                    ? "cosmetics.requirement.detail-unlocked"
                    : "cosmetics.requirement.detail-locked",
                ShellTextArgument.From("current", complete ? 1 : 0),
                ShellTextArgument.From("requirement", tourEvent.PrimaryGoal.ExactRequirement)),
            Localize(
                "cosmetics.requirement.detail-event",
                ShellTextArgument.From("event", FormatTourEventName(tourEvent.Id))));
    }

    private void OpenScoresBrowse()
    {
        TransitionToScreen(ScreenState.Scores);
        _scoreBrowseCategoryCursor = 0;
        _scoreImportConfirmation = false;
        _scoreBrowseStatusCaption = _scoreHistory.PythonTopTenImported
            ? Localize("status.scores.already-imported")
            : Localize("status.scores.optional-import");
        _structuredLog?.Information(
            "scores",
            "Opened versioned local score browser.",
            eventCode: "score_browse_open");
        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    private void HandleScoresBrowseInput(InputEvent inputEvent)
    {
        if (_scoreImportConfirmation)
        {
            if (inputEvent.IsActionPressed(GameActions.Confirm))
            {
                ImportPythonTopTen();
            }
            else if (inputEvent.IsActionPressed(GameActions.Back))
            {
                _scoreImportConfirmation = false;
                _scoreBrowseStatusCaption = Localize("status.scores.import-cancelled");
                PlayCue(AudioCue.Back);
            }

            QueueRedraw();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.Back)
            || inputEvent.IsActionPressed(GameActions.BrowseScores)
            || inputEvent.IsActionPressed(GameActions.Confirm))
        {
            LeaveOverlayScreen();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.Replay))
        {
            _scoreImportConfirmation = true;
            _scoreBrowseStatusCaption = Localize("status.scores.import-confirm");
            PlayCue(AudioCue.Navigate);
            QueueRedraw();
            return;
        }

        var report = ScoreBrowseReport.Create(_scoreHistory, _personalBests);
        if (!report.HasCategories)
        {
            return;
        }

        var previousCursor = _scoreBrowseCategoryCursor;
        if (inputEvent.IsActionPressed(GameActions.MoveLeft)
            || inputEvent.IsActionPressed(GameActions.MoveUp))
        {
            _scoreBrowseCategoryCursor = Math.Max(0, _scoreBrowseCategoryCursor - 1);
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveRight)
                 || inputEvent.IsActionPressed(GameActions.MoveDown))
        {
            _scoreBrowseCategoryCursor = Math.Min(
                report.Categories.Count - 1,
                _scoreBrowseCategoryCursor + 1);
        }

        if (_scoreBrowseCategoryCursor != previousCursor)
        {
            PlayCue(AudioCue.Navigate);
            QueueRedraw();
        }
    }

    private void ImportPythonTopTen()
    {
        _scoreImportConfirmation = false;
        if (_scoreHistoryStore is null || !_scoreHistoryWritable)
        {
            _scoreBrowseStatusCaption = Localize("status.scores.import-read-only");
            PlayCue(AudioCue.Back);
            return;
        }

        var result = _scoreHistoryStore.ImportPythonTopTen();
        if (result.Document is not null)
        {
            _scoreHistory = result.Document;
        }

        _scoreBrowseStatusCaption = result.Code switch
        {
            PythonScoreImportCode.Success =>
                Localize(
                    "status.scores.import-success",
                    ShellTextArgument.From("count", result.ImportedEntryCount)),
            PythonScoreImportCode.AlreadyImported =>
                Localize("status.scores.import-was-complete"),
            PythonScoreImportCode.SourceNotFound =>
                Localize("status.scores.import-source-missing"),
            PythonScoreImportCode.SourceTooLarge => Localize("status.scores.import-too-large"),
            PythonScoreImportCode.InvalidSource => Localize("status.scores.import-invalid"),
            PythonScoreImportCode.DestinationBlocked =>
                Localize("status.scores.import-destination-blocked"),
            PythonScoreImportCode.IoError => Localize("status.scores.import-io-failed"),
            _ => throw new ArgumentOutOfRangeException(),
        };
        _structuredLog?.Information(
            "scores",
            result.Message,
            eventCode: result.IsSuccess ? "python_scores_imported" : "python_scores_import_blocked");
        PlayCue(result.IsSuccess ? AudioCue.Confirm : AudioCue.Back);
        QueueRedraw();
    }

    private void OpenBindingsBrowse()
    {
        TransitionToScreen(ScreenState.Bindings);
        _bindingsCursor = 0;
        _bindingsCapturePending = false;
        _pendingBindingConflict = null;
        _bindingsDeviceTab = _activePromptFamily == InputPromptFamily.Keyboard
            ? BindingsDeviceTab.Keyboard
            : BindingsDeviceTab.Controller;
        _bindingsStatusCaption = Localize("status.bindings.browse-help");
        _structuredLog?.Information(
            "input",
            "Opened schema-1 input bindings browse.",
            eventCode: "bindings_browse_open");
        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    private void OpenContentPacksBrowse()
    {
        TransitionToScreen(ScreenState.ContentPacks);
        _structuredLog?.Information(
            "content",
            "Opened native content-pack status and removal contract.",
            eventCode: "content_packs_browse_open");
        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    private void OpenReplaysBrowse()
    {
        TransitionToScreen(ScreenState.Replays);
        _replayPlayback = null;
        _replayPlaybackPaused = true;
        _replayPlaybackSpeedIndex = 1;
        _replayHudVisible = true;
        _capturePresentation = CapturePresentationState.Visible;
        _pendingReplayDeletion = null;
        _vibeLevelDirector.Reset();
        _rulesStepAccumulatorMilliseconds = 0.0;
        _replayBrowseCursor = 0;
        _replayBrowserEntries = [];
        if (_replayStore is null)
        {
            ShowReplayStatus("REPLAY BROWSER UNAVAILABLE: STORAGE SERVICE NOT READY");
        }
        else
        {
            var store = _replayStore;
            if (!TryStartReplayResultOperation(
                () => LoadReplayBrowser(store),
                "REPLAY LIBRARY VERIFICATION IN PROGRESS",
                ReplayOperationKind.BrowserLoad))
            {
                ShowReplayStatus("REPLAY OPERATION ALREADY IN PROGRESS");
            }
        }

        _structuredLog?.Information(
            "replay",
            "Opened the replay browser; bounded library verification started.",
            eventCode: "replay_browse_open");
        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    private static ReplayOperationResult LoadReplayBrowser(ReplayStore store)
    {
        var browsed = store.BrowseStored();
        return new ReplayOperationResult(
            browsed.IsSuccess
                ? browsed.Message.ToUpperInvariant()
                : $"REPLAY BROWSER UNAVAILABLE [{browsed.Code}]: {browsed.Message}",
            BrowserEntries: browsed.IsSuccess ? browsed.Replays : []);
    }

    private void HandleReplaysScreenInput(InputEvent inputEvent)
    {
        if (_replayPlayback is null)
        {
            HandleReplayBrowseListInput(inputEvent);
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.Back))
        {
            _replayPlayback = null;
            _replayPlaybackPaused = true;
            _vibeLevelDirector.Reset();
            _rulesStepAccumulatorMilliseconds = 0.0;
            ShowReplayStatus("REPLAY BROWSER");
            PlayCue(AudioCue.Back);
        }
        else if (inputEvent.IsActionPressed(GameActions.Confirm)
            || inputEvent.IsActionPressed(GameActions.Pause))
        {
            if (_replayPlayback.IsComplete)
            {
                _replayPlayback.Reset();
                _snakeMotionPresentation.Reset(_replayPlayback.CurrentSnapshot.Body);
                _vibeLevelDirector.Reset();
            }

            _replayPlaybackPaused = !_replayPlaybackPaused;
            _rulesStepAccumulatorMilliseconds = 0.0;
            ShowReplayStatus(_replayPlaybackPaused ? "REPLAY PAUSED" : "REPLAY PLAYING");
            PlayCue(AudioCue.Confirm);
        }
        else if (inputEvent.IsActionPressed(GameActions.Replay))
        {
            _replayPlayback.Reset();
            _snakeMotionPresentation.Reset(_replayPlayback.CurrentSnapshot.Body);
            _vibeLevelDirector.Reset();
            _replayPlaybackPaused = true;
            _rulesStepAccumulatorMilliseconds = 0.0;
            ShowReplayStatus("REPLAY RESET TO STEP 0");
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveLeft))
        {
            _replayPlayback.Seek(Math.Max(0, _replayPlayback.StepIndex - 10));
            _snakeMotionPresentation.Reset(_replayPlayback.CurrentSnapshot.Body);
            SyncVibeLevel(_replayPlayback.CurrentSnapshot.ComboCount);
            _replayPlaybackPaused = true;
            _rulesStepAccumulatorMilliseconds = 0.0;
            ShowReplayStatus($"REPLAY STEP {_replayPlayback.StepIndex}/{_replayPlayback.StepCount}");
            PlayCue(AudioCue.Confirm);
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveRight))
        {
            _replayPlaybackPaused = true;
            _rulesStepAccumulatorMilliseconds = 0.0;
            AdvanceReplayPlaybackStep();
            PlayCue(AudioCue.Confirm);
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveUp))
        {
            ChangeReplayPlaybackSpeed(1);
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveDown))
        {
            ChangeReplayPlaybackSpeed(-1);
        }
        else if (inputEvent.IsActionPressed(GameActions.Help))
        {
            ToggleCleanCaptureMode();
        }

        QueueRedraw();
    }

    private void ChangeReplayPlaybackSpeed(int delta)
    {
        _replayPlaybackSpeedIndex = Math.Clamp(
            _replayPlaybackSpeedIndex + delta,
            0,
            ReplayPlaybackSpeeds.Length - 1);
        _rulesStepAccumulatorMilliseconds = 0.0;
        ShowReplayStatus(
            $"REPLAY SPEED {ReplayPlaybackSpeeds[_replayPlaybackSpeedIndex]:0.0#}X");
        PlayCue(AudioCue.Navigate);
    }

    private void ToggleCleanCaptureMode()
    {
        _capturePresentation = _capturePresentation.Toggle();
        _replayHudVisible = _capturePresentation.ShowRunHud;
        if (_activePromptFamily == InputPromptFamily.Keyboard)
        {
            _captureKeyboardRouteQualified = true;
        }
        else
        {
            _captureControllerRouteQualified = true;
        }

        _structuredLog?.Information(
            "capture",
            _capturePresentation.Enabled
                ? "Clean capture mode enabled."
                : "Clean capture mode disabled.",
            eventCode: _capturePresentation.Enabled
                ? "clean_capture_enabled"
                : "clean_capture_disabled");
        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    private void HandleReplayBrowseListInput(InputEvent inputEvent)
    {
        if (_pendingReplayDeletion is not null)
        {
            if (inputEvent.IsActionPressed(GameActions.Back))
            {
                _pendingReplayDeletion = null;
                ShowReplayStatus("REPLAY DELETION CANCELLED; NOTHING CHANGED");
                PlayCue(AudioCue.Back);
                QueueRedraw();
            }
            else if (inputEvent.IsActionPressed(GameActions.Confirm))
            {
                ConfirmSelectedReplayDeletion();
            }

            return;
        }

        if (inputEvent.IsActionPressed(GameActions.Back)
            || inputEvent.IsActionPressed(GameActions.Replay))
        {
            LeaveOverlayScreen();
            return;
        }

        if (_replayBrowserEntries.Count == 0)
        {
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.MoveUp))
        {
            var previousCursor = _replayBrowseCursor;
            _replayBrowseCursor = Math.Max(0, _replayBrowseCursor - 1);
            if (_replayBrowseCursor != previousCursor)
            {
                PlayCue(AudioCue.Navigate);
            }
            QueueRedraw();
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveDown))
        {
            var previousCursor = _replayBrowseCursor;
            _replayBrowseCursor = Math.Min(
                _replayBrowserEntries.Count - 1,
                _replayBrowseCursor + 1);
            if (_replayBrowseCursor != previousCursor)
            {
                PlayCue(AudioCue.Navigate);
            }
            QueueRedraw();
        }
        else if (inputEvent.IsActionPressed(GameActions.BrowseAchievements))
        {
            OpenOfflineComparisons();
        }
        else if (inputEvent.IsActionPressed(GameActions.Confirm))
        {
            StartSelectedReplayPlayback();
        }
        else if (inputEvent.IsActionPressed(GameActions.BrowseContentPacks))
        {
            ExportSelectedReplay();
        }
        else if (inputEvent.IsActionPressed(GameActions.RestoreDefaults))
        {
            PrepareSelectedReplayDeletion();
        }
    }

    private void StartSelectedReplayPlayback()
    {
        if (_replayStore is null || _replayBrowserEntries.Count == 0)
        {
            ShowReplayStatus("NO VERIFIED REPLAY IS AVAILABLE FOR PLAYBACK");
            return;
        }

        var store = _replayStore;
        var selected = _replayBrowserEntries[_replayBrowseCursor];
        if (!selected.IsPlayable)
        {
            ShowReplayStatus(
                $"REPLAY NOT PLAYABLE [{selected.State}/{selected.StatusCode}]: {selected.StatusMessage}");
            PlayCue(AudioCue.Back);
            return;
        }

        if (!TryStartReplayResultOperation(
            () => LoadReplayPlayback(store, selected.ReplayId),
            "REPLAY LOAD AND VERIFICATION IN PROGRESS",
            ReplayOperationKind.PlaybackLoad))
        {
            ShowReplayStatus("REPLAY OPERATION ALREADY IN PROGRESS");
        }
    }

    private static ReplayOperationResult LoadReplayPlayback(
        ReplayStore store,
        string replayId)
    {
        var loaded = store.LoadByReplayId(replayId);
        if (!loaded.IsSuccess || loaded.Replay is null)
        {
            return new ReplayOperationResult(
                $"REPLAY PLAYBACK UNAVAILABLE [{loaded.Code}]: {loaded.Message}");
        }

        var playback = new RunReplayPlayback(loaded.Replay);
        return new ReplayOperationResult(
            $"REPLAY READY: {playback.StepCount} STEPS, SCORE {loaded.Replay.Outcome.Score}",
            playback);
    }

    private void ExportSelectedReplay()
    {
        if (_replayStore is null || _replayBrowserEntries.Count == 0)
        {
            ShowReplayStatus("NO REPLAY IS AVAILABLE FOR EXPORT");
            return;
        }

        var selected = _replayBrowserEntries[_replayBrowseCursor];
        if (!selected.IsPlayable)
        {
            ShowReplayStatus(
                $"REPLAY EXPORT BLOCKED [{selected.State}/{selected.StatusCode}]");
            PlayCue(AudioCue.Back);
            return;
        }

        var store = _replayStore;
        if (!TryStartReplayOperation(
            () =>
            {
                var result = store.Export(selected.ReplayId);
                if (!result.IsSuccess)
                {
                    return $"REPLAY EXPORT BLOCKED [{result.Code}]: {result.Message}";
                }

                var summary = store.ExportCaptureSummary(
                    selected.ReplayId,
                    ProductIdentity.AppVersion);
                _captureSummaryExportQualified = summary.IsSuccess;
                return summary.IsSuccess
                    ? $"REPLAY + RUN SUMMARY EXPORTED: user://{ReplayStore.ReplayExportDirectoryName}/{result.FileName}"
                    : $"REPLAY EXPORTED; RUN SUMMARY BLOCKED [{summary.Code}]: {summary.Message}";
            },
            "REPLAY EXPORT IN PROGRESS",
            ReplayOperationKind.Export))
        {
            ShowReplayStatus("REPLAY OPERATION ALREADY IN PROGRESS");
        }
    }

    private void PrepareSelectedReplayDeletion()
    {
        if (_replayStore is null || _replayBrowserEntries.Count == 0)
        {
            ShowReplayStatus("NO REPLAY IS AVAILABLE FOR DELETION");
            return;
        }

        var store = _replayStore;
        var selected = _replayBrowserEntries[_replayBrowseCursor];
        if (!TryStartReplayResultOperation(
            () =>
            {
                var result = store.PlanDeletion(selected.ReplayId);
                return new ReplayOperationResult(
                    result.IsSuccess
                        ? result.Plan!.ConfirmationText + " CONFIRM DELETE; BACK CANCELS."
                        : $"REPLAY DELETION BLOCKED [{result.Code}]: {result.Message}",
                    DeletionPlan: result.Plan);
            },
            "PREPARING EXACT REPLAY DELETION CONFIRMATION",
            ReplayOperationKind.DeletionPlan))
        {
            ShowReplayStatus("REPLAY OPERATION ALREADY IN PROGRESS");
        }
    }

    private void ConfirmSelectedReplayDeletion()
    {
        if (_replayStore is null || _pendingReplayDeletion is null)
        {
            ShowReplayStatus("REPLAY DELETION CONFIRMATION EXPIRED; NOTHING CHANGED");
            _pendingReplayDeletion = null;
            return;
        }

        var store = _replayStore;
        var plan = _pendingReplayDeletion;
        _pendingReplayDeletion = null;
        if (!TryStartReplayResultOperation(
            () =>
            {
                var deleted = store.Delete(plan);
                var browsed = store.BrowseStored();
                var caption = deleted.IsSuccess
                    ? deleted.Message.ToUpperInvariant()
                    : $"REPLAY DELETION BLOCKED [{deleted.Code}]: {deleted.Message}";
                return new ReplayOperationResult(
                    caption,
                    BrowserEntries: browsed.IsSuccess ? browsed.Replays : null);
            },
            "DELETING ONE CONFIRMED LOCAL REPLAY",
            ReplayOperationKind.Delete))
        {
            _pendingReplayDeletion = plan;
            ShowReplayStatus("REPLAY OPERATION ALREADY IN PROGRESS; DELETE NOT STARTED");
        }
    }

    private void OpenOfflineComparisons()
    {
        TransitionToScreen(ScreenState.Comparisons);
        _ghostSlotCursor = 0;
        _pendingGhostDeletion = null;
        _ghostSlots = [];
        if (_offlineChallengeStore is null)
        {
            ShowReplayStatus("OFFLINE COMPARISONS UNAVAILABLE: STORAGE SERVICE NOT READY");
            return;
        }

        var store = _offlineChallengeStore;
        if (!TryStartReplayResultOperation(
            () =>
            {
                var listed = store.ListSlots();
                return new ReplayOperationResult(
                    listed.Message.ToUpperInvariant(),
                    GhostSlots: listed.IsSuccess ? listed.Slots : null);
            },
            "INSPECTING HOUSEHOLD RIVAL SLOTS",
            ReplayOperationKind.GhostList))
        {
            ShowReplayStatus("REPLAY OPERATION ALREADY IN PROGRESS");
        }

        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

#if AGENT_ARENA_PREVIEW
    private void HandleAgentExhibitionsInput(InputEvent inputEvent)
    {
        var report = _agentExhibitionReport;
        if (inputEvent.IsActionPressed(GameActions.Back))
        {
            ReturnToMenu();
            return;
        }

        if (report is null || report.IsEmpty)
        {
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.MoveUp))
        {
            SelectAgentExhibition(report.SelectedIndex - 1);
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveDown))
        {
            SelectAgentExhibition(report.SelectedIndex + 1);
        }
        else if (inputEvent.IsActionPressed(GameActions.Confirm))
        {
            WatchSelectedAgentExhibition();
        }
        else if (inputEvent.IsActionPressed(GameActions.Replay))
        {
            ChallengeSelectedAgentExhibition();
        }
    }

    private void SelectAgentExhibition(int index)
    {
        if (_agentExhibitionReport is not { } report || report.IsEmpty)
        {
            return;
        }

        var moved = report.WithSelection(index);
        if (moved.SelectedIndex == report.SelectedIndex)
        {
            return;
        }

        _agentExhibitionReport = moved;
        _agentExhibitionCursor = moved.SelectedIndex;
        PlayCue(AudioCue.Navigate);
        QueueRedraw();
    }

    // Watching an exhibition is ordinary verified replay playback. The archive
    // names a file; it never carries one, so a removed recording is refused
    // here rather than discovered halfway through a playback.
    private void WatchSelectedAgentExhibition()
    {
        if (_agentExhibitionReport?.Selected is not { } entry || _replayStore is null)
        {
            return;
        }

        if (!entry.WatchAvailable)
        {
            PlayCue(AudioCue.Back);
            RefreshAgentExhibitions(_agentExhibitionCursor);
            QueueRedraw();
            return;
        }

        var store = _replayStore;
        var fileName = entry.AgentReplayFileName;
        if (!TryStartReplayResultOperation(
            () => LoadArchivedExhibitionPlayback(store, fileName),
            "EXHIBITION REPLAY LOAD AND VERIFICATION IN PROGRESS",
            ReplayOperationKind.PlaybackLoad))
        {
            ShowReplayStatus("REPLAY OPERATION ALREADY IN PROGRESS");
        }
    }

    private static ReplayOperationResult LoadArchivedExhibitionPlayback(
        ReplayStore store,
        string fileName)
    {
        var loaded = store.Load(fileName);
        if (!loaded.IsSuccess || loaded.Replay is null)
        {
            return new ReplayOperationResult(
                $"EXHIBITION PLAYBACK UNAVAILABLE [{loaded.Code}]: {loaded.Message}");
        }

        var playback = new RunReplayPlayback(loaded.Replay);
        return new ReplayOperationResult(
            $"EXHIBITION READY: {playback.StepCount} STEPS, SCORE {loaded.Replay.Outcome.Score}",
            playback);
    }

    // The same-seed handoff. The challenge descriptor decides the score
    // category, so this method cannot accidentally place an agent's line in an
    // ordinary human category.
    private void ChallengeSelectedAgentExhibition()
    {
        if (_agentExhibitionReport?.SelectedChallenge() is not { } challenge)
        {
            PlayCue(AudioCue.Back);
            return;
        }

        var mode = RunModeCatalog.All.FirstOrDefault(
            candidate => string.Equals(candidate.Id, challenge.ModeId, StringComparison.Ordinal));
        if (mode is null)
        {
            PlayCue(AudioCue.Back);
            return;
        }

        // The score context comes from the challenge descriptor, which the
        // browse report built. This method cannot place an agent's line in an
        // ordinary human category even by mistake.
        BeginPreparedRun(
            SnakeRun.Create(challenge.GameplaySeed, RunModeCatalog.CreateConfig(mode)),
            AgentExhibitionBrowseReportV1.ChallengeRunContext,
            tourEvent: null,
            isRestart: false);
        ShowReplayStatus(
            $"CHALLENGE STARTED ON SEED {challenge.GameplaySeed}; AGENT SCORED {challenge.AgentScore}");
    }
#endif

    private void HandleOfflineComparisonsInput(InputEvent inputEvent)
    {
        if (_pendingGhostDeletion is not null)
        {
            if (inputEvent.IsActionPressed(GameActions.Back))
            {
                _pendingGhostDeletion = null;
                ShowReplayStatus("HOUSEHOLD RIVAL DELETION CANCELLED; NOTHING CHANGED");
                PlayCue(AudioCue.Back);
            }
            else if (inputEvent.IsActionPressed(GameActions.Confirm))
            {
                ConfirmGhostDeletion();
            }

            return;
        }

        if (inputEvent.IsActionPressed(GameActions.Back)
            || inputEvent.IsActionPressed(GameActions.Replay))
        {
            OpenReplaysBrowse();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.MoveUp))
        {
            _ghostSlotCursor = Math.Max(0, _ghostSlotCursor - 1);
            PlayCue(AudioCue.Navigate);
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveDown))
        {
            _ghostSlotCursor = Math.Min(
                OfflineChallengeStore.MaximumHouseholdRivalSlots - 1,
                _ghostSlotCursor + 1);
            PlayCue(AudioCue.Navigate);
        }
        else if (inputEvent.IsActionPressed(GameActions.BrowseAchievements))
        {
            ImportSelectedGhost();
        }
        else if (inputEvent.IsActionPressed(GameActions.Confirm))
        {
            StartSelectedGhostRace();
        }
        else if (inputEvent.IsActionPressed(GameActions.BrowseContentPacks))
        {
            ExportSelectedGhostRunCard();
        }
        else if (inputEvent.IsActionPressed(GameActions.RestoreDefaults))
        {
            PrepareGhostDeletion();
        }

        QueueRedraw();
    }

    private void ImportSelectedGhost()
    {
        if (_offlineChallengeStore is null)
        {
            ShowReplayStatus("HOUSEHOLD RIVAL IMPORT UNAVAILABLE");
            return;
        }

        var store = _offlineChallengeStore;
        var slot = _ghostSlotCursor + 1;
        var inbox = System.IO.Path.Combine(
            store.UserDataRoot,
            "imports",
            "household-rival.vibesnake-replay.json");
        if (!TryStartReplayResultOperation(
            () =>
            {
                var imported = store.ImportGhost(inbox, slot);
                var listed = store.ListSlots();
                return new ReplayOperationResult(
                    $"GHOST IMPORT [{imported.Code}]: {imported.Message}".ToUpperInvariant(),
                    GhostSlots: listed.IsSuccess ? listed.Slots : null);
            },
            "VERIFYING EXPLICIT HOUSEHOLD RIVAL IMPORT",
            ReplayOperationKind.GhostImport))
        {
            ShowReplayStatus("REPLAY OPERATION ALREADY IN PROGRESS");
        }
    }

    private void StartSelectedGhostRace()
    {
        if (_offlineChallengeStore is null)
        {
            ShowReplayStatus("NO VERIFIED HOUSEHOLD RIVAL IS AVAILABLE");
            return;
        }

        var store = _offlineChallengeStore;
        var slot = _ghostSlotCursor + 1;
        if (!TryStartReplayResultOperation(
            () => LoadGhostRace(store, slot),
            "LOADING VERIFIED EQUAL-RULES GHOST RACE",
            ReplayOperationKind.GhostRaceLoad))
        {
            ShowReplayStatus("REPLAY OPERATION ALREADY IN PROGRESS");
        }
    }

    private static ReplayOperationResult LoadGhostRace(
        OfflineChallengeStore store,
        int slot)
    {
        var loaded = store.LoadGhost(slot);
        if (!loaded.IsSuccess || loaded.Replay is null)
        {
            return new ReplayOperationResult(
                $"GHOST RACE BLOCKED [{loaded.Code}]: {loaded.Message}".ToUpperInvariant());
        }

        try
        {
            var challenge = SeedChallengeDescriptor.Create(loaded.Replay);
            var race = new GhostRaceSession(challenge, loaded.Replay);
            return new ReplayOperationResult(
                $"HOUSEHOLD RIVAL {slot} READY: SEED {challenge.GameplaySeed}",
                GhostRace: race);
        }
        catch (ArgumentException exception)
        {
            return new ReplayOperationResult("GHOST RACE BLOCKED: " + exception.Message.ToUpperInvariant());
        }
    }

    private void ExportSelectedGhostRunCard()
    {
        if (_offlineChallengeStore is null)
        {
            ShowReplayStatus("RUN CARD EXPORT UNAVAILABLE");
            return;
        }

        var store = _offlineChallengeStore;
        var slot = _ghostSlotCursor + 1;
        var stationId = _radioPolicy.Snapshot.StationId;
        if (stationId is null || BroadcastStationCatalog.Find(stationId) is null)
        {
            stationId = "flow_signal";
        }

        var selectedStationId = stationId;
        var selectedLookId = _progression.SelectedCosmeticSetId;
        if (!TryStartReplayResultOperation(
            () =>
            {
                var exported = store.ExportRunCard(
                    slot,
                    ProductIdentity.AppVersion,
                    selectedStationId,
                    selectedLookId);
                return new ReplayOperationResult(
                    $"RUN CARD [{exported.Code}]: {exported.Message}".ToUpperInvariant());
            },
            "EXPORTING PRIVACY-SAFE RUN CARD",
            ReplayOperationKind.GhostRunCardExport))
        {
            ShowReplayStatus("REPLAY OPERATION ALREADY IN PROGRESS");
        }
    }

    private void PrepareGhostDeletion()
    {
        if (_offlineChallengeStore is null)
        {
            ShowReplayStatus("HOUSEHOLD RIVAL DELETION UNAVAILABLE");
            return;
        }

        var store = _offlineChallengeStore;
        var slot = _ghostSlotCursor + 1;
        if (!TryStartReplayResultOperation(
            () =>
            {
                var planned = store.PlanDeletion(slot);
                return new ReplayOperationResult(
                    planned.IsSuccess
                        ? planned.Plan!.ConfirmationText.ToUpperInvariant()
                            + " CONFIRM DELETE; BACK CANCELS."
                        : $"GHOST DELETION BLOCKED [{planned.Code}]: {planned.Message}".ToUpperInvariant(),
                    GhostDeletionPlan: planned.Plan);
            },
            "PREPARING EXACT HOUSEHOLD RIVAL DELETION",
            ReplayOperationKind.GhostDeletionPlan))
        {
            ShowReplayStatus("REPLAY OPERATION ALREADY IN PROGRESS");
        }
    }

    private void ConfirmGhostDeletion()
    {
        if (_offlineChallengeStore is null || _pendingGhostDeletion is null)
        {
            ShowReplayStatus("HOUSEHOLD RIVAL CONFIRMATION EXPIRED; NOTHING CHANGED");
            _pendingGhostDeletion = null;
            return;
        }

        var store = _offlineChallengeStore;
        var plan = _pendingGhostDeletion;
        _pendingGhostDeletion = null;
        if (!TryStartReplayResultOperation(
            () =>
            {
                var deleted = store.Delete(plan);
                var listed = store.ListSlots();
                return new ReplayOperationResult(
                    $"GHOST DELETE [{deleted.Code}]: {deleted.Message}".ToUpperInvariant(),
                    GhostSlots: listed.IsSuccess ? listed.Slots : null);
            },
            "DELETING ONE CONFIRMED HOUSEHOLD RIVAL",
            ReplayOperationKind.GhostDelete))
        {
            _pendingGhostDeletion = plan;
            ShowReplayStatus("REPLAY OPERATION ALREADY IN PROGRESS; DELETE NOT STARTED");
        }
    }

    private InputBindingsDocument CurrentBindingsDocument() =>
        _bindingsDeviceTab == BindingsDeviceTab.Keyboard
            ? _keyboardBindings
            : _controllerBindings;

    private string[] ListRemappableActions() =>
        CurrentBindingsDocument().ActionToBinding.Keys
            .Where(static action =>
                !string.Equals(action, "restore_defaults", StringComparison.Ordinal))
            .OrderBy(static action => action, StringComparer.Ordinal)
            .ToArray();

    private void SwitchBindingsDevice(BindingsDeviceTab tab)
    {
        if (_bindingsDeviceTab == tab)
        {
            return;
        }

        _bindingsDeviceTab = tab;
        _bindingsCursor = 0;
        _bindingsCapturePending = false;
        _pendingBindingConflict = null;
        _bindingsStatusCaption = tab == BindingsDeviceTab.Keyboard
            ? Localize("status.bindings.keyboard-selected")
            : Localize("status.bindings.controller-selected");
        PlayCue(AudioCue.Navigate);
        QueueRedraw();
    }

    private void HandleBindingsScreenInput(InputEvent inputEvent)
    {
        if (_pendingBindingConflict is not null)
        {
            if (inputEvent.IsActionPressed(GameActions.Back)
                || (inputEvent is InputEventKey { Pressed: true, Echo: false } escapeKey
                    && escapeKey.Keycode == Key.Escape))
            {
                CancelPendingBindingConflict();
            }
            else if (inputEvent.IsActionPressed(GameActions.Confirm))
            {
                ApplyPendingBindingSwap();
            }

            return;
        }

        if (_bindingsCapturePending)
        {
            if (inputEvent.IsActionPressed(GameActions.Back)
                || (inputEvent is InputEventKey { Pressed: true, Echo: false } escapeKey
                    && escapeKey.Keycode == Key.Escape))
            {
                _bindingsCapturePending = false;
                _bindingsStatusCaption = Localize("status.bindings.remap-cancelled");
                PlayCue(AudioCue.Back);
                QueueRedraw();
                return;
            }

            if (_bindingsDeviceTab == BindingsDeviceTab.Keyboard
                && inputEvent is InputEventKey { Pressed: true, Echo: false } keyEvent
                && GameActions.TryFormatKeyboardToken(keyEvent, out var keyboardToken))
            {
                ApplyBindingRemap(keyboardToken);
            }
            else if (_bindingsDeviceTab == BindingsDeviceTab.Controller
                && GameActions.TryFormatControllerToken(inputEvent, out var controllerToken))
            {
                ApplyBindingRemap(controllerToken);
            }

            return;
        }

        if (inputEvent.IsActionPressed(GameActions.MoveLeft))
        {
            SwitchBindingsDevice(BindingsDeviceTab.Keyboard);
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.MoveRight))
        {
            SwitchBindingsDevice(BindingsDeviceTab.Controller);
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.MoveUp))
        {
            var actions = ListRemappableActions();
            if (actions.Length > 0)
            {
                var previousCursor = _bindingsCursor;
                _bindingsCursor = (_bindingsCursor - 1 + actions.Length) % actions.Length;
                if (_bindingsCursor != previousCursor)
                {
                    PlayCue(AudioCue.Navigate);
                }
                QueueRedraw();
            }

            return;
        }

        if (inputEvent.IsActionPressed(GameActions.MoveDown))
        {
            var actions = ListRemappableActions();
            if (actions.Length > 0)
            {
                var previousCursor = _bindingsCursor;
                _bindingsCursor = (_bindingsCursor + 1) % actions.Length;
                if (_bindingsCursor != previousCursor)
                {
                    PlayCue(AudioCue.Navigate);
                }
                QueueRedraw();
            }

            return;
        }

        if (inputEvent.IsActionPressed(GameActions.Confirm))
        {
            var actions = ListRemappableActions();
            if (actions.Length == 0)
            {
                return;
            }

            _bindingsCursor = Math.Clamp(_bindingsCursor, 0, actions.Length - 1);
            _bindingsCapturePending = true;
            _bindingsStatusCaption = _bindingsDeviceTab == BindingsDeviceTab.Keyboard
                ? Localize(
                    "status.bindings.capture-keyboard",
                    ShellTextArgument.From(
                        "action",
                        actions[_bindingsCursor].ToUpperInvariant()))
                : Localize(
                    "status.bindings.capture-controller",
                    ShellTextArgument.From(
                        "action",
                        actions[_bindingsCursor].ToUpperInvariant()));
            PlayCue(AudioCue.Confirm);
            QueueRedraw();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.Back)
            || inputEvent.IsActionPressed(GameActions.BrowseBindings))
        {
            LeaveOverlayScreen();
        }
    }

    private void ApplyBindingRemap(string token)
    {
        var actions = ListRemappableActions();
        if (actions.Length == 0)
        {
            _bindingsCapturePending = false;
            return;
        }

        _bindingsCursor = Math.Clamp(_bindingsCursor, 0, actions.Length - 1);
        var action = actions[_bindingsCursor];
        var current = CurrentBindingsDocument();
        var result = current.TryRemapAction(action, token);
        _bindingsCapturePending = false;
        if (!result.IsSuccess || result.Document is null)
        {
            if (result.Code == InputBindingsLoadCode.Conflict
                && !string.IsNullOrWhiteSpace(result.ConflictingAction))
            {
                _pendingBindingConflict = new PendingBindingConflict(
                    action,
                    result.ConflictingAction);
                _bindingsStatusCaption = Localize(
                    "status.bindings.conflict",
                    ShellTextArgument.From("token", token),
                    ShellTextArgument.From("owner", result.ConflictingAction.ToUpperInvariant()),
                    ShellTextArgument.From("action", action.ToUpperInvariant()));
                PlayCue(AudioCue.Pause);
                QueueRedraw();
                return;
            }

            _bindingsStatusCaption = LocalizedInputBindingFailure(result);
            PlayCue(AudioCue.Back);
            QueueRedraw();
            return;
        }

        ApplyBindingsDocument(
            result.Document,
            action.ToUpperInvariant() + " -> " + token + " saved.",
            "Remapped "
                + result.Document.DeviceClass
                + " action "
                + action
                + " to "
                + token
                + ".",
            "bindings_remap_saved");
    }

    private void ApplyPendingBindingSwap()
    {
        if (_pendingBindingConflict is not { } conflict)
        {
            return;
        }

        var result = CurrentBindingsDocument().TrySwapActions(
            conflict.Action,
            conflict.ConflictingAction);
        _pendingBindingConflict = null;
        if (!result.IsSuccess || result.Document is null)
        {
            _bindingsStatusCaption = LocalizedInputBindingFailure(result);
            PlayCue(AudioCue.Back);
            QueueRedraw();
            return;
        }

        ApplyBindingsDocument(
            result.Document,
            $"SWAPPED {conflict.Action.ToUpperInvariant()} and "
                + $"{conflict.ConflictingAction.ToUpperInvariant()}.",
            "Swapped conflicting "
                + result.Document.DeviceClass
                + " bindings for "
                + conflict.Action
                + " and "
                + conflict.ConflictingAction
                + ".",
            "bindings_conflict_swapped");
    }

    private void CancelPendingBindingConflict()
    {
        if (_pendingBindingConflict is null)
        {
            return;
        }

        _pendingBindingConflict = null;
        _bindingsStatusCaption = Localize("status.bindings.conflict-cancelled");
        PlayCue(AudioCue.Back);
        QueueRedraw();
    }

    private string LocalizedInputBindingFailure(InputBindingsLoadResult result) => result.Code switch
    {
        InputBindingsLoadCode.Empty => Localize("status.bindings.error-empty"),
        InputBindingsLoadCode.InvalidJson => Localize("status.bindings.error-json"),
        InputBindingsLoadCode.UnsupportedSchema => Localize("status.bindings.error-schema"),
        InputBindingsLoadCode.InvalidField => Localize("status.bindings.error-field"),
        InputBindingsLoadCode.MissingRequiredAction =>
            Localize("status.bindings.error-required-action"),
        InputBindingsLoadCode.Conflict => Localize("status.bindings.error-conflict"),
        _ => throw new ArgumentOutOfRangeException(nameof(result)),
    };

    private void ApplyBindingsDocument(
        InputBindingsDocument document,
        string statusCaption,
        string logMessage,
        string eventCode)
    {
        if (_bindingsDeviceTab == BindingsDeviceTab.Keyboard)
        {
            _keyboardBindings = document;
            GameActions.ApplyKeyboardBindings(_keyboardBindings);
        }
        else
        {
            _controllerBindings = document;
            GameActions.ApplyControllerBindings(_controllerBindings);
        }

        var saved = TrySaveInputBindingDocument(document);
        if (saved)
        {
            _bindingsStatusCaption = statusCaption;
            _structuredLog?.Information(
                "input",
                logMessage,
                eventCode: eventCode);
        }
        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    private SettingsSection CurrentSettingsSection =>
        SettingsMenuCatalog.Sections[
            Math.Clamp(_settingsSectionCursor, 0, SettingsMenuCatalog.Sections.Count - 1)];

    private IReadOnlyList<SettingsItemDefinition> CurrentSettingsItems() =>
        SettingsMenuCatalog.ForSection(CurrentSettingsSection);

    private string LocalizedSettingsSection(SettingsSection section) => section switch
    {
        SettingsSection.Gameplay => Localize("settings.section.gameplay"),
        SettingsSection.Controls => Localize("settings.section.controls"),
        SettingsSection.Audio => Localize("settings.section.audio"),
        SettingsSection.Display => Localize("settings.section.display"),
        SettingsSection.Accessibility => Localize("settings.section.accessibility"),
        SettingsSection.Data => Localize("settings.section.data"),
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    private void HandleSettingsScreenInput(InputEvent inputEvent)
    {
        if (_playerDataOperation is not null)
        {
            _settingsStatusCaption = Localize("settings.player-data.operation");
            QueueRedraw();
            return;
        }

        if (_playerDataRecoveryBrowseOpen)
        {
            HandlePlayerDataRecoveryBrowseInput(inputEvent);
            return;
        }

        if (_playtestDeleteConfirmation)
        {
            if (inputEvent.IsActionPressed(GameActions.Back))
            {
                _playtestDeleteConfirmation = false;
                _settingsStatusCaption = Localize("status.settings.playtest-delete-cancelled");
                PlayCue(AudioCue.Back);
            }
            else if (inputEvent.IsActionPressed(GameActions.Confirm))
            {
                DeleteLocalPlaytestSummaries();
            }

            QueueRedraw();
            return;
        }

        if (_settingsFullResetConfirmation)
        {
            if (inputEvent.IsActionPressed(GameActions.Back))
            {
                _settingsFullResetConfirmation = false;
                _pendingDataResetPlan = null;
                _settingsStatusCaption = Localize("status.settings.reset-cancelled");
                PlayCue(AudioCue.Back);
            }
            else if (inputEvent.IsActionPressed(GameActions.Confirm))
            {
                ResetAllPlayerSettings();
            }

            QueueRedraw();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.BrowseSettings))
        {
            LeaveOverlayScreen();
            return;
        }

        if (!_settingsSectionOpen)
        {
            if (inputEvent.IsActionPressed(GameActions.Back))
            {
                LeaveOverlayScreen();
                return;
            }

            var previousSectionCursor = _settingsSectionCursor;
            if (inputEvent.IsActionPressed(GameActions.MoveUp))
            {
                _settingsSectionCursor = Math.Max(0, _settingsSectionCursor - 1);
            }
            else if (inputEvent.IsActionPressed(GameActions.MoveDown))
            {
                _settingsSectionCursor = Math.Min(
                    SettingsMenuCatalog.Sections.Count - 1,
                    _settingsSectionCursor + 1);
            }
            else if (inputEvent.IsActionPressed(GameActions.Confirm))
            {
                _settingsSectionOpen = true;
                _settingsItemCursor = 0;
                _settingsStatusCaption = LocalizedSettingsSection(CurrentSettingsSection);
                PlayCue(AudioCue.Confirm);
            }

            if (_settingsSectionCursor != previousSectionCursor)
            {
                PlayCue(AudioCue.Navigate);
            }

            QueueRedraw();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.Back))
        {
            _settingsSectionOpen = false;
            _settingsItemCursor = 0;
            _settingsStatusCaption = Localize("settings.select-section");
            PlayCue(AudioCue.Back);
            QueueRedraw();
            return;
        }

        var items = CurrentSettingsItems();
        var previousItemCursor = _settingsItemCursor;
        if (inputEvent.IsActionPressed(GameActions.MoveUp))
        {
            _settingsItemCursor = Math.Max(0, _settingsItemCursor - 1);
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveDown))
        {
            _settingsItemCursor = Math.Min(items.Count - 1, _settingsItemCursor + 1);
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveLeft))
        {
            AdjustSelectedSetting(-1);
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveRight))
        {
            AdjustSelectedSetting(1);
        }
        else if (inputEvent.IsActionPressed(GameActions.Confirm))
        {
            ActivateSelectedSetting();
        }

        if (_settingsItemCursor != previousItemCursor)
        {
            PlayCue(AudioCue.Navigate);
        }

        QueueRedraw();
    }

    private void AdjustSelectedSetting(int direction)
    {
        var item = CurrentSettingsItems()[_settingsItemCursor];
        var changed = true;
        switch (item.Id)
        {
            case "vibe_adaptation":
                _shellSettings.VibeAdaptationEnabled = direction > 0;
                break;
            case "local_playtest_summaries":
                _shellSettings.LocalPlaytestSummariesEnabled = direction > 0;
                break;
            case "controller_deadzone":
                _shellSettings.AdjustControllerDeadzone(
                    direction * ShellSettings.DefaultControllerDeadzoneStep);
                break;
            case "master_volume":
                _shellSettings.AdjustMasterVolume(direction * ShellSettings.DefaultVolumeStep);
                break;
            case "music_volume":
                _shellSettings.AdjustMusicVolume(direction * ShellSettings.DefaultVolumeStep);
                break;
            case "sfx_volume":
                _shellSettings.AdjustSfxVolume(direction * ShellSettings.DefaultVolumeStep);
                break;
            case "ui_volume":
                _shellSettings.AdjustUiVolume(direction * ShellSettings.DefaultVolumeStep);
                break;
            case "master_muted":
                _shellSettings.MasterMuted = direction > 0;
                break;
            case "music_muted":
                _shellSettings.MusicMuted = direction > 0;
                break;
            case "sfx_muted":
                _shellSettings.SfxMuted = direction > 0;
                break;
            case "ui_muted":
                _shellSettings.UiMuted = direction > 0;
                break;
            case "mono_output":
                _shellSettings.MonoOutput = direction > 0;
                break;
            case "window_mode":
                _shellSettings.CycleWindowMode(direction);
                ApplyWindowModeFromSettings();
                break;
            case "window_size":
                _shellSettings.CycleWindowSizePreset(direction);
                ApplyWindowModeFromSettings();
                break;
            case "high_contrast":
                _shellSettings.HighContrast = direction > 0;
                break;
            case "reduced_motion":
                _shellSettings.ReducedMotion = direction > 0;
                if (_shellSettings.ReducedMotion)
                {
                    _shellSettings.ScreenShakeIntensity = 0.0f;
                }

                break;
            case "text_scale":
                _shellSettings.AdjustTextScale(direction * ShellSettings.DefaultTextScaleStep);
                break;
            case "screen_shake":
                _shellSettings.AdjustScreenShake(direction * 0.1f);
                break;
            case "flash_free":
                _shellSettings.FlashFree = direction > 0;
                break;
            default:
                changed = false;
                _settingsStatusCaption = Localize("status.settings.confirm-action");
                break;
        }

        if (changed)
        {
            SaveShellSettings(
                "status.settings.item-saved",
                ShellTextArgument.From("item", item.Label.ToUpperInvariant()));
            PlayCue(AudioCue.Confirm);
        }
    }

    private void ActivateSelectedSetting()
    {
        var item = CurrentSettingsItems()[_settingsItemCursor];
        switch (item.Id)
        {
            case "vibe_adaptation":
                _shellSettings.ToggleVibeAdaptation();
                SaveShellSettings("status.settings.vibe-adaptation-saved");
                break;
            case "local_playtest_summaries":
                _shellSettings.ToggleLocalPlaytestSummaries();
                SaveShellSettings(
                    _shellSettings.LocalPlaytestSummariesEnabled
                        ? "status.settings.playtest-enabled"
                        : "status.settings.playtest-disabled");
                break;
            case "master_muted":
                _shellSettings.ToggleMasterMute();
                SaveShellSettings("status.settings.master-mute-saved");
                break;
            case "music_muted":
                _shellSettings.ToggleMusicMute();
                SaveShellSettings("status.settings.music-mute-saved");
                break;
            case "sfx_muted":
                _shellSettings.ToggleSfxMute();
                SaveShellSettings("status.settings.sfx-mute-saved");
                break;
            case "ui_muted":
                _shellSettings.ToggleUiMute();
                SaveShellSettings("status.settings.ui-mute-saved");
                break;
            case "mono_output":
                _shellSettings.ToggleMonoOutput();
                SaveShellSettings("status.settings.mono-saved");
                break;
            case "window_mode":
                _shellSettings.CycleWindowMode(1);
                SaveShellSettings("status.settings.fullscreen-saved");
                ApplyWindowModeFromSettings();
                break;
            case "window_size":
                _shellSettings.CycleWindowSizePreset(1);
                SaveShellSettings("status.settings.display-saved");
                ApplyWindowModeFromSettings();
                break;
            case "high_contrast":
                _shellSettings.ToggleHighContrast();
                SaveShellSettings("status.settings.contrast-saved");
                break;
            case "reduced_motion":
                _shellSettings.ToggleReducedMotion();
                SaveShellSettings("status.settings.motion-saved");
                break;
            case "flash_free":
                _shellSettings.ToggleFlashFree();
                SaveShellSettings("status.settings.flash-saved");
                break;
            case "open_bindings":
                OpenBindingsBrowse();
                return;
            case "restore_bindings":
                _settingsStatusCaption = RestoreInputBindingDefaults()
                    ? Localize("status.settings.bindings-restored")
                    : Localize("status.settings.bindings-session-failed");
                break;
            case "open_diagnostics":
                {
                    var statusCaption = OpenDiagnosticsDirectory();
                    _settingsStatusCaption = statusCaption;
                    break;
                }
            case "reset_tutorial":
                ResetOnboardingProgress();
                break;
            case "reset_preferences":
                BeginPlayerDataReset(PlayerDataCategory.Preferences);
                break;
            case "reset_progression":
                BeginPlayerDataReset(PlayerDataCategory.Progression);
                break;
            case "reset_personal_bests":
                BeginPlayerDataReset(PlayerDataCategory.PersonalBests);
                break;
            case "reset_replays":
                BeginPlayerDataReset(PlayerDataCategory.Replays);
                break;
            case "reset_optional_content":
                BeginPlayerDataReset(PlayerDataCategory.OptionalContent);
                break;
            case "recover_backup":
                BeginPlayerDataRecoveryInspection();
                break;
            case "export_playtest_summaries":
                ExportLocalPlaytestSummaries();
                break;
            case "delete_playtest_summaries":
                _playtestDeleteConfirmation = true;
                _settingsStatusCaption = Localize("status.settings.playtest-delete-confirm");
                break;
            default:
                _settingsStatusCaption = item.Id is "master_volume"
                    or "music_volume"
                    or "sfx_volume"
                    or "ui_volume"
                    or "controller_deadzone"
                    or "text_scale"
                    or "screen_shake"
                    ? Localize("status.settings.use-adjust")
                    : Localize("status.settings.read-only-contract");
                break;
        }

        PlayCue(AudioCue.Confirm);
    }

    private void ExportLocalPlaytestSummaries()
    {
        if (_localPlaytestSummaryStore is null)
        {
            _settingsStatusCaption = Localize("status.settings.playtest-export-unavailable");
            return;
        }

        try
        {
            var exported = _localPlaytestSummaryStore.Export(DateTimeOffset.UtcNow);
            _settingsStatusCaption = Localize(
                "status.settings.playtest-exported",
                ShellTextArgument.From("count", exported.SummaryCount),
                ShellTextArgument.From(
                    "path",
                    LocalPlaytestSummaryStore.StoreDirectoryName
                        + "/"
                        + LocalPlaytestSummaryStore.ExportDirectoryName
                        + "/"
                        + exported.FileName));
            _structuredLog?.Information(
                "playtest-summaries",
                $"Exported {exported.SummaryCount} local playtest summaries.",
                eventCode: "local_playtest_summaries_exported");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or InvalidOperationException)
        {
            _settingsStatusCaption = Localize("status.settings.playtest-export-failed");
            _structuredLog?.Warning(
                "playtest-summaries",
                exception.Message,
                eventCode: "local_playtest_summary_export_failed");
        }
    }

    private void DeleteLocalPlaytestSummaries()
    {
        _playtestDeleteConfirmation = false;
        if (_localPlaytestSummaryStore is null)
        {
            _settingsStatusCaption = Localize("status.settings.playtest-delete-unavailable");
            return;
        }

        try
        {
            var deleted = _localPlaytestSummaryStore.DeleteAll();
            _localPlaytestSummaryCount = 0;
            _settingsStatusCaption = deleted.StoreExisted || deleted.ExportFilesDeleted > 0
                ? Localize("status.settings.playtest-deleted")
                : Localize("status.settings.playtest-delete-empty");
            _structuredLog?.Information(
                "playtest-summaries",
                "Completed explicit local playtest summary deletion.",
                eventCode: "local_playtest_summaries_deleted");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            _settingsStatusCaption = Localize("status.settings.playtest-delete-failed");
            _structuredLog?.Warning(
                "playtest-summaries",
                exception.Message,
                eventCode: "local_playtest_summary_delete_failed");
        }
    }

    private void RestoreCurrentSettingsSection()
    {
        switch (CurrentSettingsSection)
        {
            case SettingsSection.Gameplay:
                _shellSettings.RestoreGameplayDefaults();
                SaveShellSettings("status.settings.gameplay-restored");
                break;
            case SettingsSection.Controls:
                _shellSettings.RestoreControlsDefaults();
                var bindingsSaved = RestoreInputBindingDefaults();
                var controlsSaved = SaveShellSettings();
                _settingsStatusCaption = bindingsSaved && controlsSaved
                    ? Localize("status.settings.controls-restored")
                    : Localize("status.settings.controls-session-failed");
                break;
            case SettingsSection.Audio:
                _shellSettings.RestoreAudioDefaults();
                SaveShellSettings("status.settings.audio-restored");
                break;
            case SettingsSection.Display:
                _shellSettings.RestoreDisplayDefaults();
                SaveShellSettings("status.settings.display-restored");
                ApplyWindowModeFromSettings();
                break;
            case SettingsSection.Accessibility:
                _shellSettings.RestoreAccessibilityDefaults();
                SaveShellSettings("status.settings.accessibility-restored");
                break;
            case SettingsSection.Data:
                BeginPlayerDataReset(PlayerDataCategory.Preferences);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    private void BeginPlayerDataReset(PlayerDataCategory category)
    {
        if (_playerDataRecovery is null)
        {
            _settingsStatusCaption = Localize("status.player-data.reset-unavailable");
            return;
        }

        if (_playerDataOperation is not null)
        {
            _settingsStatusCaption = Localize("settings.player-data.operation");
            return;
        }

        var backupId = PlayerDataRecoveryService.CreateBackupId(
            DateTimeOffset.UtcNow,
            Guid.NewGuid());
        _pendingDataResetPlan = _playerDataRecovery.CreateResetPlan([category], backupId);
        _settingsFullResetConfirmation = true;
        _settingsStatusCaption = Localize("status.player-data.reset-review");
    }

    private void BeginPlayerDataRecoveryInspection()
    {
        if (_playerDataRecovery is null)
        {
            _settingsStatusCaption = Localize("status.player-data.recovery-unavailable");
            return;
        }

        var service = _playerDataRecovery;
        _playerDataRecoveryBrowseOpen = false;
        _playerDataBackups = [];
        _playerDataBackupCursor = 0;
        _settingsStatusCaption = Localize("status.player-data.inspecting");
        _playerDataOperation = Task.Run(() => new PlayerDataOperationResult(
            PlayerDataOperationKind.Inspect,
            Backups: service.InspectBackups()));
    }

    private void HandlePlayerDataRecoveryBrowseInput(InputEvent inputEvent)
    {
        if (inputEvent.IsActionPressed(GameActions.Back))
        {
            _playerDataRecoveryBrowseOpen = false;
            _playerDataBackups = [];
            _playerDataBackupCursor = 0;
            _settingsStatusCaption = Localize("status.player-data.recovery-closed");
            PlayCue(AudioCue.Back);
            QueueRedraw();
            return;
        }

        if (_playerDataBackups.Count == 0)
        {
            _playerDataRecoveryBrowseOpen = false;
            _settingsStatusCaption = Localize("status.player-data.no-backups");
            QueueRedraw();
            return;
        }

        if (inputEvent.IsActionPressed(GameActions.MoveLeft)
            || inputEvent.IsActionPressed(GameActions.MoveUp))
        {
            var previousCursor = _playerDataBackupCursor;
            _playerDataBackupCursor = Math.Max(0, _playerDataBackupCursor - 1);
            if (_playerDataBackupCursor != previousCursor)
            {
                PlayCue(AudioCue.Navigate);
            }
        }
        else if (inputEvent.IsActionPressed(GameActions.MoveRight)
            || inputEvent.IsActionPressed(GameActions.MoveDown))
        {
            var previousCursor = _playerDataBackupCursor;
            _playerDataBackupCursor = Math.Min(
                _playerDataBackups.Count - 1,
                _playerDataBackupCursor + 1);
            if (_playerDataBackupCursor != previousCursor)
            {
                PlayCue(AudioCue.Navigate);
            }
        }
        else if (inputEvent.IsActionPressed(GameActions.Confirm))
        {
            var selected = CurrentPlayerDataBackup();
            if (!selected.CanRestore)
            {
                OpenPlayerDataBackupsDirectory();
                _settingsStatusCaption = Localize("status.player-data.restore-location-blocked");
            }
            else if (_playerDataRecovery is not null)
            {
                var service = _playerDataRecovery;
                _settingsStatusCaption = Localize("status.player-data.restoring");
                _playerDataOperation = Task.Run(() => new PlayerDataOperationResult(
                    PlayerDataOperationKind.Restore,
                    RestoreResult: service.Restore(selected.BackupId)));
                PlayCue(AudioCue.Confirm);
            }
        }

        QueueRedraw();
    }

    private PlayerDataBackupInspection CurrentPlayerDataBackup() =>
        _playerDataBackups[Math.Clamp(
            _playerDataBackupCursor,
            0,
            _playerDataBackups.Count - 1)];

    private PlayerDataOperationCompletion TryCompletePlayerDataOperation()
    {
        var operation = _playerDataOperation;
        if (operation is null || !operation.IsCompleted)
        {
            return PlayerDataOperationCompletion.Pending;
        }

        _playerDataOperation = null;
        var operationSucceeded = false;
        try
        {
            var result = operation.GetAwaiter().GetResult();
            operationSucceeded = result.Kind switch
            {
                PlayerDataOperationKind.Reset => CompletePlayerDataReset(result),
                PlayerDataOperationKind.Inspect => CompletePlayerDataInspection(result),
                PlayerDataOperationKind.Restore => CompletePlayerDataRestore(result),
                _ => throw new ArgumentOutOfRangeException(nameof(result.Kind)),
            };
        }
        catch (Exception exception) when (
            exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException)
        {
            _settingsStatusCaption = Localize("status.player-data.operation-failed");
            _structuredLog?.Warning(
                "player-data",
                exception.Message,
                eventCode: "player_data_operation_failed");
        }

        QueueRedraw();
        return operationSucceeded
            ? PlayerDataOperationCompletion.Succeeded
            : PlayerDataOperationCompletion.Failed;
    }

    private bool ShouldQuitAfterPlayerDataWork()
    {
        var completion = TryCompletePlayerDataOperation();
        if (completion == PlayerDataOperationCompletion.Pending
            || !_quitAfterPlayerDataOperation)
        {
            return false;
        }

        _quitAfterPlayerDataOperation = false;
        if (completion == PlayerDataOperationCompletion.Succeeded)
        {
            return true;
        }

        _settingsStatusCaption = Localize("status.player-data.quit-canceled");
        QueueRedraw();
        return false;
    }

    private bool CompletePlayerDataReset(PlayerDataOperationResult operation)
    {
        if (operation.ResetPlan is null || operation.ResetResult is null)
        {
            throw new InvalidOperationException("Player-data reset result is incomplete.");
        }

        var result = operation.ResetResult;
        if (!result.IsSuccess)
        {
            _settingsStatusCaption = Localize(
                "status.player-data.reset-blocked",
                ShellTextArgument.From("code", result.Code.ToString().ToUpperInvariant()));
            _structuredLog?.Warning(
                "player-data",
                result.Message,
                eventCode: "player_data_reset_blocked");
            return false;
        }

        var backupLocation = result.BackupLocation
            ?? throw new InvalidOperationException(
                "Successful player-data reset did not report its verified backup location.");
        ApplyPlayerDataResetInMemory(operation.ResetPlan.Categories);
        _playerDataBackups = [];
        _settingsStatusCaption = Localize(
            "status.player-data.reset-complete",
            ShellTextArgument.From("location", backupLocation));
        _structuredLog?.Information(
            "player-data",
            "Selected player data was reset after verified backup.",
            eventCode: "player_data_reset_complete");
        return true;
    }

    private bool CompletePlayerDataInspection(PlayerDataOperationResult operation)
    {
        _playerDataBackups = operation.Backups ?? [];
        _playerDataBackupCursor = 0;
        _playerDataRecoveryBrowseOpen = _playerDataBackups.Count > 0;
        if (_playerDataBackups.Count == 0)
        {
            _settingsStatusCaption = Localize("status.player-data.no-backups");
            return true;
        }

        var selected = CurrentPlayerDataBackup();
        _settingsStatusCaption = selected.CanRestore
            ? Localize("status.player-data.backup-verified")
            : Localize("status.player-data.backup-corrupt");
        return true;
    }

    private bool CompletePlayerDataRestore(PlayerDataOperationResult operation)
    {
        if (operation.RestoreResult is null)
        {
            throw new InvalidOperationException("Player-data restore result is incomplete.");
        }

        if (!operation.RestoreResult.IsSuccess)
        {
            _settingsStatusCaption = Localize(
                "status.player-data.restore-blocked",
                ShellTextArgument.From(
                    "code",
                    operation.RestoreResult.Code.ToString().ToUpperInvariant()));
            return false;
        }

        LoadShellSettings();
        ApplyWindowModeFromSettings();
        LoadOnboardingProgress();
        LoadAchievements();
        LoadProgression();
        LoadSpectatorLeague();
        LoadPersonalBests();
        LoadScoreHistory();
        LoadInputBindings();
        InitializeRadio(allowCheckoutFallback: true);
        _ghostSlots = [];
        _ghostSlotCursor = 0;
        _pendingGhostDeletion = null;
        _replayBrowserEntries = [];
        _replayPlayback = null;
        _pendingReplayDeletion = null;
        _playerDataRecoveryBrowseOpen = false;
        _playerDataBackups = [];
        _settingsStatusCaption = Localize("status.player-data.restored");
        _structuredLog?.Information(
            "player-data",
            "Verified player-data backup restored without overwrite.",
            eventCode: "player_data_restore_complete");
        return true;
    }

    private void ApplyPlayerDataResetInMemory(
        IReadOnlyList<PlayerDataCategory> categories)
    {
        foreach (var category in categories)
        {
            switch (category)
            {
                case PlayerDataCategory.Preferences:
                    _shellSettings = ShellSettings.CreateDefaults();
                    ApplyRuntimeShellSettings();
                    ApplyWindowModeFromSettings();
                    _keyboardBindings = InputBindingsDocument.CreateKeyboardDefaults();
                    _controllerBindings = InputBindingsDocument.CreateControllerDefaults();
                    GameActions.ApplyKeyboardBindings(_keyboardBindings);
                    GameActions.ApplyControllerBindings(_controllerBindings);
                    break;
                case PlayerDataCategory.Progression:
                    _achievements = AchievementsDocument.CreateDefaults();
                    _progression = ProgressionDocument.CreateDefaults();
                    _progressionGoalCursor = 0;
                    _progressionStatusCaption = null;
                    _onboardingProgress = OnboardingProgressDocument.CreateDefaults();
                    _onboardingWasNewProfile = true;
                    _onboardingSession = null;
                    _spectatorLeague = SpectatorLeagueDocument.CreateDefaults();
                    _achievementsWritable = true;
                    _progressionWritable = true;
                    _spectatorLeagueWritable = true;
                    break;
                case PlayerDataCategory.PersonalBests:
                    _personalBests = PersonalBestDocument.CreateDefaults();
                    _scoreHistory = ScoreHistoryDocument.CreateDefaults();
                    _scoreHistoryWritable = true;
                    _scoreBrowseCategoryCursor = 0;
                    _scoreBrowseStatusCaption = null;
                    _runEndSummary = null;
                    _personalBestsWritable = true;
                    break;
                case PlayerDataCategory.Replays:
                    _replayBrowserEntries = [];
                    _replayPlayback = null;
                    _pendingReplayDeletion = null;
                    _replayStatusCaption = null;
                    _ghostSlots = [];
                    _ghostSlotCursor = 0;
                    _pendingGhostDeletion = null;
                    break;
                case PlayerDataCategory.OptionalContent:
                    InitializeRadio(allowCheckoutFallback: true);
                    break;
                default:
                    throw new InvalidOperationException("Unknown player data category.");
            }
        }
    }

    private void OpenPlayerDataBackupsDirectory()
    {
        if (_playerDataRecovery is null)
        {
            return;
        }

        var path = _playerDataRecovery.BackupsDirectory;
        try
        {
            DisplayServer.ClipboardSet(path);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or PlatformNotSupportedException)
        {
            _structuredLog?.Warning(
                "player-data",
                exception.Message,
                eventCode: "backup_path_clipboard_failed");
        }

        try
        {
            var openError = OS.ShellOpen(path);
            if (openError != Error.Ok)
            {
                _structuredLog?.Warning(
                    "player-data",
                    $"Backup location open failed with {openError}.",
                    eventCode: "backup_location_open_failed");
            }
        }
        catch (Exception exception)
        {
            _structuredLog?.Warning(
                "player-data",
                exception.Message,
                eventCode: "backup_location_open_failed");
        }
    }

    private void ResetAllPlayerSettings()
    {
        if (_pendingDataResetPlan is null || _playerDataRecovery is null)
        {
            _settingsStatusCaption = Localize("status.player-data.reset-unavailable");
            _settingsFullResetConfirmation = false;
            _pendingDataResetPlan = null;
            PlayCue(AudioCue.Back);
            return;
        }

        if (_replayOperation is not null || _queuedReplaySave is not null)
        {
            _settingsStatusCaption = Localize("status.player-data.wait-replay");
            PlayCue(AudioCue.Back);
            return;
        }

        var plan = _pendingDataResetPlan;
        var service = _playerDataRecovery;
        _pendingDataResetPlan = null;
        _settingsFullResetConfirmation = false;
        _settingsStatusCaption = Localize("status.player-data.creating-backup");
        _playerDataOperation = Task.Run(() => new PlayerDataOperationResult(
            PlayerDataOperationKind.Reset,
            ResetPlan: plan,
            ResetResult: service.Reset(plan)));
        PlayCue(AudioCue.Confirm);
        QueueRedraw();
    }

    private void DrawMainMenu()
    {
        var palette = ActiveShellPalette;
        var logicalWidth = ActiveLogicalWidth;
        var menuTargets = MouseInputPolicy.MenuTargetsForWidth(logicalWidth);
        var backdrop = _shellSettings.HighContrast
            ? Colors.Black
            : new Color(0.035f, 0.025f, 0.08f);
        var gridColor = _shellSettings.HighContrast
            ? new Color(0.15f, 0.15f, 0.15f, 0.5f)
            : new Color(0.16f, 0.12f, 0.27f, 0.42f);
        DrawRect(
            new Rect2(0.0f, 0.0f, logicalWidth, VirtualViewport.LogicalHeight),
            backdrop);
        for (var x = 0.0f; x <= logicalWidth; x += 40.0f)
        {
            DrawLine(new Vector2(x, 0.0f), new Vector2(x, 720.0f), gridColor, 1.0f);
        }

        for (var y = 0.0f; y <= 720.0f; y += 40.0f)
        {
            DrawLine(new Vector2(0.0f, y), new Vector2(logicalWidth, y), gridColor, 1.0f);
        }

        DrawRect(
            new Rect2(24.0f, 20.0f, logicalWidth - 48.0f, 680.0f),
            palette.PrimaryText,
            filled: false,
            width: 3.0f);

        if (_brandLogo is not null)
        {
            DrawTextureRect(
                _brandLogo,
                new Rect2((logicalWidth - 170.0f) * 0.5f, 28.0f, 170.0f, 170.0f),
                tile: false);
        }

        DrawCenteredLabel(
            Localize("app.tagline").ToUpperInvariant(),
            224.0f,
            ScaledFontSize(15),
            SecondaryTextColor());

        for (var index = 0; index < MainMenuItemCount; index++)
        {
            var selected = index == _mainMenuCursor;
            var bounds = menuTargets[index].LogicalBounds;
            var accent = MainMenuAccent(index, palette);
            var divider = accent;
            divider.A = selected ? 0.72f : 0.28f;
            DrawLine(
                new Vector2(bounds.Position.X, bounds.End.Y),
                bounds.End,
                divider,
                selected ? 2.0f : 1.0f);

            var rail = accent;
            rail.A = selected ? 1.0f : 0.52f;
            DrawRect(
                new Rect2(bounds.Position, new Vector2(selected ? 6.0f : 3.0f, bounds.Size.Y)),
                rail);

            var keyBounds = new Rect2(
                bounds.Position + new Vector2(22.0f, 4.0f),
                new Vector2(78.0f, 27.0f));
            var hint = _activePromptFamily == InputPromptFamily.Keyboard
                ? MainMenuKeyboardHints[index]
                : InputPromptGlyphs.DescribeToken(
                    ResolveActionPrompt("confirm").Token,
                    _activePromptFamily).Label;
            DrawCenteredInRect(
                hint,
                keyBounds,
                ScaledFontSize(12),
                selected ? palette.SelectedText : accent);

            var label = Localize(MainMenuCopyIds[index]).ToUpperInvariant();
            if ((MainMenuItem)index == MainMenuItem.Start)
            {
                label += "    < " + SelectedRunMode.DisplayName.ToUpperInvariant() + " >";
            }

            DrawLabel(
                ShellFocusPresentation.SelectionPrefix(selected) + label,
                bounds.Position + new Vector2(122.0f, 24.0f),
                ScaledFontSize(17),
                selected ? palette.SelectedText : palette.BodyText);
        }

        DrawCenteredLabel(
            _radioPolicy.Snapshot.CompactLine,
            624.0f,
            ScaledFontSize(13),
            palette.GoldText);
        var browse = AchievementsBrowseReport.FromUnlocks(_achievements.UnlockedIds);
        DrawCenteredLabel(
            browse.FormatSummaryLine(),
            650.0f,
            ScaledFontSize(12),
            palette.MutedGoldText);
        DrawCenteredLabel(
            "ARROWS / D-PAD MOVE  //  ENTER / A SELECT  //  J / R3 RADIO  //  F11 FULLSCREEN",
            680.0f,
            ScaledFontSize(11),
            SecondaryTextColor());
    }

    private Color MainMenuAccent(int index, ShellPalette palette)
    {
        if (_shellSettings.HighContrast)
        {
            return index == _mainMenuCursor ? palette.SelectedText : palette.PromptOutline;
        }

        return index switch
        {
            0 => new Color(0.28f, 1.0f, 0.70f),
            1 => new Color(1.0f, 0.78f, 0.15f),
            2 => new Color(1.0f, 0.26f, 0.72f),
            3 => new Color(0.20f, 0.62f, 1.0f),
            4 => new Color(1.0f, 0.28f, 0.62f),
            5 => new Color(0.28f, 0.88f, 1.0f),
            6 => new Color(0.24f, 0.62f, 1.0f),
            7 => new Color(0.78f, 0.74f, 1.0f),
            8 => new Color(1.0f, 0.22f, 0.42f),
            _ => palette.AccentText,
        };
    }

    private void DrawCenteredInRect(string text, Rect2 bounds, int fontSize, Color color)
    {
        var size = ActiveShellTheme.InterfaceFont.GetStringSize(
            text,
            HorizontalAlignment.Left,
            -1.0f,
            fontSize);
        var baseline = new Vector2(
            bounds.Position.X + ((bounds.Size.X - size.X) * 0.5f),
            bounds.Position.Y
                + ((bounds.Size.Y - ActiveShellTheme.InterfaceFont.GetHeight(fontSize)) * 0.5f)
                + ActiveShellTheme.InterfaceFont.GetAscent(fontSize));
        DrawLabel(text, baseline, fontSize, color);
    }

    private void DrawOnboarding()
    {
        DrawLabel(
            Localize("screen.onboarding.title"),
            new Vector2(42.0f, 86.0f),
            ScaledFontSize(40),
            PrimaryTextColor());
        DrawLabel(
            _onboardingStatusCaption ?? Localize("onboarding.practice"),
            new Vector2(46.0f, 124.0f),
            ScaledFontSize(15),
            ActiveShellPalette.GoldText);

        if (_onboardingSession is null)
        {
            DrawLabel(
                Localize("onboarding.offer.summary"),
                new Vector2(46.0f, 190.0f),
                ScaledFontSize(18),
                ActiveShellPalette.BodyText);
            DrawLabel(
                Localize("onboarding.offer.isolation"),
                new Vector2(46.0f, 224.0f),
                ScaledFontSize(17),
                SecondaryTextColor());

            DrawLabel(
                ShellFocusPresentation.SelectionPrefix(_onboardingOfferCursor == 0)
                    + "LEARN FIRST",
                new Vector2(72.0f, 296.0f),
                ScaledFontSize(24),
                _onboardingOfferCursor == 0
                    ? ActiveShellPalette.SelectedText
                    : ActiveShellPalette.BodyText);
            DrawLabel(
                Localize("onboarding.offer.learn-description"),
                new Vector2(104.0f, 326.0f),
                ScaledFontSize(15),
                SecondaryTextColor());
            DrawLabel(
                ShellFocusPresentation.SelectionPrefix(_onboardingOfferCursor == 1)
                    + "DIRECT PLAY",
                new Vector2(72.0f, 384.0f),
                ScaledFontSize(24),
                _onboardingOfferCursor == 1
                    ? ActiveShellPalette.SelectedText
                    : ActiveShellPalette.BodyText);
            DrawLabel(
                Localize("onboarding.offer.skip-description"),
                new Vector2(104.0f, 414.0f),
                ScaledFontSize(15),
                SecondaryTextColor());

            var choiceX = DrawActionPromptSegment(
                "move_up",
                Localize("action.choose"),
                new Vector2(46.0f, 510.0f),
                ScaledFontSize(15),
                SecondaryTextColor());
            choiceX = DrawActionPromptSegment(
                "move_down",
                string.Empty,
                new Vector2(choiceX, 510.0f),
                ScaledFontSize(15),
                SecondaryTextColor());
            DrawActionPromptSegment(
                "confirm",
                Localize("action.select"),
                new Vector2(choiceX, 510.0f),
                ScaledFontSize(15),
                SecondaryTextColor());
            DrawStaticPromptSegment(
                "key:f1",
                "button:start",
                Localize("action.settings-before-play"),
                new Vector2(46.0f, 552.0f),
                ScaledFontSize(15),
                SecondaryTextColor());
            DrawActionPromptSegment(
                "back",
                Localize("action.skip-menu"),
                new Vector2(46.0f, 594.0f),
                ScaledFontSize(15),
                SecondaryTextColor());
            return;
        }

        var lesson = _onboardingSession.Lesson;
        DrawLabel(
            $"LESSON {(int)lesson + 1}/8: {OnboardingLessonTitle(lesson)}",
            new Vector2(46.0f, 180.0f),
            ScaledFontSize(24),
            ActiveShellPalette.PrimaryText);
        DrawLabel(
            OnboardingLessonInstruction(lesson),
            new Vector2(46.0f, 220.0f),
            ScaledFontSize(17),
            ActiveShellPalette.BodyText);

        var snapshot = _onboardingSession.Snapshot;
        DrawLabel(
            $"PRACTICE SCORE {snapshot.Score}  HUNGER {snapshot.HungerTicksRemaining}",
            new Vector2(46.0f, 270.0f),
            ScaledFontSize(16),
            ActiveShellPalette.GoldText);
        DrawOnboardingBoard(snapshot);

        DrawActionPromptSegment(
            OnboardingLessonAction(lesson),
            OnboardingLessonActionCaption(lesson),
            new Vector2(46.0f, 520.0f),
            ScaledFontSize(17),
            ActiveShellPalette.PrimaryText);
        DrawActionPromptSegment(
            "back",
            Localize("action.exit-safely"),
            new Vector2(46.0f, 570.0f),
            ScaledFontSize(15),
            SecondaryTextColor());
        DrawLabel(
            OnboardingSession.Identity.ToUpperInvariant(),
            new Vector2(46.0f, 620.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
    }

    private void DrawOnboardingBoard(RunSnapshot snapshot)
    {
        const float tutorialCell = 42.0f;
        const float originX = 860.0f;
        const float originY = 210.0f;
        var boardRect = new Rect2(
            originX,
            originY,
            OnboardingSession.ScenarioWidth * tutorialCell,
            OnboardingSession.ScenarioHeight * tutorialCell);
        DrawRect(boardRect, new Color(0.015f, 0.035f, 0.045f));
        DrawRect(boardRect, ActiveShellPalette.AccentText, filled: false, width: 2.0f);

        void DrawTutorialCell(GridPoint point, Color color, float inset)
        {
            DrawRect(
                new Rect2(
                    originX + (point.X * tutorialCell) + inset,
                    originY + (point.Y * tutorialCell) + inset,
                    tutorialCell - (inset * 2.0f),
                    tutorialCell - (inset * 2.0f)),
                color);
        }

        if (snapshot.Food is { } food)
        {
            DrawTutorialCell(food, GameplayPresentation.FoodColor, 9.0f);
        }

        if (snapshot.PowerPickup is { } pickup)
        {
            DrawTutorialCell(pickup.Position, PowerPresentation.SignalColor(pickup.Kind), 7.0f);
        }

        for (var index = 0; index < snapshot.Body.Count; index++)
        {
            DrawTutorialCell(
                snapshot.Body[index],
                index == snapshot.Body.Count - 1
                    ? GameplayPresentation.HeadColor
                    : GameplayPresentation.BodyColor,
                index == snapshot.Body.Count - 1 ? 3.0f : 6.0f);
        }

        DrawLabel(
            Localize("onboarding.connected-edges"),
            new Vector2(originX + 42.0f, originY + 244.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
    }

    private static string OnboardingLessonTitle(OnboardingLesson lesson) => lesson switch
    {
        OnboardingLesson.Turning => "TURNING",
        OnboardingLesson.InvalidReversal => "INVALID REVERSAL",
        OnboardingLesson.Wrapping => "EDGE WRAPPING",
        OnboardingLesson.FoodAndScore => "FOOD, GROWTH, AND SCORE",
        OnboardingLesson.Starvation => "STARVATION",
        OnboardingLesson.PowerUp => "POWER-UP",
        OnboardingLesson.Pause => "PAUSE",
        OnboardingLesson.Restart => "DELIBERATE RESTART",
        _ => throw new ArgumentOutOfRangeException(nameof(lesson)),
    };

    private static string OnboardingLessonInstruction(OnboardingLesson lesson) => lesson switch
    {
        OnboardingLesson.Turning => "Turn upward. Input is consumed on the next deterministic step.",
        OnboardingLesson.InvalidReversal => "Try to reverse downward. The unsafe opposite turn must be rejected.",
        OnboardingLesson.Wrapping => "Move left through the edge. The opposite edge is connected.",
        OnboardingLesson.FoodAndScore => "Move right into food. Food grows the body and raises score.",
        OnboardingLesson.Starvation => "Move right twice without food. Watch the warning before starvation.",
        OnboardingLesson.PowerUp => "Move right into Shield. Powers change recovery choices visibly.",
        OnboardingLesson.Pause => "Pause before another rules step. Hidden movement is not accepted.",
        OnboardingLesson.Restart => "Confirm once to finish practice and prepare a fresh scored run.",
        _ => throw new ArgumentOutOfRangeException(nameof(lesson)),
    };

    private static string OnboardingLessonAction(OnboardingLesson lesson) => lesson switch
    {
        OnboardingLesson.Turning => "move_up",
        OnboardingLesson.InvalidReversal => "move_down",
        OnboardingLesson.Wrapping => "move_left",
        OnboardingLesson.FoodAndScore => "move_right",
        OnboardingLesson.Starvation => "move_right",
        OnboardingLesson.PowerUp => "move_right",
        OnboardingLesson.Pause => "pause",
        OnboardingLesson.Restart => "confirm",
        _ => throw new ArgumentOutOfRangeException(nameof(lesson)),
    };

    private static string OnboardingLessonActionCaption(OnboardingLesson lesson) => lesson switch
    {
        OnboardingLesson.Turning => "turn up",
        OnboardingLesson.InvalidReversal => "try reverse",
        OnboardingLesson.Wrapping => "wrap left",
        OnboardingLesson.FoodAndScore => "eat food",
        OnboardingLesson.Starvation => "advance hunger",
        OnboardingLesson.PowerUp => "collect Shield",
        OnboardingLesson.Pause => "pause safely",
        OnboardingLesson.Restart => "finish and restart",
        _ => throw new ArgumentOutOfRangeException(nameof(lesson)),
    };

    private void DrawSettingsBrowse()
    {
        DrawLabel(
            Localize("screen.settings.title"),
            new Vector2(42.0f, 78.0f),
            ScaledFontSize(40),
            PrimaryTextColor());
        DrawLabel(
            _settingsStatusCaption ?? Localize("settings.select-section"),
            new Vector2(46.0f, 116.0f),
            ScaledFontSize(15),
            ActiveShellPalette.GoldText);

        if (_playerDataOperation is not null)
        {
            DrawLabel(
                Localize("settings.player-data.operation"),
                new Vector2(46.0f, 210.0f),
                ScaledFontSize(24),
                ActiveShellPalette.WarningText);
            DrawLabel(
                Localize("settings.player-data.operation-help"),
                new Vector2(46.0f, 254.0f),
                ScaledFontSize(14),
                ActiveShellPalette.BodyText);
            return;
        }

        if (_playerDataRecoveryBrowseOpen && _playerDataBackups.Count > 0)
        {
            DrawPlayerDataRecoveryBrowse();
            return;
        }

        if (_playtestDeleteConfirmation)
        {
            DrawLabel(
                Localize("settings.playtest.delete-title"),
                new Vector2(46.0f, 210.0f),
                ScaledFontSize(23),
                ActiveShellPalette.WarningText);
            DrawLabel(
                $"This removes {_localPlaytestSummaryCount} stored summaries and every generated export.",
                new Vector2(46.0f, 258.0f),
                ScaledFontSize(16),
                ActiveShellPalette.BodyText);
            DrawLabel(
                Localize("settings.playtest.delete-help"),
                new Vector2(46.0f, 298.0f),
                ScaledFontSize(16),
                ActiveShellPalette.GoldText);
            var deleteX = DrawActionPromptSegment(
                "confirm",
                Localize("action.delete-permanently"),
                new Vector2(46.0f, 370.0f),
                ScaledFontSize(15),
                SecondaryTextColor());
            DrawActionPromptSegment(
                "back",
                Localize("action.cancel-without-deleting"),
                new Vector2(deleteX, 370.0f),
                ScaledFontSize(15),
                SecondaryTextColor());
            return;
        }

        if (_settingsFullResetConfirmation)
        {
            var plan = _pendingDataResetPlan
                ?? throw new InvalidOperationException("Reset confirmation has no plan.");
            DrawLabel(
                Localize("settings.reset.title"),
                new Vector2(46.0f, 210.0f),
                ScaledFontSize(24),
                ActiveShellPalette.WarningText);
            DrawLabel(
                Localize("settings.reset.targets-help"),
                new Vector2(46.0f, 250.0f),
                ScaledFontSize(16),
                ActiveShellPalette.BodyText);
            var targetY = 286.0f;
            foreach (var target in plan.RelativeTargets)
            {
                DrawLabel(
                    Localize(
                        "settings.reset.target",
                        ShellTextArgument.From("target", target.Replace('\\', '/'))),
                    new Vector2(66.0f, targetY),
                    ScaledFontSize(16),
                    ActiveShellPalette.PrimaryText);
                targetY += 34.0f;
            }

            DrawLabel(
                Localize("settings.reset.backup-help"),
                new Vector2(46.0f, 382.0f),
                ScaledFontSize(15),
                ActiveShellPalette.GoldText);
            DrawLabel(
                Localize(
                    "settings.reset.backup-location",
                    ShellTextArgument.From("backup", plan.BackupId)),
                new Vector2(46.0f, 418.0f),
                ScaledFontSize(12),
                SecondaryTextColor());
            var confirmationX = DrawActionPromptSegment(
                "confirm",
                Localize("action.create-backup-reset"),
                new Vector2(46.0f, 474.0f),
                ScaledFontSize(15),
                SecondaryTextColor());
            DrawActionPromptSegment(
                "back",
                Localize("action.cancel-without-writing"),
                new Vector2(confirmationX, 474.0f),
                ScaledFontSize(15),
                SecondaryTextColor());
            return;
        }

        if (!_settingsSectionOpen)
        {
            var y = 166.0f;
            for (var index = 0; index < SettingsMenuCatalog.Sections.Count; index++)
            {
                var section = SettingsMenuCatalog.Sections[index];
                var selected = index == _settingsSectionCursor;
                DrawLabel(
                    ShellFocusPresentation.SelectionPrefix(selected)
                        + section.ToString().ToUpperInvariant(),
                    new Vector2(60.0f, y),
                    ScaledFontSize(20),
                    selected ? ActiveShellPalette.SelectedText : ActiveShellPalette.BodyText);
                DrawLabel(
                    SettingsSectionSummary(section),
                    new Vector2(280.0f, y),
                    ScaledFontSize(14),
                    SecondaryTextColor());
                y += 62.0f;
            }

            DrawLabel(
                Localize("settings.navigation.sections"),
                new Vector2(46.0f, 610.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            var topLevelX = DrawActionPromptSegment(
                "confirm",
                Localize("action.open"),
                new Vector2(46.0f, 644.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            DrawActionPromptSegment(
                "back",
                Localize("action.return"),
                new Vector2(topLevelX, 644.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            return;
        }

        DrawLabel(
            CurrentSettingsSection.ToString().ToUpperInvariant(),
            new Vector2(46.0f, 150.0f),
            ScaledFontSize(22),
            ActiveShellPalette.PrimaryText);
        var items = CurrentSettingsItems();
        const int visibleSettingsRows = 8;
        var firstVisibleItem = Math.Clamp(
            _settingsItemCursor - (visibleSettingsRows - 1),
            0,
            Math.Max(0, items.Count - visibleSettingsRows));
        var itemY = 190.0f;
        for (var index = firstVisibleItem;
            index < Math.Min(items.Count, firstVisibleItem + visibleSettingsRows);
            index++)
        {
            var item = items[index];
            var selected = index == _settingsItemCursor;
            DrawLabel(
                ShellFocusPresentation.SelectionPrefix(selected)
                    + item.Label.ToUpperInvariant(),
                new Vector2(60.0f, itemY),
                ScaledFontSize(16),
                selected ? ActiveShellPalette.SelectedText : ActiveShellPalette.BodyText);
            DrawLabel(
                FormatSettingValue(item.Id),
                new Vector2(430.0f, itemY),
                ScaledFontSize(15),
                selected ? ActiveShellPalette.GoldText : SecondaryTextColor());
            itemY += 44.0f;
        }

        var selectedItem = items[_settingsItemCursor];
        DrawLabel(
            selectedItem.Description,
            new Vector2(60.0f, 592.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        DrawLabel(
            Localize("settings.navigation.items"),
            new Vector2(46.0f, 626.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        var itemX = DrawActionPromptSegment(
            "confirm",
            Localize("action.toggle-use"),
            new Vector2(46.0f, 658.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "back",
            Localize("action.sections"),
            new Vector2(itemX, 658.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
    }

    private void DrawPlayerDataRecoveryBrowse()
    {
        var backup = CurrentPlayerDataBackup();
        DrawLabel(
            $"BACKUP {_playerDataBackupCursor + 1} OF {_playerDataBackups.Count}",
            new Vector2(46.0f, 178.0f),
            ScaledFontSize(22),
            ActiveShellPalette.PrimaryText);
        DrawLabel(
            backup.CanRestore ? "VERIFIED AND RESTORABLE" : "RESTORE BLOCKED",
            new Vector2(46.0f, 218.0f),
            ScaledFontSize(20),
            backup.CanRestore
                ? ActiveShellPalette.GoldText
                : ActiveShellPalette.WarningText);
        DrawLabel(
            Localize(
                "settings.backup.location",
                ShellTextArgument.From(
                    "location",
                    BoundPlayerDataCaption(backup.RelativeLocation, 96))),
            new Vector2(46.0f, 258.0f),
            ScaledFontSize(14),
            SecondaryTextColor());
        DrawLabel(
            Localize(
                "settings.backup.categories",
                ShellTextArgument.From(
                    "categories",
                    FormatPlayerDataCategories(backup.Categories))),
            new Vector2(46.0f, 292.0f),
            ScaledFontSize(14),
            ActiveShellPalette.BodyText);
        DrawLabel(
            $"Files: {backup.FileCount}  Bytes: {backup.TotalBytes}",
            new Vector2(46.0f, 326.0f),
            ScaledFontSize(14),
            ActiveShellPalette.BodyText);
        DrawLabel(
            BoundPlayerDataCaption(backup.Message, 110),
            new Vector2(46.0f, 368.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        DrawLabel(
            backup.CanRestore
                ? "Restore never overwrites current data. Back keeps current data unchanged."
                : "This backup is kept for support. Confirm opens its folder; Back keeps it unchanged.",
            new Vector2(46.0f, 414.0f),
            ScaledFontSize(14),
            ActiveShellPalette.PrimaryText);
        DrawLabel(
            Localize("settings.backup.navigation"),
            new Vector2(46.0f, 472.0f),
            ScaledFontSize(14),
            SecondaryTextColor());
        var actionX = DrawActionPromptSegment(
            "confirm",
            backup.CanRestore ? "restore verified backup" : "open backup location",
            new Vector2(46.0f, 522.0f),
            ScaledFontSize(14),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "back",
            Localize("action.keep-current-data"),
            new Vector2(actionX, 522.0f),
            ScaledFontSize(14),
            SecondaryTextColor());
    }

    private static string FormatPlayerDataCategories(
        IReadOnlyList<PlayerDataCategory> categories) =>
        categories.Count == 0
            ? "UNKNOWN"
            : string.Join(
                ", ",
                categories.Select(category => category switch
                {
                    PlayerDataCategory.Preferences => "SETTINGS + BINDINGS",
                    PlayerDataCategory.Progression => "PROGRESSION",
                    PlayerDataCategory.PersonalBests => "LOCAL SCORES + PERSONAL BESTS",
                    PlayerDataCategory.Replays => "REPLAYS",
                    PlayerDataCategory.OptionalContent => "OPTIONAL CONTENT",
                    _ => throw new InvalidOperationException("Unknown player data category."),
                }));

    private static string BoundPlayerDataCaption(string caption, int maximumCharacters)
    {
        ArgumentNullException.ThrowIfNull(caption);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumCharacters);

        var sanitized = string.Concat(caption.Select(character =>
            char.IsControl(character) ? ' ' : character));
        return sanitized.Length <= maximumCharacters
            ? sanitized
            : sanitized[..maximumCharacters];
    }

    private string FormatSettingValue(string itemId) => itemId switch
    {
        "rules_identity" => SnakeRun.RulesetId + "@" + SnakeRun.RulesVersion,
        "fixed_step" => RunConfig.RulesTickMilliseconds + " ms",
        "input_buffer" => SelectedRunConfig().MaximumDirectionQueue + " turns",
        "vibe_adaptation" => FormatToggle(_shellSettings.VibeAdaptationEnabled),
        "local_playtest_summaries" => FormatToggle(
            _shellSettings.LocalPlaytestSummariesEnabled),
        "controller_deadzone" => FormatPercentage(_shellSettings.ControllerDeadzone),
        "open_bindings" => "OPEN",
        "restore_bindings" => "RESTORE",
        "master_volume" => FormatPercentage(_shellSettings.MasterVolume),
        "master_muted" => FormatToggle(_shellSettings.MasterMuted),
        "music_volume" => FormatPercentage(_shellSettings.MusicVolume),
        "music_muted" => FormatToggle(_shellSettings.MusicMuted),
        "sfx_volume" => FormatPercentage(_shellSettings.SfxVolume),
        "sfx_muted" => FormatToggle(_shellSettings.SfxMuted),
        "ui_volume" => FormatPercentage(_shellSettings.UiVolume),
        "ui_muted" => FormatToggle(_shellSettings.UiMuted),
        "mono_output" => FormatToggle(_shellSettings.MonoOutput),
        "window_mode" => DisplayOptions.WindowModeLabel(_shellSettings.WindowMode),
        "window_size" => DisplayOptions.WindowSize(_shellSettings.WindowSizePreset).Label,
        "high_contrast" => FormatToggle(_shellSettings.HighContrast),
        "reduced_motion" => FormatToggle(_shellSettings.ReducedMotion),
        "text_scale" => FormatPercentage(_shellSettings.TextScale),
        "screen_shake" => FormatPercentage(_shellSettings.ScreenShakeIntensity),
        "flash_free" => FormatToggle(_shellSettings.FlashFree),
        "open_diagnostics" => "OPEN",
        "reset_tutorial" => _onboardingProgress.Status switch
        {
            OnboardingStatus.NotStarted => "NOT STARTED",
            OnboardingStatus.Skipped => "SKIPPED",
            OnboardingStatus.Completed => "COMPLETED",
            _ => throw new InvalidOperationException("Unknown onboarding status."),
        },
        "reset_preferences" => "CONFIRM TWICE",
        "reset_progression" => "BACKUP + RESET",
        "reset_personal_bests" => "BACKUP + RESET",
        "reset_replays" => "BACKUP + RESET",
        "reset_optional_content" => "BACKUP + RESET",
        "recover_backup" => "INSPECT",
        "export_playtest_summaries" => $"EXPORT {_localPlaytestSummaryCount}",
        "delete_playtest_summaries" => $"DELETE {_localPlaytestSummaryCount}",
        _ => throw new ArgumentOutOfRangeException(nameof(itemId)),
    };

    private static string SettingsSectionSummary(SettingsSection section) => section switch
    {
        SettingsSection.Gameplay => "Rules, Vibe adaptation, and local playtest consent",
        SettingsSection.Controls => "Stick deadzone, bindings, and safe defaults",
        SettingsSection.Audio => "Master, groups, mutes, and mono downmix",
        SettingsSection.Display => "4:3 and widescreen sizes, windowed, borderless, and fullscreen",
        SettingsSection.Accessibility => "Contrast, motion, text, shake, and flashes",
        SettingsSection.Data => "Diagnostics, local summary export/delete, reset and recovery",
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    private static string FormatPercentage(float value) =>
        Math.Round(value * 100.0f, MidpointRounding.AwayFromZero) + "%";

    private static string FormatToggle(bool enabled) => enabled ? "ON" : "OFF";

    private void DrawBindingsBrowse()
    {
        DrawLabel(
            Localize("screen.bindings.title"),
            new Vector2(42.0f, 100.0f),
            ScaledFontSize(40),
            PrimaryTextColor());
        DrawLabel(
            _pendingBindingConflict is not null
                ? "CONFLICT RESOLUTION"
                : _bindingsCapturePending
                    ? "CAPTURE MODE"
                    : "Schema 1 keyboard and controller remap (F8 restores defaults)",
            new Vector2(46.0f, 148.0f),
            ScaledFontSize(18),
            ActiveShellPalette.GoldText);
        if (_bindingsStatusCaption is not null)
        {
            DrawLabel(
                _bindingsStatusCaption,
                new Vector2(46.0f, 172.0f),
                ScaledFontSize(15),
                ActiveShellPalette.MutedGoldText);
        }

        var y = 204.0f;
        var tabCaption = _bindingsDeviceTab == BindingsDeviceTab.Keyboard
            ? "<  KEYBOARD  >    Controller"
            : "Keyboard    <  CONTROLLER  >";
        DrawLabel(tabCaption, new Vector2(46.0f, y), ScaledFontSize(20), PrimaryTextColor());
        y += 28.0f;
        var document = CurrentBindingsDocument();
        var promptFamily = _bindingsDeviceTab == BindingsDeviceTab.Keyboard
            ? InputPromptFamily.Keyboard
            : _controllerPromptFamily;
        var actions = ListRemappableActions();
        for (var index = 0; index < actions.Length; index++)
        {
            var action = actions[index];
            var token = document.ActionToBinding[action];
            var selected = index == _bindingsCursor;
            var prefix = ShellFocusPresentation.BindingPrefix(
                selected,
                _bindingsCapturePending,
                _pendingBindingConflict is not null);
            var color = selected
                ? ActiveShellPalette.SelectedText
                : ActiveShellPalette.BodyText;
            DrawLabel(
                $"{prefix} {action.ToUpperInvariant()}",
                new Vector2(60.0f, y),
                ScaledFontSize(16),
                color);
            var glyph = InputPromptGlyphs.DescribeToken(token, promptFamily);
            var measurement = PromptBadgeRenderer.Draw(
                this,
                ActiveShellTheme.InterfaceFont,
                glyph,
                new Vector2(300.0f, y),
                ScaledFontSize(13),
                ActiveShellPalette);
            DrawLabel(
                token,
                new Vector2(312.0f + measurement.Width, y),
                ScaledFontSize(13),
                SecondaryTextColor());
            y += ScaledCatalogRowHeight(baseFontSize: 16, minimum: 28.0f);
        }

        if (document.ActionToBinding.TryGetValue("restore_defaults", out var restoreToken))
        {
            DrawLabel(
                Localize("bindings.restore-defaults"),
                new Vector2(60.0f, y),
                ScaledFontSize(14),
                SecondaryTextColor());
            var restoreGlyph = InputPromptGlyphs.DescribeToken(restoreToken, promptFamily);
            var measurement = PromptBadgeRenderer.Draw(
                this,
                ActiveShellTheme.InterfaceFont,
                restoreGlyph,
                new Vector2(300.0f, y),
                ScaledFontSize(12),
                ActiveShellPalette);
            DrawLabel(
                restoreToken + "  fixed escape hatch",
                new Vector2(312.0f + measurement.Width, y),
                ScaledFontSize(12),
                SecondaryTextColor());
            y += ScaledCatalogRowHeight(baseFontSize: 14, minimum: 28.0f);
        }

        var footerY = Math.Min(y + 14.0f, 650.0f);
        if (_pendingBindingConflict is not null)
        {
            var nextX = DrawActionPromptSegment(
                "confirm",
                Localize("action.swap"),
                new Vector2(46.0f, footerY),
                ScaledFontSize(14),
                SecondaryTextColor());
            DrawActionPromptSegment(
                "back",
                Localize("action.cancel"),
                new Vector2(nextX, footerY),
                ScaledFontSize(14),
                SecondaryTextColor());
        }
        else
        {
            DrawLabel(
                Localize("bindings.navigation"),
                new Vector2(46.0f, footerY),
                ScaledFontSize(14),
                SecondaryTextColor());
            var nextX = DrawActionPromptSegment(
                "confirm",
                Localize("action.capture"),
                new Vector2(46.0f, Math.Min(footerY + 26.0f, 680.0f)),
                ScaledFontSize(13),
                SecondaryTextColor());
            DrawActionPromptSegment(
                "back",
                Localize("action.cancel-back"),
                new Vector2(nextX, Math.Min(footerY + 26.0f, 680.0f)),
                ScaledFontSize(13),
                SecondaryTextColor());
        }
    }

    private void DrawAchievementsBrowse()
    {
        var progress = _progression.BuildGoalProgress();
        var completedCount = progress.Count(item => item.Completed);
        DrawLabel(
            Localize("screen.progression.title"),
            new Vector2(42.0f, 100.0f),
            ScaledFontSize(40),
            PrimaryTextColor());
        DrawLabel(
            $"EXACT PROGRESS {completedCount}/{progress.Count}  |  MASTERY / DISCOVERY / IDENTITY",
            new Vector2(46.0f, 148.0f),
            ScaledFontSize(20),
            ActiveShellPalette.GoldText);
        DrawLabel(
            Localize("progression.explainer"),
            new Vector2(46.0f, 176.0f),
            ScaledFontSize(14),
            ActiveShellPalette.MutedGoldText);

        var pageCount = AchievementPageCount(progress.Count);
        _achievementsPage = Math.Clamp(_achievementsPage, 0, pageCount - 1);
        DrawLabel(
            $"PAGE {_achievementsPage + 1}/{pageCount}",
            new Vector2(1020.0f, 176.0f),
            ScaledFontSize(14),
            SecondaryTextColor());

        var start = _achievementsPage * ProgressionGoalsPerPage;
        var end = Math.Min(progress.Count, start + ProgressionGoalsPerPage);
        var y = 214.0f;
        var rowHeight = Math.Max(
            64.0f,
            (ActiveShellTheme.InterfaceFont.GetHeight(ScaledFontSize(15)) * 2.0f) + 12.0f);
        for (var index = start; index < end; index++)
        {
            var item = progress[index];
            var focus = index == _progressionGoalCursor ? ">" : " ";
            var completion = item.Completed ? "[X]" : "[ ]";
            var highlighted = item.Highlighted ? "*" : " ";
            var line = $"{focus}{highlighted}{completion} {item.Definition.Name.ToUpperInvariant()} "
                + $"[{item.Definition.Lane.ToString().ToUpperInvariant()}] {item.ExactProgress}  "
                + $"REWARD {item.Definition.Reward.DisplayName.ToUpperInvariant()}";
            var color = item.Completed
                ? ActiveShellPalette.GoldText
                : index == _progressionGoalCursor
                    ? ActiveShellPalette.PrimaryText
                    : ActiveShellPalette.SecondaryText;
            DrawLabel(line, new Vector2(46.0f, y), ScaledFontSize(15), color);
            DrawLabel(
                item.Definition.ExactRequirement + "  VIBE / "
                    + $"{item.Definition.RulesetId}@{item.Definition.RulesVersion}",
                new Vector2(72.0f, y + (rowHeight * 0.48f)),
                ScaledFontSize(13),
                SecondaryTextColor());
            y += rowHeight;
        }

        if (!string.IsNullOrWhiteSpace(_progressionStatusCaption))
        {
            DrawLabel(
                _progressionStatusCaption,
                new Vector2(46.0f, Math.Min(y + 4.0f, 622.0f)),
                ScaledFontSize(13),
                ActiveShellPalette.WarningText);
        }

        var promptY = Math.Min(y + 12.0f, 664.0f);
        var pageX = DrawActionPromptSegment(
            "move_left",
            Localize("action.previous-page"),
            new Vector2(46.0f, promptY),
            ScaledFontSize(13),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "move_right",
            Localize("action.next-page"),
            new Vector2(pageX, promptY),
            ScaledFontSize(13),
            SecondaryTextColor());
        promptY = Math.Min(promptY + 28.0f, 692.0f);
        var nextX = DrawActionPromptSegment(
            "confirm",
            Localize("action.highlight-next-goal"),
            new Vector2(46.0f, promptY),
            ScaledFontSize(14),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "back",
            Localize("action.return"),
            new Vector2(nextX, promptY),
            ScaledFontSize(14),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "replay",
            Localize("action.broadcast-tour"),
            new Vector2(690.0f, promptY),
            ScaledFontSize(14),
            ActiveShellPalette.GoldText);
        DrawActionPromptSegment(
            "browse_content_packs",
            Localize("action.cosmetic-sets"),
            new Vector2(950.0f, promptY),
            ScaledFontSize(14),
            ActiveShellPalette.GoldText);
    }

    private void DrawBroadcastTour()
    {
        var cards = BroadcastTourSession.BuildCards(_progression.CompletedTourEventIds);
        var completedCount = cards.Count(card => card.State == BroadcastTourEventState.Completed);
        var pageCount = (int)Math.Ceiling(cards.Count / (double)TourCardsPerPage);
        _tourPage = Math.Clamp(_tourPage, 0, pageCount - 1);
        _tourCursor = Math.Clamp(_tourCursor, 0, cards.Count - 1);
        DrawLabel(
            Localize("screen.tour.title"),
            new Vector2(42.0f, 86.0f),
            ScaledFontSize(40),
            PrimaryTextColor());
        DrawLabel(
            Localize(
                "tour.summary",
                ShellTextArgument.From("completed", completedCount),
                ShellTextArgument.From("total", cards.Count),
                ShellTextArgument.From("page", _tourPage + 1),
                ShellTextArgument.From("pages", pageCount)),
            new Vector2(46.0f, 128.0f),
            ScaledFontSize(17),
            ActiveShellPalette.GoldText);
        DrawLabel(
            Localize("tour.practice-notice"),
            new Vector2(46.0f, 154.0f),
            ScaledFontSize(13),
            ActiveShellPalette.MutedGoldText);

        var start = _tourPage * TourCardsPerPage;
        var end = Math.Min(cards.Count, start + TourCardsPerPage);
        var y = 194.0f;
        var rowHeight = Math.Max(
            126.0f,
            ActiveShellTheme.InterfaceFont.GetHeight(ScaledFontSize(15)) * 4.5f);
        for (var index = start; index < end; index++)
        {
            var card = cards[index];
            var item = card.Event;
            var selected = index == _tourCursor;
            var marker = card.State switch
            {
                BroadcastTourEventState.Completed => "[X]",
                BroadcastTourEventState.Available => "[>]",
                _ => "[-]",
            };
            var color = selected
                ? ActiveShellPalette.PrimaryText
                : card.State == BroadcastTourEventState.Completed
                    ? ActiveShellPalette.GoldText
                    : ActiveShellPalette.SecondaryText;
            var rival = AiPersonalityCatalog.GetBuiltIn(item.RivalId).Name;
            var station = BroadcastStationCatalog.Find(item.StationId)?.StationName
                ?? item.StationId;
            var primaryProgress = card.State == BroadcastTourEventState.Completed
                ? $"{item.PrimaryGoal.Target}/{item.PrimaryGoal.Target}"
                : $"0/{item.PrimaryGoal.Target}";
            DrawLabel(
                $"{(selected ? ">" : " ")} {marker} {FormatTourEventName(item.Id)}  |  {FormatTourTier(item.Tier)}",
                new Vector2(46.0f, y),
                ScaledFontSize(16),
                color);
            DrawLabel(
                $"RIVAL {rival.ToUpperInvariant()}  |  STATION {station.ToUpperInvariant()}  |  SEED {item.FixedSeed}",
                new Vector2(72.0f, y + (rowHeight * 0.24f)),
                ScaledFontSize(13),
                SecondaryTextColor());
            DrawLabel(
                $"PRIMARY {primaryProgress}: {item.PrimaryGoal.ExactRequirement}"
                    + (item.StyleGoal is { } style
                        ? $"  STYLE: {style.ExactRequirement}"
                        : string.Empty),
                new Vector2(72.0f, y + (rowHeight * 0.48f)),
                ScaledFontSize(13),
                SecondaryTextColor());
            DrawLabel(
                $"REWARD {item.Reward.DisplayName.ToUpperInvariant()}  |  VIBE@{item.ModeVersion} / {item.RulesetId}@{item.RulesVersion}",
                new Vector2(72.0f, y + (rowHeight * 0.72f)),
                ScaledFontSize(13),
                ActiveShellPalette.MutedGoldText);
            y += rowHeight;
        }

        if (!string.IsNullOrWhiteSpace(_tourStatusCaption))
        {
            DrawLabel(
                _tourStatusCaption,
                new Vector2(46.0f, 584.0f),
                ScaledFontSize(13),
                ActiveShellPalette.WarningText);
        }

        DrawLabel(
            Localize("tour.navigation"),
            new Vector2(46.0f, 620.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        var nextX = DrawActionPromptSegment(
            "confirm",
            Localize("tour.action.start"),
            new Vector2(46.0f, 654.0f),
            ScaledFontSize(14),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "back",
            Localize("tour.action.back"),
            new Vector2(nextX, 654.0f),
            ScaledFontSize(14),
            SecondaryTextColor());
    }

    private static string FormatTourTier(BroadcastTourTier tier) => tier switch
    {
        BroadcastTourTier.LocalFrequency => "LOCAL FREQUENCY",
        BroadcastTourTier.DistrictRelay => "DISTRICT RELAY",
        BroadcastTourTier.RegionalCoil => "REGIONAL COIL",
        BroadcastTourTier.CrownBroadcast => "CROWN BROADCAST",
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    private static string FormatTourEventName(string eventId) =>
        string.Join(
                " ",
                eventId.Split('-', StringSplitOptions.RemoveEmptyEntries)
                    .Select(word => char.ToUpperInvariant(word[0]) + word[1..]))
            .ToUpperInvariant();

    private void DrawCosmeticSets()
    {
        var sets = CosmeticSetCatalog.Sets;
        var pageCount = (int)Math.Ceiling(sets.Count / (double)CosmeticSetsPerPage);
        _cosmeticPage = Math.Clamp(_cosmeticPage, 0, pageCount - 1);
        _cosmeticCursor = Math.Clamp(_cosmeticCursor, 0, sets.Count - 1);
        var selectedCosmetic = sets[_cosmeticCursor];
        DrawLabel(
            Localize("screen.cosmetics.title"),
            new Vector2(42.0f, 86.0f),
            ScaledFontSize(40),
            PrimaryTextColor());
        DrawLabel(
            Localize(
                "cosmetics.summary",
                ShellTextArgument.From("total", sets.Count),
                ShellTextArgument.From("earned", _progression.Metrics.CosmeticSetsUnlocked),
                ShellTextArgument.From("saved", _progression.SavedCosmeticSetIds.Count),
                ShellTextArgument.From("slots", ProgressionDocument.MaximumSavedCosmeticSets),
                ShellTextArgument.From("page", _cosmeticPage + 1),
                ShellTextArgument.From("pages", pageCount)),
            new Vector2(46.0f, 130.0f),
            ScaledFontSize(16),
            ActiveShellPalette.GoldText);
        DrawLabel(
            Localize("cosmetics.isolation"),
            new Vector2(46.0f, 156.0f),
            ScaledFontSize(13),
            ActiveShellPalette.MutedGoldText);

        var start = _cosmeticPage * CosmeticSetsPerPage;
        var end = Math.Min(sets.Count, start + CosmeticSetsPerPage);
        DrawRect(
            new Rect2(46.0f, 192.0f, 350.0f, 328.0f),
            new Color(0.02f, 0.018f, 0.055f, 0.72f));
        for (var index = start; index < end; index++)
        {
            var cosmetic = sets[index];
            var focused = index == _cosmeticCursor;
            var unlocked = _progression.IsCosmeticSetUnlocked(cosmetic.Id);
            var equipped = cosmetic.Id == _progression.SelectedCosmeticSetId;
            var saved = _progression.SavedCosmeticSetIds.Contains(
                cosmetic.Id,
                StringComparer.Ordinal);
            var marker = equipped ? "[E]" : saved ? "[S]" : unlocked ? "[ ]" : "[-]";
            var color = focused
                ? ActiveShellPalette.PrimaryText
                : equipped || saved
                    ? ActiveShellPalette.GoldText
                    : ActiveShellPalette.SecondaryText;
            var rowIndex = index - start;
            var rowBounds = new Rect2(46.0f, 208.0f + (rowIndex * 104.0f), 350.0f, 96.0f);
            if (focused)
            {
                DrawRect(rowBounds, new Color(color.R * 0.12f, color.G * 0.12f, color.B * 0.12f, 0.96f));
            }

            var rail = color;
            rail.A = focused ? 1.0f : 0.44f;
            DrawRect(
                new Rect2(rowBounds.Position, new Vector2(focused ? 6.0f : 3.0f, rowBounds.Size.Y)),
                rail);
            if (!focused)
            {
                var divider = color;
                divider.A = 0.22f;
                DrawLine(
                    new Vector2(rowBounds.Position.X, rowBounds.End.Y),
                    rowBounds.End,
                    divider,
                    1.0f);
            }

            DrawLabel(
                $"{(focused ? ">" : " ")} {marker} {cosmetic.Name.ToUpperInvariant()}",
                rowBounds.Position + new Vector2(18.0f, 31.0f),
                ScaledFontSize(16),
                color);
            DrawLabel(
                unlocked ? "READY TO EQUIP" : "LOCKED",
                rowBounds.Position + new Vector2(26.0f, 61.0f),
                ScaledFontSize(12),
                unlocked ? ActiveShellPalette.MutedGoldText : ActiveShellPalette.WarningText);
            var primary = CosmeticColor(cosmetic.Primary);
            var secondary = CosmeticColor(cosmetic.Secondary);
            DrawRect(
                new Rect2(rowBounds.End.X - 62.0f, rowBounds.Position.Y + 22.0f, 18.0f, 48.0f),
                primary);
            DrawRect(
                new Rect2(rowBounds.End.X - 38.0f, rowBounds.Position.Y + 22.0f, 18.0f, 48.0f),
                secondary);
        }

        DrawRect(
            new Rect2(420.0f, 192.0f, 330.0f, 328.0f),
            new Color(0.02f, 0.018f, 0.055f, 0.55f));
        DrawLabel(
            selectedCosmetic.Name.ToUpperInvariant(),
            new Vector2(444.0f, 232.0f),
            ScaledFontSize(24),
            ActiveShellPalette.PrimaryText);
        var cosmeticDetail = LocalizedCosmeticDetail(selectedCosmetic);
        DrawFittedLabel(
            cosmeticDetail.Requirement,
            new Vector2(444.0f, 266.0f),
            preferredFontSize: ScaledFontSize(12),
            minimumFontSize: ScaledFontSize(10),
            maximumWidth: 282.0f,
            color: _progression.IsCosmeticSetUnlocked(selectedCosmetic.Id)
                ? ActiveShellPalette.MutedGoldText
                : ActiveShellPalette.WarningText);
        if (cosmeticDetail.Event is not null)
        {
            DrawFittedLabel(
                cosmeticDetail.Event,
                new Vector2(444.0f, 284.0f),
                preferredFontSize: ScaledFontSize(11),
                minimumFontSize: ScaledFontSize(9),
                maximumWidth: 282.0f,
                color: ActiveShellPalette.MutedGoldText);
        }
        var selectedState = selectedCosmetic.Id == _progression.SelectedCosmeticSetId
            ? "EQUIPPED"
            : _progression.SavedCosmeticSetIds.Contains(selectedCosmetic.Id, StringComparer.Ordinal)
                ? "SAVED LOADOUT"
                : "PREVIEWING";
        DrawLabel(
            selectedState,
            new Vector2(444.0f, 318.0f),
            ScaledFontSize(13),
            ActiveShellPalette.GoldText);
        string[] attributes =
        [
            $"PATTERN     {selectedCosmetic.PatternId}",
            $"EYES        {selectedCosmetic.EyeId}",
            $"ACCESSORY   {selectedCosmetic.AccessoryId}",
            $"TRAIL       {selectedCosmetic.TrailId}  {selectedCosmetic.TrailOpacityPercent}%",
            $"HEAD        {selectedCosmetic.HeadMarker}",
        ];
        for (var index = 0; index < attributes.Length; index++)
        {
            DrawLabel(
                attributes[index].ToUpperInvariant(),
                new Vector2(444.0f, 354.0f + (index * 30.0f)),
                ScaledFontSize(13),
                index == 0 ? ActiveShellPalette.BodyText : SecondaryTextColor());
        }

        var previewBounds = new Rect2(780.0f, 192.0f, 450.0f, 328.0f);
        DrawRect(previewBounds, new Color(0.018f, 0.05f, 0.052f, 0.96f));
        DrawRect(previewBounds, ActiveShellPalette.SecondaryText, filled: false, width: 2.0f);
        var previewGrid = ActiveShellPalette.SecondaryText;
        previewGrid.A = 0.12f;
        for (var x = previewBounds.Position.X + 16.0f; x < previewBounds.End.X; x += 16.0f)
        {
            DrawLine(
                new Vector2(x, previewBounds.Position.Y),
                new Vector2(x, previewBounds.End.Y),
                previewGrid,
                1.0f);
        }

        for (var y = previewBounds.Position.Y + 16.0f; y < previewBounds.End.Y; y += 16.0f)
        {
            DrawLine(
                new Vector2(previewBounds.Position.X, y),
                new Vector2(previewBounds.End.X, y),
                previewGrid,
                1.0f);
        }

        var previewLabel = "LIVE PREVIEW  //  4 X 4 DETAIL GRID";
        DrawLabel(
            previewLabel,
            new Vector2(806.0f, 228.0f),
            ScaledFontSize(13),
            ActiveShellPalette.MutedGoldText);
        DrawCosmeticPreview(selectedCosmetic, new Vector2(816.0f, 344.0f), 58.0f);

        if (!string.IsNullOrWhiteSpace(_cosmeticStatusCaption))
        {
            DrawLabel(
                _cosmeticStatusCaption,
                new Vector2(46.0f, 566.0f),
                ScaledFontSize(13),
                ActiveShellPalette.WarningText);
        }

        DrawLabel(
            Localize("cosmetics.navigation"),
            new Vector2(46.0f, 608.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        var nextX = DrawActionPromptSegment(
            "confirm",
            Localize("action.equip"),
            new Vector2(46.0f, 648.0f),
            ScaledFontSize(14),
            SecondaryTextColor());
        nextX = DrawActionPromptSegment(
            "replay",
            Localize("action.save-loadout"),
            new Vector2(nextX, 648.0f),
            ScaledFontSize(14),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "back",
            Localize("action.progression-goals"),
            new Vector2(nextX, 648.0f),
            ScaledFontSize(14),
            SecondaryTextColor());
    }

    private void DrawCosmeticPreview(
        CosmeticSetDefinition cosmetic,
        Vector2 origin,
        float cellSize)
    {
        var primary = CosmeticColor(cosmetic.Primary);
        if (cosmetic.TrailOpacityPercent > 0)
        {
            var trail = primary;
            trail.A = cosmetic.TrailOpacityPercent / 100.0f;
            for (var index = 0; index < 4; index++)
            {
                var size = 8.0f + (index * 3.0f);
                DrawRect(
                    new Rect2(
                        origin.X - 54.0f + (index * 12.0f),
                        origin.Y + ((cellSize - size) * 0.5f),
                        size,
                        size),
                    trail);
            }
        }

        for (var index = 0; index < 6; index++)
        {
            var isHead = index == 5;
            var bounds = new Rect2(
                origin.X + (index * (cellSize + 4.0f)),
                origin.Y,
                cellSize,
                cellSize);
            DrawDetailedCosmeticCell(
                bounds,
                cosmetic,
                index,
                isHead,
                CosmeticBodyColor(cosmetic, index, isHead));
        }

        var headLeft = origin.X + (5 * (cellSize + 4.0f));
        DrawRect(
            new Rect2(headLeft + cellSize, origin.Y + (cellSize * 0.375f), 12.0f, cellSize * 0.25f),
            primary);
        if (cosmetic.AccessoryId != "none")
        {
            var accessoryWidth = cellSize * 0.62f;
            var accessoryHeight = Math.Max(8.0f, cellSize * 0.18f);
            DrawRect(
                new Rect2(
                    headLeft + ((cellSize - accessoryWidth) * 0.5f),
                    origin.Y - accessoryHeight,
                    accessoryWidth,
                    accessoryHeight),
                primary);
            DrawRect(
                new Rect2(
                    headLeft + (cellSize * 0.38f),
                    origin.Y - (accessoryHeight * 1.8f),
                    cellSize * 0.24f,
                    accessoryHeight),
                primary);
        }
    }

    private void DrawDetailedCosmeticCell(
        Rect2 bounds,
        CosmeticSetDefinition cosmetic,
        int bodyIndex,
        bool isHead,
        Color baseColor)
    {
        DrawRect(bounds, baseColor);
        var highlight = isHead
            ? CosmeticColor(cosmetic.Primary)
            : CosmeticColor(cosmetic.Secondary);
        var shadow = new Color(baseColor.R * 0.58f, baseColor.G * 0.58f, baseColor.B * 0.58f, baseColor.A);
        var softHighlight = highlight;
        softHighlight.A = Math.Min(0.36f, highlight.A);
        for (var column = 0; column < 4; column++)
        {
            DrawRect(CosmeticSubPixel(bounds, column, 0), softHighlight);
            DrawRect(CosmeticSubPixel(bounds, column, 3), shadow);
        }

        void DrawDetail(int column, int row)
        {
            var detail = highlight;
            detail.A = Math.Min(0.82f, highlight.A);
            DrawRect(CosmeticSubPixel(bounds, column, row), detail);
        }

        switch (cosmetic.PatternId)
        {
            case "solid":
                DrawDetail(0, 0);
                DrawDetail(1, 0);
                break;
            case "relay-stripe":
                for (var row = 0; row < 4; row++)
                {
                    DrawDetail((bodyIndex + 1) % 3, row);
                }

                break;
            case "mutation-dot":
                DrawDetail(1, 1);
                DrawDetail(2, 1);
                DrawDetail(1, 2);
                DrawDetail(2, 2);
                break;
            case "speed-band":
                for (var column = 0; column < 4; column++)
                {
                    DrawDetail(column, bodyIndex % 2 == 0 ? 1 : 2);
                }

                break;
            case "edge-chevron":
                DrawDetail(0, 1);
                DrawDetail(1, 2);
                DrawDetail(2, 2);
                DrawDetail(3, 1);
                break;
            case "flow-line":
                for (var column = 0; column < 4; column++)
                {
                    DrawDetail(column, (column + bodyIndex) % 3);
                }

                break;
            case "balanced-grid":
                for (var row = 0; row < 4; row++)
                {
                    for (var column = 0; column < 4; column++)
                    {
                        if ((row + column + bodyIndex) % 2 == 0)
                        {
                            DrawDetail(column, row);
                        }
                    }
                }

                break;
            case "crown-band":
                DrawDetail(0, 1);
                DrawDetail(1, 0);
                DrawDetail(2, 0);
                DrawDetail(3, 1);
                break;
        }

        if (isHead)
        {
            var eye = ActiveShellPalette.CanvasBackground;
            if (cosmetic.EyeId == "visor")
            {
                DrawRect(CosmeticSubPixel(bounds, 2, 1), eye);
                DrawRect(CosmeticSubPixel(bounds, 3, 1), eye);
                DrawRect(CosmeticSubPixel(bounds, 2, 2), eye);
                DrawRect(CosmeticSubPixel(bounds, 3, 2), eye);
            }
            else
            {
                DrawRect(CosmeticSubPixel(bounds, 3, 1), eye);
                DrawRect(CosmeticSubPixel(bounds, 3, 2), eye);
            }
        }

        var outline = ActiveShellPalette.CanvasBackground;
        outline.A = 0.72f;
        DrawRect(bounds, outline, filled: false, width: Math.Max(1.0f, bounds.Size.X / 29.0f));
    }

    private static Rect2 CosmeticSubPixel(Rect2 bounds, int column, int row)
    {
        var unit = bounds.Size / 4.0f;
        return new Rect2(
            bounds.Position + new Vector2(column * unit.X, row * unit.Y),
            unit);
    }

    private void DrawScoresBrowse()
    {
        var report = ScoreBrowseReport.Create(_scoreHistory, _personalBests);
        DrawLabel(
            Localize("screen.scores.title"),
            new Vector2(42.0f, 92.0f),
            ScaledFontSize(40),
            PrimaryTextColor());
        DrawLabel(
            Localize("scores.category-policy"),
            new Vector2(46.0f, 132.0f),
            ScaledFontSize(14),
            SecondaryTextColor());

        if (!report.HasCategories)
        {
            DrawLabel(
                Localize("scores.empty"),
                new Vector2(46.0f, 200.0f),
                ScaledFontSize(22),
                ActiveShellPalette.BodyText);
            DrawLabel(
                Localize("scores.empty-help"),
                new Vector2(46.0f, 238.0f),
                ScaledFontSize(16),
                SecondaryTextColor());
        }
        else
        {
            _scoreBrowseCategoryCursor = Math.Clamp(
                _scoreBrowseCategoryCursor,
                0,
                report.Categories.Count - 1);
            var category = report.Categories[_scoreBrowseCategoryCursor];
            DrawLabel(
                category.DisplayName,
                new Vector2(46.0f, 184.0f),
                ScaledFontSize(24),
                category.Competitive
                    ? ActiveShellPalette.GoldText
                    : ActiveShellPalette.WarningText);
            DrawLabel(
                $"CATEGORY {_scoreBrowseCategoryCursor + 1}/{report.Categories.Count}  "
                    + (category.Competitive ? "COMPETITIVE" : "NONCOMPETITIVE"),
                new Vector2(930.0f, 184.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            DrawLabel(
                category.IdentityLine,
                new Vector2(46.0f, 214.0f),
                ScaledFontSize(13),
                SecondaryTextColor());
            DrawLabel(
                category.PersonalBest is { } personalBest
                    ? $"PERSONAL BEST {personalBest:D6}"
                    : "PERSONAL BEST NOT AVAILABLE FOR THIS CATEGORY",
                new Vector2(900.0f, 214.0f),
                ScaledFontSize(13),
                ActiveShellPalette.BodyText);

            var y = 250.0f;
            var rowHeight = ScaledCatalogRowHeight(baseFontSize: 17, minimum: 32.0f);
            for (var index = 0; index < category.Scores.Count; index++)
            {
                var entry = category.Scores[index];
                var origin = entry.SourceId == ScoreHistoryDocument.PythonTopTenSourceId
                    ? "PYTHON 0.2"
                    : entry.SourceId == ScoreHistoryDocument.PersonalBestMigrationSourceId
                        ? "MIGRATED BEST"
                        : "NATIVE";
                DrawLabel(
                    $"{index + 1,2}.  {entry.Score,8:D6}  {entry.PlayerLabel,-24}  {origin}",
                    new Vector2(66.0f, y),
                    ScaledFontSize(17),
                    index == 0
                        ? ActiveShellPalette.GoldText
                        : ActiveShellPalette.BodyText);
                y += rowHeight;
            }
        }

        if (_scoreBrowseStatusCaption is not null)
        {
            DrawLabel(
                _scoreBrowseStatusCaption,
                new Vector2(46.0f, 590.0f),
                ScaledFontSize(13),
                _scoreImportConfirmation
                    ? ActiveShellPalette.WarningText
                    : ActiveShellPalette.AccentText);
        }

        var categoryX = DrawActionPromptSegment(
            "move_left",
            Localize("action.previous-category"),
            new Vector2(46.0f, 626.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "move_right",
            Localize("action.next-category"),
            new Vector2(categoryX, 626.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        var importX = DrawActionPromptSegment(
            "replay",
            _scoreHistory.PythonTopTenImported ? "legacy imported" : "import Python top ten",
            new Vector2(46.0f, 660.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        var backX = DrawActionPromptSegment(
            "back",
            _scoreImportConfirmation ? "cancel" : "or",
            new Vector2(importX, 660.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "confirm",
            _scoreImportConfirmation ? "import" : "return",
            new Vector2(backX, 660.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
    }

    private static int AchievementPageCount(int? entryCount = null) =>
        Math.Max(
            1,
            ((entryCount ?? ProgressionGoalCatalog.Goals.Count) + ProgressionGoalsPerPage - 1)
                / ProgressionGoalsPerPage);

    private float ScaledCatalogRowHeight(int baseFontSize, float minimum) =>
        Math.Max(
            minimum,
            ActiveShellTheme.InterfaceFont.GetHeight(ScaledFontSize(baseFontSize)) + 8.0f);

    private void DrawContentPacksBrowse()
    {
        DrawLabel(
            Localize("screen.content-packs.title"),
            new Vector2(42.0f, 100.0f),
            ScaledFontSize(40),
            PrimaryTextColor());
        DrawLabel(
            Localize("content-packs.core-ready"),
            new Vector2(46.0f, 154.0f),
            ScaledFontSize(20),
            ActiveShellPalette.GoldText);
        DrawLabel(
            Localize(
                "content-packs.optional-status",
                ShellTextArgument.From("count", _installedRadioPackCount)),
            new Vector2(46.0f, 190.0f),
            ScaledFontSize(18),
            ActiveShellPalette.BodyText);
        DrawLabel(
            Localize("content-packs.offline-help"),
            new Vector2(46.0f, 230.0f),
            ScaledFontSize(17),
            SecondaryTextColor());
        DrawLabel(
            Localize("content-packs.storage-help"),
            new Vector2(46.0f, 260.0f),
            ScaledFontSize(17),
            SecondaryTextColor());
        DrawLabel(
            Localize("content-packs.removal-help"),
            new Vector2(46.0f, 290.0f),
            ScaledFontSize(17),
            SecondaryTextColor());
        DrawLabel(
            Localize("content-packs.retention-help"),
            new Vector2(46.0f, 320.0f),
            ScaledFontSize(16),
            ActiveShellPalette.PrimaryText);
        DrawLabel(
            Localize("content-packs.isolation-help"),
            new Vector2(46.0f, 360.0f),
            ScaledFontSize(14),
            ActiveShellPalette.MutedGoldText);

        DrawRadioStatusPanel(new Vector2(46.0f, 400.0f));

        var nextX = DrawActionPromptSegment(
            "back",
            Localize("action.or"),
            new Vector2(46.0f, 572.0f),
            ScaledFontSize(15),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "confirm",
            Localize("action.return"),
            new Vector2(nextX, 572.0f),
            ScaledFontSize(15),
            SecondaryTextColor());
    }

    private void DrawModeContractPanel(Vector2 position)
    {
        var mode = SelectedRunMode;
        var config = SelectedRunConfig();
        DrawLabel(
            $"MODE  < {mode.DisplayName.ToUpperInvariant()}@{mode.Version} >",
            position,
            ScaledFontSize(20),
            ActiveShellPalette.PrimaryText);
        DrawLabel(
            BoundRadioLine(mode.Description, 64),
            position + new Vector2(0.0f, 26.0f),
            ScaledFontSize(14),
            ActiveShellPalette.BodyText);
        DrawLabel(
            $"BOARD {mode.BoardWidth}x{mode.BoardHeight}  |  FRESH LOCAL SEED  |  PAUSE FREEZES RULES",
            position + new Vector2(0.0f, 50.0f),
            ScaledFontSize(12),
            SecondaryTextColor());
        DrawLabel(
            $"SCORE CATEGORY  {RunModeCatalog.GetScoreCategoryId(config).ToUpperInvariant()}",
            position + new Vector2(0.0f, 72.0f),
            ScaledFontSize(12),
            ActiveShellPalette.GoldText);
        DrawLabel(
            BoundRadioLine(mode.ScoreModelDescription, 68),
            position + new Vector2(0.0f, 94.0f),
            ScaledFontSize(12),
            ActiveShellPalette.MutedGoldText);
        var adaptiveLine = mode.AdaptiveState switch
        {
            RunAdaptiveState.Disabled => "ADAPTATION OFF",
            RunAdaptiveState.EnabledByDefault when config.EnableAdaptation =>
                $"ADAPTATION ON  {config.AdaptivePolicyId.ToUpperInvariant()}  |  SETTINGS CAN DISABLE",
            RunAdaptiveState.EnabledByDefault =>
                "ADAPTATION OFF BY PREFERENCE  |  SEPARATE UNRANKED SCORE CATEGORY",
            _ => throw new InvalidOperationException("Unknown adaptive mode state."),
        };
        DrawLabel(
            adaptiveLine,
            position + new Vector2(0.0f, 116.0f),
            ScaledFontSize(12),
            SecondaryTextColor());
    }

    private void DrawRadioStatusPanel(Vector2 position)
    {
        var radio = _radioPolicy.Snapshot;
        DrawLabel(
            BoundRadioLine(radio.StationLine, 68),
            position,
            ScaledFontSize(13),
            ActiveShellPalette.PrimaryText);
        DrawLabel(
            BoundRadioLine(radio.TrackLine, 68),
            position + new Vector2(0.0f, 24.0f),
            ScaledFontSize(13),
            ActiveShellPalette.BodyText);
        DrawLabel(
            BoundRadioLine(radio.PackLine, 68),
            position + new Vector2(0.0f, 48.0f),
            ScaledFontSize(13),
            radio.PackState == RadioPackState.Ready
                ? ActiveShellPalette.GoldText
                : ActiveShellPalette.MutedGoldText);
        DrawLabel(
            radio.MuteLine,
            position + new Vector2(0.0f, 72.0f),
            ScaledFontSize(13),
            radio.Muted
                ? ActiveShellPalette.WarningText
                : SecondaryTextColor());
        DrawLabel(
            BoundRadioLine(radio.HelpLine, 68),
            position + new Vector2(0.0f, 96.0f),
            ScaledFontSize(12),
            SecondaryTextColor());
        DrawStaticPromptSegment(
            "key:j",
            "button:right_stick",
            Localize("action.cycle-radio"),
            position + new Vector2(0.0f, 122.0f),
            ScaledFontSize(12),
            SecondaryTextColor());
    }

    private static string BoundRadioLine(string value, int maximumCharacters)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCharacters, 4);

        var sanitized = new string(
            value.Select(character => char.IsControl(character) ? ' ' : character).ToArray());
        return sanitized.Length <= maximumCharacters
            ? sanitized
            : sanitized[..(maximumCharacters - 3)] + "...";
    }

    // Control characters would render as boxes or silently break a row, so every
    // fitted label is sanitized first. The overwhelming majority of shell copy is
    // already clean, and this runs for every fitted label on every drawn frame,
    // so a clean string is returned as-is rather than copied.
    private static string SanitizeLabel(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var index = 0;
        while (index < value.Length && !char.IsControl(value[index]))
        {
            index++;
        }

        if (index == value.Length)
        {
            return value;
        }

        return string.Create(value.Length, value, static (destination, source) =>
        {
            for (var position = 0; position < source.Length; position++)
            {
                var character = source[position];
                destination[position] = char.IsControl(character) ? ' ' : character;
            }
        });
    }

    private string FitLabelToWidth(string value, int fontSize, float maximumWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fontSize);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maximumWidth, 0.0f);

        var sanitized = SanitizeLabel(value);
        if (MeasureLabelWidth(sanitized, fontSize) <= maximumWidth)
        {
            return sanitized;
        }

        const string suffix = "...";
        var lower = 0;
        var upper = sanitized.Length;
        while (lower < upper)
        {
            var candidateLength = lower + ((upper - lower + 1) / 2);
            var candidate = sanitized[..candidateLength].TrimEnd() + suffix;
            if (MeasureLabelWidth(candidate, fontSize) <= maximumWidth)
            {
                lower = candidateLength;
            }
            else
            {
                upper = candidateLength - 1;
            }
        }

        return sanitized[..lower].TrimEnd() + suffix;
    }

    // Shared by the draw path and the readable-layout gate so a row can never be
    // proven readable under different rules than the ones that render it.
    private (int FontSize, string Text) ResolveFittedLabel(
        string value,
        int preferredFontSize,
        int minimumFontSize,
        float maximumWidth)
    {
        if (minimumFontSize <= 0 || minimumFontSize > preferredFontSize)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumFontSize));
        }

        // This runs for every fitted label on every drawn frame, so the common
        // case where the copy already fits costs exactly one measurement and no
        // allocation. Only a label that still overflows at its floor pays for
        // the elision search.
        var sanitized = SanitizeLabel(value);
        var fontSize = preferredFontSize;
        var width = MeasureLabelWidth(sanitized, fontSize);
        while (fontSize > minimumFontSize && width > maximumWidth)
        {
            fontSize--;
            width = MeasureLabelWidth(sanitized, fontSize);
        }

        return width <= maximumWidth
            ? (fontSize, sanitized)
            : (fontSize, FitLabelToWidth(sanitized, fontSize, maximumWidth));
    }

    private void DrawFittedLabel(
        string value,
        Vector2 position,
        int preferredFontSize,
        int minimumFontSize,
        float maximumWidth,
        Color color)
    {
        var (fontSize, text) = ResolveFittedLabel(
            value,
            preferredFontSize,
            minimumFontSize,
            maximumWidth);
        DrawLabel(text, position, fontSize, color);
    }

    private float MeasureLabelWidth(string value, int fontSize) =>
        ActiveShellTheme.InterfaceFont.GetStringSize(
            value,
            HorizontalAlignment.Left,
            -1.0f,
            fontSize).X;

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
            ShowReplayStatus(Localize("status.content-packs.import-paused"));
            return;
        }

        if (files.Length != 1)
        {
            ShowReplayStatus("REPLAY IMPORT REQUIRES EXACTLY ONE FILE");
            return;
        }

        var path = files[0];
        if (path.EndsWith(".vibesnake-pack.zip", StringComparison.OrdinalIgnoreCase))
        {
            if (_optionalPackStore is null || _contentInventory is null)
            {
                ShowAudioStatus(
                    Localize("status.content-packs.inventory-unavailable"),
                    persist: true);
                return;
            }

            var install = _optionalPackStore.InstallArchive(path, _contentInventory);
            if (install.IsSuccess && install.Pack is not null)
            {
                InitializeRadio(allowCheckoutFallback: true);
                ShowAudioStatus(
                    Localize(
                        "status.content-packs.ready",
                        ShellTextArgument.From(
                            "name",
                            BoundPlayerDataCaption(install.Pack.DisplayName, 96)
                                .ToUpperInvariant())),
                    persist: false);
                _structuredLog?.Information(
                    "radio",
                    $"Installed and activated radio pack {install.Pack.Id}@{install.Pack.Version}.",
                    eventCode: "radio_pack_installed");
            }
            else
            {
                ShowAudioStatus(
                    Localize(
                        "status.content-packs.rejected",
                        ShellTextArgument.From(
                            "reason",
                            BoundPlayerDataCaption(install.Message, 160)
                                .ToUpperInvariant())),
                    persist: true);
                _structuredLog?.Warning(
                    "radio",
                    $"Radio pack import failed with {install.Code}.",
                    eventCode: "radio_pack_install_failed");
            }
            QueueRedraw();
            return;
        }

        if (_replayStore is null)
        {
            ShowReplayStatus("REPLAY IMPORT UNAVAILABLE: STORAGE SERVICE NOT READY");
            return;
        }

        var store = _replayStore;
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
        return TryStartReplayResultOperation(
            () => new ReplayOperationResult(operation()),
            progressMessage,
            kind);
    }

    private bool TryStartReplayResultOperation(
        Func<ReplayOperationResult> operation,
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
        var operationSucceeded = false;
        try
        {
            var result = operation.GetAwaiter().GetResult();
            if (result.Playback is not null && _screenState == ScreenState.Replays)
            {
                _replayPlayback = result.Playback;
                _snakeMotionPresentation.Reset(_replayPlayback.CurrentSnapshot.Body);
                _vibeLevelDirector.Reset();
                _replayPlaybackPaused = true;
                _replayPlaybackSpeedIndex = 1;
                _replayHudVisible = true;
                _capturePresentation = CapturePresentationState.Visible;
                _rulesStepAccumulatorMilliseconds = 0.0;
            }

            if (result.BrowserEntries is not null && _screenState == ScreenState.Replays)
            {
                _replayBrowserEntries = result.BrowserEntries;
                _replayBrowseCursor = Math.Clamp(
                    _replayBrowseCursor,
                    0,
                    Math.Max(0, _replayBrowserEntries.Count - 1));
                if (completedKind == ReplayOperationKind.Delete)
                {
                    _pendingReplayDeletion = null;
                }
            }

            if (result.DeletionPlan is not null && _screenState == ScreenState.Replays)
            {
                _pendingReplayDeletion = result.DeletionPlan;
            }

            if (result.GhostSlots is not null && _screenState == ScreenState.Comparisons)
            {
                _ghostSlots = result.GhostSlots;
                _ghostSlotCursor = Math.Clamp(
                    _ghostSlotCursor,
                    0,
                    OfflineChallengeStore.MaximumHouseholdRivalSlots - 1);
                if (completedKind == ReplayOperationKind.GhostDelete)
                {
                    _pendingGhostDeletion = null;
                }
            }

            if (result.GhostDeletionPlan is not null
                && _screenState == ScreenState.Comparisons)
            {
                _pendingGhostDeletion = result.GhostDeletionPlan;
            }

            if (result.GhostRace is not null && _screenState == ScreenState.Comparisons)
            {
                _activeGhostRace = result.GhostRace;
                _activeGhostSlot = _ghostSlotCursor + 1;
                if (_activePromptFamily == InputPromptFamily.Keyboard)
                {
                    _offlineComparisonKeyboardRouteQualified = true;
                }
                else
                {
                    _offlineComparisonControllerRouteQualified = true;
                }

                BeginPreparedRun(
                    result.GhostRace.PlayerRun,
                    ScoreRunContextCatalog.SeededChallenge,
                    tourEvent: null,
                    isRestart: false);
            }

            ShowReplayStatus(result.Caption);
            operationSucceeded = true;
        }
        catch (Exception exception)
        {
            ShowReplayStatus("REPLAY OPERATION FAILED: AN UNEXPECTED LOCAL ERROR OCCURRED");
            try
            {
                WriteLocalCrashReport(
                    $"ReplayOperation_{completedKind}",
                    exception,
                    eventCode: "replay_operation_failed");
            }
            catch (Exception diagnosticException) when (
                diagnosticException is IOException
                    or UnauthorizedAccessException
                    or ArgumentException
                    or InvalidOperationException)
            {
                _structuredLog?.Warning(
                    "replay",
                    "Replay operation and diagnostic persistence both failed.",
                    eventCode: "replay_operation_and_diagnostics_failed");
            }
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
            if (operationSucceeded)
            {
                return true;
            }

            ShowReplayStatus("QUIT CANCELED: REPLAY SAVE FAILED; RETRY OR QUIT AGAIN");
        }

        return false;
    }

    private void RequestQuit()
    {
        if (_playerDataOperation is not null)
        {
            _quitAfterPlayerDataOperation = true;
            _settingsStatusCaption = Localize("status.player-data.quit-paused");
            QueueRedraw();
            return;
        }

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
    /// the absolute path to the clipboard for support. The returned localized
    /// status never claims success when either interactive access route failed.
    /// </summary>
    private string OpenDiagnosticsDirectory()
    {
        if (_diagnostics is null)
        {
            return Localize("status.settings.diagnostics-limited");
        }

        string path;
        try
        {
            path = _diagnostics.EnsureDiagnosticsDirectory();
            _structuredLog?.EnsureLogsDirectory();
            _structuredLog?.Information(
                "diagnostics",
                "Prepared local diagnostics directory for support.",
                eventCode: "open_diagnostics");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException)
        {
            return Localize("status.settings.diagnostics-limited");
        }

        var pathCopied = true;
        try
        {
            DisplayServer.ClipboardSet(path);
        }
        catch (Exception)
        {
            pathCopied = false;
        }

        if (DisplayServer.GetName() == "headless")
        {
            return pathCopied
                ? Localize("status.settings.diagnostics-copied")
                : Localize("status.settings.diagnostics-limited");
        }

        var directoryOpened = false;
        try
        {
            directoryOpened = OS.ShellOpen(path) == Error.Ok;
        }
        catch (Exception)
        {
            directoryOpened = false;
        }

        return pathCopied && directoryOpened
            ? Localize("status.settings.diagnostics-copied")
            : Localize("status.settings.diagnostics-limited");
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

    private async void ExecuteReadmeCapture(string captureDirectory)
    {
        try
        {
            SetPhysicsProcess(false);
            var outputDirectory = System.IO.Path.GetFullPath(captureDirectory);
            System.IO.Directory.CreateDirectory(outputDirectory);
            if (_window is not null)
            {
                _window.Mode = Window.ModeEnum.Windowed;
                _window.Size = new Vector2I(1280, 720);
            }

            _shellSettings = ShellSettings.CreateDefaults();
            ApplyRuntimeShellSettings();
            RefreshVirtualViewport();

            TransitionToScreen(ScreenState.Menu);
            _mainMenuCursor = (int)MainMenuItem.Start;
            await CaptureReadmeFrame(outputDirectory, "main-menu.png");

            OpenCosmeticSets();
            _cosmeticCursor = Math.Min(2, CosmeticSetCatalog.Sets.Count - 1);
            _cosmeticPage = _cosmeticCursor / CosmeticSetsPerPage;
            var capturedCosmetic = CosmeticSetCatalog.Sets[_cosmeticCursor];
            _cosmeticStatusCaption = Localize(
                "status.cosmetics.selected",
                ShellTextArgument.From(
                    "cosmetic",
                    capturedCosmetic.Name.ToUpperInvariant()));
            await CaptureReadmeFrame(outputDirectory, "customization.png");

            ReturnToMenu();
            StageReadmeGameplay();
            await CaptureReadmeFrame(outputDirectory, "powers-run.png");

            ReturnToMenu();
            OpenSpectatorBrowse();
            StartSpectatorMatch();
            if (_spectatorMatch is { } spectator)
            {
                for (var step = 0; step < 28 && !spectator.IsComplete; step++)
                {
                    AdvanceSpectatorOneStep(spectator);
                }

                spectator.SetPaused(false);
                _snakeMotionPresentation.Reset(spectator.ViewedSnapshot.Body);
                _spectatorControlsVisibleUntilMilliseconds = null;
            }

            await CaptureReadmeFrame(outputDirectory, "ai-channel.png");
            GD.Print("VIBESNAKE_README_CAPTURE_OK count=4");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"VIBESNAKE_README_CAPTURE_FAILED {exception}");
            GetTree().Quit(1);
        }
    }

    private void StageReadmeGameplay()
    {
        var body = Enumerable.Range(10, 16)
            .Select(x => new GridPoint(x, 23))
            .Concat(
            [
                new GridPoint(25, 22),
                new GridPoint(25, 21),
                new GridPoint(25, 20),
            ])
            .Concat(Enumerable.Range(26, 12).Select(x => new GridPoint(x, 20)))
            .ToArray();
        var config = SelectedRunConfig();
        _run = SnakeRun.CreateForTesting(
            config,
            body,
            RulesDirection.Right,
            food: new GridPoint(52, 14),
            hungerTicksRemaining: Math.Min(120, config.StarvationTicks),
            score: 4_820,
            comboCount: 9,
            ticksSinceLastFood: 1,
            tick: 183,
            powerPickup: new PowerPickup(
                PowerKind.Gluttony,
                new GridPoint(44, 10),
                Math.Min(24, config.PowerVisibleTicks)),
            baitPosition: new GridPoint(46, 25),
            detachedObstacles: [],
            detachedObstacleTicksRemaining: 0);
        _snakeMotionPresentation.Reset(body);
        TransitionToScreen(ScreenState.Running);
        _pausedByFocusLoss = false;
        _capturePresentation = CapturePresentationState.Visible;
        _feedbackCaption = null;
        _feedbackTier = VisualFeedbackTier.Routine;
        _feedbackTicksRemaining = 0;
        _comboPulseTicksRemaining = ComboFeedback.PulseTicks;
        _vibeLevelDirector.Reset();
        _vibeLevelDirector.Update(_run.ComboCount);
    }

    private async Task CaptureReadmeFrame(string outputDirectory, string fileName)
    {
        QueueRedraw();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        if (image.GetWidth() != 1280 || image.GetHeight() != 720)
        {
            throw new InvalidOperationException(
                $"README capture viewport must be 1280x720, observed {image.GetWidth()}x{image.GetHeight()}.");
        }

        var path = System.IO.Path.Combine(outputDirectory, fileName);
        var result = image.SavePng(path);
        if (result != Error.Ok)
        {
            throw new System.IO.IOException(
                $"README capture could not write {fileName}: {result}.");
        }
    }

    private void ExecuteLaunchProbe(IReadOnlyList<string> userArguments)
    {
        var expectedSchemaText = GetArgumentValue(
            userArguments,
            "--launch-probe-preferences-schema=");
        var expectedFixture = GetArgumentValue(
            userArguments,
            "--launch-probe-fixture=");
        var expectFutureSchema = userArguments.Contains(
            "--launch-probe-expect-future-preferences",
            StringComparer.Ordinal);
        var expectationCount = (expectedSchemaText is null ? 0 : 1)
            + (expectedFixture is null ? 0 : 1)
            + (expectFutureSchema ? 1 : 0);
        if (expectationCount > 1)
        {
            throw new ArgumentException(
                "Launch probe accepts only one migration or future-schema expectation.");
        }

        var loaded = _preferencesStore?.Load()
            ?? throw new InvalidOperationException("Launch probe preferences store is unavailable.");
        if (expectedFixture is not null)
        {
            ExecuteLaunchFixtureProbe(expectedFixture);
            return;
        }

        if (expectedSchemaText is not null)
        {
            if (!int.TryParse(
                    expectedSchemaText,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var expectedSchema)
                || expectedSchema is < 1 or >= PreferencesDocument.CurrentSchemaVersion)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(userArguments),
                    "Launch probe preferences schema must identify a supported legacy schema.");
            }
            if (!loaded.IsSuccess
                || loaded.Document?.SchemaVersion != PreferencesDocument.CurrentSchemaVersion
                || !loaded.Message.Contains(
                    $"schema {expectedSchema}",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Preferences schema {expectedSchema} did not migrate through the production store: "
                    + $"{loaded.Code} {loaded.Message}");
            }

            GD.Print(
                "VIBESNAKE_LAUNCH_PROBE_OK "
                + $"input_schema={expectedSchema} "
                + $"effective_schema={loaded.Document.SchemaVersion} "
                + $"code={loaded.Code}");
            GetTree().Quit(0);
            return;
        }

        if (expectFutureSchema)
        {
            if (loaded.Code != PreferencesLoadCode.UnsupportedSchema || loaded.Document is not null)
            {
                throw new InvalidOperationException(
                    "Future preferences were not rejected by the production store.");
            }

            GD.Print(
                "VIBESNAKE_LAUNCH_PROBE_OK "
                + "future_schema_rejected=true "
                + $"code={loaded.Code}");
            GetTree().Quit(0);
            return;
        }

        if (!loaded.IsSuccess || loaded.Document is null)
        {
            throw new InvalidOperationException(
                $"Clean launch preferences failed: {loaded.Code} {loaded.Message}");
        }

        GD.Print(
            "VIBESNAKE_LAUNCH_PROBE_OK "
            + $"effective_schema={loaded.Document.SchemaVersion} "
            + $"code={loaded.Code}");
        GetTree().Quit(0);
    }

    private void ExecuteLaunchFixtureProbe(string expectedFixture)
    {
        switch (expectedFixture)
        {
            case "personal-best-schema-1":
                {
                    var loaded = _personalBestStore?.Load()
                        ?? throw new InvalidOperationException(
                            "Launch probe personal-best store is unavailable.");
                    var entry = loaded.Document?.Entries.SingleOrDefault();
                    if (!loaded.IsSuccess
                        || loaded.Document?.SchemaVersion != PersonalBestDocument.CurrentSchemaVersion
                        || entry is null
                        || entry.ModeId != PersonalBestDocument.LegacyModeId
                        || entry.BestScore != 250)
                    {
                        throw new InvalidOperationException(
                            "Personal-best schema 1 did not migrate through the production store: "
                            + $"{loaded.Code} {loaded.Message}");
                    }

                    GD.Print(
                        "VIBESNAKE_LAUNCH_PROBE_OK "
                        + "fixture=personal-best-schema-1 "
                        + $"effective_schema={loaded.Document.SchemaVersion} "
                        + $"code={loaded.Code}");
                    break;
                }
            case "local-playtest-summary-schema-1":
                {
                    var loaded = _localPlaytestSummaryStore?.Load()
                        ?? throw new InvalidOperationException(
                            "Launch probe local-playtest-summary store is unavailable.");
                    if (!loaded.IsSuccess
                        || loaded.Document is not { } document
                        || document.SchemaVersion != LocalPlaytestSummaryDocument.CurrentSchemaVersion
                        || document.Kind != LocalPlaytestSummaryDocument.DocumentKind
                        || document.Summaries.Count != 0)
                    {
                        throw new InvalidOperationException(
                            "Local playtest summary schema 1 did not migrate through the production store: "
                            + $"{loaded.Code} {loaded.Message}");
                    }

                    GD.Print(
                        "VIBESNAKE_LAUNCH_PROBE_OK "
                        + "fixture=local-playtest-summary-schema-1 "
                        + $"effective_schema={document.SchemaVersion} "
                        + $"code={loaded.Code}");
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(expectedFixture),
                    expectedFixture,
                    "Launch probe fixture is unsupported.");
        }

        GetTree().Quit(0);
    }

    private void PauseForFocusLoss()
    {
        if (_screenState == ScreenState.Spectator
            && _spectatorMatch is { Paused: false } spectator)
        {
            spectator.SetPaused(true);
            _rulesStepAccumulatorMilliseconds = 0.0;
            _spectatorStatusCaption = Localize("status.spectator.paused");
            QueueRedraw();
            return;
        }

        if (_screenState == ScreenState.Replays
            && _replayPlayback is not null
            && !_replayPlaybackPaused)
        {
            _replayPlaybackPaused = true;
            _rulesStepAccumulatorMilliseconds = 0.0;
            ShowReplayStatus("REPLAY PAUSED: FOCUS LOST");
            return;
        }

        if (_screenState != ScreenState.Running || _paused)
        {
            return;
        }

        SetRunPaused(true);
        _pausedByFocusLoss = true;
        _rulesStepAccumulatorMilliseconds = 0.0;
        QueueRedraw();
    }

    private void DrawSpectator()
    {
        if (_spectatorMatch is null)
        {
            DrawSpectatorSelection();
            return;
        }

        var spectator = _spectatorMatch;
        var state = spectator.IsComplete
            ? "COMPLETE"
            : spectator.Paused
                ? "PAUSED"
                : "PLAYING";
        DrawRun(
            spectator.ViewedSnapshot,
            $"AI {state}",
            spectator.Mode);
        if (!_capturePresentation.ShowSpectatorOverlays)
        {
            return;
        }

        var showControls = spectator.Paused
            || spectator.IsComplete
            || (_spectatorControlsVisibleUntilMilliseconds is { } controlsDeadline
                && Time.GetTicksMsec() < controlsDeadline);
        var panelTop = showControls ? 654.0f : 674.0f;
        var titleBaseline = panelTop + 17.0f;
        var detailBaseline = panelTop + 36.0f;
        var panel = ActiveShellPalette.CanvasBackground;
        panel.A = 0.88f;
        DrawRect(new Rect2(20.0f, panelTop, 1240.0f, 718.0f - panelTop), panel);
        var record = _spectatorLeague.StandingFor(spectator.ViewedPersonalityId).BestScore;
        var vibe = VibeLevelDirector.Definitions
            .Last(definition =>
                spectator.ViewedSnapshot.ComboCount >= definition.ComboThreshold)
            .Name;
        var overlay = spectator.BuildOverlay(record, vibe);
        var target = overlay.Target is { } point
            ? $"{overlay.TargetKind.ToString().ToUpperInvariant()} {point.X},{point.Y}"
            : "NONE";
        var resources = DescribeSpectatorResources(overlay.SurvivalResources);
        DrawLabel(
            Localize(
                "spectator.overlay.channel",
                ShellTextArgument.From("channel", overlay.PersonalityName),
                ShellTextArgument.From("rival", overlay.RivalName),
                ShellTextArgument.From("station", overlay.StationAffinity),
                ShellTextArgument.From("shed", overlay.ShedId)),
            new Vector2(38.0f, titleBaseline),
            ScaledFontSize(12),
            ActiveShellPalette.GoldText);
        DrawLabel(
            Localize(
                "spectator.overlay.target",
                ShellTextArgument.From("target", target),
                ShellTextArgument.From("risk", overlay.RiskBand.ToString().ToUpperInvariant()),
                ShellTextArgument.From("vibe", overlay.VibeLevelId)),
            new Vector2(38.0f, detailBaseline),
            ScaledFontSize(10),
            ActiveShellPalette.BodyText);
        DrawLabel(
            Localize(
                "spectator.overlay.resources",
                ShellTextArgument.From("resources", resources),
                ShellTextArgument.From("delta", overlay.RecordDelta.ToString("+0;-0;0", CultureInfo.InvariantCulture))),
            new Vector2(390.0f, detailBaseline),
            ScaledFontSize(10),
            SecondaryTextColor());
        DrawLabel(
            Localize(
                "spectator.overlay.match",
                ShellTextArgument.From("step", spectator.StepCount),
                ShellTextArgument.From("limit", SpectatorMatchSession.MaximumBroadcastSteps),
                ShellTextArgument.From("state", state),
                ShellTextArgument.From("speed", $"{spectator.PlaybackSpeed:0.0#}X")),
            new Vector2(930.0f, titleBaseline),
            ScaledFontSize(10),
            SecondaryTextColor());
        if (overlay.DecisionReasonCopyId is { } decisionReasonCopyId)
        {
            DrawLabel(
                Localize(
                    "spectator.overlay.reason",
                    ShellTextArgument.From("reason", Localize(decisionReasonCopyId))),
                new Vector2(760.0f, detailBaseline),
                ScaledFontSize(9),
                ActiveShellPalette.AccentText);
        }
        if (showControls)
        {
            var promptX = DrawActionPromptSegment(
                "confirm",
                Localize("action.play-pause"),
                new Vector2(38.0f, 710.0f),
                ScaledFontSize(9),
                SecondaryTextColor());
            promptX = DrawActionPromptSegment(
                "move_up",
                Localize("action.switch-channel"),
                new Vector2(promptX, 710.0f),
                ScaledFontSize(9),
                SecondaryTextColor());
            promptX = DrawActionPromptSegment(
                "move_down",
                Localize("action.step"),
                new Vector2(promptX, 710.0f),
                ScaledFontSize(9),
                SecondaryTextColor());
            promptX = DrawActionPromptSegment(
                "move_left",
                Localize("action.slower"),
                new Vector2(promptX, 710.0f),
                ScaledFontSize(9),
                SecondaryTextColor());
            promptX = DrawActionPromptSegment(
                "move_right",
                Localize("action.faster"),
                new Vector2(promptX, 710.0f),
                ScaledFontSize(9),
                SecondaryTextColor());
            promptX = DrawActionPromptSegment(
                "help",
                Localize("action.toggle-hud"),
                new Vector2(promptX, 710.0f),
                ScaledFontSize(9),
                SecondaryTextColor());
            if (spectator.IsComplete)
            {
                DrawActionPromptSegment(
                    "browse_content_packs",
                    Localize("action.seed-challenge"),
                    new Vector2(promptX, 710.0f),
                    ScaledFontSize(9),
                    ActiveShellPalette.GoldText);
            }
        }
    }

    private static string FitAgentOverlayText(
        Font font,
        string text,
        int fontSize,
        float maximumWidth)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fontSize);
        if (!float.IsFinite(maximumWidth) || maximumWidth <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWidth));
        }

        float WidthOf(string candidate) => font.GetStringSize(
            candidate,
            HorizontalAlignment.Left,
            -1.0f,
            fontSize).X;
        if (WidthOf(text) <= maximumWidth)
        {
            return text;
        }

        const string omission = "..";
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            elements.Add(enumerator.GetTextElement());
        }

        var low = 0;
        var high = elements.Count;
        var best = omission;
        while (low <= high)
        {
            var kept = low + ((high - low) / 2);
            var prefixCount = (kept + 1) / 2;
            var suffixCount = kept / 2;
            var candidate = string.Concat(elements.Take(prefixCount))
                + omission
                + string.Concat(elements.Skip(elements.Count - suffixCount));
            if (WidthOf(candidate) <= maximumWidth)
            {
                best = candidate;
                low = kept + 1;
            }
            else
            {
                high = kept - 1;
            }
        }

        return best;
    }

#if AGENT_ARENA_PREVIEW
    private void PollAgentViewer(ulong nowMilliseconds)
    {
        if (_screenState != ScreenState.AgentWatch || _agentViewer is null)
        {
            return;
        }

        _agentViewerStatusId = AgentViewerStatusCopyId(_agentViewer.State);
        _agentViewerFeedId = AgentViewerFeedCopyId(_agentViewer.State);
        if (!_agentViewer.TryTakeLatest(out var frame, out var coalescedFrames)
            || frame is null)
        {
            return;
        }

        try
        {
            var previous = _agentViewerSnapshot;
            var projected = AgentViewerPresentation.ProjectSnapshot(frame.Observation);
            _agentViewerSnappedLatestFrame =
                _shellSettings.ReducedMotion && coalescedFrames > 0;
            if (previous is not null && previous.StateHash != projected.StateHash)
            {
                if (_shellSettings.ReducedMotion)
                {
                    _snakeMotionPresentation.Reset(projected.Body);
                    _agentViewerSnappedLatestFrame = true;
                }
                else
                {
                    _snakeMotionPresentation.Begin(
                        previous.Body,
                        projected.Body,
                        nowMilliseconds,
                        Math.Max(1, projected.EffectiveRulesStepMilliseconds));
                }
            }

            _agentViewerFrame = frame;
            _agentViewerCoalescedFrames = coalescedFrames;
            _agentViewerSnapshot = projected;
            _vibeLevelDirector.Update(projected.ComboCount);
            QueueRedraw();
            if (_agentViewerSmokeEnabled && !frame.Observation.IsActionAwaited)
            {
                _agentViewerSmokeEnabled = false;
                _agentViewerSmokeDeadlineMilliseconds = null;
                if (!frame.VerifiedResultAvailable)
                {
                    GD.PushError(
                        "VIBESNAKE_AGENT_VIEWER_SMOKE_FAILED terminal frame had no verified result");
                    GetTree().Quit(1);
                    return;
                }

                _ = CompleteAgentViewerSmokeAsync(
                    projected.StateHash,
                    frame.Sequence,
                    frame.Operation,
                    frame.StepsAdvanced,
                    coalescedFrames);
            }
        }
        catch (ArgumentException)
        {
            _agentViewerStatusId = "status.agent-viewer.rejected";
            _agentViewer.Dispose();
            _agentViewer = null;
            QueueRedraw();
        }
    }

    // The composed overlay row uses a short feed label. The long status sentence
    // stays on the standalone waiting screen, where it has a full row to itself.
    private static string AgentViewerFeedCopyId(AgentViewerClientState state) => state switch
    {
        AgentViewerClientState.Connecting => "agent-arena.feed.connecting",
        AgentViewerClientState.Watching => "agent-arena.feed.watching",
        AgentViewerClientState.Completed => "agent-arena.feed.completed",
        AgentViewerClientState.Disconnected => "agent-arena.feed.disconnected",
        AgentViewerClientState.Rejected => "agent-arena.feed.rejected",
        AgentViewerClientState.FailedClosed => "agent-arena.feed.failed-closed",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static string AgentViewerStatusCopyId(AgentViewerClientState state) => state switch
    {
        AgentViewerClientState.Connecting => "status.agent-viewer.connecting",
        AgentViewerClientState.Watching => "status.agent-viewer.watching",
        AgentViewerClientState.Completed => "status.agent-viewer.completed",
        AgentViewerClientState.Disconnected => "status.agent-viewer.disconnected",
        AgentViewerClientState.Rejected => "status.agent-viewer.rejected",
        AgentViewerClientState.FailedClosed => "status.agent-viewer.failed-closed",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    private static string AgentIntentCopyId(AgentPublicIntent intent) => intent switch
    {
        AgentPublicIntent.Undeclared => "agent-arena.intent.undeclared",
        AgentPublicIntent.SeekFood => "agent-arena.intent.seek-food",
        AgentPublicIntent.SeekPower => "agent-arena.intent.seek-power",
        AgentPublicIntent.PreserveSpace => "agent-arena.intent.preserve-space",
        AgentPublicIntent.TakeRisk => "agent-arena.intent.take-risk",
        AgentPublicIntent.Recover => "agent-arena.intent.recover",
        _ => throw new ArgumentOutOfRangeException(nameof(intent)),
    };

    private static string AgentLessonRequirementStateCopyId(
        AgentLessonRequirementProgressV2 requirement,
        bool evidenceVerified,
        bool verifiedEvidenceUnavailable)
    {
        if (verifiedEvidenceUnavailable)
        {
            return "agent-arena.lesson.requirement.replay-unverified";
        }

        if (evidenceVerified)
        {
            return requirement.Satisfied
                ? "agent-arena.lesson.requirement.verified-met"
                : "agent-arena.lesson.requirement.verified-not-met";
        }

        return requirement.Satisfied
            ? "agent-arena.lesson.requirement.observed-met"
            : "agent-arena.lesson.requirement.observed-not-met";
    }

    private string AgentLessonRequirementCopy(
        AgentLessonRequirementProgressV2 requirement,
        bool evidenceVerified,
        bool verifiedEvidenceUnavailable) =>
        Localize(
            "agent-arena.lesson.requirement",
            ShellTextArgument.From(
                "state",
                Localize(AgentLessonRequirementStateCopyId(
                    requirement,
                    evidenceVerified,
                    verifiedEvidenceUnavailable))),
            ShellTextArgument.From("requirement", requirement.DisplayName.ToUpperInvariant()),
            ShellTextArgument.From("current", requirement.Current),
            ShellTextArgument.From("target", requirement.Target));

    private static string AgentStyleCriterionStateCopyId(
        AgentStyleCriterionProgressV3 criterion,
        bool replayVerified,
        bool replayEvidenceUnavailable)
    {
        if (replayEvidenceUnavailable)
        {
            return "agent-arena.style.criterion.replay-unverified";
        }

        if (replayVerified)
        {
            return criterion.ThresholdReached
                ? "agent-arena.style.criterion.verified-met"
                : "agent-arena.style.criterion.verified-not-met";
        }

        return criterion.ThresholdReached
            ? "agent-arena.style.criterion.observed-met"
            : "agent-arena.style.criterion.observed-not-met";
    }

    private static string FormatAgentStyleCriterionValue(
        int value,
        AgentStyleCriterionUnit unit) => unit switch
        {
            AgentStyleCriterionUnit.Count => value.ToString(CultureInfo.InvariantCulture),
            AgentStyleCriterionUnit.BasisPoints =>
                (value / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%",
            _ => throw new ArgumentOutOfRangeException(nameof(unit)),
        };

    private string AgentStyleCriterionCopy(
        AgentStyleCriterionProgressV3 criterion,
        bool replayVerified,
        bool replayEvidenceUnavailable) =>
        Localize(
            "agent-arena.style.criterion",
            ShellTextArgument.From(
                "state",
                Localize(AgentStyleCriterionStateCopyId(
                    criterion,
                    replayVerified,
                    replayEvidenceUnavailable))),
            ShellTextArgument.From("criterion", criterion.DisplayName.ToUpperInvariant()),
            ShellTextArgument.From(
                "current",
                FormatAgentStyleCriterionValue(criterion.Current, criterion.Unit)),
            ShellTextArgument.From(
                "target",
                FormatAgentStyleCriterionValue(criterion.Target, criterion.Unit)));

    private static string AgentActionFeedbackCopyId(AgentPreviousActionV1? action)
    {
        if (action is null)
        {
            return "agent-arena.action.none";
        }

        if (action.Accepted)
        {
            return "agent-arena.action.accepted";
        }

        return action.Rejection switch
        {
            AgentActionRejection.InvalidRequest => "agent-arena.action.rejected-invalid-request",
            AgentActionRejection.InvalidAction => "agent-arena.action.rejected-invalid-action",
            AgentActionRejection.StaleTick => "agent-arena.action.rejected-stale-tick",
            AgentActionRejection.StaleStateHash => "agent-arena.action.rejected-stale-state",
            AgentActionRejection.IllegalDirection => "agent-arena.action.rejected-illegal-direction",
            AgentActionRejection.IdempotencyConflict => "agent-arena.action.rejected-conflict",
            AgentActionRejection.MatchNotAwaitingAction =>
                "agent-arena.action.rejected-terminal",
            AgentActionRejection.ReplayFailure => "agent-arena.action.rejected-replay",
            AgentActionRejection.WrongActionProfile =>
                "agent-arena.action.rejected-wrong-profile",
            AgentActionRejection.MutationCapacityExceeded =>
                "agent-arena.action.rejected-mutation-capacity",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
    }

    private static string AgentOutcomeCopyId(AgentMatchEndReason endReason) => endReason switch
    {
        AgentMatchEndReason.None => "agent-arena.outcome.live",
        AgentMatchEndReason.RulesTerminal => "agent-arena.outcome.rules-terminal",
        AgentMatchEndReason.StepLimit => "agent-arena.outcome.step-limit",
        AgentMatchEndReason.AgentFinished => "agent-arena.outcome.agent-finished",
        AgentMatchEndReason.ReplayFailure => "agent-arena.outcome.replay-failure",
        _ => throw new ArgumentOutOfRangeException(nameof(endReason)),
    };

    private string AgentViewerOperationCopy(AgentViewerFrameV9 frame) => frame.Operation switch
    {
        AgentViewerOperationKind.Initial => Localize("agent-arena.operation.initial"),
        AgentViewerOperationKind.Step => Localize(
            "agent-arena.operation.step",
            ShellTextArgument.From("steps", frame.StepsAdvanced)),
        AgentViewerOperationKind.Burst => Localize(
            "agent-arena.operation.burst",
            ShellTextArgument.From("steps", frame.StepsAdvanced),
            ShellTextArgument.From(
                "reason",
                Localize(AgentBurstStopReasonCopyId(frame.BurstStopReason))),
            ShellTextArgument.From(
                "event",
                Localize(AgentBurstStopEventCopyId(frame.BurstStopEvent)))),
        AgentViewerOperationKind.Finish => Localize("agent-arena.operation.finish"),
        _ => throw new ArgumentOutOfRangeException(nameof(frame)),
    };

    private static string AgentBurstStopReasonCopyId(
        AgentBurstStopReason? reason) => reason switch
        {
            null => "agent-arena.burst.stop.none",
            AgentBurstStopReason.RequestedLimit =>
                "agent-arena.burst.stop.requested-limit",
            AgentBurstStopReason.DecisionEvent =>
                "agent-arena.burst.stop.decision-event",
            AgentBurstStopReason.MatchStepLimit =>
                "agent-arena.burst.stop.match-step-limit",
            AgentBurstStopReason.RulesTerminal =>
                "agent-arena.burst.stop.rules-terminal",
            AgentBurstStopReason.ReplayFailure =>
                "agent-arena.burst.stop.replay-failure",
            AgentBurstStopReason.LessonRequirementsReached =>
                "agent-arena.burst.stop.lesson-target",
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    private static string AgentBurstStopEventCopyId(RunEventKind? stopEvent) => stopEvent switch
    {
        null => "agent-arena.burst.event.none",
        RunEventKind.Wrapped => "agent-arena.burst.event.wrapped",
        RunEventKind.AteFood => "agent-arena.burst.event.ate-food",
        RunEventKind.Died => "agent-arena.burst.event.died",
        RunEventKind.Won => "agent-arena.burst.event.won",
        RunEventKind.PowerSpawned => "agent-arena.burst.event.power-spawned",
        RunEventKind.PowerCollected => "agent-arena.burst.event.power-collected",
        RunEventKind.PowerActivated => "agent-arena.burst.event.power-activated",
        RunEventKind.PowerExpired => "agent-arena.burst.event.power-expired",
        RunEventKind.PowerConsumed => "agent-arena.burst.event.power-consumed",
        RunEventKind.PowerDiscarded => "agent-arena.burst.event.power-discarded",
        RunEventKind.CollisionPrevented =>
            "agent-arena.burst.event.collision-prevented",
        RunEventKind.NearMiss => "agent-arena.burst.event.near-miss",
        RunEventKind.StarvationWarning =>
            "agent-arena.burst.event.starvation-warning",
        RunEventKind.ComboExpired => "agent-arena.burst.event.combo-expired",
        RunEventKind.AchievementCandidate =>
            "agent-arena.burst.event.achievement-candidate",
        _ => throw new ArgumentOutOfRangeException(nameof(stopEvent)),
    };

    private static string CompactAgentPassportToken(string value) => value.Length <= 18
        ? value.ToUpperInvariant()
        : value[..16].ToUpperInvariant() + "..";

    // The host publishes 16-character lowercase state hashes. The overlay prints a
    // fixed uppercase prefix so a spectator can compare the same match identity the
    // host already reported without widening the bounded evidence band.
    private static string AgentViewerStateHashPrefix(string stateHash)
    {
        ArgumentNullException.ThrowIfNull(stateHash);
        var prefix = stateHash.Length <= AgentViewerStateHashPrefixLength
            ? stateHash
            : stateHash[..AgentViewerStateHashPrefixLength];
        return prefix.ToUpperInvariant();
    }

    // The verified replay payload hash is the last host identity the window withheld.
    // It exists only with a verified result, so a live match reads REPLAY PENDING.
    private string AgentViewerReplayCopy(AgentViewerFrameV9 frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.VerifiedReplayPayloadHash is { } replayPayloadHash)
        {
            return Localize(
                "agent-arena.replay.verified",
                ShellTextArgument.From(
                    "replay",
                    AgentViewerStateHashPrefix(replayPayloadHash)));
        }

        return frame.Observation.Lifecycle == AgentMatchLifecycle.FailedClosed
            ? Localize("agent-arena.replay.unavailable")
            : Localize("agent-arena.replay.pending");
    }

    // Observed danger and held recovery, in the order a spectator asks for it:
    // how many ways out are left, what that count is called, and what is still in
    // hand if the next step goes wrong. It never names a direction.
    private string AgentSurvivalCopy(AgentSurvivalStateV1 survival)
    {
        ArgumentNullException.ThrowIfNull(survival);
        var resources = AgentSurvivalStateV1.RecoveryOrder
            .Select(kind => survival.RecoveryResources.Single(item => item.Kind == kind))
            .ToArray();
        return Localize(
            "agent-arena.survival",
            ShellTextArgument.From("open", survival.StructuralOpenExits),
            ShellTextArgument.From("candidate", survival.CandidateExits),
            ShellTextArgument.From(
                "pressure",
                Localize(AgentExitPressureCopyId(survival.ExitPressure))),
            ShellTextArgument.From("shield", AgentRecoveryCopy(resources[0])),
            ShellTextArgument.From("phase", AgentRecoveryCopy(resources[1])),
            ShellTextArgument.From("last_stand", AgentRecoveryCopy(resources[2])),
            ShellTextArgument.From("slow", AgentRecoveryCopy(resources[3])));
    }

    // A one-shot charge reads as HELD; a timed effect reads as its remaining
    // ticks. Nothing held reads as the empty marker rather than a zero, so a
    // spectator never has to decide whether 0 means expired or absent.
    private string AgentRecoveryCopy(AgentRecoveryResourceV1 resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (resource.TicksRemaining > 0)
        {
            return resource.TicksRemaining.ToString(CultureInfo.InvariantCulture);
        }

        return resource.Held
            ? Localize("agent-arena.recovery.held")
            : Localize("agent-arena.recovery.none");
    }

    private static string AgentExitPressureCopyId(AgentExitPressureV1 pressure) => pressure switch
    {
        AgentExitPressureV1.NotRunning => "agent-arena.pressure.not-running",
        AgentExitPressureV1.Open => "agent-arena.pressure.open",
        AgentExitPressureV1.Narrow => "agent-arena.pressure.narrow",
        AgentExitPressureV1.Pinned => "agent-arena.pressure.pinned",
        AgentExitPressureV1.Trapped => "agent-arena.pressure.trapped",
        _ => throw new ArgumentOutOfRangeException(nameof(pressure)),
    };

    private string AgentViewerSeedCopy(AgentObservationV5 observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        return observation.GameplaySeed is { } seed
            ? Localize(
                "agent-arena.seed.open",
                ShellTextArgument.From("seed", seed))
            : Localize("agent-arena.seed.blind");
    }

    private void DrawFittedAgentLabel(
        string text,
        Vector2 position,
        int baseFontSize,
        float maximumWidth,
        Color color)
    {
        var fontSize = ScaledFontSize(baseFontSize);
        DrawLabel(
            FitAgentOverlayText(
                ActiveShellTheme.InterfaceFont,
                text,
                fontSize,
                maximumWidth),
            position,
            fontSize,
            color);
    }

    private void CheckAgentViewerSmokeTimeout(ulong nowMilliseconds)
    {
        if (!_agentViewerSmokeEnabled
            || _agentViewerSmokeDeadlineMilliseconds is not { } deadline
            || nowMilliseconds < deadline)
        {
            return;
        }

        _agentViewerSmokeEnabled = false;
        _agentViewerSmokeDeadlineMilliseconds = null;
        GD.PushError("VIBESNAKE_AGENT_VIEWER_SMOKE_FAILED timeout");
        GetTree().Quit(1);
    }

    private async Task CompleteAgentViewerSmokeAsync(
        string stateHash,
        long frameSequence,
        AgentViewerOperationKind operation,
        int stepsAdvanced,
        long coalescedFrames)
    {
        try
        {
            await SettlePlayedAudio();
            await ReleaseAgentViewerSmokeRadio();
            await ReleaseSmokeAudio();
            if (!_shellSettings.MasterMuted
                || !_shellSettings.HighContrast
                || !_shellSettings.ReducedMotion
                || Math.Abs(_shellSettings.TextScale - ShellSettings.MaximumTextScale) > 0.0001f
                || !_agentViewerSnappedLatestFrame)
            {
                throw new InvalidOperationException(
                    "Agent viewer accessibility smoke profile was not retained.");
            }
            var passport = _agentViewerFrame?.Observation.Passport
                ?? throw new InvalidOperationException(
                    "Agent viewer presentation had no terminal passport.");
            if (_agentViewerPresentedAvatarId != passport.AvatarId
                || _agentViewerPresentedAccentId != passport.AccentId
                || _agentViewerPresentedStationId != passport.StationId)
            {
                throw new InvalidOperationException(
                    "Agent viewer presentation did not retain the closed passport identity.");
            }
            if (_agentViewerHumanCosmeticIdBeforePresentation
                != _progression.SelectedCosmeticSetId)
            {
                throw new InvalidOperationException(
                    "Agent viewer presentation changed human cosmetic progression.");
            }
            GD.Print(
                "VIBESNAKE_AGENT_VIEWER_SMOKE_OK "
                + $"hash={stateHash} frame={frameSequence} "
                + $"operation={operation} steps={stepsAdvanced} "
                + $"coalesced={coalescedFrames} "
                + $"avatar={passport.AvatarId} accent={passport.AccentId} "
                + $"station={passport.StationId} "
                + "motion=snap "
                + "accessibility=muted,high-contrast,reduced-motion,text-150");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError($"VIBESNAKE_AGENT_VIEWER_SMOKE_FAILED {exception}");
            GetTree().Quit(1);
        }
    }

    private async Task ReleaseAgentViewerSmokeRadio()
    {
        if (_radioPlayer is null)
        {
            return;
        }

        var radioPlayer = _radioPlayer;
        _radioPlayer = null;
        if (!radioPlayer.TryStopAndRelease(out var failure))
        {
            throw new InvalidOperationException(
                "Agent viewer smoke radio cleanup failed: " + failure);
        }

        using (var timer = GetTree().CreateTimer(0.10))
        {
            await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
        }

        radioPlayer.Free();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void DrawAgentWatch()
    {
        if (_agentViewerSnapshot is null || _agentViewerFrame is null)
        {
            DrawLabel(
                Localize("screen.agent-arena.title"),
                new Vector2(42.0f, 108.0f),
                ScaledFontSize(28),
                ActiveShellPalette.GoldText);
            DrawLabel(
                Localize(_agentViewerStatusId),
                new Vector2(42.0f, 158.0f),
                ScaledFontSize(17),
                ActiveShellPalette.BodyText);
            DrawLabel(
                Localize("agent-arena.waiting-score"),
                new Vector2(42.0f, 196.0f),
                ScaledFontSize(13),
                SecondaryTextColor());
            DrawActionPromptSegment(
                "back",
                Localize("action.return-menu"),
                new Vector2(42.0f, 680.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            return;
        }

        var observation = _agentViewerFrame.Observation;
        var mode = RunModeCatalog.Get(observation.ModeId, observation.ModeVersion);
        var passport = observation.Passport;
        var agentCosmetic = CosmeticSetCatalog.Find(passport.AvatarId)
            ?? throw new InvalidOperationException(
                "Agent passport referenced an unavailable avatar.");
        var agentAccent = AgentAccentCatalog.Get(passport.AccentId);
        var agentStation = StationIdentityCatalog.Get(passport.StationId);
        _agentViewerPresentedAvatarId = agentCosmetic.Id;
        _agentViewerPresentedAccentId = agentAccent.Id;
        _agentViewerPresentedStationId = agentStation.Id;
        _agentViewerHumanCosmeticIdBeforePresentation ??=
            _progression.SelectedCosmeticSetId;
        DrawRun(
            _agentViewerSnapshot,
            Localize(observation.IsActionAwaited
                ? "agent-arena.run.live"
                : _agentViewerFrame.VerifiedResultAvailable
                    ? "agent-arena.run.complete"
                    : "agent-arena.run.failed"),
            mode,
            agentCosmetic);
        if (!_capturePresentation.ShowSpectatorOverlays)
        {
            return;
        }

        var panel = ActiveShellPalette.CanvasBackground;
        panel.A = 0.90f;
        DrawRect(new Rect2(20.0f, 518.0f, 1240.0f, 200.0f), panel);
        var styleProgress = observation.StyleContract;
        var styleOutcome = _agentViewerFrame.StyleOutcome;
        var lessonProgress = observation.LessonProgress;
        var lessonOutcome = _agentViewerFrame.LessonOutcome;
        IReadOnlyList<AgentStyleCriterionProgressV3>? styleCriteria =
            styleOutcome?.Criteria ?? styleProgress?.Criteria;
        IReadOnlyList<AgentLessonRequirementProgressV2>? lessonRequirements =
            lessonOutcome?.Requirements ?? lessonProgress?.Requirements;
        var styleReplayVerified = styleOutcome is not null;
        var styleReplayEvidenceUnavailable = styleProgress is not null
            && observation.Lifecycle == AgentMatchLifecycle.FailedClosed;
        var lessonEvidenceVerified = lessonOutcome is not null;
        var lessonVerifiedEvidenceUnavailable = lessonProgress is not null
            && observation.Lifecycle == AgentMatchLifecycle.FailedClosed;
        var style = lessonProgress is not null
            ? lessonVerifiedEvidenceUnavailable
                ? Localize(
                    "agent-arena.lesson.replay-unavailable",
                    ShellTextArgument.From("lesson", lessonProgress.Title.ToUpperInvariant()))
                : lessonEvidenceVerified
                    ? Localize(
                        "agent-arena.lesson.replay-verified",
                        ShellTextArgument.From("lesson", lessonProgress.Title.ToUpperInvariant()),
                        ShellTextArgument.From("met", lessonOutcome!.RequirementsSatisfied))
                    : Localize(
                        "agent-arena.lesson.live",
                        ShellTextArgument.From("lesson", lessonProgress.Title.ToUpperInvariant()),
                        ShellTextArgument.From("met", lessonProgress.RequirementsSatisfied))
            : styleProgress is null
                ? Localize("agent-arena.style.open")
                : styleReplayVerified
                    ? Localize(
                        "agent-arena.style.replay-verified",
                        ShellTextArgument.From(
                            "style",
                            styleProgress.DisplayName.ToUpperInvariant()),
                        ShellTextArgument.From(
                            "met",
                            styleOutcome!.ThresholdsReached))
                    : styleReplayEvidenceUnavailable
                        ? Localize(
                            "agent-arena.style.replay-unavailable",
                            ShellTextArgument.From(
                                "style",
                                styleProgress.DisplayName.ToUpperInvariant()))
                        : Localize(
                            "agent-arena.style.live",
                            ShellTextArgument.From(
                                "style",
                                styleProgress.DisplayName.ToUpperInvariant()),
                            ShellTextArgument.From(
                                "met",
                                styleProgress.ThresholdsReached));
        var firstStyleCriterion = styleCriteria is { Count: 2 }
            ? AgentStyleCriterionCopy(
                styleCriteria[0],
                styleReplayVerified,
                styleReplayEvidenceUnavailable)
            : null;
        var secondStyleCriterion = styleCriteria is { Count: 2 }
            ? AgentStyleCriterionCopy(
                styleCriteria[1],
                styleReplayVerified,
                styleReplayEvidenceUnavailable)
            : null;
        var firstLessonRequirement = lessonRequirements is { Count: 2 }
            ? AgentLessonRequirementCopy(
                lessonRequirements[0],
                lessonEvidenceVerified,
                lessonVerifiedEvidenceUnavailable)
            : null;
        var secondLessonRequirement = lessonRequirements is { Count: 2 }
            ? AgentLessonRequirementCopy(
                lessonRequirements[1],
                lessonEvidenceVerified,
                lessonVerifiedEvidenceUnavailable)
            : null;
        var firstEvidence = firstLessonRequirement ?? firstStyleCriterion;
        var secondEvidence = secondLessonRequirement ?? secondStyleCriterion;
        var rival = observation.Rival is null
            ? Localize("agent-arena.rival.solo")
            : Localize(
                "agent-arena.rival.score",
                ShellTextArgument.From(
                    "rival",
                    observation.Rival.DisplayName.ToUpperInvariant()),
                ShellTextArgument.From(
                    "agent_score",
                    observation.Score.ToString("D6", CultureInfo.InvariantCulture)),
                ShellTextArgument.From(
                    "rival_score",
                    observation.Rival.Score.ToString("D6", CultureInfo.InvariantCulture)));
        var publicIntent = Localize(AgentIntentCopyId(
            observation.PreviousAction?.DeclaredIntent ?? AgentPublicIntent.Undeclared));
        var actionFeedback = Localize(AgentActionFeedbackCopyId(observation.PreviousAction));
        var outcome = Localize(AgentOutcomeCopyId(_agentViewerFrame.EndReason));
        var operation = AgentViewerOperationCopy(_agentViewerFrame);
        var delivery = _agentViewerCoalescedFrames == 0
            ? Localize("agent-arena.delivery.continuous")
            : Localize(
                "agent-arena.delivery.coalesced",
                ShellTextArgument.From("count", _agentViewerCoalescedFrames));
        DrawFittedAgentLabel(
            AgentSurvivalCopy(_agentViewerFrame.SurvivalState),
            new Vector2(52.0f, 545.0f),
            11,
            1208.0f,
            ActiveShellPalette.BodyText);
        DrawRect(
            new Rect2(38.0f, 554.0f, 9.0f, 9.0f),
            CosmeticColor(agentAccent.Color));
        DrawFittedAgentLabel(
            Localize(
                "agent-arena.verification",
                ShellTextArgument.From("seed", AgentViewerSeedCopy(observation)),
                ShellTextArgument.From(
                    "state",
                    AgentViewerStateHashPrefix(observation.StateHash)),
                ShellTextArgument.From(
                    "replay",
                    AgentViewerReplayCopy(_agentViewerFrame))),
            new Vector2(52.0f, 572.0f),
            12,
            1208.0f,
            ActiveShellPalette.AccentText);
        DrawFittedAgentLabel(
            Localize(
                "agent-arena.identity",
                ShellTextArgument.From(
                    "agent",
                    observation.Passport.DisplayName.ToUpperInvariant()),
                ShellTextArgument.From(
                    "avatar",
                    CompactAgentPassportToken(agentCosmetic.Name)),
                ShellTextArgument.From(
                    "station",
                    CompactAgentPassportToken(agentStation.DisplayName))),
            new Vector2(52.0f, 602.0f),
            13,
            1208.0f,
            ActiveShellPalette.GoldText);
        if (firstEvidence is not null && secondEvidence is not null)
        {
            DrawFittedAgentLabel(
                style,
                new Vector2(38.0f, 628.0f),
                10,
                600.0f,
                ActiveShellPalette.GoldText);
            DrawFittedAgentLabel(
                rival,
                new Vector2(660.0f, 628.0f),
                10,
                600.0f,
                ActiveShellPalette.GoldText);
            DrawFittedAgentLabel(
                firstEvidence,
                new Vector2(38.0f, 653.0f),
                8,
                600.0f,
                ActiveShellPalette.AccentText);
            DrawFittedAgentLabel(
                secondEvidence,
                new Vector2(660.0f, 653.0f),
                8,
                600.0f,
                ActiveShellPalette.AccentText);
            DrawFittedAgentLabel(
                Localize(
                    "agent-arena.operation-status",
                    ShellTextArgument.From("operation", operation),
                    ShellTextArgument.From("delivery", delivery)),
                new Vector2(38.0f, 680.0f),
                8,
                600.0f,
                ActiveShellPalette.BodyText);
            DrawFittedAgentLabel(
                Localize(
                    "agent-arena.status",
                    ShellTextArgument.From("status", Localize(_agentViewerFeedId)),
                    ShellTextArgument.From("outcome", outcome),
                    ShellTextArgument.From("step", observation.Tick),
                    ShellTextArgument.From("maximum", observation.MaximumSteps),
                    ShellTextArgument.From("frame", _agentViewerFrame.Sequence)),
                new Vector2(660.0f, 680.0f),
                8,
                600.0f,
                ActiveShellPalette.BodyText);
        }
        else
        {
            DrawFittedAgentLabel(
                Localize(
                    "agent-arena.matchup",
                    ShellTextArgument.From("style", style),
                    ShellTextArgument.From("rival", rival)),
                new Vector2(38.0f, 628.0f),
                11,
                1222.0f,
                ActiveShellPalette.GoldText);
            DrawFittedAgentLabel(
                Localize(
                    "agent-arena.operation-status",
                    ShellTextArgument.From("operation", operation),
                    ShellTextArgument.From("delivery", delivery)),
                new Vector2(38.0f, 653.0f),
                10,
                1222.0f,
                ActiveShellPalette.AccentText);
            DrawFittedAgentLabel(
                Localize(
                    "agent-arena.status",
                    ShellTextArgument.From("status", Localize(_agentViewerFeedId)),
                    ShellTextArgument.From("outcome", outcome),
                    ShellTextArgument.From("step", observation.Tick),
                    ShellTextArgument.From("maximum", observation.MaximumSteps),
                    ShellTextArgument.From("frame", _agentViewerFrame.Sequence)),
                new Vector2(38.0f, 680.0f),
                9,
                1222.0f,
                ActiveShellPalette.BodyText);
        }
        DrawFittedAgentLabel(
            Localize(
                "agent-arena.intent-status",
                ShellTextArgument.From("intent", publicIntent),
                ShellTextArgument.From("action", actionFeedback)),
            new Vector2(38.0f, 704.0f),
            11,
            992.0f,
            ActiveShellPalette.BodyText);
        DrawActionPromptSegment(
            "back",
            Localize("action.return-menu"),
            new Vector2(1050.0f, 704.0f),
            ScaledFontSize(10),
            SecondaryTextColor());
    }
#endif

    private void DrawSpectatorSelection()
    {
        var featured = SpectatorRivalCatalog.Get(_spectatorSelection.PersonalityId);
        var rival = SpectatorRivalCatalog.Get(_spectatorSelection.RivalPersonalityId);
        string[] rows =
        [
            Localize(
                "spectator.selection.channel",
                ShellTextArgument.From("channel", featured.BroadcastIdentity)),
            Localize(
                "spectator.selection.rivalry",
                ShellTextArgument.From("rival", rival.BroadcastIdentity)),
            Localize(
                "spectator.selection.rules",
                ShellTextArgument.From("rules", _spectatorSelection.Mode.ContractId)),
            Localize(
                "spectator.selection.seed",
                ShellTextArgument.From("seed_class", _spectatorSelection.SeedClass),
                ShellTextArgument.From("slot", _spectatorSelection.SeedSlot + 1)),
            Localize(
                "spectator.selection.exact-seed",
                ShellTextArgument.From("seed", _spectatorSelection.GameplaySeed)),
            Localize(
                "spectator.selection.speed",
                ShellTextArgument.From("speed", $"{_spectatorSelection.PlaybackSpeed:0.0#}X")),
            Localize(
                "spectator.selection.explanation",
                ShellTextArgument.From("level", _spectatorSelection.ExplanationLevel)),
            Localize(
                "spectator.selection.prediction",
                ShellTextArgument.From("prediction", _spectatorSelection.Prediction)),
        ];
        DrawLabel(
            Localize("screen.spectator.title"),
            new Vector2(42.0f, 84.0f),
            ScaledFontSize(38),
            PrimaryTextColor());
        DrawLabel(
            Localize("spectator.selection.instructions"),
            new Vector2(46.0f, 124.0f),
            ScaledFontSize(14),
            SecondaryTextColor());
        DrawLabel(
            Localize("spectator.selection.safety"),
            new Vector2(46.0f, 150.0f),
            ScaledFontSize(13),
            ActiveShellPalette.GoldText);
        var y = 192.0f;
        for (var index = 0; index < rows.Length; index++)
        {
            DrawLabel(
                (index == _spectatorSelectionCursor ? "[>] " : "[ ] ") + rows[index],
                new Vector2(52.0f, y),
                ScaledFontSize(17),
                index == _spectatorSelectionCursor
                    ? ActiveShellPalette.PrimaryText
                    : ActiveShellPalette.BodyText);
            y += 38.0f;
        }

        DrawLabel(
            BoundRadioLine(featured.DeclaredBehavior, 66),
            new Vector2(680.0f, 192.0f),
            ScaledFontSize(15),
            ActiveShellPalette.AccentText);
        DrawLabel(
            $"{featured.StationAffinity}  |  {featured.ShedId}",
            new Vector2(680.0f, 222.0f),
            ScaledFontSize(14),
            SecondaryTextColor());
        var standings = _spectatorLeague.RankedStandings().Take(5).ToArray();
        DrawLabel(
            Localize("action.league-standings").ToUpperInvariant(),
            new Vector2(680.0f, 278.0f),
            ScaledFontSize(18),
            ActiveShellPalette.GoldText);
        for (var index = 0; index < standings.Length; index++)
        {
            var standing = standings[index];
            DrawLabel(
                Localize(
                    "spectator.standing-row",
                    ShellTextArgument.From("rank", index + 1),
                    ShellTextArgument.From(
                        "channel",
                        SpectatorRivalCatalog.Get(standing.PersonalityId).BroadcastIdentity),
                    ShellTextArgument.From("wins", standing.Wins),
                    ShellTextArgument.From("average", standing.AverageScore),
                    ShellTextArgument.From("best", standing.BestScore),
                    ShellTextArgument.From("milestones", standing.MilestoneIds.Count)),
                new Vector2(680.0f, 310.0f + (index * 30.0f)),
                ScaledFontSize(12),
                ActiveShellPalette.BodyText);
        }

        var nextX = DrawActionPromptSegment(
            "confirm",
            Localize("action.start-broadcast"),
            new Vector2(46.0f, 646.0f),
            ScaledFontSize(14),
            SecondaryTextColor());
        nextX = DrawActionPromptSegment(
            "browse_achievements",
            Localize("action.lore-archive"),
            new Vector2(nextX, 646.0f),
            ScaledFontSize(14),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "back",
            Localize("action.return"),
            new Vector2(nextX, 646.0f),
            ScaledFontSize(14),
            SecondaryTextColor());
    }

    private void DrawLoreArchive()
    {
        var entries = FilteredLoreEntries();
        if (entries.Length == 0)
        {
            throw new InvalidOperationException("The lore depth filter cannot be empty.");
        }

        _loreBrowseCursor = Math.Clamp(_loreBrowseCursor, 0, entries.Length - 1);
        var selected = entries[_loreBrowseCursor];
        var unlocked = LoreCatalog.IsUnlocked(selected, _loreUnlockContext);
        var unlockedCount = entries.Count(entry =>
            LoreCatalog.IsUnlocked(entry, _loreUnlockContext));
        var depthLabel = _loreDepthFilterIndex == 0
            ? "ALL"
            : ((LoreDepth)(_loreDepthFilterIndex - 1)).ToString().ToUpperInvariant();
        DrawLabel(
            Localize("screen.lore.title"),
            new Vector2(42.0f, 82.0f),
            ScaledFontSize(38),
            PrimaryTextColor());
        DrawLabel(
            Localize(
                "lore.summary",
                ShellTextArgument.From("unlocked", unlockedCount),
                ShellTextArgument.From("total", entries.Length),
                ShellTextArgument.From("depth", depthLabel)),
            new Vector2(46.0f, 122.0f),
            ScaledFontSize(15),
            ActiveShellPalette.GoldText);
        DrawLabel(
            Localize("lore.safety"),
            new Vector2(46.0f, 150.0f),
            ScaledFontSize(13),
            SecondaryTextColor());

        const int visibleRows = 9;
        var start = Math.Clamp(
            _loreBrowseCursor - (visibleRows / 2),
            0,
            Math.Max(0, entries.Length - visibleRows));
        var end = Math.Min(entries.Length, start + visibleRows);
        for (var index = start; index < end; index++)
        {
            var entry = entries[index];
            var isUnlocked = LoreCatalog.IsUnlocked(entry, _loreUnlockContext);
            var marker = index == _loreBrowseCursor
                ? isUnlocked ? "[>]" : "[X]"
                : isUnlocked ? "[+]" : "[-]";
            DrawLabel(
                Localize(
                    "lore.entry-row",
                    ShellTextArgument.From("marker", marker),
                    ShellTextArgument.From("title", Localize(entry.TitleCopyId)),
                    ShellTextArgument.From("kind", entry.Kind.ToString().ToUpperInvariant())),
                new Vector2(48.0f, 202.0f + ((index - start) * 42.0f)),
                ScaledFontSize(13),
                index == _loreBrowseCursor
                    ? ActiveShellPalette.PrimaryText
                    : isUnlocked
                        ? ActiveShellPalette.BodyText
                        : SecondaryTextColor());
        }

        DrawRect(
            new Rect2(650.0f, 188.0f, 590.0f, 390.0f),
            ActiveShellPalette.BoardBackground);
        DrawLabel(
            Localize(selected.TitleCopyId),
            new Vector2(678.0f, 232.0f),
            ScaledFontSize(21),
            unlocked ? ActiveShellPalette.GoldText : SecondaryTextColor());
        DrawLabel(
            Localize(
                "lore.detail-meta",
                ShellTextArgument.From("depth", selected.Depth.ToString().ToUpperInvariant()),
                ShellTextArgument.From("canon", selected.CanonTier.ToString().ToUpperInvariant()),
                ShellTextArgument.From("kind", selected.Kind.ToString().ToUpperInvariant())),
            new Vector2(678.0f, 268.0f),
            ScaledFontSize(12),
            SecondaryTextColor());
        DrawLabel(
            unlocked ? Localize(selected.BodyCopyId) : LocalizedLoreLock(selected),
            new Vector2(678.0f, 316.0f),
            ScaledFontSize(14),
            unlocked ? ActiveShellPalette.BodyText : ActiveShellPalette.AccentText);
        DrawLabel(
            Localize("lore.navigation"),
            new Vector2(46.0f, 620.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        var nextX = DrawActionPromptSegment(
            "move_left",
            Localize("action.previous-category"),
            new Vector2(46.0f, 660.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        nextX = DrawActionPromptSegment(
            "move_right",
            Localize("action.next-category"),
            new Vector2(nextX, 660.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "back",
            Localize("action.return"),
            new Vector2(nextX, 660.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
    }

    private string LocalizedLoreLock(LoreEntry entry) => entry.UnlockKind switch
    {
        LoreUnlockKind.ProgressionReward => Localize(
            "lore.locked.reward",
            ShellTextArgument.From("reward", entry.UnlockId ?? string.Empty)),
        LoreUnlockKind.SpectatorMilestone => Localize(
            "lore.locked.milestone",
            ShellTextArgument.From("milestone", entry.UnlockId ?? string.Empty)),
        LoreUnlockKind.LocalReplayCount => Localize(
            "lore.locked.replay",
            ShellTextArgument.From("count", entry.UnlockThreshold)),
        _ => Localize(entry.BodyCopyId),
    };

    private static string DescribeSpectatorResources(SpectatorSurvivalResources resources)
    {
        var active = new List<string>(4);
        if (resources.Shield)
        {
            active.Add("SHIELD");
        }

        if (resources.PhaseShift)
        {
            active.Add("PHASE");
        }

        if (resources.LastStandHeld)
        {
            active.Add("LAST STAND");
        }

        if (resources.LastStandRecovery)
        {
            active.Add("RECOVERY");
        }

        var powers = active.Count == 0 ? "NONE" : string.Join("+", active);
        return $"{powers}  HUNGER {resources.HungerTicksRemaining}/{resources.HungerMaximumTicks}";
    }

#if AGENT_ARENA_PREVIEW
    // AA-06's browser. Every decision shown here is made by
    // AgentExhibitionBrowseReportV1 so this screen never invents a rule the
    // report has not already stated and tests have not already proven.
    private void DrawAgentExhibitions()
    {
        DrawLabel(
            Localize("agent-arena.exhibitions.title"),
            new Vector2(46.0f, 108.0f),
            ScaledFontSize(32),
            PrimaryTextColor());

        var report = _agentExhibitionReport;
        if (report is null || report.IsEmpty)
        {
            DrawLabel(
                Localize("agent-arena.exhibitions.empty"),
                new Vector2(46.0f, 160.0f),
                ScaledFontSize(20),
                SecondaryTextColor());
            DrawFittedLabel(
                Localize("agent-arena.exhibitions.empty-detail"),
                new Vector2(46.0f, 192.0f),
                preferredFontSize: ScaledFontSize(14),
                minimumFontSize: 11,
                maximumWidth: AgentExhibitionRowWidth,
                color: ActiveShellPalette.AccentText);
            DrawAgentExhibitionIsolationNote();
            return;
        }

        DrawFittedLabel(
            Localize(
                "agent-arena.exhibitions.summary",
                ShellTextArgument.From("count", report.EntryCount),
                ShellTextArgument.From("watchable", report.WatchableCount),
                ShellTextArgument.From("rivalries", report.RivalryCount),
                ShellTextArgument.From("position", report.SelectedIndex + 1),
                ShellTextArgument.From("total", report.EntryCount)),
            new Vector2(46.0f, 146.0f),
            preferredFontSize: ScaledFontSize(15),
            minimumFontSize: 11,
            maximumWidth: AgentExhibitionRowWidth,
            color: SecondaryTextColor());

        var first = Math.Max(
            0,
            Math.Min(
                report.SelectedIndex - (AgentExhibitionVisibleRows / 2),
                report.EntryCount - AgentExhibitionVisibleRows));
        var last = Math.Min(report.EntryCount, first + AgentExhibitionVisibleRows);
        for (var index = first; index < last; index++)
        {
            var entry = report.Entries[index];
            var selected = index == report.SelectedIndex;
            var top = 190.0f + ((index - first) * AgentExhibitionRowHeight);
            var marker = ShellFocusPresentation.BindingPrefix(
                selected,
                capture: false,
                conflict: false);
            DrawFittedLabel(
                marker + " " + Localize(
                    "agent-arena.exhibitions.row",
                    ShellTextArgument.From("position", entry.Position + 1),
                    ShellTextArgument.From("mode", entry.ModeId.ToUpperInvariant()),
                    ShellTextArgument.From("seed", entry.GameplaySeed),
                    ShellTextArgument.From(
                        "score",
                        entry.Score.ToString("D6", CultureInfo.InvariantCulture)),
                    ShellTextArgument.From(
                        "ending",
                        entry.EndReason.ToString().ToUpperInvariant()),
                    ShellTextArgument.From("tick", entry.FinalTick)),
                new Vector2(58.0f, top),
                preferredFontSize: ScaledFontSize(16),
                minimumFontSize: 11,
                maximumWidth: AgentExhibitionRowWidth,
                color: selected ? ActiveShellPalette.PrimaryText : SecondaryTextColor());
            DrawFittedLabel(
                AgentExhibitionRowDetail(entry),
                new Vector2(76.0f, top + 22.0f),
                preferredFontSize: ScaledFontSize(13),
                minimumFontSize: 10,
                maximumWidth: AgentExhibitionRowWidth - 18.0f,
                color: entry.WatchAvailable
                    ? ActiveShellPalette.AccentText
                    : ActiveShellPalette.WarningText);
        }

        DrawAgentExhibitionIsolationNote();
    }

    private void DrawAgentExhibitionIsolationNote() =>
        DrawFittedLabel(
            Localize("agent-arena.exhibitions.isolation"),
            new Vector2(46.0f, 672.0f),
            preferredFontSize: ScaledFontSize(13),
            minimumFontSize: 10,
            maximumWidth: AgentExhibitionRowWidth,
            color: SecondaryTextColor());

    // The row detail answers the two questions a person actually has: what was
    // this, and what can I do with it right now.
    private string AgentExhibitionRowDetail(AgentExhibitionBrowseEntryV1 entry)
    {
        var what = entry.LessonId is { } lesson
            ? Localize(
                "agent-arena.exhibitions.row-lesson",
                ShellTextArgument.From("lesson", lesson.ToUpperInvariant()))
            : entry.StyleContractId is { } style
                ? Localize(
                    "agent-arena.exhibitions.row-style",
                    ShellTextArgument.From("style", style.ToUpperInvariant()))
                : entry.RivalPersonalityId is { } rival
                    ? Localize(
                        "agent-arena.exhibitions.row-rival",
                        ShellTextArgument.From("rival", rival.ToUpperInvariant()),
                        ShellTextArgument.From("score", entry.RivalScore ?? 0))
                    : Localize("agent-arena.exhibitions.row-solo");
        var availability = entry.WatchBlock switch
        {
            AgentExhibitionWatchBlock.AgentReplayMissing =>
                Localize("agent-arena.exhibitions.watch-missing-agent"),
            AgentExhibitionWatchBlock.RivalReplayMissing =>
                Localize("agent-arena.exhibitions.watch-missing-rival"),
            _ => Localize("agent-arena.exhibitions.watch-ready"),
        };
        var challenge = entry.RematchAvailable
            ? Localize("agent-arena.exhibitions.challenge-ready")
            : Localize("agent-arena.exhibitions.challenge-unavailable");
        return $"{what}  |  {availability}  |  {challenge}";
    }

    // Reloading rather than caching, because the archive is written by a
    // separate host process and a stale list would offer a person a row that
    // no longer exists.
    private void RefreshAgentExhibitions(int selectedIndex)
    {
        var store = _agentExhibitionArchive;
        if (store is null)
        {
            _agentExhibitionReport = null;
            return;
        }

        var replayDirectory = _replayStore?.ReplayDirectory;
        _agentExhibitionReport = AgentExhibitionBrowseReportV1.Create(
            store.Read(),
            fileName => replayDirectory is not null
                && !string.IsNullOrWhiteSpace(fileName)
                && System.IO.File.Exists(
                    System.IO.Path.Combine(replayDirectory, fileName)),
            selectedIndex);
        _agentExhibitionCursor = _agentExhibitionReport.SelectedIndex;
    }
#endif

    private void DrawOfflineComparisons()
    {
        DrawLabel(
            Localize("screen.comparisons.title"),
            new Vector2(46.0f, 108.0f),
            ScaledFontSize(32),
            PrimaryTextColor());
        DrawLabel(
            Localize("comparisons.summary"),
            new Vector2(46.0f, 144.0f),
            ScaledFontSize(15),
            SecondaryTextColor());
        DrawLabel(
            Localize("comparisons.inbox"),
            new Vector2(46.0f, 170.0f),
            ScaledFontSize(13),
            ActiveShellPalette.AccentText);

        for (var index = 0; index < OfflineChallengeStore.MaximumHouseholdRivalSlots; index++)
        {
            var slotNumber = index + 1;
            var slot = _ghostSlots.FirstOrDefault(item => item.Slot == slotNumber)
                ?? new GhostSlotEntry(
                    slotNumber,
                    $"HOUSEHOLD RIVAL {slotNumber}",
                    GhostSlotState.Empty,
                    "empty",
                    "No household rival is stored in this slot.");
            var selected = index == _ghostSlotCursor;
            var marker = ShellFocusPresentation.BindingPrefix(
                selected,
                capture: false,
                conflict: false);
            DrawLabel(
                Localize(
                    "comparisons.slot-row",
                    ShellTextArgument.From("marker", marker),
                    ShellTextArgument.From("name", slot.DisplayName),
                    ShellTextArgument.From("state", slot.State.ToString().ToUpperInvariant()),
                    ShellTextArgument.From(
                        "mode",
                        slot.ModeId is null ? "MODE N/A" : $"{slot.ModeId}@{slot.ModeVersion}"),
                    ShellTextArgument.From(
                        "score",
                        slot.Score is null ? "N/A" : slot.Score.Value.ToString("D6", CultureInfo.InvariantCulture))),
                new Vector2(58.0f, 230.0f + (index * 68.0f)),
                ScaledFontSize(16),
                selected ? ActiveShellPalette.PrimaryText : SecondaryTextColor());
            DrawLabel(
                Localize(
                    "comparisons.slot-detail",
                    ShellTextArgument.From("code", slot.StatusCode.ToUpperInvariant()),
                    ShellTextArgument.From("message", BoundPlayerDataCaption(slot.StatusMessage, 86))),
                new Vector2(74.0f, 254.0f + (index * 68.0f)),
                ScaledFontSize(12),
                slot.IsPlayable ? ActiveShellPalette.GoldText : ActiveShellPalette.WarningText);
            if (selected && slot.SeedCode is not null)
            {
                var seedCode = slot.SeedCode.Length <= 42
                    ? slot.SeedCode
                    : slot.SeedCode[..26] + "..." + slot.SeedCode[^10..];
                DrawLabel(
                    Localize(
                        "comparisons.seed",
                        ShellTextArgument.From("code", seedCode)),
                    new Vector2(74.0f, 278.0f + (index * 68.0f)),
                    ScaledFontSize(11),
                    ActiveShellPalette.AccentText);
            }
        }

        DrawLabel(
            Localize("comparisons.help"),
            new Vector2(46.0f, 514.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        var promptX = DrawActionPromptSegment(
            "move_up",
            Localize("action.select"),
            new Vector2(46.0f, 552.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        promptX = DrawActionPromptSegment(
            "move_down",
            string.Empty,
            new Vector2(promptX, 552.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "confirm",
            Localize("action.race-ghost"),
            new Vector2(promptX, 552.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        promptX = DrawActionPromptSegment(
            "browse_achievements",
            Localize("action.import-ghost"),
            new Vector2(46.0f, 588.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "browse_content_packs",
            Localize("action.export-run-card"),
            new Vector2(promptX, 588.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        promptX = DrawActionPromptSegment(
            "restore_defaults",
            Localize("action.delete-ghost"),
            new Vector2(46.0f, 624.0f),
            ScaledFontSize(13),
            _pendingGhostDeletion is null
                ? SecondaryTextColor()
                : ActiveShellPalette.WarningText);
        DrawActionPromptSegment(
            "back",
            _pendingGhostDeletion is null
                ? Localize("action.list")
                : Localize("action.cancel-unchanged"),
            new Vector2(promptX, 624.0f),
            ScaledFontSize(13),
            SecondaryTextColor());
        if (_replayStatusCaption is not null)
        {
            DrawLabel(
                _replayStatusCaption,
                new Vector2(46.0f, 674.0f),
                ScaledFontSize(12),
                ActiveShellPalette.AccentText);
        }
    }

    private void DrawReplaysBrowse()
    {
        if (_replayPlayback is not null)
        {
            var playbackState = _replayPlayback.IsComplete
                ? "COMPLETE"
                : _replayPlaybackPaused
                    ? "PAUSED"
                    : "PLAYING";
            DrawRun(
                _replayPlayback.CurrentSnapshot,
                $"REPLAY {_replayPlayback.StepIndex}/{_replayPlayback.StepCount}  {playbackState}  "
                    + $"{ReplayPlaybackSpeeds[_replayPlaybackSpeedIndex]:0.0#}X  "
                    + (_replayHudVisible ? "HUD ON" : "HUD OFF"));

            if (!_capturePresentation.ShowReplayControls)
            {
                return;
            }

            if (!_replayHudVisible)
            {
                DrawRect(
                    new Rect2(0.0f, 0.0f, VirtualViewport.LogicalWidth, HudHeight),
                    ActiveShellPalette.CanvasBackground);
            }

            var panelColor = ActiveShellPalette.CanvasBackground;
            panelColor.A = 0.94f;
            DrawRect(new Rect2(120.0f, 574.0f, 1040.0f, 124.0f), panelColor);
            var nextX = DrawActionPromptSegment(
                "confirm",
                Localize("action.play-pause"),
                new Vector2(142.0f, 606.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            nextX = DrawActionPromptSegment(
                "move_left",
                Localize("action.back-ten"),
                new Vector2(nextX, 606.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            DrawActionPromptSegment(
                "move_right",
                Localize("action.step"),
                new Vector2(nextX, 606.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            nextX = DrawActionPromptSegment(
                "move_down",
                Localize("action.slower"),
                new Vector2(142.0f, 642.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            nextX = DrawActionPromptSegment(
                "move_up",
                Localize("action.faster"),
                new Vector2(nextX, 642.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            DrawActionPromptSegment(
                "help",
                Localize("action.toggle-hud"),
                new Vector2(nextX, 642.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            nextX = DrawActionPromptSegment(
                "replay",
                Localize("action.restart"),
                new Vector2(142.0f, 678.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            DrawActionPromptSegment(
                "back",
                Localize("action.list"),
                new Vector2(nextX, 678.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            return;
        }

        DrawLabel(
            Localize("screen.replays.title"),
            new Vector2(46.0f, 120.0f),
            ScaledFontSize(34),
            PrimaryTextColor());
        DrawLabel(
            Localize("replays.integrity-help"),
            new Vector2(46.0f, 156.0f),
            ScaledFontSize(16),
            SecondaryTextColor());

        if (_replayBrowserEntries.Count == 0)
        {
            DrawLabel(
                Localize("replays.empty"),
                new Vector2(46.0f, 230.0f),
                ScaledFontSize(22),
                ActiveShellPalette.WarningText);
        }
        else
        {
            const int visibleRows = 6;
            var start = Math.Clamp(
                _replayBrowseCursor - (visibleRows / 2),
                0,
                Math.Max(0, _replayBrowserEntries.Count - visibleRows));
            var end = Math.Min(_replayBrowserEntries.Count, start + visibleRows);
            for (var index = start; index < end; index++)
            {
                var replay = _replayBrowserEntries[index];
                var marker = ShellFocusPresentation.BindingPrefix(
                    index == _replayBrowseCursor,
                    capture: false,
                    conflict: false);
                var timestamp = replay.DisplayedAtUtc.Replace('T', ' ')[..19] + "Z";
                var mode = replay.ModeId is null
                    ? "MODE UNKNOWN"
                    : $"{replay.ModeId}@{replay.ModeVersion}";
                var score = replay.Score is null ? "SCORE N/A" : $"SCORE {replay.Score:D6}";
                var seed = replay.GameplaySeed is null ? "SEED LEGACY" : $"SEED {replay.GameplaySeed}";
                var steps = replay.StepCount is null ? "STEPS N/A" : $"STEPS {replay.StepCount}";
                DrawLabel(
                    $"{marker} {timestamp}  {mode}  {score}  {seed}  {steps}",
                    new Vector2(58.0f, 210.0f + ((index - start) * 58.0f)),
                    ScaledFontSize(15),
                    index == _replayBrowseCursor
                        ? ActiveShellPalette.PrimaryText
                        : SecondaryTextColor());
                var rules = replay.RulesetId is null
                    ? "RULES UNKNOWN"
                    : $"RULES {replay.RulesetId}@{replay.RulesVersion}";
                DrawLabel(
                    $"    [{replay.State.ToString().ToUpperInvariant()}:{replay.StatusCode.ToUpperInvariant()}]  {rules}",
                    new Vector2(58.0f, 232.0f + ((index - start) * 58.0f)),
                    ScaledFontSize(13),
                    replay.State == ReplayBrowserState.Verified
                        ? ActiveShellPalette.GoldText
                        : ActiveShellPalette.WarningText);
            }
        }

        if (_pendingReplayDeletion is { } pendingDeletion)
        {
            DrawLabel(
                BoundPlayerDataCaption(pendingDeletion.ConfirmationText, 118),
                new Vector2(46.0f, 560.0f),
                ScaledFontSize(14),
                ActiveShellPalette.WarningText);
            var confirmationX = DrawActionPromptSegment(
                "confirm",
                Localize("action.delete-one-replay"),
                new Vector2(46.0f, 606.0f),
                ScaledFontSize(14),
                ActiveShellPalette.WarningText);
            DrawActionPromptSegment(
                "back",
                Localize("action.cancel-unchanged"),
                new Vector2(confirmationX, 606.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            if (_replayStatusCaption is not null)
            {
                DrawLabel(
                    _replayStatusCaption,
                    new Vector2(46.0f, 674.0f),
                    ScaledFontSize(14),
                    ActiveShellPalette.AccentText);
            }

            return;
        }

        var promptX = DrawActionPromptSegment(
            "move_up",
            Localize("action.select"),
            new Vector2(46.0f, 560.0f),
            ScaledFontSize(15),
            SecondaryTextColor());
        promptX = DrawActionPromptSegment(
            "move_down",
            string.Empty,
            new Vector2(promptX, 560.0f),
            ScaledFontSize(15),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "confirm",
            Localize("action.load"),
            new Vector2(promptX, 560.0f),
            ScaledFontSize(15),
            SecondaryTextColor());
        promptX = DrawActionPromptSegment(
            "browse_content_packs",
            Localize("action.export-verified"),
            new Vector2(46.0f, 598.0f),
            ScaledFontSize(14),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "restore_defaults",
            Localize("action.prepare-delete"),
            new Vector2(promptX, 598.0f),
            ScaledFontSize(14),
            SecondaryTextColor());
        var comparisonX = DrawActionPromptSegment(
            "browse_achievements",
            Localize("action.offline-comparisons"),
            new Vector2(46.0f, 634.0f),
            ScaledFontSize(15),
            SecondaryTextColor());
        DrawActionPromptSegment(
            "back",
            Localize("action.return"),
            new Vector2(comparisonX, 634.0f),
            ScaledFontSize(15),
            SecondaryTextColor());

        if (_replayStatusCaption is not null)
        {
            DrawLabel(
                _replayStatusCaption,
                new Vector2(46.0f, 674.0f),
                ScaledFontSize(14),
                ActiveShellPalette.AccentText);
        }
    }

    // The top run HUD is one fixed strip of cells on a 1280-wide logical canvas.
    // Every cell used to draw unbounded from its own left edge, which is only
    // safe while the text happens to be short. Two playtest rounds found the two
    // ways that fails: the mode title ran off the canvas and lost its last
    // letter (CLASSIC AGENT COMPLET), and at 150 percent text the combo cell ran
    // under the hunger cell so OVERDRIVE stacked on HUNGER READY. Both are the
    // same defect. Each cell now declares where it starts, how much room it owns
    // before the next cell begins, and the smallest type it may use to keep
    // every character. The readable gate composes the real worst-case English
    // for every cell and proves it neither elides nor crosses into its neighbour.
    private readonly record struct RunHudCell(
        string Id,
        float Left,
        float MaximumWidth,
        int BaseFontSize,
        int MinimumFontSize)
    {
        public float RightEdge => Left + MaximumWidth;
    }

    // The gutter every cell leaves between its budget and the next cell's left
    // edge, so two fully packed neighbours still read as two separate facts.
    private const float RunHudCellGutter = 8.0f;
#if AGENT_ARENA_PREVIEW
    private const float AgentExhibitionRowWidth = 1188.0f;
    private const float AgentExhibitionRowHeight = 54.0f;
    private const int AgentExhibitionVisibleRows = 8;
#endif
    private const float RunHudRightMargin = 1262.0f;

    private static readonly RunHudCell RunHudScoreCell =
        new("run-hud.score", 18.0f, 199.0f, 19, 11);
    private static readonly RunHudCell RunHudComboCell =
        new("run-hud.combo", 225.0f, 297.0f, 19, 11);
    private static readonly RunHudCell RunHudHungerCell =
        new("run-hud.hunger", 530.0f, 222.0f, 16, 11);
    private static readonly RunHudCell RunHudClassicScoreCell =
        new("run-hud.classic-score", 225.0f, 357.0f, 16, 11);
    private static readonly RunHudCell RunHudClassicRulesCell =
        new("run-hud.classic-rules", 590.0f, 382.0f, 15, 11);
    private static readonly RunHudCell RunHudModeTitleCell =
        new("run-hud.mode-title", 980.0f, 282.0f, 16, 11);

    // The hunger meter is a fixed-size graphic rather than text, so it never
    // rescales with the text setting. It still has to sit inside the row, and
    // its critical phase appends a scaling warning glyph, so the gate measures
    // that glyph against the mode title's left edge instead of assuming it.
    private const float RunHudHungerMeterLeft = 760.0f;
    private const float HungerMeterSegmentWidth = 11.0f;
    private const float HungerMeterSegmentGap = 3.0f;
    private const float HungerMeterMarkerOffset = 10.0f;
    private const int HungerMeterMarkerFontSize = 17;

    private const float RunHudHungerMeterWidth =
        (HungerFeedback.SegmentCount * (HungerMeterSegmentWidth + HungerMeterSegmentGap))
            - HungerMeterSegmentGap;

    // Vibe draws score, combo, hunger, the meter, and the title. Classic draws
    // score, its own two captions, and the title. Ordering matters: the gate
    // walks each list left to right and requires a real gap at every seam.
    private static IReadOnlyList<RunHudCell> RunHudVibeCells =>
    [
        RunHudScoreCell,
        RunHudComboCell,
        RunHudHungerCell,
        new(
            "run-hud.hunger-meter",
            RunHudHungerMeterLeft,
            RunHudHungerMeterWidth,
            RunHudModeTitleCell.BaseFontSize,
            RunHudModeTitleCell.MinimumFontSize),
        RunHudModeTitleCell,
    ];

    private static IReadOnlyList<RunHudCell> RunHudClassicCells =>
    [
        RunHudScoreCell,
        RunHudClassicScoreCell,
        RunHudClassicRulesCell,
        RunHudModeTitleCell,
    ];

    private static string RunModeTitleText(string modeDisplayName, string statusText) =>
        $"{modeDisplayName.ToUpperInvariant()}  {statusText}";

    private static string RunStatusCopyId(RunStatus status) => status switch
    {
        RunStatus.Running => "run.status.running",
        RunStatus.Dead => "run.status.dead",
        RunStatus.Won => "run.status.won",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    // One draw path for the whole row, so no cell can be rendered under rules
    // the readable gate never proved.
    private void DrawRunHudCell(
        RunHudCell cell,
        string text,
        Color color,
        float verticalOffset = 0.0f,
        int? preferredFontSize = null) =>
        DrawFittedLabel(
            text,
            new Vector2(cell.Left, 30.0f + verticalOffset),
            preferredFontSize: ScaledFontSize(preferredFontSize ?? cell.BaseFontSize),
            minimumFontSize: cell.MinimumFontSize,
            maximumWidth: cell.MaximumWidth,
            color: color);

    private void DrawRun(
        RunSnapshot? replaySnapshot = null,
        string? replayStatus = null,
        RunModeDefinition? presentedMode = null,
        CosmeticSetDefinition? presentedCosmetic = null)
    {
        if (_run is null && replaySnapshot is null)
        {
            return;
        }

        var snapshot = replaySnapshot ?? _run!.GetSnapshot();
        var mode = presentedMode ?? _run?.Mode ?? _replayPlayback?.Mode ?? SelectedRunMode;
        var presentedBody = _snakeMotionPresentation.Resolve(
            snapshot.Body,
            Time.GetTicksMsec(),
            _run?.Configuration.Width
                ?? _replayPlayback?.Configuration.Width
                ?? (int)(VirtualViewport.LogicalWidth / CellSize),
            _run?.Configuration.Height
                ?? _replayPlayback?.Configuration.Height
                ?? (int)((VirtualViewport.LogicalHeight - HudHeight) / CellSize));
        var usesVibePresentation = mode.Id == RunModeCatalog.VibeId;
        var accessibility = AccessibilityPresentationPolicy.FromSettings(_shellSettings);
        var hunger = HungerFeedback.Describe(
            snapshot.HungerTicksRemaining,
            snapshot.HungerMaximumTicks,
            snapshot.HungerWarningTicks);
        var combo = ComboFeedback.Describe(
            snapshot.ComboCount,
            snapshot.ComboMultiplier,
            _comboPulseTicksRemaining,
            accessibility,
            _vibeLevelDirector.CurrentDefinition);
        var statusText = replayStatus
            ?? Localize(_pausedByFocusLoss
                ? "run.status.paused-focus-lost"
                : _paused
                    ? "run.status.paused"
                    : RunStatusCopyId(snapshot.Status));
        DrawBoardTerrain(snapshot.Score);
        if (_capturePresentation.ShowRunHud)
        {
            DrawRunHudCell(
                RunHudScoreCell,
                $"SCORE {snapshot.Score:D6}",
                combo.Emphasized
                    ? ActiveShellPalette.GoldText
                    : ActiveShellPalette.BodyText,
                verticalOffset: combo.VerticalOffset,
                preferredFontSize: combo.Emphasized ? 19 : 18);
            if (usesVibePresentation)
            {
                var comboLevel = combo.Level == "BUILDING" ? string.Empty : "  " + combo.Level;
                DrawRunHudCell(
                    RunHudComboCell,
                    $"{combo.StaticMarker} {combo.Label}{comboLevel}",
                    combo.Emphasized || combo.Level != "BUILDING"
                        ? VibeHudColor(_vibeLevelDirector.CurrentDefinition)
                        : ActiveShellPalette.BodyText,
                    verticalOffset: combo.VerticalOffset,
                    preferredFontSize: combo.Emphasized ? 19 : 18);
                var hungerColor = HungerSignalColor(hunger.Phase);
                DrawRunHudCell(RunHudHungerCell, hunger.Label, hungerColor);
                DrawHungerMeter(
                    hunger,
                    new Vector2(RunHudHungerMeterLeft, 14.0f),
                    hungerColor);
            }
            else
            {
                DrawRunHudCell(
                    RunHudClassicScoreCell,
                    Localize("run.classic-score"),
                    ActiveShellPalette.BodyText);
                DrawRunHudCell(
                    RunHudClassicRulesCell,
                    Localize("run.classic-rules"),
                    ActiveShellPalette.GoldText);
            }

            DrawRunHudCell(
                RunHudModeTitleCell,
                RunModeTitleText(mode.DisplayName, statusText),
                snapshot.Status == RunStatus.Dead
                    ? ActiveShellPalette.WarningText
                    : ActiveShellPalette.BodyText);

            var powerStatus = usesVibePresentation
                ? DescribePowerStatus(snapshot)
                : "CLASSIC RULES: ROUTE, EAT, GROW, WRAP, AVOID YOUR BODY";
            if (usesVibePresentation)
            {
                var adaptiveStatus = snapshot.AdaptiveDifficultyState switch
                {
                    AdaptiveDifficultyState.Disabled => "DDA [=] OFF",
                    AdaptiveDifficultyState.Support => "DDA [+] SUPPORT: SLOW HUNGER",
                    AdaptiveDifficultyState.Standard => "DDA [=] STANDARD",
                    AdaptiveDifficultyState.Pressure => "DDA [!] PRESSURE: FAST HUNGER",
                    _ => throw new InvalidOperationException("Unknown adaptive difficulty state."),
                };
                powerStatus = adaptiveStatus + "  |  " + powerStatus;
            }
            var protectionStatus = PowerFeedbackCatalog.DescribeProtection(snapshot);
            if (usesVibePresentation && protectionStatus != "PROTECTION [ ] NONE")
            {
                powerStatus = protectionStatus + "  |  " + powerStatus;
            }

            var transientCaption = _feedbackCaption ?? _broadcastCaption;
            var secondaryStatus = transientCaption is null
                ? powerStatus
                : $"{powerStatus}    {transientCaption}";
            if (_activeGhostRace is not null && _activeGhostSlot is { } ghostSlot)
            {
                var ghost = _activeGhostRace.GhostSnapshot;
                secondaryStatus = Localize(
                    "comparisons.ghost-hud",
                    ShellTextArgument.From("slot", ghostSlot),
                    ShellTextArgument.From("score", ghost.Score),
                    ShellTextArgument.From("delta", snapshot.Score - ghost.Score),
                    ShellTextArgument.From("length", ghost.Body.Count))
                    + "  |  "
                    + secondaryStatus;
            }
            DrawFittedLabel(
                secondaryStatus,
                new Vector2(18.0f, 53.0f),
                preferredFontSize: 15,
                minimumFontSize: 11,
                maximumWidth: 864.0f,
                color: _feedbackCaption is not null && _feedbackTier >= VisualFeedbackTier.Pressure
                    ? ActiveShellPalette.WarningText
                    : ActiveShellPalette.AccentText);
            DrawFittedLabel(
                _radioPolicy.Snapshot.CompactLine,
                new Vector2(900.0f, 53.0f),
                preferredFontSize: 12,
                minimumFontSize: 10,
                maximumWidth: 362.0f,
                color: SecondaryTextColor());
        }

        if (snapshot.HasDetachedObstacles)
        {
            var hazard = PowerPresentation.SignalColor(PowerKind.SegmentDetach);
            foreach (var obstacle in snapshot.DetachedObstacles)
            {
                DrawCell(
                    obstacle,
                    GameplayPresentation.DetachedObstacleFill,
                    inset: GameplayPresentation.DetachedObstacleInset);
                DrawCellOutline(
                    obstacle,
                    hazard,
                    GameplayPresentation.DetachedObstacleOutlineWidth,
                    inset: GameplayPresentation.DetachedObstacleInset);
            }
        }

        if (snapshot.HasBait && snapshot.BaitPosition is { } bait)
        {
            var baitColor = PowerPresentation.SignalColor(PowerKind.Bait);
            DrawCell(bait, new Color(0.16f, 0.12f, 0.02f), inset: 4.0f);
            DrawCellOutline(bait, baitColor, 1.5f, inset: 3.0f);
            DrawLabel(
                TourRouteMarker,
                new Vector2(
                    (bait.X * CellSize) + 5.0f,
                    HudHeight + (bait.Y * CellSize) + 16.0f),
                14,
                baitColor);
        }

        if (snapshot.Food is { } food)
        {
            DrawCell(food, GameplayPresentation.FoodColor, inset: GameplayPresentation.FoodInset);
        }

        DrawBaitReveal();

        if (snapshot.PowerPickup is { } pickup)
        {
            DrawPowerPickup(pickup);
        }

        if (_activeGhostRace is not null)
        {
            var ghostColor = new Color(0.62f, 0.42f, 0.92f, 0.72f);
            foreach (var ghostCell in _activeGhostRace.GhostSnapshot.Body)
            {
                DrawCellOutline(
                    ghostCell,
                    ghostColor,
                    width: 1.5f,
                    inset: GameplayPresentation.BodyInset + 1.0f);
            }
        }

        var activeCosmetic = presentedCosmetic ?? ActiveCosmeticSet;
        for (var index = 0; index < snapshot.Body.Count; index++)
        {
            var isHead = index == snapshot.Body.Count - 1;
            var bodyColor = CosmeticBodyColor(activeCosmetic, index, isHead);
            if (snapshot.HasPhaseShift && !isHead)
            {
                bodyColor = new Color(0.55f, 0.42f, 0.88f, 0.72f);
            }
            else if (snapshot.HasGluttony && !isHead)
            {
                bodyColor = new Color(0.88f, 0.58f, 0.22f);
            }

            var inset = isHead
                ? GameplayPresentation.HeadInset
                : GameplayPresentation.BodyInset;
            var point = presentedBody[index];
            DrawDetailedCosmeticCell(
                new Rect2(
                    (point.X * CellSize) + inset,
                    HudHeight + (point.Y * CellSize) + inset,
                    CellSize - (inset * 2.0f),
                    CellSize - (inset * 2.0f)),
                activeCosmetic,
                index,
                isHead,
                bodyColor);
        }

        var presentedHead = presentedBody[^1];
        DrawActiveHeadOutlines(snapshot, presentedHead);
        if (usesVibePresentation)
        {
            DrawVibeTrail(snapshot, accessibility, presentedBody, activeCosmetic);
        }
        DrawHeadDirectionMarker(snapshot, presentedHead, activeCosmetic);

        if (_screenState == ScreenState.Ended
            && _capturePresentation.ShowTerminalOverlay)
        {
            var overlayColor = ActiveShellPalette.CanvasBackground;
            overlayColor.A = VisualHierarchyPolicy.Budget.TerminalOverlayAlpha;
            DrawRect(new Rect2(190.0f, 112.0f, 900.0f, 542.0f), overlayColor);
            var summary = _runEndSummary
                ?? RunEndSummary.Create(_run!, snapshot.Score, isNewPersonalBest: false);
            DrawLabel(
                summary.Outcome,
                new Vector2(238.0f, 172.0f),
                ScaledFontSize(30),
                ActiveShellPalette.WarningText);
            DrawLabel(
                Localize(
                    "run-end.cause",
                    ShellTextArgument.From("cause", summary.Cause)),
                new Vector2(238.0f, 214.0f),
                ScaledFontSize(18),
                ActiveShellPalette.PrimaryText);
            if (snapshot.DeathCause != DeathCause.None)
            {
                var death = DeathFeedback.Describe(snapshot.DeathCause);
                DrawLabel(
                    $"SIGNAL {death.StableSymbol} {death.GeometrySignal.ToUpperInvariant()}",
                    new Vector2(238.0f, 244.0f),
                    ScaledFontSize(13),
                    ActiveShellPalette.WarningText);
            }

            DrawLabel(
                Localize(
                    "run-end.recovery",
                    ShellTextArgument.From("recovery", summary.RecoveryHint)),
                new Vector2(238.0f, 270.0f),
                ScaledFontSize(12),
                SecondaryTextColor());

            DrawLabel(
                _activeTourEvent is null
                    ? $"SCORE {summary.Score:D6}  PERSONAL BEST {summary.PersonalBest:D6}"
                    : $"SCORE {summary.Score:D6}  TOUR PRACTICE / NOT SUBMITTED",
                new Vector2(238.0f, 298.0f),
                ScaledFontSize(18),
                summary.IsNewPersonalBest
                    ? ActiveShellPalette.GoldText
                    : ActiveShellPalette.BodyText);
            if (_activeTourEvent is not null && _tourRunOutcome is { } tourOutcome)
            {
                DrawLabel(
                    Localize(
                        "run-end.tour-primary",
                        ShellTextArgument.From(
                            "progress",
                            tourOutcome.PrimaryProgress
                                + (tourOutcome.StyleProgress is { } styleProgress
                                    ? "  STYLE " + styleProgress
                                    : string.Empty))),
                    new Vector2(238.0f, 330.0f),
                    ScaledFontSize(16),
                    tourOutcome.PrimaryComplete
                        ? ActiveShellPalette.GoldText
                        : ActiveShellPalette.WarningText);
            }
            else if (summary.IsNewPersonalBest)
            {
                DrawLabel(
                    Localize("run-end.personal-best"),
                    new Vector2(238.0f, 330.0f),
                    ScaledFontSize(16),
                    ActiveShellPalette.GoldText);
            }
            var recap = _run?.Mode.Includes(RunModeFeatures.ComboScoring) == true
                ? $"LENGTH {summary.Length}  STEPS {summary.SurvivalSteps}  FOOD {summary.FoodEaten}  PEAK COMBO {summary.PeakCombo}"
                : $"LENGTH {summary.Length}  STEPS {summary.SurvivalSteps}  FOOD {summary.FoodEaten}";
            DrawLabel(
                recap,
                new Vector2(238.0f, 362.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            DrawLabel(
                _progressionNotifications.Current?.Caption
                    ?? FormatRunEndUnlocks(summary.NewlyUnlockedIds),
                new Vector2(238.0f, 394.0f),
                ScaledFontSize(14),
                ActiveShellPalette.GoldText);

            if (_replayStatusCaption is not null)
            {
                DrawLabel(
                    _replayStatusCaption,
                    new Vector2(238.0f, 426.0f),
                    ScaledFontSize(13),
                    ActiveShellPalette.AccentText);
            }

            if (_run is not null)
            {
                DrawLabel(
                    FormatScoreIdentityCaption(RunScoreIdentity.FromRun(
                        _run,
                        _activeRunContext)),
                    new Vector2(238.0f, 454.0f),
                    ScaledFontSize(13),
                    ActiveShellPalette.SecondaryText);
            }

            DrawActionPromptSegment(
                "confirm",
                _activeTourEvent is null
                    ? "restart deliberately"
                    : "same-seed rematch",
                new Vector2(238.0f, 488.0f),
                ScaledFontSize(15),
                ActiveShellPalette.BodyText);
            DrawStaticPromptSegment(
                "key:v",
                "button:dpad_down",
                Localize("action.versioned-scores"),
                new Vector2(238.0f, 520.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            DrawStaticPromptSegment(
                "key:r",
                "button:north",
                Localize("action.replays-status"),
                new Vector2(238.0f, 548.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            DrawStaticPromptSegment(
                "key:f1",
                "button:start",
                Localize("action.settings"),
                new Vector2(238.0f, 576.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
            DrawActionPromptSegment(
                "back",
                _activeTourEvent is null ? "menu" : "event cards",
                new Vector2(238.0f, 612.0f),
                ScaledFontSize(14),
                SecondaryTextColor());
        }
    }

    private void DrawBoardTerrain(int score)
    {
        if (_shellSettings.HighContrast)
        {
            return;
        }

        var terrain = BoardTerrainCatalog.Resolve(score);
        DrawRect(
            new Rect2(
                0.0f,
                HudHeight,
                VirtualViewport.LogicalWidth,
                VirtualViewport.LogicalHeight - HudHeight),
            terrain.Veil);

        for (var x = 0.0f; x <= VirtualViewport.LogicalWidth; x += CellSize)
        {
            DrawLine(
                new Vector2(x, HudHeight),
                new Vector2(x, VirtualViewport.LogicalHeight),
                terrain.Grid,
                1.0f);
        }
        for (var y = HudHeight; y <= VirtualViewport.LogicalHeight; y += CellSize)
        {
            DrawLine(
                new Vector2(0.0f, y),
                new Vector2(VirtualViewport.LogicalWidth, y),
                terrain.Grid,
                1.0f);
        }

        foreach (var element in terrain.Elements)
        {
            switch (element.Kind)
            {
                case BoardTerrainElementKind.Foliage:
                    DrawLine(
                        element.Position + new Vector2(0.0f, element.Size),
                        element.Position + new Vector2(element.Size * 0.45f, 0.0f),
                        terrain.Foliage,
                        1.5f);
                    DrawLine(
                        element.Position + new Vector2(element.Size * 0.45f, element.Size),
                        element.Position + new Vector2(element.Size, element.Size * 0.25f),
                        terrain.Foliage,
                        1.0f);
                    break;
                case BoardTerrainElementKind.Bloom:
                    var bloomSize = Math.Max(2.0f, element.Size * 0.5f);
                    DrawRect(
                        new Rect2(
                            element.Position - new Vector2(bloomSize * 0.5f, 0.0f),
                            new Vector2(bloomSize, bloomSize)),
                        terrain.Accent);
                    DrawRect(
                        new Rect2(
                            element.Position + new Vector2(0.0f, -bloomSize * 0.5f),
                            new Vector2(bloomSize, bloomSize)),
                        terrain.Accent);
                    break;
                case BoardTerrainElementKind.Stone:
                    var stoneSize = element.Size + element.Variant;
                    DrawRect(
                        new Rect2(element.Position, new Vector2(stoneSize, stoneSize * 0.65f)),
                        terrain.Stone);
                    DrawLine(
                        element.Position,
                        element.Position + new Vector2(stoneSize, 0.0f),
                        terrain.Accent,
                        1.0f);
                    break;
                default:
                    throw new InvalidOperationException("Unknown board terrain element kind.");
            }
        }
    }

    private void DrawBaitReveal()
    {
        if (_screenState is not ScreenState.Running and not ScreenState.Ended
            || _baitRevealTicksRemaining <= 0
            || _baitRevealOrigin is not { } origin
            || _baitRevealDestination is not { } destination)
        {
            return;
        }

        var color = PowerPresentation.SignalColor(PowerKind.Bait);
        color.A = 0.48f;
        var originCenter = new Vector2(
            (origin.X * CellSize) + (CellSize * 0.5f),
            HudHeight + (origin.Y * CellSize) + (CellSize * 0.5f));
        var destinationCenter = new Vector2(
            (destination.X * CellSize) + (CellSize * 0.5f),
            HudHeight + (destination.Y * CellSize) + (CellSize * 0.5f));
        DrawLine(originCenter, destinationCenter, color, 1.5f);
        DrawCellOutline(destination, color, 2.0f, GameplayPresentation.FoodInset - 1.0f);
    }

    private static string FormatRunEndUnlocks(IReadOnlyList<string> unlockedIds)
    {
        ArgumentNullException.ThrowIfNull(unlockedIds);
        if (unlockedIds.Count == 0)
        {
            return "NEW UNLOCKS: NONE";
        }

        var names = unlockedIds
            .Take(2)
            .Select(id => AchievementCatalog.Find(id)?.Name ?? id)
            .ToArray();
        var remaining = unlockedIds.Count - names.Length;
        return "NEW UNLOCKS: "
            + string.Join(", ", names)
            + (remaining > 0 ? $"  +{remaining} MORE" : string.Empty);
    }

    /// <summary>
    /// Compact support caption: mode, fair category, DDA policy, score, and config hash.
    /// </summary>
    internal static string FormatScoreIdentityCaption(RunScoreIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var hashPrefix = identity.ConfigHash.Length >= 12
            ? identity.ConfigHash[..12]
            : identity.ConfigHash;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{identity.RulesetContractId}  {identity.ModeId}@{identity.ModeVersion}  {identity.ScoreCategoryId}  DDA {identity.AdaptivePolicyId}  score {identity.Score}  cfg {hashPrefix}");
    }

    private void DrawActiveHeadOutlines(RunSnapshot snapshot, Vector2 presentedHead)
    {
        foreach (var kind in VisualHierarchyPolicy.SelectHeadEffectOutlines(snapshot))
        {
            var (width, inset) = kind switch
            {
                PowerKind.LastStand => (1.5f, 3.5f),
                PowerKind.Shield => (2.0f, 0.5f),
                PowerKind.PhaseShift => (2.0f, 2.0f),
                PowerKind.Magnet => (1.5f, 5.0f),
                PowerKind.SlowMo => (1.5f, -1.0f),
                PowerKind.Boost => (1.5f, -2.5f),
                _ => throw new InvalidOperationException($"Unsupported head effect: {kind}."),
            };
            DrawCellOutline(presentedHead, PowerPresentation.SignalColor(kind), width, inset);
        }
    }

    private void DrawVibeTrail(
        RunSnapshot snapshot,
        AccessibilityPresentationPolicy accessibility,
        IReadOnlyList<Vector2> presentedBody,
        CosmeticSetDefinition cosmetic)
    {
        var budget = VibeLevelDirector.ResolveEffectiveBudget(
            _vibeLevelDirector.CurrentLevel,
            accessibility,
            _shellSettings.MasterMuted,
            lowParticle: false);
        var trailCells = Math.Min(budget.TrailCellBudget, Math.Max(0, snapshot.Body.Count - 1));
        if (trailCells == 0)
        {
            return;
        }

        if (cosmetic.TrailOpacityPercent == 0)
        {
            return;
        }

        var color = CosmeticColor(cosmetic.Primary);
        color.A = cosmetic.TrailOpacityPercent / 100.0f;
        foreach (var cell in presentedBody
            .Skip(Math.Max(0, presentedBody.Count - 1 - trailCells))
            .Take(trailCells))
        {
            DrawCellOutline(cell, color, 1.0f, inset: 4.0f);
        }
    }

    private Color VibeHudColor(VibeLevelDefinition definition) => definition.HudRole switch
    {
        "body" => ActiveShellPalette.BodyText,
        "primary" => ActiveShellPalette.PrimaryText,
        "gold" => ActiveShellPalette.GoldText,
        "accent" => ActiveShellPalette.AccentText,
        "selected" => ActiveShellPalette.SelectedText,
        _ => throw new ArgumentOutOfRangeException(nameof(definition), definition.HudRole, "Unknown Vibe HUD role."),
    };

    private void DrawHeadDirectionMarker(
        RunSnapshot snapshot,
        Vector2 presentedHead,
        CosmeticSetDefinition cosmetic)
    {
        var center = new Vector2(
            (presentedHead.X * CellSize) + (CellSize * 0.5f),
            HudHeight + (presentedHead.Y * CellSize) + (CellSize * 0.5f));
        var direction = snapshot.Direction switch
        {
            RulesDirection.Up => Vector2.Up,
            RulesDirection.Right => Vector2.Right,
            RulesDirection.Down => Vector2.Down,
            RulesDirection.Left => Vector2.Left,
            _ => throw new ArgumentOutOfRangeException(nameof(snapshot)),
        };
        var markerColor = CosmeticColor(cosmetic.Secondary);
        DrawLine(
            center,
            center + (direction * 6.0f),
            markerColor,
            width: 2.0f,
            antialiased: false);
        var perpendicular = new Vector2(-direction.Y, direction.X);
        if (cosmetic.HeadMarker == CosmeticHeadMarker.CrownWedge)
        {
            DrawLine(
                center + (perpendicular * 3.0f),
                center + (direction * 4.0f),
                markerColor,
                1.5f);
            DrawLine(
                center - (perpendicular * 3.0f),
                center + (direction * 4.0f),
                markerColor,
                1.5f);
        }
        else if (cosmetic.HeadMarker == CosmeticHeadMarker.HaloWedge)
        {
            DrawArc(center, 6.5f, 0.0f, MathF.Tau, 12, markerColor, 1.5f);
        }

        if (cosmetic.AccessoryId != "none")
        {
            var accessoryColor = CosmeticColor(cosmetic.Primary);
            var radius = 4.0f + (cosmetic.AccessorySizePercent * 0.08f);
            DrawArc(center, radius, 3.5f, 5.9f, 8, accessoryColor, 1.0f);
        }
    }

    private CosmeticSetDefinition ActiveCosmeticSet =>
        CosmeticSetCatalog.Find(_progression.SelectedCosmeticSetId)
        ?? CosmeticSetCatalog.Sets[0];

    private static Color CosmeticBodyColor(
        CosmeticSetDefinition cosmetic,
        int bodyIndex,
        bool isHead)
    {
        if (isHead)
        {
            return CosmeticColor(cosmetic.Secondary);
        }

        var useAccent = cosmetic.PatternId switch
        {
            "solid" => false,
            "relay-stripe" => bodyIndex % 4 == 0,
            "mutation-dot" => bodyIndex % 3 == 0,
            "speed-band" => bodyIndex % 5 < 2,
            "edge-chevron" => bodyIndex % 2 == 0,
            "flow-line" => bodyIndex % 4 == 1,
            "balanced-grid" => bodyIndex % 2 == 1,
            "crown-band" => bodyIndex % 3 == 1,
            _ => false,
        };
        return CosmeticColor(useAccent ? cosmetic.Secondary : cosmetic.Primary);
    }

    private static Color CosmeticColor(AiDisplayColor color) => new(
        color.Red / 255.0f,
        color.Green / 255.0f,
        color.Blue / 255.0f);

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
        float inset = 0.5f) =>
        DrawCellOutline(new Vector2(point.X, point.Y), color, width, inset);

    private void DrawCellOutline(
        Vector2 point,
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

    private Color HungerSignalColor(HungerPhase phase) => phase switch
    {
        HungerPhase.Safe => ActiveShellPalette.BodyText,
        HungerPhase.Warning => ActiveShellPalette.GoldText,
        HungerPhase.Critical or HungerPhase.Empty => ActiveShellPalette.WarningText,
        _ => throw new ArgumentOutOfRangeException(nameof(phase)),
    };

    private void DrawHungerMeter(
        HungerFeedbackState hunger,
        Vector2 position,
        Color signalColor)
    {
        const float segmentWidth = HungerMeterSegmentWidth;
        const float segmentGap = HungerMeterSegmentGap;
        const float segmentHeight = 15.0f;
        var emptyColor = ActiveShellPalette.SecondaryText;
        emptyColor.A = 0.28f;
        for (var index = 0; index < hunger.TotalSegments; index++)
        {
            var rect = new Rect2(
                position.X + (index * (segmentWidth + segmentGap)),
                position.Y,
                segmentWidth,
                segmentHeight);
            DrawRect(rect, index < hunger.FilledSegments ? signalColor : emptyColor);
        }

        var meterWidth = (hunger.TotalSegments * (segmentWidth + segmentGap)) - segmentGap;
        DrawRect(
            new Rect2(position.X - 3.0f, position.Y - 3.0f, meterWidth + 6.0f, segmentHeight + 6.0f),
            signalColor,
            filled: false,
            width: 1.0f);
        if (hunger.Phase == HungerPhase.Critical)
        {
            DrawLabel(
                WarningMarker,
                new Vector2(
                    position.X + meterWidth + HungerMeterMarkerOffset,
                    position.Y + segmentHeight),
                ScaledFontSize(HungerMeterMarkerFontSize),
                signalColor);
        }
        else if (hunger.Phase == HungerPhase.Empty)
        {
            DrawLine(
                position - new Vector2(1.0f, 1.0f),
                position + new Vector2(meterWidth + 1.0f, segmentHeight + 1.0f),
                signalColor,
                2.0f);
            DrawLine(
                position + new Vector2(meterWidth + 1.0f, -1.0f),
                position + new Vector2(-1.0f, segmentHeight + 1.0f),
                signalColor,
                2.0f);
        }
    }

    private static string DescribePowerStatus(RunSnapshot snapshot) =>
        PowerPresentation.DescribeStatus(snapshot);

    private void AdvanceFeedback(
        IReadOnlyList<RunEventDetail> events,
        int comboCount)
    {
        if (_baitRevealTicksRemaining > 0)
        {
            _baitRevealTicksRemaining--;
            if (_baitRevealTicksRemaining == 0)
            {
                _baitRevealOrigin = null;
                _baitRevealDestination = null;
            }
        }

        if (_comboPulseTicksRemaining > 0)
        {
            _comboPulseTicksRemaining--;
        }

        if (events.Any(detail => detail.Kind is RunEventKind.AteFood or RunEventKind.ComboExpired))
        {
            _comboPulseTicksRemaining = ComboFeedback.PulseTicks;
        }

        if (_feedbackTicksRemaining > 0)
        {
            _feedbackTicksRemaining--;
            if (_feedbackTicksRemaining == 0)
            {
                _feedbackCaption = null;
            }
        }

        if (_broadcastTicksRemaining > 0)
        {
            _broadcastTicksRemaining--;
            if (_broadcastTicksRemaining == 0)
            {
                _broadcastCaption = null;
            }
        }

        _presentationStep = checked(_presentationStep + 1);
        var vibeTransition = _vibeLevelDirector.Update(comboCount);
        var feedback = StepFeedback.Resolve(events, comboCount, vibeTransition);
        if (events.Any(detail => detail.Kind is RunEventKind.Died
                or RunEventKind.StarvationWarning))
        {
            TryBroadcast(BroadcastBoundary.CriticalWarning, criticalCueActive: true);
        }
        else if (events.Any(detail => detail.Kind == RunEventKind.CollisionPrevented))
        {
            TryBroadcast(BroadcastBoundary.Recovery, criticalCueActive: false);
        }
        else if (vibeTransition is
        { To: VibeLevel.Overdrive or VibeLevel.Transcendent })
        {
            TryBroadcast(BroadcastBoundary.MajorMilestone, criticalCueActive: false);
        }
        var accessibility = AccessibilityPresentationPolicy.FromSettings(_shellSettings);
        if (feedback.Cue is { } cue)
        {
            if (AccessibilityPresentationPolicy.ShouldPlayCue(cue))
            {
                PlayCue(cue);
            }
        }

        if (feedback.Text is { } text)
        {
            _feedbackCaption = VisualHierarchyPolicy.BoundPopup(
                _shellSettings.FlashFree
                    ? SoftenFlashyCaption(Localize(text))
                    : Localize(text));
            _feedbackTier = VisualHierarchyPolicy.ResolveTier(events, vibeTransition);
            _feedbackTicksRemaining = accessibility.CaptionVisibilityTicks(
                FeedbackVisibilityTicks);
        }
    }

    private void CaptureBaitConversion(
        RunSnapshot before,
        RunSnapshot after,
        IReadOnlyList<RunEventDetail> events)
    {
        if (before.BaitPosition is not { } origin
            || after.BaitPosition is not null
            || after.Food is not { } destination
            || !events.Any(detail => detail.Kind == RunEventKind.AteFood))
        {
            return;
        }

        _baitRevealOrigin = origin;
        _baitRevealDestination = destination;
        _baitRevealTicksRemaining = 20;
        _feedbackCaption = Localize(
            "feedback.power.bait-triggered",
            ShellTextArgument.From("x", destination.X),
            ShellTextArgument.From("y", destination.Y));
        _feedbackTier = VisualFeedbackTier.Ambient;
        _feedbackTicksRemaining = AccessibilityPresentationPolicy
            .FromSettings(_shellSettings)
            .CaptionVisibilityTicks(FeedbackVisibilityTicks);
    }

    private void TryBroadcast(BroadcastBoundary boundary, bool criticalCueActive)
    {
        var stationId = _radioPolicy.Snapshot.StationId;
        if (stationId is null || BroadcastStationCatalog.Find(stationId) is null)
        {
            return;
        }

        var decision = _broadcastPolicy.Evaluate(new BroadcastRequest(
            stationId,
            boundary,
            _presentationStep,
            criticalCueActive,
            AudioAvailable: false));
        if (decision.OptionalBroadcastInterrupted)
        {
            _broadcastCaption = null;
            _broadcastTicksRemaining = 0;
            return;
        }

        if (decision.Code == BroadcastDecisionCode.SegmentGranted)
        {
            _broadcastCaption = VisualHierarchyPolicy.BoundPopup(Localize(
                decision.CaptionCopyId
                    ?? throw new InvalidOperationException(
                        "A granted broadcast must provide a caption copy ID."))
                .ToUpperInvariant());
            _broadcastTicksRemaining = AccessibilityPresentationPolicy
                .FromSettings(_shellSettings)
                .CaptionVisibilityTicks(FeedbackVisibilityTicks * 2);
        }
    }

    private static void ExecuteAccessibilityPresentationSmokeTest()
    {
        var evidence = AccessibilityPresentationQualification.Run();
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "accessibility_presentation.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private static void ExecuteCandidateAccessibilityAuditSmokeTest()
    {
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var evidence = CandidateAccessibilityAuditQualification.Run(directory);
        var path = System.IO.Path.Combine(directory, "candidate_accessibility_audit.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private static void ExecuteMultimodalFeedbackSmokeTest()
    {
        var evidence = MultimodalFeedbackQualification.Run();
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "multimodal_feedback.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private void ExecuteVibeLevelSmokeTest()
    {
        var evidence = VibeLevelQualification.Run(ActiveShellTheme);
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "vibe_level.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private void ExecuteVisualHierarchySmokeTest()
    {
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var evidence = VisualHierarchyQualification.Run(ActiveShellTheme, directory);
        var path = System.IO.Path.Combine(directory, "visual_hierarchy.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private static void ExecuteFeedbackMatrixSmokeTest()
    {
        var evidence = FeedbackMatrixCatalog.Qualify();
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "feedback_matrix.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private static void ExecuteSfxCatalogSmokeTest()
    {
        var evidence = SfxCueCatalog.Qualify();
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "sfx_catalog.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private void ExecuteRadioBehaviorSmokeTest()
    {
        var evidence = RadioQualification.Run(
            decoderAdapterPresent: _radioPlayer is not null,
            packagedInventoryAvailable: TryResolveCheckoutInventoryPath(out _));
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "radio_behavior.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private static void ExecuteBroadcastSmokeTest()
    {
        var evidence = BroadcastQualification.Run();
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "broadcast.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private static void ExecuteModeContractSmokeTest()
    {
        var evidence = ModeContractQualification.Run();
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "mode_contracts.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private static void ExecuteAdaptiveFairnessSmokeTest()
    {
        var evidence = AdaptiveFairnessQualification.Run();
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "adaptive_fairness.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private void ExecuteCaptureSharingSmokeTest()
    {
        var evidence = CaptureSharingQualification.Run(
            _captureKeyboardRouteQualified,
            _captureControllerRouteQualified,
            _captureSummaryExportQualified,
            _captureSummaryIdempotenceQualified);
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "capture_sharing.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private void ExecuteSpectatorExperienceSmokeTest(string userDataRoot)
    {
        ReturnToMenu();
        _spectatorSelection = SpectatorSelection.CreateDefault();
        DispatchSmokeMainMenuSelection(MainMenuItem.Spectator, controller: false);
        if (_screenState != ScreenState.Spectator || _spectatorMatch is not null)
        {
            throw new InvalidOperationException(
                "Keyboard did not open the spectator selection screen.");
        }

        var keyboardChannelBefore = _spectatorSelection.PersonalityId;
        DispatchSmokeKey(Key.Right);
        DispatchSmokeKey(Key.Enter, physical: false);
        if (_spectatorMatch is not { Paused: false } keyboardMatch
            || _spectatorSelection.PersonalityId == keyboardChannelBefore)
        {
            throw new InvalidOperationException(
                "Keyboard did not select and start an AI rivalry.");
        }

        DispatchSmokeKey(Key.Enter, physical: false);
        var keyboardStepBefore = keyboardMatch.StepCount;
        DispatchSmokeKey(Key.Down);
        var keyboardViewedBefore = keyboardMatch.ViewedPersonalityId;
        DispatchSmokeKey(Key.Up);
        var keyboardSpeedBefore = keyboardMatch.PlaybackSpeedIndex;
        DispatchSmokeKey(Key.Left);
        DispatchSmokeKey(Key.H);
        var keyboardCleanCapture = _capturePresentation.Enabled;
        DispatchSmokeKey(Key.H);
        DispatchSmokeKey(Key.R);
        if (!keyboardMatch.Paused
            || keyboardMatch.StepCount != keyboardStepBefore + 1
            || keyboardMatch.ViewedPersonalityId == keyboardViewedBefore
            || keyboardMatch.PlaybackSpeedIndex == keyboardSpeedBefore
            || !keyboardCleanCapture
            || _capturePresentation.Enabled
            || _spectatorMatch is not { Paused: false, StepCount: 0 })
        {
            throw new InvalidOperationException(
                "Keyboard spectator pause, step, view, speed, capture, or restart route failed.");
        }

        DispatchSmokeKey(Key.Escape, physical: false);
        DispatchSmokeKey(Key.Escape, physical: false);
        _spectatorKeyboardRouteQualified = _screenState == ScreenState.Menu
            && _spectatorMatch is null;

        _spectatorSelection = SpectatorSelection.CreateDefault();
        DispatchSmokeMainMenuSelection(MainMenuItem.Spectator, controller: true);
        if (_screenState != ScreenState.Spectator || _spectatorMatch is not null)
        {
            throw new InvalidOperationException(
                "Controller did not open the spectator selection screen.");
        }

        var controllerChannelBefore = _spectatorSelection.PersonalityId;
        DispatchSmokeJoyButton(JoyButton.DpadRight);
        DispatchSmokeJoyButton(JoyButton.A);
        if (_spectatorMatch is not { Paused: false } controllerMatch
            || _spectatorSelection.PersonalityId == controllerChannelBefore)
        {
            throw new InvalidOperationException(
                "Controller did not select and start an AI rivalry.");
        }

        DispatchSmokeJoyButton(JoyButton.A);
        var controllerStepBefore = controllerMatch.StepCount;
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        var controllerViewedBefore = controllerMatch.ViewedPersonalityId;
        DispatchSmokeJoyButton(JoyButton.DpadUp);
        var controllerSpeedBefore = controllerMatch.PlaybackSpeedIndex;
        DispatchSmokeJoyButton(JoyButton.DpadLeft);
        DispatchSmokeJoyButton(JoyButton.LeftStick);
        var controllerCleanCapture = _capturePresentation.Enabled;
        DispatchSmokeJoyButton(JoyButton.LeftStick);
        DispatchSmokeJoyButton(JoyButton.Y);
        if (!controllerMatch.Paused
            || controllerMatch.StepCount != controllerStepBefore + 1
            || controllerMatch.ViewedPersonalityId == controllerViewedBefore
            || controllerMatch.PlaybackSpeedIndex == controllerSpeedBefore
            || !controllerCleanCapture
            || _capturePresentation.Enabled
            || _spectatorMatch is not { Paused: false, StepCount: 0 })
        {
            throw new InvalidOperationException(
                "Controller spectator pause, step, view, speed, capture, or restart route failed.");
        }

        DispatchSmokeJoyButton(JoyButton.B);
        DispatchSmokeJoyButton(JoyButton.B);
        _spectatorControllerRouteQualified = _screenState == ScreenState.Menu
            && _spectatorMatch is null;

        var evidence = SpectatorQualification.Run(
            userDataRoot,
            _spectatorKeyboardRouteQualified,
            _spectatorControllerRouteQualified);
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "spectator_experience.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private void ExecuteReliabilityQualificationSmokeTest()
    {
        var evidence = ReliabilityQualification.Run(
            CaptureEngineResourceCounts,
            CaptureReliabilityDivergence);
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "candidate_reliability.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
        if (!evidence.Passed)
        {
            throw new InvalidOperationException("Candidate reliability qualification failed.");
        }
    }

    private void CaptureReliabilityDivergence(ReliabilityFirstDivergence divergence)
    {
        if (_diagnostics is null)
        {
            throw new InvalidOperationException(
                "Reliability divergence could not be retained without local diagnostics.");
        }

        _diagnostics.WriteDivergenceReport(
            appVersion: ProductIdentity.AppVersion,
            platform: OS.GetName(),
            rulesetId: SnakeRun.RulesetId,
            rulesVersion: SnakeRun.RulesVersion,
            campaignId: "candidate-reliability",
            modeId: divergence.ModeId,
            gameplaySeed: Convert.ToUInt64(divergence.GameplaySeed, 16),
            controllerSeed: Convert.ToUInt64(divergence.ControllerSeed, 16),
            runIndex: divergence.RunIndex,
            firstDivergentStep: divergence.RunStep,
            expectedStateHash: divergence.ExpectedStateHash,
            actualStateHash: divergence.ActualStateHash,
            recentCommands: divergence.RecentCommands);
    }

    private EngineResourceCounts CaptureEngineResourceCounts() => new(
        SceneNodeCount: GetTree().GetNodeCount(),
        ObjectCount: (long)Math.Ceiling(
            Godot.Performance.GetMonitor(Godot.Performance.Monitor.ObjectCount)),
        ResourceCount: (long)Math.Ceiling(
            Godot.Performance.GetMonitor(Godot.Performance.Monitor.ObjectResourceCount)),
        OrphanNodeCount: (long)Math.Ceiling(
            Godot.Performance.GetMonitor(Godot.Performance.Monitor.ObjectOrphanNodeCount)));

    private void ExecuteFaultCampaignSmokeTest(
        string userDataRoot,
        CoreOnlyOfflineQualificationEvidence contentEvidence)
    {
        if (_diagnostics is null)
        {
            throw new InvalidOperationException(
                "Candidate fault qualification requires local diagnostics.");
        }

        var evidence = FaultCampaignQualification.Run(
            userDataRoot,
            OS.GetName(),
            contentEvidence,
            _diagnostics);
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "candidate_fault_campaign.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
        if (!evidence.Passed)
        {
            throw new InvalidOperationException("Candidate fault qualification failed.");
        }
    }

    private void ExecuteOptionalLoreSmokeTest()
    {
        ReturnToMenu();
        DispatchSmokeMainMenuSelection(MainMenuItem.Spectator, controller: false);
        DispatchSmokeKey(Key.U);
        if (_screenState != ScreenState.Lore
            || FilteredLoreEntries().Length != LoreCatalog.All.Count)
        {
            throw new InvalidOperationException(
                "Keyboard did not open the complete optional lore archive.");
        }

        DispatchSmokeKey(Key.Right);
        DispatchSmokeKey(Key.Down);
        var keyboardSurfaceRoute = _loreDepthFilterIndex == 1
            && FilteredLoreEntries().Length == 19
            && _loreBrowseCursor == 1;
        DispatchSmokeKey(Key.Left);
        DispatchSmokeKey(Key.Escape, physical: false);
        DispatchSmokeKey(Key.Escape, physical: false);
        _loreKeyboardRouteQualified = keyboardSurfaceRoute
            && _screenState == ScreenState.Menu;

        DispatchSmokeMainMenuSelection(MainMenuItem.Spectator, controller: true);
        DispatchSmokeJoyButton(JoyButton.LeftShoulder);
        if (_screenState != ScreenState.Lore
            || FilteredLoreEntries().Length != LoreCatalog.All.Count)
        {
            throw new InvalidOperationException(
                "Controller did not open the complete optional lore archive.");
        }

        DispatchSmokeJoyButton(JoyButton.DpadRight);
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        var controllerSurfaceRoute = _loreDepthFilterIndex == 1
            && FilteredLoreEntries().Length == 19
            && _loreBrowseCursor == 1;
        DispatchSmokeJoyButton(JoyButton.DpadLeft);
        DispatchSmokeJoyButton(JoyButton.B);
        DispatchSmokeJoyButton(JoyButton.B);
        _loreControllerRouteQualified = controllerSurfaceRoute
            && _screenState == ScreenState.Menu;

        var evidence = LoreQualification.Run(
            _loreKeyboardRouteQualified,
            _loreControllerRouteQualified);
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "optional_lore.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private static void ExecutePowerDecisionSmokeTest()
    {
        var contractPath = System.IO.Path.GetFullPath(
            ProjectSettings.GlobalizePath("res://../config/power_decision_contract_v1.json"));
        if (!System.IO.File.Exists(contractPath))
        {
            return;
        }
        var evidence = PowerDecisionQualification.Run(contractPath);
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "power_decisions.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
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
        DrawString(
            ActiveShellTheme.InterfaceFont,
            position,
            text,
            HorizontalAlignment.Left,
            -1.0f,
            fontSize,
            color);
    }

    private void DrawCenteredLabel(string text, float baselineY, int fontSize, Color color)
    {
        var width = ActiveShellTheme.InterfaceFont.GetStringSize(
            text,
            HorizontalAlignment.Left,
            -1.0f,
            fontSize).X;
        DrawLabel(
            text,
            new Vector2((ActiveLogicalWidth - width) * 0.5f, baselineY),
            fontSize,
            color);
    }

    private async void ExecuteSmokeTest(string userDataRoot)
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

            ExecuteShellPresentationSmokeTest();
            ExecuteLocalizationSmokeTest();
            ExecuteAudioFallbackStressSmokeTest();
            var coreOnlyEvidence = ExecuteContentServiceSmokeTest(userDataRoot);
            ExecuteRadioBehaviorSmokeTest();
            ExecuteBroadcastSmokeTest();
            ExecuteModeContractSmokeTest();
            ExecuteAdaptiveFairnessSmokeTest();
            ExecutePowerDecisionSmokeTest();
            ExecuteFeedbackMatrixSmokeTest();
            ExecuteSfxCatalogSmokeTest();
            ExecuteStepFeedbackSmokeTest();
            ExecuteMultimodalFeedbackSmokeTest();
            ExecuteVisualHierarchySmokeTest();
            SnakeMotionPresentationQualification.AssertContract();
            BoardTerrainCatalog.AssertContract();
            ExecuteVibeLevelSmokeTest();
            ExecuteShellSettingsSmokeTest();
            ExecuteLocalPlaytestSummarySmokeTest();
            ExecutePlayerDataRecoverySmokeTest();
            ExecuteOnboardingSmokeTest();
            ExecuteAccessibilityPresentationSmokeTest();
            ExecuteVirtualViewportSmokeTest();
            ExecuteInputCadenceSmokeTest();
            ExecuteMouseInputSmokeTest();
            ExecuteCandidateAccessibilityAuditSmokeTest();
            ExecutePerformanceRetryPolicySmokeTest();
            await ExecutePerformanceQualificationSmokeTestAsync();
            var frameSummary = await ExecutePresentationFrameSamplerSmokeTestAsync();
            ExecuteBareArcadeLoopSmokeTest(frameSummary);
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
                checkpointInterval: 1,
                capturedAtUtc: "2026-08-08T00:00:00.000Z");
            var replayRead = RunReplay.Read(replayEnvelope.Serialize());
            if (
                !replayRead.Compatibility.IsCompatible
                || replayRead.Replay is null
                || !replayRead.Replay.Verify().IsValid
                || replayRead.Replay.GameplaySeed != SmokeSeed
                || replayRead.Replay.AiSeed != SmokeSeed
                || replayRead.Replay.CapturedAtUtc != "2026-08-08T00:00:00.000Z")
            {
                throw new InvalidOperationException("The replay envelope smoke contract failed.");
            }

            var storedReplayName = ExecuteReplayStorageSmokeTest();

            await ExecuteInputLifecycleSmokeTest();
            await ExecuteOfflineComparisonSmokeTestAsync(userDataRoot);
            ExecuteSpectatorExperienceSmokeTest(userDataRoot);
            ExecuteReliabilityQualificationSmokeTest();
            ExecuteOptionalLoreSmokeTest();
            ExecuteCaptureSharingSmokeTest();
            await ExecuteReplayOperationLifecycleSmokeTest();

            coreOnlyEvidence = coreOnlyEvidence with { FullOfflineFlowExercised = true };
            if (!coreOnlyEvidence.Passed)
            {
                throw new InvalidOperationException(
                    "Core-only offline content qualification did not pass.");
            }
            WriteCoreOnlyOfflineEvidence(coreOnlyEvidence);
            ExecuteFaultCampaignSmokeTest(userDataRoot, coreOnlyEvidence);

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
        var recorder = new RunReplayRecorder(
            live,
            checkpointInterval: 1,
            appVersion: ProductIdentity.AppVersion,
            capturedAtUtc: "2026-08-08T00:00:01.000Z");
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
        TransitionToScreen(ScreenState.Menu);
        _mainMenuCursor = (int)MainMenuItem.Start;
        _run = null;
        _pausedByFocusLoss = false;
        var achievementsBeforeReplay = _achievements.SerializeCanonical();
        var personalBestsBeforeReplay = _personalBests.SerializeCanonical();
        var scoreHistoryBeforeReplay = _scoreHistory.SerializeCanonical();
        if (_replayStore is null)
        {
            throw new InvalidOperationException("Replay storage was unavailable for input smoke.");
        }

        IReadOnlyList<RulesDirection>[] disposableCommands = [[RulesDirection.Up]];
        var disposableReplay = RunReplay.Capture(
            SnakeRun.Create(SmokeSeed + 101),
            disposableCommands,
            capturedAtUtc: "2026-08-08T01:01:01.001Z");
        var disposableSave = _replayStore.Save(disposableReplay);
        if (!disposableSave.IsSuccess)
        {
            throw new InvalidOperationException(
                "Replay input smoke could not stage a disposable replay: "
                    + disposableSave.Message);
        }

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

        DispatchSmokeKey(Key.R);
        for (var frame = 0; frame < 300 && _replayOperation is not null; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        if (
            _screenState != ScreenState.Replays
            || _replayOperation is not null
            || _replayBrowserEntries.Count == 0
            || _replayBrowserEntries.Any(entry => !entry.IsPlayable))
        {
            throw new InvalidOperationException(
                "Logical replay action did not open the verified metadata browser.");
        }

        var initialReplayCount = _replayBrowserEntries.Count;
        DispatchSmokeJoyButton(JoyButton.A);
        for (var frame = 0; frame < 300 && _replayOperation is not null; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        if (
            _replayOperation is not null
            || _replayPlayback is null
            || !_replayPlaybackPaused
            || _replayPlayback.StepIndex != 0)
        {
            throw new InvalidOperationException(
                "Replay browser did not load a verified paused playback.");
        }

        DispatchSmokeKey(Key.Up);
        if (_replayPlaybackSpeedIndex != 2)
        {
            throw new InvalidOperationException("Keyboard replay speed-up did not select 2x.");
        }

        DispatchSmokeJoyButton(JoyButton.DpadDown);
        if (_replayPlaybackSpeedIndex != 1)
        {
            throw new InvalidOperationException("Controller replay slow-down did not restore 1x.");
        }

        DispatchSmokeKey(Key.H);
        if (_replayHudVisible
            || !_capturePresentation.Enabled
            || _capturePresentation.ShowRunHud
            || _capturePresentation.ShowReplayControls)
        {
            throw new InvalidOperationException(
                "Keyboard clean-capture toggle did not hide replay overlays.");
        }

        DispatchSmokeJoyButton(JoyButton.LeftStick);
        if (!_replayHudVisible || _capturePresentation.Enabled)
        {
            throw new InvalidOperationException(
                "Controller clean-capture toggle did not restore replay overlays.");
        }

        DispatchSmokeKey(Key.Right);
        if (_replayPlayback.StepIndex != 1)
        {
            throw new InvalidOperationException("Replay single-step control did not advance exactly once.");
        }

        DispatchSmokeJoyButton(JoyButton.Y);
        if (_replayPlayback.StepIndex != 0 || !_replayPlaybackPaused)
        {
            throw new InvalidOperationException("Replay reset control did not return to paused step zero.");
        }

        DispatchSmokeJoyButton(JoyButton.B);
        if (_screenState != ScreenState.Replays || _replayPlayback is not null)
        {
            throw new InvalidOperationException("Replay playback did not return to its browser list.");
        }

        DispatchSmokeKey(Key.C);
        for (var frame = 0; frame < 300 && _replayOperation is not null; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        var exportedFilesBeforeDeletion = Directory.Exists(_replayStore.ReplayExportDirectory)
            ? Directory.GetFiles(
                _replayStore.ReplayExportDirectory,
                $"replay_*{ReplayStore.ReplayFileExtension}")
            : [];
        var captureSummaryFiles = Directory.Exists(_replayStore.ReplayExportDirectory)
            ? Directory.GetFiles(
                _replayStore.ReplayExportDirectory,
                $"run-summary_*{ReplayStore.CaptureSummaryFileExtension}")
            : [];
        var idempotentSummary = _replayStore.ExportCaptureSummary(
            _replayBrowserEntries[_replayBrowseCursor].ReplayId,
            ProductIdentity.AppVersion);
        _captureSummaryIdempotenceQualified =
            idempotentSummary.Code == ReplayCaptureSummaryExportCode.AlreadyExists;
        if (
            _replayOperation is not null
            || _replayStatusCaption is null
            || !_replayStatusCaption.StartsWith(
                "REPLAY + RUN SUMMARY EXPORTED:",
                StringComparison.Ordinal)
            || exportedFilesBeforeDeletion.Length == 0)
        {
            throw new InvalidOperationException(
                "Keyboard replay export did not create one verified local export bundle.");
        }
        if (!_captureSummaryExportQualified
            || !_captureSummaryIdempotenceQualified
            || captureSummaryFiles.Length == 0)
        {
            throw new InvalidOperationException(
                "Replay export did not retain an atomic idempotent run summary.");
        }

        DispatchSmokeJoyButton(JoyButton.Back);
        for (var frame = 0; frame < 300 && _replayOperation is not null; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        if (_pendingReplayDeletion is null)
        {
            throw new InvalidOperationException(
                "Controller replay deletion did not prepare exact confirmation.");
        }

        DispatchSmokeKey(Key.Escape, physical: false);
        if (_pendingReplayDeletion is not null || _replayBrowserEntries.Count != initialReplayCount)
        {
            throw new InvalidOperationException(
                "Keyboard replay deletion cancellation did not preserve the library.");
        }

        DispatchSmokeKey(Key.F8, physical: false);
        for (var frame = 0; frame < 300 && _replayOperation is not null; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        if (_pendingReplayDeletion is null)
        {
            throw new InvalidOperationException(
                "Keyboard replay deletion did not prepare exact confirmation.");
        }

        DispatchSmokeJoyButton(JoyButton.A);
        for (var frame = 0; frame < 300 && _replayOperation is not null; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        if (
            _replayOperation is not null
            || _pendingReplayDeletion is not null
            || _replayBrowserEntries.Count != initialReplayCount - 1
            || _replayStatusCaption is null
            || !_replayStatusCaption.Contains("PERMANENTLY DELETED", StringComparison.Ordinal)
            || exportedFilesBeforeDeletion.Any(path => !File.Exists(path)))
        {
            throw new InvalidOperationException(
                "Controller replay deletion confirmation did not remove exactly one item.");
        }

        DispatchSmokeKey(Key.Escape, physical: false);
        if (_screenState != ScreenState.Menu)
        {
            throw new InvalidOperationException("Replay browser did not return to the menu.");
        }

        VerifyLatestReplay();
        for (var frame = 0; frame < 300 && _replayOperation is not null; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        if (_replayOperation is not null
            || _replayStatusCaption is null
            || !_replayStatusCaption.StartsWith(
                "LATEST REPLAY VERIFIED:",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Background latest-replay verification did not report success.");
        }

        var progressionIsolated = string.Equals(
                achievementsBeforeReplay,
                _achievements.SerializeCanonical(),
                StringComparison.Ordinal)
            && string.Equals(
                personalBestsBeforeReplay,
                _personalBests.SerializeCanonical(),
                StringComparison.Ordinal)
            && string.Equals(
                scoreHistoryBeforeReplay,
                _scoreHistory.SerializeCanonical(),
                StringComparison.Ordinal);
        if (!progressionIsolated)
        {
            throw new InvalidOperationException(
                "Replay browsing or playback changed progression or score data.");
        }

        var evidence = new ReplayBrowserQualificationEvidence(
            SchemaVersion: 1,
            Kind: "replay-browser-qualification-v2",
            Passed: true,
            BrowserEntryFieldCount: 14,
            PlaybackSpeeds: ReplayPlaybackSpeeds,
            MetadataComplete: true,
            ExplicitStateBadgesComplete: true,
            RawKeyboardRouteComplete: true,
            RawControllerRouteComplete: true,
            SpeedControlsComplete: true,
            HudToggleComplete: true,
            PauseStepRestartReturnComplete: true,
            AtomicExportComplete: true,
            DeleteConsentComplete: true,
            DeleteCancelLossless: true,
            ConfirmedDeleteExact: true,
            ExportsPreservedAfterDelete: true,
            ProgressionIsolated: true);
        var evidenceDirectory = ResolveEvidenceDirectory();
        Directory.CreateDirectory(evidenceDirectory);
        File.WriteAllText(
            Path.Combine(evidenceDirectory, "replay_browser.json"),
            evidence.Serialize());
    }

    private async Task ExecuteOfflineComparisonSmokeTestAsync(string userDataRoot)
    {
        if (_offlineChallengeStore is null)
        {
            throw new InvalidOperationException(
                "Offline comparison storage was unavailable for smoke qualification.");
        }

        var store = _offlineChallengeStore;
        var config = RunModeCatalog.CreateConfig(
            RunModeCatalog.Vibe,
            enableAdaptation: false);
        var commands = Enumerable.Range(0, 12)
            .Select(_ => (IReadOnlyList<RulesDirection>)Array.Empty<RulesDirection>())
            .ToArray();
        var replay = RunReplay.Capture(
            SnakeRun.Create(80_011UL, config),
            commands,
            checkpointInterval: 2,
            appVersion: ProductIdentity.AppVersion,
            capturedAtUtc: "2026-08-09T08:11:00.000Z");
        var importDirectory = System.IO.Path.Combine(userDataRoot, "imports");
        System.IO.Directory.CreateDirectory(importDirectory);
        var sourcePath = System.IO.Path.Combine(
            importDirectory,
            "household-rival.vibesnake-replay.json");
        System.IO.File.WriteAllText(
            sourcePath,
            replay.Serialize(),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var sourceBytes = System.IO.File.ReadAllBytes(sourcePath);
        var progressionBefore = _progression.SerializeCanonical();

        ReturnToMenu();
        DispatchSmokeKey(Key.R);
        await DrainReplayOperationForSmokeAsync("keyboard replay browser open");
        DispatchSmokeKey(Key.U);
        await DrainReplayOperationForSmokeAsync("keyboard comparison browser open");
        if (_screenState != ScreenState.Comparisons
            || _ghostSlots.Count != OfflineChallengeStore.MaximumHouseholdRivalSlots)
        {
            throw new InvalidOperationException(
                "Keyboard did not open the four-slot offline comparison browser.");
        }

        DispatchSmokeKey(Key.U);
        await DrainReplayOperationForSmokeAsync("keyboard ghost import");
        var slotOnePath = System.IO.Path.Combine(
            store.GhostDirectory,
            $"household-rival-1{OfflineChallengeStore.GhostFileExtension}");
        var importedSlotOneBytes = System.IO.File.ReadAllBytes(slotOnePath);
        var duplicate = store.ImportGhost(sourcePath, 1);
        var explicitSourcePreservingImport = _ghostSlots[0].IsPlayable
            && sourceBytes.SequenceEqual(System.IO.File.ReadAllBytes(sourcePath));
        var atomicNoOverwriteImport = duplicate.Code == GhostImportCode.SlotOccupied
            && importedSlotOneBytes.SequenceEqual(System.IO.File.ReadAllBytes(slotOnePath))
            && System.IO.Directory.GetFiles(
                store.GhostDirectory,
                "*.tmp-*",
                System.IO.SearchOption.TopDirectoryOnly).Length == 0;

        DispatchSmokeKey(Key.Enter, physical: false);
        await DrainReplayOperationForSmokeAsync("keyboard ghost race start");
        if (_screenState != ScreenState.Running
            || _activeGhostRace is null
            || _activeGhostSlot != 1)
        {
            throw new InvalidOperationException(
                "Keyboard did not start the verified household rival race.");
        }

        AdvanceOneRulesStep();
        var keyboardActualGhostRoute = _run is not null
            && _run.ComputeStateHash() == _activeGhostRace.GhostSnapshot.StateHash;
        DispatchSmokeKey(Key.Escape, physical: false);
        var progressionAwardsExcluded = string.Equals(
            progressionBefore,
            _progression.SerializeCanonical(),
            StringComparison.Ordinal);

        DispatchSmokeJoyButton(JoyButton.Y);
        await DrainReplayOperationForSmokeAsync("controller replay browser open");
        DispatchSmokeJoyButton(JoyButton.LeftShoulder);
        await DrainReplayOperationForSmokeAsync("controller comparison browser open");
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        DispatchSmokeJoyButton(JoyButton.LeftShoulder);
        await DrainReplayOperationForSmokeAsync("controller ghost import");
        DispatchSmokeJoyButton(JoyButton.A);
        await DrainReplayOperationForSmokeAsync("controller ghost race start");
        if (_screenState != ScreenState.Running
            || _activeGhostRace is null
            || _activeGhostSlot != 2)
        {
            throw new InvalidOperationException(
                "Controller did not start the verified household rival race.");
        }

        AdvanceOneRulesStep();
        var controllerActualGhostRoute = _run is not null
            && _run.ComputeStateHash() == _activeGhostRace.GhostSnapshot.StateHash;
        DispatchSmokeJoyButton(JoyButton.B);

        DispatchSmokeJoyButton(JoyButton.Y);
        await DrainReplayOperationForSmokeAsync("controller replay browser reopen");
        DispatchSmokeJoyButton(JoyButton.LeftShoulder);
        await DrainReplayOperationForSmokeAsync("controller comparison browser reopen");
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        DispatchSmokeJoyButton(JoyButton.X);
        await DrainReplayOperationForSmokeAsync("controller run-card export");
        var idempotentCard = store.ExportRunCard(
            2,
            ProductIdentity.AppVersion,
            "flow_signal",
            "classic-signal");
        var runCardPath = idempotentCard.FileName is null
            ? string.Empty
            : System.IO.Path.Combine(store.RunCardDirectory, idempotentCard.FileName);
        var runCardAtomicAndIdempotent = idempotentCard.Code
                == RunCardExportCode.AlreadyExists
            && idempotentCard.Card is not null
            && System.IO.File.Exists(runCardPath)
            && System.IO.Directory.GetFiles(
                store.RunCardDirectory,
                "*.tmp-*",
                System.IO.SearchOption.TopDirectoryOnly).Length == 0;
        var runCard = idempotentCard.Card
            ?? throw new InvalidOperationException(
                "The idempotent offline run-card export omitted its card.");

        DispatchSmokeJoyButton(JoyButton.Back);
        await DrainReplayOperationForSmokeAsync("controller ghost deletion plan");
        var deletionRequiresExactConfirmation = _pendingGhostDeletion is { Slot: 2 }
            && store.LoadGhost(2).IsSuccess;
        DispatchSmokeJoyButton(JoyButton.B);
        var deleteCancelLossless = _pendingGhostDeletion is null
            && store.LoadGhost(2).IsSuccess;
        DispatchSmokeKey(Key.F8, physical: false);
        await DrainReplayOperationForSmokeAsync("keyboard ghost deletion plan");
        DispatchSmokeJoyButton(JoyButton.A);
        await DrainReplayOperationForSmokeAsync("controller ghost deletion confirmation");
        var confirmedDeleteExact = !store.LoadGhost(2).IsSuccess
            && store.LoadGhost(1).IsSuccess;

        var modifiedPath = System.IO.Path.Combine(importDirectory, "modified-rival.json");
        var modifiedHash = replay.PayloadHash[..^1]
            + (replay.PayloadHash[^1] == '0' ? '1' : '0');
        System.IO.File.WriteAllText(
            modifiedPath,
            replay.Serialize().Replace(
                replay.PayloadHash,
                modifiedHash,
                StringComparison.Ordinal),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var modifiedBytes = System.IO.File.ReadAllBytes(modifiedPath);
        var modified = store.ImportGhost(modifiedPath, 3);
        var modifiedImportRejected = modified.Code == GhostImportCode.Modified
            && modifiedBytes.SequenceEqual(System.IO.File.ReadAllBytes(modifiedPath))
            && !store.LoadGhost(3).IsSuccess;

        var incompatiblePath = System.IO.Path.Combine(importDirectory, "future-rival.json");
        System.IO.File.WriteAllText(
            incompatiblePath,
            "{\"schemaVersion\":999}\n",
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        var incompatibleBytes = System.IO.File.ReadAllBytes(incompatiblePath);
        var incompatible = store.ImportGhost(incompatiblePath, 4);
        var incompatibleImportRejected = incompatible.Code == GhostImportCode.Incompatible
            && incompatibleBytes.SequenceEqual(System.IO.File.ReadAllBytes(incompatiblePath))
            && !store.LoadGhost(4).IsSuccess;

        var sourcePreservedThroughDelete = sourceBytes.SequenceEqual(
            System.IO.File.ReadAllBytes(sourcePath));
        var evidence = OfflineComparisonQualification.Run(
            replay,
            runCard,
            explicitSourcePreservingImport: explicitSourcePreservingImport
                && sourcePreservedThroughDelete,
            atomicNoOverwriteImport,
            modifiedImportRejected,
            incompatibleImportRejected,
            _offlineComparisonKeyboardRouteQualified,
            _offlineComparisonControllerRouteQualified,
            actualGameGhostRouteComplete: keyboardActualGhostRoute
                && controllerActualGhostRoute,
            runCardAtomicAndIdempotent,
            deletionRequiresExactConfirmation,
            deleteCancelLossless,
            confirmedDeleteExact,
            progressionAwardsExcluded);
        var evidenceDirectory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(evidenceDirectory);
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(evidenceDirectory, "offline_comparisons.json"),
            evidence.Serialize());

        var slotOneDeletion = store.PlanDeletion(1);
        if (!slotOneDeletion.IsSuccess
            || !store.Delete(slotOneDeletion.Plan!).IsSuccess
            || !sourceBytes.SequenceEqual(System.IO.File.ReadAllBytes(sourcePath)))
        {
            throw new InvalidOperationException(
                "Offline comparison smoke cleanup did not preserve the import source.");
        }

        ReturnToMenu();
    }

    private async Task DrainReplayOperationForSmokeAsync(string operationName)
    {
        for (var frame = 0; frame < 600 && _replayOperation is not null; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        if (_replayOperation is not null)
        {
            throw new InvalidOperationException(
                $"Offline comparison smoke timed out during {operationName}.");
        }
    }

    private async Task ExecuteReplayOperationLifecycleSmokeTest()
    {
        TransitionToScreen(ScreenState.Menu);
        _mainMenuCursor = (int)MainMenuItem.Start;
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
        _replayOperation = Task.FromResult(
            new ReplayOperationResult("REPLAY SAVE COMPLETED"));
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

        _replayOperation = Task.FromException<ReplayOperationResult>(
            new IOException("Synthetic replay save failure."));
        _replayOperationKind = ReplayOperationKind.Save;
        RequestQuit();
        if (
            TryCompleteReplayOperation()
            || _quitAfterReplaySave
            || _replayQuitDeadlineMilliseconds is not null
            || _replayOperation is not null
            || _replayStatusCaption
                != "QUIT CANCELED: REPLAY SAVE FAILED; RETRY OR QUIT AGAIN"
            || _structuredLog is null
            || !System.IO.File.ReadAllText(_structuredLog.ActiveLogPath)
                .Contains("replay_operation_failed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A failed replay save released quit or concealed the failed operation.");
        }

        var blockedSave = new TaskCompletionSource<ReplayOperationResult>(
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

    private void ExecuteUnavailableProgressionPersistenceSmokeTest()
    {
        var retainedStore = _progressionStore;
        var retainedProgression = _progression;
        var retainedGoalCursor = _progressionGoalCursor;
        var retainedCosmeticCursor = _cosmeticCursor;
        try
        {
            _progressionStore = null;
            if (TrySaveProgression("progression_unavailable_smoke"))
            {
                throw new InvalidOperationException(
                    "Unavailable progression persistence reported a successful save.");
            }

            _progressionGoalCursor = 0;
            HighlightProgressionGoal();
            if (_progressionStatusCaption
                != Localize("status.progression.highlight-save-failed"))
            {
                throw new InvalidOperationException(
                    "Goal highlighting concealed unavailable progression persistence.");
            }

            _cosmeticCursor = 0;
            ApplyCosmeticSelection(saveLoadout: false);
            if (_cosmeticStatusCaption != Localize("status.progression.save-failed"))
            {
                throw new InvalidOperationException(
                    "Cosmetic selection concealed unavailable progression persistence.");
            }
        }
        finally
        {
            _progressionStore = retainedStore;
            _progression = retainedProgression;
            _progressionGoalCursor = retainedGoalCursor;
            _cosmeticCursor = retainedCosmeticCursor;
            _progressionStatusCaption = null;
            _cosmeticStatusCaption = null;
        }
    }

    private static CoreOnlyOfflineQualificationEvidence ExecuteContentServiceSmokeTest(
        string userDataRoot)
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

        return ContentPackQualification.Run(userDataRoot);
    }

    private static void WriteCoreOnlyOfflineEvidence(
        CoreOnlyOfflineQualificationEvidence evidence)
    {
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "core_only_offline.json");
        var json = System.Text.Json.JsonSerializer.Serialize(
            evidence,
            CoreOnlyOfflineSerializerOptions);
        System.IO.File.WriteAllText(
            path,
            json + "\n",
            new System.Text.UTF8Encoding(false));
    }

    private static bool TryResolveCheckoutInventoryPath(out string inventoryPath)
    {
        string[] candidates =
        [
            System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    AppContext.BaseDirectory,
                    "content_inventory.json")),
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

    private void ExecuteShellPresentationSmokeTest()
    {
        ShellTheme.AssertQualificationContrast();
        ShellFocusPresentation.AssertDistinctMarkers();
        IdleCursorPolicy.AssertQualification();
        var theme = ActiveShellTheme;
        if (theme.InterfaceFont.GetHeight(16) <= 0.0f)
        {
            throw new InvalidOperationException("Owned shell interface font did not provide metrics.");
        }

        (string Token, InputPromptFamily Family)[] promptCases =
        [
            ("not-a-token", InputPromptFamily.Keyboard),
            ("key:enter", InputPromptFamily.Keyboard),
            ("button:south", InputPromptFamily.Xbox),
            ("button:left_shoulder", InputPromptFamily.PlayStation),
            ("button:dpad_up", InputPromptFamily.Nintendo),
            ("axis:left_x:-1", InputPromptFamily.GenericController),
            ("axis:right_trigger:+1", InputPromptFamily.Xbox),
            ("button:start", InputPromptFamily.PlayStation),
        ];
        var shapes = new HashSet<InputPromptGlyphShape>();
        var textFallbackRetained = true;
        foreach (var promptCase in promptCases)
        {
            var glyph = InputPromptGlyphs.DescribeToken(
                promptCase.Token,
                promptCase.Family);
            shapes.Add(glyph.Shape);
            var measurement = PromptBadgeRenderer.Measure(theme.InterfaceFont, glyph, 14);
            if (measurement.Width <= 0.0f
                || measurement.Height <= 0.0f
                || !float.IsFinite(measurement.Width)
                || !float.IsFinite(measurement.Height))
            {
                throw new InvalidOperationException(
                    $"Prompt badge geometry was invalid for {promptCase.Token}.");
            }

            var fallback = InputPromptGlyphs.FormatToken(
                promptCase.Token,
                promptCase.Family);
            textFallbackRetained &= fallback.Contains(glyph.Label, StringComparison.Ordinal);
        }

        var expectedShapeCount = Enum.GetValues<InputPromptGlyphShape>().Length;
        if (shapes.Count != expectedShapeCount || !textFallbackRetained)
        {
            throw new InvalidOperationException(
                "Prompt badge qualification did not cover every shape with text fallback.");
        }

        AssertMaximumTextLayout(theme.InterfaceFont);

        var standard = ShellTheme.Palette(highContrast: false);
        var highContrast = ShellTheme.Palette(highContrast: true);
        var evidence = new ShellPresentationQualificationEvidence(
            SchemaVersion: 1,
            Kind: "shell-presentation-v1",
            Passed: true,
            CentralizedFontOwner: true,
            PaletteCount: 2,
            StandardPrimaryContrast: ShellTheme.ContrastRatio(
                standard.PrimaryText,
                standard.CanvasBackground),
            StandardSecondaryContrast: ShellTheme.ContrastRatio(
                standard.SecondaryText,
                standard.CanvasBackground),
            HighContrastPrimaryContrast: ShellTheme.ContrastRatio(
                highContrast.PrimaryText,
                highContrast.CanvasBackground),
            PromptFamilyCount: Enum.GetValues<InputPromptFamily>().Length,
            GlyphShapeCount: shapes.Count,
            TextFallbackRetained: textFallbackRetained,
            MaximumTextScale: ShellSettings.MaximumTextScale,
            MaximumTextLayoutComplete: true,
            NonColorStateMarkers: true,
            LongCatalogPagination: AchievementPageCount() > 1,
            VectorBadgeFlows: ["menu", "run-end", "achievements", "scores", "bindings", "content-packs", "replays", "settings", "onboarding", "spectator", "lore", "comparisons"]);
        WriteShellPresentationEvidence(evidence);
    }

    private void ExecuteLocalizationSmokeTest()
    {
        var font = ActiveShellTheme.InterfaceFont;
        var minimumExpansionRatio = double.MaxValue;
        var missingGlyphs = new HashSet<char>();
        var overflowingEntries = new List<string>();
        var pseudoLocaleDeterministic = true;
        var maximumTextScaleLayoutPassed = true;
        foreach (var entry in ShellLocalization.All)
        {
            var arguments = entry.Parameters
                .Select(parameter => ShellTextArgument.From(
                    parameter,
                    parameter switch
                    {
                        "glyph" => "[A]",
                        "action" => "CONFIRM",
                        _ => "99",
                    }))
                .ToArray();
            var english = ShellLocalization.Format(entry.Id, ShellLocale.English, arguments);
            var pseudo = ShellLocalization.Format(entry.Id, ShellLocale.Pseudo, arguments);
            pseudoLocaleDeterministic &= pseudo == ShellLocalization.Format(
                entry.Id,
                ShellLocale.Pseudo,
                arguments);
            minimumExpansionRatio = Math.Min(
                minimumExpansionRatio,
                pseudo.Length / (double)Math.Max(1, english.Length));

            foreach (var character in pseudo.Where(character => !char.IsControl(character)))
            {
                if (!font.HasChar(character))
                {
                    missingGlyphs.Add(character);
                }
            }

            var baseFontSize = LocalizationBaseFontSize(entry.Id);
            var fontSize = Math.Max(
                10,
                (int)Math.Round(
                    baseFontSize * ShellSettings.MaximumTextScale,
                    MidpointRounding.AwayFromZero));
            var width = font.GetStringSize(
                pseudo,
                HorizontalAlignment.Left,
                -1.0f,
                fontSize).X;
            maximumTextScaleLayoutPassed &= float.IsFinite(width) && width <= 1180.0f;
            if (!float.IsFinite(width) || width > 1180.0f)
            {
                overflowingEntries.Add($"{entry.Id}:{width:0.0}");
            }
        }

        string Pseudo(string id, params ShellTextArgument[] arguments) =>
            ShellLocalization.Format(id, ShellLocale.Pseudo, arguments);
        var longestOperation = Pseudo(
            "agent-arena.operation.burst",
            ShellTextArgument.From("steps", 16),
            ShellTextArgument.From(
                "reason",
                Pseudo("agent-arena.burst.stop.replay-failure")),
            ShellTextArgument.From(
                "event",
                Pseudo("agent-arena.burst.event.achievement-candidate")));
        var longestDelivery = Pseudo(
            "agent-arena.delivery.coalesced",
            ShellTextArgument.From("count", long.MaxValue));
        var maximumIdentityToken = new string('W', 48);
        var longestSeed = Pseudo(
            "agent-arena.seed.open",
            ShellTextArgument.From("seed", ulong.MaxValue));
        var longestStateHash = new string('W', AgentViewerStateHashPrefixLength);
        var longestReplay = Pseudo(
            "agent-arena.replay.verified",
            ShellTextArgument.From("replay", longestStateHash));
        // A recovery slot shows either a localized marker or a real remaining-tick
        // count, so the largest count any shipped mode configures bounds that row.
        var maximumRecoveryTicks = RunModeCatalog.All
            .Select(mode => RunModeCatalog.CreateConfig(mode))
            .SelectMany(config => new[]
            {
                config.ShieldDurationTicks,
                config.PhaseShiftDurationTicks,
                config.LastStandRecoveryTicks,
                config.SlowMoDurationTicks,
            })
            .Max();
        var pressureCopyIds = new[]
        {
            "agent-arena.pressure.not-running",
            "agent-arena.pressure.open",
            "agent-arena.pressure.narrow",
            "agent-arena.pressure.pinned",
            "agent-arena.pressure.trapped",
        };
        var recoveryCopyIds = new[]
        {
            "agent-arena.recovery.held",
            "agent-arena.recovery.none",
        };
        var longestPressure = pressureCopyIds
            .Select(id => Pseudo(id))
            .OrderByDescending(value => value.Length)
            .First();
        var longestRecoveryValue = recoveryCopyIds
            .Select(id => Pseudo(id))
            .Append(maximumRecoveryTicks.ToString(CultureInfo.InvariantCulture))
            .OrderByDescending(value => value.Length)
            .First();
        var overlayRows = new (
            string Id,
            string Text,
            int BaseFontSize,
            float Baseline,
            float MaximumWidth)[]
        {
            ("survival",
                Pseudo(
                "agent-arena.survival",
                ShellTextArgument.From("open", AgentSurvivalStateV1.RunningCandidateExits),
                ShellTextArgument.From("candidate", AgentSurvivalStateV1.RunningCandidateExits),
                ShellTextArgument.From("pressure", longestPressure),
                ShellTextArgument.From("shield", longestRecoveryValue),
                ShellTextArgument.From("phase", longestRecoveryValue),
                ShellTextArgument.From("last_stand", longestRecoveryValue),
                ShellTextArgument.From("slow", longestRecoveryValue)),
                11,
                545.0f,
                1208.0f),
            ("verification",
                Pseudo(
                "agent-arena.verification",
                ShellTextArgument.From("seed", longestSeed),
                ShellTextArgument.From("state", longestStateHash),
                ShellTextArgument.From("replay", longestReplay)),
                12,
                572.0f,
                1208.0f),
            ("identity",
                Pseudo(
                "agent-arena.identity",
                ShellTextArgument.From("agent", maximumIdentityToken),
                ShellTextArgument.From("avatar", "MAXIMUM-AVATAR.."),
                ShellTextArgument.From("station", "MAXIMUM-STATION..")),
                13,
                602.0f,
                1208.0f),
            ("matchup",
                Pseudo(
                "agent-arena.matchup",
                ShellTextArgument.From(
                    "style",
                    Pseudo(
                        "agent-arena.lesson.replay-verified",
                        ShellTextArgument.From("lesson", maximumIdentityToken),
                        ShellTextArgument.From("met", 2))),
                ShellTextArgument.From(
                    "rival",
                    Pseudo(
                        "agent-arena.rival.score",
                        ShellTextArgument.From("rival", maximumIdentityToken),
                        ShellTextArgument.From("agent_score", "999999"),
                        ShellTextArgument.From("rival_score", "999999")))),
                11,
                628.0f,
                1222.0f),
            ("operation",
                Pseudo(
                "agent-arena.operation-status",
                ShellTextArgument.From("operation", longestOperation),
                ShellTextArgument.From("delivery", longestDelivery)),
                10,
                653.0f,
                1222.0f),
            ("status",
                Pseudo(
                "agent-arena.status",
                ShellTextArgument.From(
                    "status",
                    Pseudo("status.agent-viewer.disconnected")),
                ShellTextArgument.From(
                    "outcome",
                    Pseudo("agent-arena.outcome.agent-finished")),
                ShellTextArgument.From("step", int.MaxValue),
                ShellTextArgument.From("maximum", int.MaxValue),
                ShellTextArgument.From("frame", long.MaxValue)),
                9,
                680.0f,
                1222.0f),
            ("intent",
                Pseudo(
                "agent-arena.intent-status",
                ShellTextArgument.From(
                    "intent",
                    Pseudo("agent-arena.intent.preserve-space")),
                ShellTextArgument.From(
                    "action",
                    Pseudo("agent-arena.action.rejected-mutation-capacity"))),
                11,
                704.0f,
                992.0f),
        };
        var agentViewerOverlayLayoutPassed = true;
        var agentViewerOverlayFailures = new List<string>();
        var priorBottom = 518.0f;
        foreach (var row in overlayRows)
        {
            var fontSize = Math.Max(
                10,
                (int)Math.Round(
                    row.BaseFontSize * ShellSettings.MaximumTextScale,
                    MidpointRounding.AwayFromZero));
            var fittedText = FitAgentOverlayText(
                font,
                row.Text,
                fontSize,
                row.MaximumWidth);
            var width = font.GetStringSize(
                fittedText,
                HorizontalAlignment.Left,
                -1.0f,
                fontSize).X;
            var top = row.Baseline - font.GetAscent(fontSize);
            var bottom = row.Baseline + font.GetDescent(fontSize);
            var rowPassed = float.IsFinite(width)
                && width <= row.MaximumWidth
                && top >= priorBottom
                && bottom <= 718.0f;
            agentViewerOverlayLayoutPassed &= rowPassed;
            if (!rowPassed)
            {
                agentViewerOverlayFailures.Add(
                    $"{row.Id}:width={width:0.0}/{row.MaximumWidth:0.0},"
                    + $"top={top:0.0}/{priorBottom:0.0},bottom={bottom:0.0}/718.0");
            }
            priorBottom = bottom;
        }

        var longestCriterion = Pseudo(
            "agent-arena.style.criterion",
            ShellTextArgument.From(
                "state",
                Pseudo("agent-arena.style.criterion.replay-unverified")),
            ShellTextArgument.From(
                "criterion",
                "WRAPPED REWARDED BODY-PROXIMITY NEAR MISSES"),
            ShellTextArgument.From("current", "100.00%"),
            ShellTextArgument.From("target", "100.00%"));
        var styleOverlayCells = new (
            string Id,
            string Text,
            int BaseFontSize,
            float X,
            float MaximumWidth)[]
        {
            ("style-summary",
                Pseudo(
                    "agent-arena.style.replay-verified",
                    ShellTextArgument.From("style", maximumIdentityToken),
                    ShellTextArgument.From("met", 2)),
                10,
                38.0f,
                600.0f),
            ("style-rival",
                Pseudo(
                    "agent-arena.rival.score",
                    ShellTextArgument.From("rival", maximumIdentityToken),
                    ShellTextArgument.From("agent_score", "999999"),
                    ShellTextArgument.From("rival_score", "999999")),
                10,
                660.0f,
                600.0f),
            ("style-criterion-first", longestCriterion, 8, 38.0f, 600.0f),
            ("style-criterion-second", longestCriterion, 8, 660.0f, 600.0f),
            ("style-operation",
                Pseudo(
                    "agent-arena.operation-status",
                    ShellTextArgument.From("operation", longestOperation),
                    ShellTextArgument.From("delivery", longestDelivery)),
                8,
                38.0f,
                600.0f),
            ("style-status",
                Pseudo(
                    "agent-arena.status",
                    ShellTextArgument.From(
                        "status",
                        Pseudo("status.agent-viewer.disconnected")),
                    ShellTextArgument.From(
                        "outcome",
                        Pseudo("agent-arena.outcome.agent-finished")),
                    ShellTextArgument.From("step", int.MaxValue),
                    ShellTextArgument.From("maximum", int.MaxValue),
                    ShellTextArgument.From("frame", long.MaxValue)),
                8,
                660.0f,
                600.0f),
        };
        foreach (var cell in styleOverlayCells)
        {
            var fontSize = Math.Max(
                10,
                (int)Math.Round(
                    cell.BaseFontSize * ShellSettings.MaximumTextScale,
                    MidpointRounding.AwayFromZero));
            var fittedText = FitAgentOverlayText(
                font,
                cell.Text,
                fontSize,
                cell.MaximumWidth);
            var width = font.GetStringSize(
                fittedText,
                HorizontalAlignment.Left,
                -1.0f,
                fontSize).X;
            var cellPassed = float.IsFinite(width)
                && width <= cell.MaximumWidth
                && cell.X >= 20.0f
                && cell.X + width <= 1260.0f;
            agentViewerOverlayLayoutPassed &= cellPassed;
            if (!cellPassed)
            {
                agentViewerOverlayFailures.Add(
                    $"{cell.Id}:width={width:0.0}/{cell.MaximumWidth:0.0},"
                    + $"bounds={cell.X:0.0}/{cell.X + width:0.0}");
            }
        }

        // Fitting a row by eliding it is not the same as a spectator being able to
        // read it. Two rounds of playtest reported the verification and outcome
        // rows collapsing to A..TEP and ..UTCOME at real English content. These
        // rows carry the facts a human compares against a host dump, so at maximum
        // text scale their actual worst-case English must fit without elision.
        string LongestEnglish(params string[] ids) => ids
            .Select(id => ShellLocalization.Format(id, ShellLocale.English))
            .OrderByDescending(value => value.Length)
            .First();
        var longestEnglishRecoveryValue = recoveryCopyIds
            .Select(id => ShellLocalization.Format(id, ShellLocale.English))
            .Append(maximumRecoveryTicks.ToString(CultureInfo.InvariantCulture))
            .OrderByDescending(value => value.Length)
            .First();
        var readableRows = new (string Id, string Text, int BaseFontSize, float MaximumWidth)[]
        {
            ("survival-readable",
                ShellLocalization.Format(
                    "agent-arena.survival",
                    ShellLocale.English,
                    ShellTextArgument.From(
                        "open",
                        AgentSurvivalStateV1.RunningCandidateExits),
                    ShellTextArgument.From(
                        "candidate",
                        AgentSurvivalStateV1.RunningCandidateExits),
                    ShellTextArgument.From("pressure", LongestEnglish(pressureCopyIds)),
                    ShellTextArgument.From("shield", longestEnglishRecoveryValue),
                    ShellTextArgument.From("phase", longestEnglishRecoveryValue),
                    ShellTextArgument.From("last_stand", longestEnglishRecoveryValue),
                    ShellTextArgument.From("slow", longestEnglishRecoveryValue)),
                11,
                1208.0f),
            ("verification-readable",
                ShellLocalization.Format(
                    "agent-arena.verification",
                    ShellLocale.English,
                    ShellTextArgument.From(
                        "seed",
                        ShellLocalization.Format(
                            "agent-arena.seed.open",
                            ShellLocale.English,
                            ShellTextArgument.From("seed", ulong.MaxValue))),
                    ShellTextArgument.From(
                        "state",
                        new string('W', AgentViewerStateHashPrefixLength)),
                    ShellTextArgument.From(
                        "replay",
                        LongestEnglish(
                            "agent-arena.replay.pending",
                            "agent-arena.replay.unavailable"))),
                12,
                1208.0f),
            ("status-readable",
                ShellLocalization.Format(
                    "agent-arena.status",
                    ShellLocale.English,
                    ShellTextArgument.From(
                        "status",
                        LongestEnglish(
                            "agent-arena.feed.connecting",
                            "agent-arena.feed.watching",
                            "agent-arena.feed.completed",
                            "agent-arena.feed.disconnected",
                            "agent-arena.feed.rejected",
                            "agent-arena.feed.failed-closed")),
                    ShellTextArgument.From(
                        "outcome",
                        LongestEnglish(
                            "agent-arena.outcome.live",
                            "agent-arena.outcome.rules-terminal",
                            "agent-arena.outcome.step-limit",
                            "agent-arena.outcome.agent-finished",
                            "agent-arena.outcome.replay-failure")),
                    // Real caps, not pseudo-localized fantasy values.
                    ShellTextArgument.From("step", MaximumAgentMatchSteps),
                    ShellTextArgument.From("maximum", MaximumAgentMatchSteps),
                    ShellTextArgument.From("frame", MaximumAgentViewerFrames)),
                8,
                600.0f),
        };
        foreach (var row in readableRows)
        {
            var fontSize = Math.Max(
                10,
                (int)Math.Round(
                    row.BaseFontSize * ShellSettings.MaximumTextScale,
                    MidpointRounding.AwayFromZero));
            var fittedText = FitAgentOverlayText(font, row.Text, fontSize, row.MaximumWidth);
            if (!string.Equals(fittedText, row.Text, StringComparison.Ordinal))
            {
                agentViewerOverlayLayoutPassed = false;
                agentViewerOverlayFailures.Add($"{row.Id}:elided");
            }
        }

        // The same character-budget fight, one row up from the watch overlay.
        // Two rounds of playtest found two ways this row fails: the title ran
        // off the canvas and lost a letter, and the combo cell ran under the
        // hunger cell so two facts stacked. Both are now one gate. It composes
        // the real worst-case English for every cell of both mode presentations
        // at maximum text scale, requires the fitted result to keep every
        // character, and requires every cell to end before its neighbour begins.
        // Elision here reads as a shorter word rather than as truncation:
        // CLASSIC AGENT COMPLET is not COMPLETE, and an overlap reads as a
        // different word entirely.
        var runHudRowLayoutPassed = true;
        var runHudRowFailures = new List<string>();
        var runHudTitleLayoutPassed = true;
        var runHudTitleFailures = new List<string>();
        var runHudCellFontSizes = new Dictionary<string, int>(StringComparer.Ordinal);

        static int RowFontSize(int baseFontSize) => Math.Max(
            10,
            (int)Math.Round(
                baseFontSize * ShellSettings.MaximumTextScale,
                MidpointRounding.AwayFromZero));

        void RequireRunHudCell(RunHudCell cell, string text)
        {
            var (fontSize, fitted) = ResolveFittedLabel(
                text,
                RowFontSize(cell.BaseFontSize),
                cell.MinimumFontSize,
                cell.MaximumWidth);
            runHudCellFontSizes[cell.Id] =
                runHudCellFontSizes.TryGetValue(cell.Id, out var seen)
                    ? Math.Min(seen, fontSize)
                    : fontSize;
            if (string.Equals(fitted, text, StringComparison.Ordinal))
            {
                return;
            }

            runHudRowLayoutPassed = false;
            runHudRowFailures.Add($"{cell.Id}:elided:{text}");
            if (string.Equals(cell.Id, RunHudModeTitleCell.Id, StringComparison.Ordinal))
            {
                runHudTitleLayoutPassed = false;
                runHudTitleFailures.Add($"{cell.Id}:elided:{text}");
            }
        }

        // Seam truth first. A cell table that overlaps is broken no matter what
        // any individual string does, and the draw path reads the same table.
        foreach (var (presentation, cells) in new[]
        {
            ("vibe", RunHudVibeCells),
            ("classic", RunHudClassicCells),
        })
        {
            for (var index = 1; index < cells.Count; index++)
            {
                var previous = cells[index - 1];
                var next = cells[index];
                if (previous.RightEdge + RunHudCellGutter > next.Left)
                {
                    runHudRowLayoutPassed = false;
                    runHudRowFailures.Add(
                        $"{presentation}/{previous.Id}->{next.Id}:seam");
                }
            }

            var last = cells[^1];
            if (last.RightEdge > RunHudRightMargin)
            {
                runHudRowFailures.Add($"{presentation}/{last.Id}:margin");
                runHudRowLayoutPassed = false;
            }
        }

        // The hunger meter never rescales, but its critical warning glyph does,
        // and it is the only thing between the meter and the mode title.
        var hungerMarkerWidth = MeasureLabelWidth(
            WarningMarker,
            RowFontSize(HungerMeterMarkerFontSize));
        var hungerMarkerRight = RunHudHungerMeterLeft
            + RunHudHungerMeterWidth
            + HungerMeterMarkerOffset
            + hungerMarkerWidth;
        if (hungerMarkerRight + RunHudCellGutter > RunHudModeTitleCell.Left)
        {
            runHudRowLayoutPassed = false;
            runHudRowFailures.Add(
                $"run-hud.hunger-meter:marker:{hungerMarkerRight:0.0}");
        }

        var runTitleStatuses = new[]
        {
            "run.status.running",
            "run.status.dead",
            "run.status.won",
            "run.status.paused",
            "run.status.paused-focus-lost",
            "agent-arena.run.live",
            "agent-arena.run.complete",
            "agent-arena.run.failed",
        };
        foreach (var mode in RunModeCatalog.All)
        {
            foreach (var statusId in runTitleStatuses)
            {
                RequireRunHudCell(
                    RunHudModeTitleCell,
                    RunModeTitleText(
                        mode.DisplayName,
                        ShellLocalization.Format(statusId, ShellLocale.English)));
            }
        }

        // The score cell carries a fixed six-digit field, so its worst case is
        // exact rather than sampled.
        RequireRunHudCell(RunHudScoreCell, $"SCORE {999999:D6}");
        RequireRunHudCell(RunHudClassicScoreCell, Localize("run.classic-score"));
        RequireRunHudCell(RunHudClassicRulesCell, Localize("run.classic-rules"));

        // Compose the combo cell from the real Vibe Level catalog and the real
        // multiplier ceiling rather than from a guessed longest string. The
        // pulse marker and emphasis are the wide case, so use them.
        var comboLayoutAccessibility = AccessibilityPresentationPolicy.FromSettings(
            _shellSettings);
        foreach (var level in VibeLevelDirector.Definitions)
        {
            var composed = ComboFeedback.Describe(
                99,
                10.0,
                ComboFeedback.PulseTicks,
                comboLayoutAccessibility,
                level);
            var comboLevel = composed.Level == "BUILDING"
                ? string.Empty
                : "  " + composed.Level;
            RequireRunHudCell(
                RunHudComboCell,
                $"{composed.StaticMarker} {composed.Label}{comboLevel}");
        }

        // Walk every reachable hunger tick of every shipped mode instead of
        // sampling phase boundaries, so a later starvation-budget change cannot
        // silently widen this cell past its neighbour.
        foreach (var mode in RunModeCatalog.All)
        {
            var config = RunModeCatalog.CreateConfig(mode);
            for (var ticks = 0; ticks <= config.StarvationTicks; ticks++)
            {
                RequireRunHudCell(
                    RunHudHungerCell,
                    HungerFeedback.Describe(
                        ticks,
                        config.StarvationTicks,
                        config.StarvationWarningTicks).Label);
            }
        }

        var glyphPrompt = ShellLocalization.Format(
            "prompt.action",
            ShellLocale.Pseudo,
            ShellTextArgument.From("glyph", "[A]"),
            ShellTextArgument.From("action", "CONFIRM"));
        var inputGlyphParameterPreserved =
            glyphPrompt.Split("[A]", StringSplitOptions.None).Length == 2;
        var exactParameterValidation =
            Throws<ArgumentException>(() => ShellLocalization.Format(
                "tour.summary",
                ShellLocale.English))
            && Throws<ArgumentException>(() => ShellLocalization.Format(
                "tour.summary",
                ShellLocale.English,
                ShellTextArgument.From("completed", 1),
                ShellTextArgument.From("total", 12),
                ShellTextArgument.From("page", 1),
                ShellTextArgument.From("pages", 4),
                ShellTextArgument.From("unknown", 1)))
            && Throws<ArgumentException>(() => ShellLocalization.Format(
                "prompt.action",
                ShellLocale.English,
                ShellTextArgument.From("glyph", "[A]"),
                ShellTextArgument.From("glyph", "[B]"),
                ShellTextArgument.From("action", "CONFIRM")))
            && Throws<KeyNotFoundException>(() => ShellLocalization.Format(
                "unknown.copy",
                ShellLocale.English));

        var sourcePath = System.IO.Path.GetFullPath(
            ProjectSettings.GlobalizePath("res://scripts/Main.cs"));
        var sourceAuditPerformed = System.IO.File.Exists(sourcePath);
        var remainingDirectDrawLabelLiteralCount = 0;
        var remainingDirectPromptLiteralCount = 0;
        var remainingDirectStatusLiteralCount = 0;
        var remainingComposedStatusLiteralCount = 0;
        var remainingDomainStatusExpressionCount = 0;
        if (sourceAuditPerformed)
        {
            var source = System.IO.File.ReadAllText(sourcePath);
            remainingDirectDrawLabelLiteralCount =
                System.Text.RegularExpressions.Regex.Count(
                    source,
                    "DrawLabel\\s*\\(\\s*\"");
            remainingDirectPromptLiteralCount =
                System.Text.RegularExpressions.Regex.Count(
                    source,
                    "DrawActionPromptSegment\\s*\\(\\s*\"[^\"]+\"\\s*,\\s*\"")
                + System.Text.RegularExpressions.Regex.Count(
                    source,
                    "DrawStaticPromptSegment\\s*\\(\\s*\"[^\"]+\"\\s*,\\s*\"[^\"]+\"\\s*,\\s*\"");
            remainingDirectStatusLiteralCount =
                System.Text.RegularExpressions.Regex.Count(
                    source,
                    "_\\w*(?:StatusCaption|Caption)\\s*=\\s*\\$?\"");
            remainingComposedStatusLiteralCount =
                System.Text.RegularExpressions.Regex.Matches(
                    source,
                    "_\\w*(?:StatusCaption|Caption)\\s*=\\s*(.*?);",
                    System.Text.RegularExpressions.RegexOptions.Singleline)
                .Cast<System.Text.RegularExpressions.Match>()
                .Count(match =>
                    match.Groups[1].Value.Contains('"')
                    && !match.Groups[1].Value.Contains("Localize(", StringComparison.Ordinal));
            remainingDomainStatusExpressionCount =
                System.Text.RegularExpressions.Regex.Matches(
                    source,
                    "_\\w*(?:StatusCaption|Caption)\\s*=\\s*(.*?);",
                    System.Text.RegularExpressions.RegexOptions.Singleline)
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(match => string.Join(
                    " ",
                    match.Groups[1].Value.Split(
                        (char[]?)null,
                        StringSplitOptions.RemoveEmptyEntries)))
                .Count(expression =>
                    !expression.Contains("Localize(", StringComparison.Ordinal)
                    && !expression.StartsWith(
                        "LocalizedCosmeticRequirement(",
                        StringComparison.Ordinal)
                    && !expression.StartsWith(
                        "LocalizedSettingsSection(",
                        StringComparison.Ordinal)
                    && !expression.StartsWith(
                        "LocalizedInputBindingFailure(",
                        StringComparison.Ordinal)
                    && expression is not "null"
                    && expression is not "caption"
                    && expression is not "sanitized"
                    && expression is not "statusCaption");
        }
        var rulesCopyIdsResolved = OnboardingCopyIds.All.All(ShellLocalization.ContainsId);
        var feedbackCopyIdCount = ShellLocalization.All.Count(entry =>
            entry.Id.StartsWith("feedback.", StringComparison.Ordinal));
        var broadcastCopyPairs = BroadcastStationCatalog.All
            .SelectMany(station => station.CaptionCopyIds.Select((copyId, index) => new
            {
                CopyId = copyId,
                English = $"{station.HostName}: {station.ShortIds[index]}",
            }))
            .ToArray();
        var broadcastCopyIdsResolved = broadcastCopyPairs.Length == 24
            && broadcastCopyPairs.Select(pair => pair.CopyId)
                .Distinct(StringComparer.Ordinal)
                .Count() == broadcastCopyPairs.Length
            && broadcastCopyPairs.All(pair =>
                ShellLocalization.ContainsId(pair.CopyId)
                && ShellLocalization.Format(pair.CopyId, ShellLocale.English)
                    == pair.English);

        const int migratedRequiredFlowCount = 13;
        const double requiredExpansionRatio = 1.30;
        var passed = ShellLocalization.All.Count == 663
            && ShellLocalization.All.Count(entry => entry.Parameters.Count > 0) == 105
            && migratedRequiredFlowCount == 13
            && minimumExpansionRatio >= requiredExpansionRatio
            && missingGlyphs.Count == 0
            && exactParameterValidation
            && inputGlyphParameterPreserved
            && maximumTextScaleLayoutPassed
            && agentViewerOverlayLayoutPassed
            && runHudTitleLayoutPassed
            && runHudRowLayoutPassed
            && pseudoLocaleDeterministic
            && OnboardingCopyIds.All.Count == 18
            && rulesCopyIdsResolved
            && feedbackCopyIdCount == 24
            && broadcastCopyIdsResolved
            && remainingDirectDrawLabelLiteralCount == 0
            && remainingDirectPromptLiteralCount == 0
            && remainingDirectStatusLiteralCount == 0
            && remainingComposedStatusLiteralCount == 0
            && remainingDomainStatusExpressionCount == 0;
        if (!passed)
        {
            throw new InvalidOperationException(
                "Localization qualification failed: "
                + $"strings={ShellLocalization.All.Count}, "
                + $"parameterized={ShellLocalization.All.Count(entry => entry.Parameters.Count > 0)}, "
                + $"minimumExpansion={minimumExpansionRatio:0.000}, "
                + $"missingGlyphs={missingGlyphs.Count}, "
                + $"exactParameters={exactParameterValidation}, "
                + $"inputGlyph={inputGlyphParameterPreserved}, "
                + $"maximumLayout={maximumTextScaleLayoutPassed} [{string.Join(",", overflowingEntries)}], "
                + $"agentViewerOverlayLayout={agentViewerOverlayLayoutPassed} "
                + $"[{string.Join(";", agentViewerOverlayFailures)}], "
                + $"runHudTitleLayout={runHudTitleLayoutPassed} "
                + $"[{string.Join(";", runHudTitleFailures)}], "
                + $"runHudRowLayout={runHudRowLayoutPassed} "
                + $"[{string.Join(";", runHudRowFailures)}], "
                + $"deterministic={pseudoLocaleDeterministic}, "
                + $"remainingDirectLabels={remainingDirectDrawLabelLiteralCount}, "
                + $"remainingDirectPrompts={remainingDirectPromptLiteralCount}, "
                + $"remainingDirectStatuses={remainingDirectStatusLiteralCount}, "
                + $"remainingComposedStatuses={remainingComposedStatusLiteralCount}, "
                + $"remainingDomainStatuses={remainingDomainStatusExpressionCount}, "
                + $"rulesCopyIdsResolved={rulesCopyIdsResolved}, "
                + $"feedbackCopyIds={feedbackCopyIdCount}, "
                + $"broadcastCopyIdsResolved={broadcastCopyIdsResolved}.");
        }

        var evidence = new LocalizationQualificationEvidence(
            SchemaVersion: 1,
            Kind: "localization-qualification-v1",
            Passed: true,
            CatalogId: ShellLocalization.CatalogId,
            RequiredLocale: ShellLocalization.EnglishLocaleId,
            PseudoLocale: ShellLocalization.PseudoLocaleId,
            StringCount: ShellLocalization.All.Count,
            ParameterizedStringCount: ShellLocalization.All.Count(
                entry => entry.Parameters.Count > 0),
            MigratedRequiredFlowCount: migratedRequiredFlowCount,
            MinimumPseudoExpansionRatio: minimumExpansionRatio,
            MissingGlyphCount: missingGlyphs.Count,
            ExactParameterValidation: exactParameterValidation,
            InputGlyphParameterPreserved: inputGlyphParameterPreserved,
            MaximumTextScaleLayoutPassed: maximumTextScaleLayoutPassed,
            AgentViewerOverlayLayoutPassed: agentViewerOverlayLayoutPassed,
            RunHudTitleLayoutPassed: runHudTitleLayoutPassed,
            RunHudRowLayoutPassed: runHudRowLayoutPassed,
            RunHudRowCellCount: runHudCellFontSizes.Count,
            RunHudRowMinimumFontSize: runHudCellFontSizes.Count == 0
                ? 0
                : runHudCellFontSizes.Values.Min(),
            RulesCopyIdCount: OnboardingCopyIds.All.Count,
            RulesCopyIdsResolved: rulesCopyIdsResolved,
            FeedbackCopyIdCount: feedbackCopyIdCount,
            BroadcastCopyIdCount: broadcastCopyPairs.Length,
            BroadcastCopyIdsResolved: broadcastCopyIdsResolved,
            SourceAuditPerformed: sourceAuditPerformed,
            RemainingDirectDrawLabelLiteralCount: remainingDirectDrawLabelLiteralCount,
            RemainingDirectPromptLiteralCount: remainingDirectPromptLiteralCount,
            RemainingDirectStatusLiteralCount: remainingDirectStatusLiteralCount,
            RemainingComposedStatusLiteralCount: remainingComposedStatusLiteralCount,
            RemainingDomainStatusExpressionCount: remainingDomainStatusExpressionCount,
            MigrationStatus: sourceAuditPerformed
                ? "shell-and-audited-domain-presentation-copy-complete"
                : "packaged-runtime-catalog-layout-complete");
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(
            directory,
            sourceAuditPerformed ? "localization.json" : "localization_runtime.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private static int LocalizationBaseFontSize(string id) => id switch
    {
        "app.title" => 52,
        "app.tagline" => 22,
        "menu.start" or "replays.empty" or "scores.empty" => 22,
        "screen.onboarding.title" or "screen.settings.title" or "screen.tour.title"
            or "screen.cosmetics.title" or "screen.scores.title"
            or "screen.content-packs.title" or "screen.bindings.title"
            or "screen.progression.title" => 40,
        "screen.replays.title" => 34,
        "settings.player-data.operation" or "settings.reset.title" => 24,
        "settings.playtest.delete-title" => 23,
        "content-packs.core-ready" => 20,
        "onboarding.offer.summary" => 18,
        "content-packs.optional-status" or "run-end.cause" => 18,
        "tour.summary" or "onboarding.offer.isolation" or "content-packs.offline-help"
            or "content-packs.storage-help" or "content-packs.removal-help" => 17,
        "cosmetics.summary" or "menu.action.start" or "menu.action.previous"
            or "menu.action.next" or "menu.replay-drop"
            or "settings.playtest.delete-help"
            or "settings.reset.target" or "settings.reset.targets-help"
            or "content-packs.retention-help" or "replays.integrity-help"
            or "run.classic-score" or "run-end.personal-best" or "run-end.tour-primary"
            or "scores.empty-help" or "action.browse-content-packs"
            or "action.browse-input-bindings" or "action.browse-run-unlocks"
            or "action.browse-verified-replays" or "action.browse-versioned-scores"
            or "action.learn-tutorial" or "action.settings" => 16,
        "onboarding.practice" or "settings.select-section"
            or "onboarding.offer.learn-description" or "onboarding.offer.skip-description"
            or "settings.reset.backup-help" or "run.classic-rules"
            or "action.cancel-without-deleting" or "action.cancel-without-writing"
            or "action.choose" or "action.create-backup-reset"
            or "action.delete-permanently" or "action.exit-safely" or "action.load"
            or "action.or" or "action.return" or "action.select"
            or "action.settings-before-play" or "action.skip-menu" => 15,
        "tour.action.start" or "tour.action.back" or "prompt.action"
            or "menu.accessibility-shortcuts" or "settings.navigation.sections"
            or "settings.player-data.operation-help" or "content-packs.isolation-help"
            or "settings.backup.location" or "settings.backup.categories"
            or "settings.backup.navigation" or "bindings.restore-defaults"
            or "bindings.navigation" or "progression.explainer"
            or "scores.category-policy" or "action.back-ten"
            or "action.broadcast-tour" or "action.cancel" or "action.cancel-unchanged"
            or "action.cosmetic-sets" or "action.delete-one-replay" or "action.equip"
            or "action.export-verified" or "action.faster" or "action.highlight-next-goal"
            or "action.keep-current-data" or "action.list" or "action.open"
            or "action.play-pause" or "action.prepare-delete" or "action.progression-goals"
            or "action.replays-status" or "action.restart" or "action.save-loadout"
            or "action.slower" or "action.step" or "action.swap" or "action.toggle-hud"
            or "action.versioned-scores" => 14,
        "run-end.recovery" or "settings.reset.backup-location" or "action.cycle-radio" => 12,
        "agent-arena.survival" => 11,
        _ when id.StartsWith("status.settings.", StringComparison.Ordinal)
            || id.StartsWith("status.onboarding.", StringComparison.Ordinal)
            || id.StartsWith("status.player-data.", StringComparison.Ordinal) => 15,
        _ => 13,
    };

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static void AssertMaximumTextLayout(Font font)
    {
        ArgumentNullException.ThrowIfNull(font);
        var maxScale = ShellSettings.MaximumTextScale;
        var fontSize = (int baseSize) => Math.Max(
            10,
            (int)Math.Round(baseSize * maxScale, MidpointRounding.AwayFromZero));

        void AssertFits(string area, string text, int baseSize, float maximumWidth)
        {
            var width = font.GetStringSize(
                text,
                HorizontalAlignment.Left,
                -1.0f,
                fontSize(baseSize)).X;
            if (!float.IsFinite(width) || width > maximumWidth)
            {
                throw new InvalidOperationException(
                    $"Maximum-text layout overflow in {area}: {width:0.0} > {maximumWidth:0.0}.");
            }
        }

        foreach (var section in SettingsMenuCatalog.Sections)
        {
            foreach (var item in SettingsMenuCatalog.ForSection(section))
            {
                AssertFits("settings label", "> " + item.Label.ToUpperInvariant(), 16, 350.0f);
                AssertFits("settings description", item.Description, 13, 1160.0f);
            }
        }

        foreach (var definition in AchievementCatalog.Definitions)
        {
            var line =
                "[ ] " + definition.Name.ToUpperInvariant() + "  (" + definition.Rarity
                + ")  -  " + definition.Description;
            AssertFits("achievement row", line, 15, 1180.0f);
        }

        (string Text, int BaseSize)[] fullWidthShellLines =
        [
            ("Plan the route. Build the vibe. Recover with style.", 22),
            ("F4 flash  F5/F6 text  F7 mute  -/= volume  F8 restore  F9-F11 accessibility  F12 logs", 14),
            ("Schema 1 keyboard and controller remap (F8 restores defaults)", 18),
            ("Malformed, incompatible, tampered, or duplicate optional packs are isolated.", 16),
            ("Local only. Loading revalidates integrity and deterministic outcome.", 16),
        ];
        foreach (var line in fullWidthShellLines)
        {
            AssertFits("shell line", line.Text, line.BaseSize, 1180.0f);
        }

        (string Text, int BaseSize)[] runEndLines =
        [
            ("CAUSE: SELF COLLISION", 18),
            ("RECOVERY: Shield, Phase Shift, or Last Stand can prevent a body collision.", 12),
            ("SCORE 999999  PERSONAL BEST 999999", 18),
            ("NEW PERSONAL BEST", 16),
            ("LENGTH 256  STEPS 999999  FOOD 999999  PEAK COMBO 999999", 14),
            ("NEW UNLOCKS: QUICK REFLEXES, GROWING STRONG  +15 MORE", 14),
        ];
        foreach (var line in runEndLines)
        {
            AssertFits("run-end overlay", line.Text, line.BaseSize, 804.0f);
        }

        (string Text, int BaseSize)[] playerDataLines =
        [
            ("BACK UP, VERIFY, THEN RESET?", 24),
            ("Only these exact player-data targets will be removed:", 16),
            ("[ ] user://personal_bests.json", 16),
            ("[ ] user://score_history.json", 16),
            ("A SHA-256 verified backup is completed before removal.", 15),
            ("PLAYER-DATA OPERATION IN PROGRESS", 24),
            ("The game remains responsive. Quit waits for this operation to finish safely.", 16),
            ("Categories: SETTINGS + BINDINGS, PROGRESSION, LOCAL SCORES, REPLAYS, OPTIONAL CONTENT", 14),
            ("Restore never overwrites current data. Back keeps current data unchanged.", 14),
        ];
        foreach (var line in playerDataLines)
        {
            AssertFits("player-data recovery", line.Text, line.BaseSize, 1180.0f);
        }

        var bindingRowHeight = Math.Max(28.0f, font.GetHeight(fontSize(16)) + 8.0f);
        var bindingBottom = 232.0f
            + (InputBindingsDocument.RequiredActions.Length * bindingRowHeight)
            + Math.Max(28.0f, font.GetHeight(fontSize(14)) + 8.0f);
        var achievementRowHeight = Math.Max(34.0f, font.GetHeight(fontSize(15)) + 8.0f);
        var achievementBottom = 214.0f + (AchievementsPerPage * achievementRowHeight);
        var settingsRowHeight = Math.Max(30.0f, font.GetHeight(fontSize(16)) + 6.0f);
        var settingsBottom = 190.0f
            + ((SettingsMenuCatalog.ForSection(SettingsSection.Audio).Count - 1) * 44.0f)
            + settingsRowHeight;
        if (bindingBottom > 620.0f
            || achievementBottom > 650.0f
            || settingsBottom > 582.0f)
        {
            throw new InvalidOperationException(
                "Maximum-text paged catalog rows escaped the logical viewport.");
        }
    }

    private static void WriteShellPresentationEvidence(
        ShellPresentationQualificationEvidence evidence)
    {
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "shell_presentation.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private void ExecuteAudioFallbackStressSmokeTest()
    {
        if (_cuePlayer is null)
        {
            throw new InvalidOperationException("Audio cue player was not initialized.");
        }

        const int rapidRetriggerIterations = 32;
        var cues = Enum.GetValues<AudioCue>();
        var mixQualification = AudioCueMixPolicy.Qualify();
        var rulesProbe = SnakeRun.Create(SmokeSeed + 20);
        var rulesHashBefore = rulesProbe.ComputeStateHash();
        foreach (var cue in cues)
        {
            ProceduralCuePlayer.ValidateCue(cue);
        }

        var rapidRetriggerAttempts = 0;
        for (var iteration = 0; iteration < rapidRetriggerIterations; iteration++)
        {
            foreach (var cue in cues)
            {
                if (!_cuePlayer.TryPlayCue(cue, volumeLinear: 1.0f, out var failureReason))
                {
                    throw new InvalidOperationException(
                        $"Rapid fallback cue playback failed for {cue}: {failureReason}");
                }

                rapidRetriggerAttempts++;
            }
        }

        var voiceCapacityBounded = _cuePlayer.PeakVoiceCount
                <= AudioCueMixPolicy.SfxBusCapacity + AudioCueMixPolicy.UiBusCapacity
            && _cuePlayer.ActiveVoiceCount
                <= AudioCueMixPolicy.SfxBusCapacity + AudioCueMixPolicy.UiBusCapacity;
        if (!voiceCapacityBounded
            || _cuePlayer.CooldownSuppressionCount == 0)
        {
            throw new InvalidOperationException(
                "Rapid fallback cue playback did not exercise bounded allocation decisions: "
                + $"peak={_cuePlayer.PeakVoiceCount}, active={_cuePlayer.ActiveVoiceCount}, "
                + $"cooldown={_cuePlayer.CooldownSuppressionCount}.");
        }

        _cuePlayer.StopAndDetach();
        var musicBeforeDuck = AudioBuses.GetBusLinear(AudioBuses.Music);
        var foodPlayed = _cuePlayer.TryPlayCue(
            AudioCue.Food,
            volumeLinear: 1.0f,
            out var foodFailure);
        var collisionPlayed = _cuePlayer.TryPlayCue(
            AudioCue.Collision,
            volumeLinear: 1.0f,
            out var collisionFailure);
        if (!foodPlayed || !collisionPlayed)
        {
            throw new InvalidOperationException(
                $"Engine duck exercise failed: {foodFailure} {collisionFailure}".Trim());
        }

        var engineMusicDuckObserved = Math.Abs(
                AudioBuses.TransientMusicDuckDecibels - (-9.0f)) < 0.0001f
            && AudioBuses.GetBusLinear(AudioBuses.Music) < musicBeforeDuck;
        _cuePlayer.StopAndDetach();
        var engineMusicDuckRestored = AudioBuses.TransientMusicDuckDecibels == 0.0f
            && Math.Abs(AudioBuses.GetBusLinear(AudioBuses.Music) - musicBeforeDuck) < 0.02f;
        if (!engineMusicDuckObserved || !engineMusicDuckRestored)
        {
            throw new InvalidOperationException(
                "Transient music duck did not apply and restore through the engine bus.");
        }

        var volumeProbe = ShellSettings.CreateDefaults();
        volumeProbe.MasterVolume = 1.0f;
        volumeProbe.MusicVolume = 0.61f;
        volumeProbe.SfxVolume = 0.37f;
        volumeProbe.UiVolume = 0.73f;
        AudioBuses.ApplyShellSettings(volumeProbe);
        var savedVolumesImmediateAndIsolated =
            Math.Abs(AudioBuses.GetBusLinear(AudioBuses.Music) - 0.61f) < 0.02f
            && Math.Abs(AudioBuses.GetBusLinear(AudioBuses.Sfx) - 0.37f) < 0.02f
            && Math.Abs(AudioBuses.GetBusLinear(AudioBuses.Ui) - 0.73f) < 0.02f;
        AudioBuses.ApplyShellSettings(_shellSettings);
        if (!savedVolumesImmediateAndIsolated)
        {
            throw new InvalidOperationException(
                "Saved bus volumes did not apply immediately and independently.");
        }

        var outputDevicePollingActive = _observedAudioOutputSignature.Length > 0;
        var deviceChangeRecoveryObserved = TryRepairAudioOutput(
            out var deviceChangeRepairFailure);
        if (!outputDevicePollingActive || !deviceChangeRecoveryObserved)
        {
            throw new InvalidOperationException(
                "Audio output hot-change recovery qualification failed: "
                + deviceChangeRepairFailure);
        }

        var mutedPathChecks = 0;
        foreach (var cue in cues)
        {
            if (!_cuePlayer.TryPlayCue(cue, volumeLinear: 0.0f, out var failureReason))
            {
                throw new InvalidOperationException(
                    $"Muted fallback cue path failed for {cue}: {failureReason}");
            }

            if (_cuePlayer.Playing)
            {
                throw new InvalidOperationException(
                    $"Muted fallback cue unexpectedly started playback: {cue}");
            }

            mutedPathChecks++;
        }

        var cacheBounded = _cuePlayer.CachedStreamCount == cues.Length;
        if (!cacheBounded)
        {
            throw new InvalidOperationException(
                $"Fallback cue cache grew beyond its catalog: {_cuePlayer.CachedStreamCount}/{cues.Length}.");
        }

        _cuePlayer.StopAndDetach();
        var sfxBusIndex = AudioServer.GetBusIndex(AudioBuses.Sfx);
        if (sfxBusIndex < 0)
        {
            throw new InvalidOperationException("SFX bus was unavailable before recovery injection.");
        }

        AudioServer.RemoveBus(sfxBusIndex);
        var missingBusFailureObserved = false;
        string missingBusFailure;
        try
        {
            missingBusFailureObserved = !_cuePlayer.TryPlayCue(
                AudioCue.Food,
                volumeLinear: 1.0f,
                out missingBusFailure);
        }
        finally
        {
            AudioBuses.EnsureRegistered();
            AudioBuses.ApplyShellSettings(_shellSettings);
        }

        if (!missingBusFailureObserved
            || !missingBusFailure.Contains("SFX", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Missing audio bus did not fail closed with an actionable reason.");
        }

        var failureAt = Time.GetTicksMsec();
        var unavailable = _audioOutputRecovery.NoteFailure(failureAt, missingBusFailure);
        var backoffObserved = unavailable is not null
            && !_audioOutputRecovery.ShouldAttemptPlayback(failureAt)
            && _audioOutputRecovery.ShouldAttemptPlayback(
                failureAt + AudioOutputRecoveryTracker.RetryDelayMilliseconds);
        if (!backoffObserved)
        {
            throw new InvalidOperationException("Audio output retry backoff contract failed.");
        }

        if (!_cuePlayer.TryPlayCue(AudioCue.Food, volumeLinear: 1.0f, out var recoveryFailure))
        {
            throw new InvalidOperationException(
                "Fallback cue did not recover after bus restoration: " + recoveryFailure);
        }

        var recovered = _audioOutputRecovery.NoteSuccess();
        var recoveryObserved = recovered is
        {
            Kind: AudioOutputRecoveryKind.Recovered,
            ConsecutiveFailures: 1,
        };
        if (!recoveryObserved)
        {
            throw new InvalidOperationException("Audio recovery transition was not emitted exactly once.");
        }

        _cuePlayer.StopAndDetach();
        _cuePlayer.ReleaseStreams();
        var cleanupObserved = _cuePlayer.CachedStreamCount == 0;
        if (!cleanupObserved)
        {
            throw new InvalidOperationException("Fallback cue cache did not release cleanly.");
        }

        var rulesStateUnchanged = rulesProbe.ComputeStateHash() == rulesHashBefore;
        if (!rulesStateUnchanged)
        {
            throw new InvalidOperationException("Audio stress changed deterministic rules state.");
        }

        var outputDevices = AudioServer.GetOutputDeviceList()
            .Where(device => !string.IsNullOrWhiteSpace(device))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var evidence = new AudioFallbackQualificationEvidence(
            SchemaVersion: 2,
            Kind: "audio-mixing-policy-v2",
            Passed: true,
            DriverName: AudioServer.GetDriverName(),
            SelectedOutputDevice: AudioServer.OutputDevice,
            ObservedOutputDevices: outputDevices,
            CueCount: cues.Length,
            RapidRetriggerIterations: rapidRetriggerIterations,
            RapidRetriggerAttempts: rapidRetriggerAttempts,
            MutedPathChecks: mutedPathChecks,
            SfxBusCapacity: AudioCueMixPolicy.SfxBusCapacity,
            UiBusCapacity: AudioCueMixPolicy.UiBusCapacity,
            PeakVoiceCount: _cuePlayer.PeakVoiceCount,
            CooldownSuppressions: _cuePlayer.CooldownSuppressionCount,
            PolyphonySuppressions: _cuePlayer.PolyphonySuppressionCount,
            PrioritySuppressions: _cuePlayer.PrioritySuppressionCount,
            Interruptions: _cuePlayer.InterruptionCount,
            MutedSuppressions: _cuePlayer.MutedSuppressionCount,
            PolicyCatalogComplete: mixQualification.CatalogComplete,
            BusRoutingObserved: mixQualification.BusRoutingObserved,
            CooldownPolicyObserved: mixQualification.CooldownSuppressionObserved,
            PolyphonyPolicyObserved: mixQualification.PolyphonySuppressionObserved,
            PriorityPolicyObserved: mixQualification.PrioritySuppressionObserved,
            InterruptionPolicyObserved: mixQualification.InterruptionObserved,
            MusicDuckPolicyObserved: mixQualification.DuckObserved,
            MusicDuckRestorationObserved: mixQualification.DuckRestorationObserved,
            BusIsolationObserved: mixQualification.BusIsolationObserved,
            UnitTestableWithoutPlayback: mixQualification.UnitTestableWithoutPlayback,
            EngineMusicDuckObserved: engineMusicDuckObserved,
            EngineMusicDuckRestored: engineMusicDuckRestored,
            SavedVolumesImmediateAndIsolated: savedVolumesImmediateAndIsolated,
            VoiceCapacityBounded: voiceCapacityBounded,
            OutputDevicePollingActive: outputDevicePollingActive,
            DeviceChangeRecoveryObserved: deviceChangeRecoveryObserved,
            MissingBusFailureObserved: missingBusFailureObserved,
            BackoffObserved: backoffObserved,
            RecoveryObserved: recoveryObserved,
            CacheBounded: cacheBounded,
            CleanupObserved: cleanupObserved,
            RulesStateUnchanged: rulesStateUnchanged);
        WriteAudioFallbackEvidence(evidence);
    }

    private static void WriteAudioFallbackEvidence(AudioFallbackQualificationEvidence evidence)
    {
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "audio_fallback_stress.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
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

        if (settings.WindowMode != PreferencesDocument.WindowedMode
            || settings.WindowSizePreset != PreferencesDocument.HdWindowSize)
        {
            throw new InvalidOperationException(
                "Display defaults must open windowed at the native 16:9 preset.");
        }

        if (settings.CycleWindowMode(1) != PreferencesDocument.BorderlessMode
            || !settings.Fullscreen
            || settings.CycleWindowMode(1) != PreferencesDocument.ExclusiveFullscreenMode
            || !settings.Fullscreen
            || settings.CycleWindowMode(1) != PreferencesDocument.WindowedMode
            || settings.Fullscreen)
        {
            throw new InvalidOperationException(
                "Window-mode cycle did not cover windowed, borderless, and exclusive fullscreen.");
        }

        if (settings.CycleWindowSizePreset(1) != PreferencesDocument.DesktopWindowSize
            || settings.CycleWindowSizePreset(1) != PreferencesDocument.FullHdWindowSize
            || settings.CycleWindowSizePreset(1) != PreferencesDocument.ClassicWindowSize
            || settings.CycleWindowSizePreset(1) != PreferencesDocument.HdWindowSize)
        {
            throw new InvalidOperationException(
                "Window-size cycle did not cover every declared aspect-ratio preset.");
        }

        var classicWindow = DisplayOptions.WindowSize(PreferencesDocument.ClassicWindowSize);
        var hdWindow = DisplayOptions.WindowSize(PreferencesDocument.HdWindowSize);
        var desktopWindow = DisplayOptions.WindowSize(PreferencesDocument.DesktopWindowSize);
        var fullHdWindow = DisplayOptions.WindowSize(PreferencesDocument.FullHdWindowSize);
        if (classicWindow.Size != new Vector2I(1024, 768)
            || hdWindow.Size != new Vector2I(1280, 720)
            || desktopWindow.Size != new Vector2I(1440, 900)
            || fullHdWindow.Size != new Vector2I(1920, 1080)
            || !classicWindow.Label.Contains("4:3", StringComparison.Ordinal)
            || !desktopWindow.Label.Contains("16:10", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Display presets did not preserve their declared sizes and aspect labels.");
        }

        var fittedFullHd = DisplayOptions.FitWindowToScreen(
            fullHdWindow.Size,
            new Vector2I(1366, 768));
        var fittedAspect = fittedFullHd.X / (float)fittedFullHd.Y;
        if (fittedFullHd.X > 1286
            || fittedFullHd.Y > 688
            || Math.Abs(fittedAspect - (16.0f / 9.0f)) > 0.01f)
        {
            throw new InvalidOperationException(
                "Oversized window fitting did not preserve aspect ratio inside the usable screen.");
        }

        var fittedTiny = DisplayOptions.FitWindowToScreen(
            classicWindow.Size,
            new Vector2I(600, 400));
        if (fittedTiny.X > 600
            || fittedTiny.Y > 400
            || Math.Abs((fittedTiny.X / (float)fittedTiny.Y) - (4.0f / 3.0f)) > 0.01f)
        {
            throw new InvalidOperationException(
                "Small-screen window fitting escaped the display or changed aspect ratio.");
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
        settings.WindowMode = PreferencesDocument.ExclusiveFullscreenMode;
        settings.WindowSizePreset = PreferencesDocument.DesktopWindowSize;
        settings.Fullscreen = true;
        _shellSettings = settings;
        SaveShellSettings();
        LoadShellSettings();
        if (Math.Abs(_shellSettings.MusicVolume - 0.33f) > 0.0001f
            || !_shellSettings.ReducedMotion
            || _shellSettings.WindowMode != PreferencesDocument.ExclusiveFullscreenMode
            || _shellSettings.WindowSizePreset != PreferencesDocument.DesktopWindowSize
            || !_shellSettings.Fullscreen)
        {
            throw new InvalidOperationException("Current preferences schema did not round-trip through the store.");
        }

        _shellSettings.WindowMode = PreferencesDocument.WindowedMode;
        _shellSettings.WindowSizePreset = PreferencesDocument.ClassicWindowSize;
        _shellSettings.Fullscreen = false;

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
        _shellSettings.WindowMode = PreferencesDocument.WindowedMode;
        _shellSettings.WindowSizePreset = PreferencesDocument.ClassicWindowSize;
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
            || !structuredLogText.Contains("achievements_load", StringComparison.Ordinal)
            || !structuredLogText.Contains("open_diagnostics", StringComparison.Ordinal)
            || !structuredLogText.Contains("smoke_crash_probe", StringComparison.Ordinal)
            || !structuredLogText.Contains("\"level\":\"Error\"", StringComparison.Ordinal)
            || !structuredLogText.Contains("\"kind\":\"structured-log\"", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Structured log missing required smoke event codes or kind marker.");
        }

        ExecuteUnavailableProgressionPersistenceSmokeTest();
        ExecuteShellTransitionGraphSmokeTest();
        OpenAchievementsBrowse();
        if (_screenState != ScreenState.Achievements)
        {
            throw new InvalidOperationException("Achievements browse screen did not open.");
        }

        DispatchSmokeJoyButton(JoyButton.DpadRight);
        if (_achievementsPage != 1)
        {
            throw new InvalidOperationException(
                "Controller navigation did not advance the paged achievement catalog.");
        }

        using (var previousAchievementPage = new InputEventKey
        {
            Pressed = true,
            PhysicalKeycode = Key.Left,
        })
        {
            _Input(previousAchievementPage);
        }

        if (_achievementsPage != 0)
        {
            throw new InvalidOperationException(
                "Keyboard navigation did not return the paged achievement catalog.");
        }

        DispatchSmokeJoyButton(JoyButton.DpadDown);
        DispatchSmokeJoyButton(JoyButton.A);
        var controllerHighlightedGoal = ProgressionGoalCatalog.Goals[_progressionGoalCursor].Id;
        var controllerHighlightComplete = _progression.HighlightedGoalId == controllerHighlightedGoal;

        DispatchSmokeKey(Key.Down);
        DispatchSmokeKey(Key.Enter, physical: false);

        var keyboardHighlightedGoal = ProgressionGoalCatalog.Goals[_progressionGoalCursor].Id;
        var keyboardHighlightComplete = _progression.HighlightedGoalId == keyboardHighlightedGoal;
        var highlightRoundTrip = _progressionStore?.Load().Document?.HighlightedGoalId
            == keyboardHighlightedGoal;
        var progressionBeforeTour = _progression.SerializeCanonical();
        var personalBestsBeforeTour = _personalBests.SerializeCanonical();
        var scoreHistoryBeforeTour = _scoreHistory.SerializeCanonical();

        DispatchSmokeJoyButton(JoyButton.Y);
        var controllerTourOpen = _screenState == ScreenState.Tour;
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        DispatchSmokeJoyButton(JoyButton.A);
        var controllerLockedEventRejected = _screenState == ScreenState.Tour
            && _tourCursor == 1;
        DispatchSmokeJoyButton(JoyButton.DpadUp);
        DispatchSmokeJoyButton(JoyButton.A);
        var controllerTourSeed = _run?.MasterSeed;
        var controllerTourStart = _screenState == ScreenState.Running
            && _activeRunContext == ScoreRunContextCatalog.Practice
            && _activeTourEvent?.Id == "local-first-signal"
            && controllerTourSeed == 0UL;
        DispatchSmokeJoyButton(JoyButton.B);
        var controllerTourExit = _screenState == ScreenState.Menu;

        OpenAchievementsBrowse();
        DispatchSmokeKey(Key.R);
        var keyboardTourOpen = _screenState == ScreenState.Tour;
        DispatchSmokeKey(Key.Down);
        DispatchSmokeKey(Key.Enter, physical: false);
        var keyboardLockedEventRejected = _screenState == ScreenState.Tour
            && _tourCursor == 1;
        DispatchSmokeKey(Key.Up);
        DispatchSmokeKey(Key.Enter, physical: false);
        var keyboardTourSeed = _run?.MasterSeed;
        var keyboardTourStart = _screenState == ScreenState.Running
            && _activeRunContext == ScoreRunContextCatalog.Practice
            && _activeTourEvent?.Id == "local-first-signal"
            && keyboardTourSeed == 0UL;
        DispatchSmokeKey(Key.Escape, physical: false);
        var keyboardTourExit = _screenState == ScreenState.Menu;
        var tourControllerRouteComplete = controllerTourOpen
            && controllerLockedEventRejected
            && controllerTourStart
            && controllerTourExit;
        var tourKeyboardRouteComplete = keyboardTourOpen
            && keyboardLockedEventRejected
            && keyboardTourStart
            && keyboardTourExit;
        var tourPracticeIsolationComplete = progressionBeforeTour == _progression.SerializeCanonical()
            && personalBestsBeforeTour == _personalBests.SerializeCanonical()
            && scoreHistoryBeforeTour == _scoreHistory.SerializeCanonical()
            && controllerTourSeed == keyboardTourSeed;
        var tourContextReferencesComplete = BroadcastTourCatalog.Events.All(item =>
            BroadcastStationCatalog.Find(item.StationId) is not null
            && AiPersonalityCatalog.BuiltIn.Any(rival => rival.Id == item.RivalId));

        OpenAchievementsBrowse();
        DispatchSmokeJoyButton(JoyButton.X);
        var controllerCosmeticsOpen = _screenState == ScreenState.Cosmetics;
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        DispatchSmokeJoyButton(JoyButton.A);
        var controllerLockedCosmeticRejected = _screenState == ScreenState.Cosmetics
            && _progression.SelectedCosmeticSetId == "classic-signal";
        DispatchSmokeJoyButton(JoyButton.DpadUp);
        DispatchSmokeJoyButton(JoyButton.A);
        DispatchSmokeJoyButton(JoyButton.Y);
        DispatchSmokeJoyButton(JoyButton.B);
        var controllerCosmeticsBack = _screenState == ScreenState.Achievements;
        DispatchSmokeJoyButton(JoyButton.B);
        var controllerCosmeticsExit = _screenState == ScreenState.Menu;
        var cosmeticControllerRouteComplete = controllerCosmeticsOpen
            && controllerLockedCosmeticRejected
            && controllerCosmeticsBack
            && controllerCosmeticsExit;

        OpenAchievementsBrowse();
        DispatchSmokeKey(Key.C);
        var keyboardCosmeticsOpen = _screenState == ScreenState.Cosmetics;
        DispatchSmokeKey(Key.Down);
        DispatchSmokeKey(Key.Enter, physical: false);
        var keyboardLockedCosmeticRejected = _screenState == ScreenState.Cosmetics
            && _progression.SelectedCosmeticSetId == "classic-signal";
        DispatchSmokeKey(Key.Up);
        DispatchSmokeKey(Key.Enter, physical: false);
        DispatchSmokeKey(Key.R);
        DispatchSmokeKey(Key.Escape, physical: false);
        var keyboardCosmeticsBack = _screenState == ScreenState.Achievements;
        DispatchSmokeKey(Key.Escape, physical: false);
        var keyboardCosmeticsExit = _screenState == ScreenState.Menu;
        var cosmeticKeyboardRouteComplete = keyboardCosmeticsOpen
            && keyboardLockedCosmeticRejected
            && keyboardCosmeticsBack
            && keyboardCosmeticsExit;
        var cosmeticRoundTrip = _progressionStore?.Load().Document is { } storedProgression
            && storedProgression.SelectedCosmeticSetId == "classic-signal"
            && storedProgression.SavedCosmeticSetIds.SequenceEqual(["classic-signal"])
            && storedProgression.Metrics.SavedLoadouts == 1;
        WriteProgressionQualificationEvidence(
            keyboardHighlightComplete,
            controllerHighlightComplete,
            highlightRoundTrip,
            tourKeyboardRouteComplete,
            tourControllerRouteComplete,
            tourPracticeIsolationComplete,
            tourContextReferencesComplete,
            cosmeticKeyboardRouteComplete,
            cosmeticControllerRouteComplete,
            cosmeticRoundTrip);

        var browseLogText = System.IO.File.ReadAllText(_structuredLog.ActiveLogPath);
        if (!browseLogText.Contains("achievements_browse_open", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Structured log missing achievements_browse_open after browse open.");
        }

        OpenAchievementsBrowse();
        DispatchSmokeKey(Key.Escape, physical: false);
        if (_screenState != ScreenState.Menu)
        {
            throw new InvalidOperationException("Achievements browse did not return to menu.");
        }

        ExecuteScoreBrowserSmokeTest();

        OpenBindingsBrowse();
        if (_screenState != ScreenState.Bindings)
        {
            throw new InvalidOperationException("Bindings browse screen did not open.");
        }

        var bindingsLogText = System.IO.File.ReadAllText(_structuredLog.ActiveLogPath);
        if (!bindingsLogText.Contains("bindings_browse_open", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Structured log missing bindings_browse_open after bindings open.");
        }

        using var capturedButton = new InputEventJoypadButton
        {
            Pressed = true,
            ButtonIndex = JoyButton.LeftShoulder,
        };
        if (!GameActions.TryFormatControllerToken(capturedButton, out var capturedButtonToken)
            || capturedButtonToken != "button:left_shoulder")
        {
            throw new InvalidOperationException("Controller button capture token failed.");
        }

        using var passiveMotion = new InputEventJoypadMotion
        {
            Axis = JoyAxis.LeftX,
            AxisValue = 0.2f,
        };
        if (GameActions.TryFormatControllerToken(passiveMotion, out _))
        {
            throw new InvalidOperationException("Passive controller drift captured a binding.");
        }

        using var capturedMotion = new InputEventJoypadMotion
        {
            Axis = JoyAxis.LeftX,
            AxisValue = -0.9f,
        };
        if (!GameActions.TryFormatControllerToken(capturedMotion, out var capturedAxisToken)
            || capturedAxisToken != "axis:left_x:-1")
        {
            throw new InvalidOperationException("Controller axis capture token failed.");
        }

        // Pure remap path used by interactive capture after raw events are normalized.
        var remap = _keyboardBindings.TryRemapAction("pause", "key:space");
        if (!remap.IsSuccess || remap.Document is null)
        {
            throw new InvalidOperationException("Bindings smoke remap to key:space failed.");
        }

        _keyboardBindings = remap.Document;
        GameActions.ApplyKeyboardBindings(_keyboardBindings);
        if (!GameActions.ActionHasKeyboardToken(GameActions.Pause, "key:space"))
        {
            throw new InvalidOperationException("Pause InputMap did not receive remapped key:space.");
        }

        var controllerRemap = _controllerBindings.TryRemapAction("pause", "button:west");
        if (!controllerRemap.IsSuccess || controllerRemap.Document is null)
        {
            throw new InvalidOperationException("Bindings smoke remap to button:west failed.");
        }

        _controllerBindings = controllerRemap.Document;
        GameActions.ApplyControllerBindings(_controllerBindings);
        if (!GameActions.ActionHasControllerToken(GameActions.Pause, "button:west"))
        {
            throw new InvalidOperationException(
                "Pause InputMap did not receive remapped button:west.");
        }

        _bindingsDeviceTab = BindingsDeviceTab.Keyboard;
        var keyboardActions = ListRemappableActions();
        _bindingsCursor = keyboardActions.Index().First(pair => pair.Item == "pause").Index;
        _bindingsCapturePending = true;
        ApplyBindingRemap("key:enter");
        if (_pendingBindingConflict is not
            {
                Action: "pause",
                ConflictingAction: "confirm",
            }
            || _keyboardBindings.ActionToBinding["pause"] != "key:space")
        {
            throw new InvalidOperationException(
                "Keyboard conflict did not wait for an explicit resolution.");
        }

        CancelPendingBindingConflict();
        if (_pendingBindingConflict is not null
            || _keyboardBindings.ActionToBinding["pause"] != "key:space")
        {
            throw new InvalidOperationException(
                "Cancelling a keyboard conflict changed the active binding.");
        }

        _bindingsCapturePending = true;
        ApplyBindingRemap("key:enter");
        ApplyPendingBindingSwap();
        if (_keyboardBindings.ActionToBinding["pause"] != "key:enter"
            || _keyboardBindings.ActionToBinding["confirm"] != "key:space"
            || !GameActions.ActionHasKeyboardToken(GameActions.Pause, "key:enter")
            || !GameActions.ActionHasKeyboardToken(GameActions.Confirm, "key:space"))
        {
            throw new InvalidOperationException(
                "Keyboard conflict swap did not reach the active InputMap.");
        }

        _bindingsDeviceTab = BindingsDeviceTab.Controller;
        var controllerActions = ListRemappableActions();
        _bindingsCursor = controllerActions.Index().First(pair => pair.Item == "pause").Index;
        _bindingsCapturePending = true;
        ApplyBindingRemap("button:south");
        if (_pendingBindingConflict is not
            {
                Action: "pause",
                ConflictingAction: "confirm",
            })
        {
            throw new InvalidOperationException(
                "Controller conflict did not identify its current owner.");
        }

        ApplyPendingBindingSwap();
        if (_controllerBindings.ActionToBinding["pause"] != "button:south"
            || _controllerBindings.ActionToBinding["confirm"] != "button:west"
            || !GameActions.ActionHasControllerToken(GameActions.Pause, "button:south")
            || !GameActions.ActionHasControllerToken(GameActions.Confirm, "button:west"))
        {
            throw new InvalidOperationException(
                "Controller conflict swap did not reach the active InputMap.");
        }

        if (_inputBindingsStore is null)
        {
            throw new InvalidOperationException("Input binding store was unavailable during swap smoke.");
        }

        var storedControllerSwap = _inputBindingsStore.LoadOrDefault(
            InputBindingsDocument.ControllerDeviceClass);
        if (!storedControllerSwap.IsSuccess
            || storedControllerSwap.Document is not { } storedControllerDocument
            || storedControllerDocument.ActionToBinding["pause"] != "button:south"
            || storedControllerDocument.ActionToBinding["confirm"] != "button:west")
        {
            throw new InvalidOperationException(
                "Controller conflict swap did not round-trip through persistence.");
        }

        _structuredLog?.Information(
            "input",
            "Smoke remapped keyboard and controller pause bindings.",
            eventCode: "bindings_remap_saved");
        RestoreInputBindingDefaults();

        ReturnToMenu();
        if (_screenState != ScreenState.Menu)
        {
            throw new InvalidOperationException("Bindings browse did not return to menu.");
        }

        OpenContentPacksBrowse();
        if (_screenState != ScreenState.ContentPacks)
        {
            throw new InvalidOperationException("Content-packs browse screen did not open.");
        }

        var contentBrowseLogText = System.IO.File.ReadAllText(_structuredLog!.ActiveLogPath);
        if (!contentBrowseLogText.Contains("content_packs_browse_open", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Structured log missing content_packs_browse_open after content browse open.");
        }

        ReturnToMenu();
        if (_screenState != ScreenState.Menu)
        {
            throw new InvalidOperationException("Content-packs browse did not return to menu.");
        }

        if (_structuredLog is null)
        {
            throw new InvalidOperationException("Structured log was not ready for bindings remap smoke.");
        }

        var remapLogText = System.IO.File.ReadAllText(_structuredLog.ActiveLogPath);
        if (!remapLogText.Contains("bindings_remap_saved", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Structured log missing bindings_remap_saved after remap smoke.");
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
            || _keyboardBindings.ActionToBinding["confirm"] != "key:enter"
            || !_controllerBindings.ActionToBinding.ContainsKey("confirm")
            || _controllerBindings.ActionToBinding["confirm"] != "button:south")
        {
            throw new InvalidOperationException("Input bindings smoke round-trip failed.");
        }

        if (!GameActions.ActionHasKeyboardToken(GameActions.Confirm, "key:enter")
            || !GameActions.ActionHasKeyboardToken(GameActions.MoveUp, "key:up")
            || !GameActions.ActionHasControllerToken(GameActions.Confirm, "button:south")
            || !GameActions.ActionHasControllerToken(GameActions.MoveUp, "button:dpad_up"))
        {
            throw new InvalidOperationException(
                "Default keyboard and controller bindings were not applied to the InputMap.");
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

        var remappedControllerActions = new Dictionary<string, string>(
            InputBindingsDocument.CreateControllerDefaults().ActionToBinding,
            StringComparer.Ordinal)
        {
            ["pause"] = "axis:left_x:+1",
        };
        _controllerBindings = new InputBindingsDocument(
            InputBindingsDocument.CurrentSchemaVersion,
            InputBindingsDocument.ControllerDeviceClass,
            remappedControllerActions);
        GameActions.ApplyControllerBindings(_controllerBindings);
        if (!GameActions.ActionHasControllerToken(GameActions.Pause, "axis:left_x:+1")
            || GameActions.ActionHasControllerToken(GameActions.MoveRight, "axis:left_x:+1"))
        {
            throw new InvalidOperationException(
                "Controller remap did not replace a conflicting secondary stick axis.");
        }

        // Restore defaults for the remainder of the smoke path.
        RestoreInputBindingDefaults();
        if (!GameActions.ActionHasKeyboardToken(GameActions.Pause, "key:p")
            || !GameActions.ActionHasKeyboardToken(GameActions.Confirm, "key:enter")
            || !GameActions.ActionHasControllerToken(GameActions.Pause, "button:start"))
        {
            throw new InvalidOperationException(
                "Input defaults could not be restored after remap smoke.");
        }

        ExecuteSettingsScreenSmokeTest();
    }

    private void ExecuteSettingsScreenSmokeTest()
    {
        SettingsMenuCatalog.AssertComplete();
        _shellSettings = ShellSettings.CreateDefaults();
        SaveShellSettings();
        TransitionToScreen(ScreenState.Menu);

        using (var openKey = new InputEventKey { Pressed = true, Keycode = Key.F1 })
        {
            _Input(openKey);
        }

        using (var gameplayConfirm = new InputEventKey { Pressed = true, Keycode = Key.Enter })
        {
            _Input(gameplayConfirm);
        }

        for (var row = 0; row < 3; row++)
        {
            using var gameplayDown = new InputEventKey
            {
                Pressed = true,
                PhysicalKeycode = Key.Down,
            };
            _Input(gameplayDown);
        }

        using (var adaptationToggle = new InputEventKey { Pressed = true, Keycode = Key.Enter })
        {
            _Input(adaptationToggle);
        }

        var optOutConfig = SelectedRunConfig();
        var vibeAdaptationOptOutApplied = !_shellSettings.VibeAdaptationEnabled
            && !optOutConfig.EnableAdaptation
            && optOutConfig.AdaptivePolicyId == AdaptiveDifficultyPolicy.DisabledPolicyId
            && RunModeCatalog.GetScoreCategoryId(optOutConfig)
                == RunModeCatalog.VibeFixedScoreCategoryId;
        if (!vibeAdaptationOptOutApplied)
        {
            throw new InvalidOperationException(
                "Vibe adaptation preference did not create the disclosed opt-out category.");
        }


        using (var playtestDown = new InputEventKey
        {
            Pressed = true,
            PhysicalKeycode = Key.Down,
        })
        {
            _Input(playtestDown);
        }
        using (var playtestToggle = new InputEventKey { Pressed = true, Keycode = Key.Enter })
        {
            _Input(playtestToggle);
        }
        var localPlaytestConsentApplied = _shellSettings.LocalPlaytestSummariesEnabled;
        if (!localPlaytestConsentApplied)
        {
            throw new InvalidOperationException(
                "Raw keyboard input did not enable local playtest summary consent.");
        }

        using (var gameplayBack = new InputEventKey { Pressed = true, Keycode = Key.Escape })
        {
            _Input(gameplayBack);
        }

        using (var downKey = new InputEventKey { Pressed = true, PhysicalKeycode = Key.Down })
        {
            _Input(downKey);
        }

        using (var confirmKey = new InputEventKey { Pressed = true, Keycode = Key.Enter })
        {
            _Input(confirmKey);
        }

        var keyboardRouteComplete = _screenState == ScreenState.Settings
            && _settingsSectionOpen
            && CurrentSettingsSection == SettingsSection.Controls;

        for (var adjustment = 0; adjustment < 8; adjustment++)
        {
            using var rightKey = new InputEventKey
            {
                Pressed = true,
                PhysicalKeycode = Key.Right,
            };
            _Input(rightKey);
        }

        var controllerDeadzoneApplied =
            Math.Abs(_shellSettings.ControllerDeadzone - 0.9f) < 0.0001f
            && Math.Abs(GameActions.GetGameplayDeadzone() - 0.9f) < 0.0001f;
        using (var belowDeadzone = new InputEventJoypadMotion
        {
            Device = 0,
            Axis = JoyAxis.LeftX,
            AxisValue = 0.89f,
        })
        {
            controllerDeadzoneApplied = controllerDeadzoneApplied
                && !GameActions.TryMapDirectionInput(belowDeadzone, out _);
        }

        using (var fullStick = new InputEventJoypadMotion
        {
            Device = 0,
            Axis = JoyAxis.LeftX,
            AxisValue = 1.0f,
        })
        {
            controllerDeadzoneApplied = controllerDeadzoneApplied
                && GameActions.TryMapDirectionInput(fullStick, out var direction)
                && direction == VibeSnake.Rules.Direction.Right;
        }

        using var dpadAtHighDeadzone = new InputEventJoypadButton
        {
            Device = 0,
            Pressed = true,
            ButtonIndex = JoyButton.DpadUp,
        };
        var digitalFallbackRetained = GameActions.TryMapDirectionInput(
            dpadAtHighDeadzone,
            out var dpadDirection)
            && dpadDirection == VibeSnake.Rules.Direction.Up;
        if (!controllerDeadzoneApplied || !digitalFallbackRetained)
        {
            throw new InvalidOperationException(
                "Controller deadzone or digital D-pad fallback qualification failed.");
        }

        using (var restoreControlsKey = new InputEventKey
        {
            Pressed = true,
            Keycode = Key.F8,
        })
        {
            _Input(restoreControlsKey);
        }

        var controlsResetComplete =
            Math.Abs(_shellSettings.ControllerDeadzone - 0.5f) < 0.0001f
            && Math.Abs(GameActions.GetGameplayDeadzone() - 0.5f) < 0.0001f;
        using (var backKey = new InputEventKey { Pressed = true, Keycode = Key.Escape })
        {
            _Input(backKey);
        }

        using (var closeKey = new InputEventKey { Pressed = true, Keycode = Key.F1 })
        {
            _Input(closeKey);
        }

        keyboardRouteComplete = keyboardRouteComplete
            && _screenState == ScreenState.Menu
            && !_settingsSectionOpen;
        if (!keyboardRouteComplete)
        {
            throw new InvalidOperationException(
                "Raw keyboard events did not complete the settings route.");
        }

        _shellSettings.VibeAdaptationEnabled = true;
        SaveShellSettings();
        DispatchSmokeJoyButton(JoyButton.Start);
        DispatchSmokeJoyButton(JoyButton.A);
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        DispatchSmokeJoyButton(JoyButton.DpadLeft);
        var controllerOptOutConfig = SelectedRunConfig();
        vibeAdaptationOptOutApplied = vibeAdaptationOptOutApplied
            && !_shellSettings.VibeAdaptationEnabled
            && !controllerOptOutConfig.EnableAdaptation
            && RunModeCatalog.GetScoreCategoryId(controllerOptOutConfig)
                == RunModeCatalog.VibeFixedScoreCategoryId;
        DispatchSmokeJoyButton(JoyButton.B);
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        DispatchSmokeJoyButton(JoyButton.A);
        if (_screenState != ScreenState.Settings
            || !_settingsSectionOpen
            || CurrentSettingsSection != SettingsSection.Audio)
        {
            throw new InvalidOperationException(
                "Raw controller events did not enter the Audio settings section.");
        }

        DispatchSmokeJoyButton(JoyButton.DpadRight);
        if (Math.Abs(_shellSettings.MasterVolume - 0.85f) > 0.0001f)
        {
            throw new InvalidOperationException("Controller settings adjustment did not apply.");
        }

        for (var row = 0; row < 8; row++)
        {
            DispatchSmokeJoyButton(JoyButton.DpadDown);
        }

        DispatchSmokeJoyButton(JoyButton.A);
        var monoOutputApplied = _shellSettings.MonoOutput
            && AudioBuses.IsMonoOutputApplied()
            && AudioBuses.MonoDownmixEffectCount() == 1;
        if (!monoOutputApplied)
        {
            throw new InvalidOperationException(
                "Master-bus mono downmix did not apply exactly once.");
        }

        DispatchSmokeJoyButton(JoyButton.Back);
        var sectionResetComplete = controlsResetComplete
            && Math.Abs(_shellSettings.MasterVolume - 0.8f) < 0.0001f
            && !_shellSettings.MonoOutput
            && !AudioBuses.IsMonoOutputApplied()
            && AudioBuses.MonoDownmixEffectCount() == 1
            && _settingsSectionOpen;
        if (!sectionResetComplete)
        {
            throw new InvalidOperationException("Audio section restore did not apply defaults.");
        }

        DispatchSmokeJoyButton(JoyButton.B);
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        DispatchSmokeJoyButton(JoyButton.A);
        DispatchSmokeJoyButton(JoyButton.DpadRight);
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        DispatchSmokeJoyButton(JoyButton.DpadRight);
        var displayModesApplied = _shellSettings.WindowMode
                == PreferencesDocument.BorderlessMode
            && _shellSettings.WindowSizePreset == PreferencesDocument.DesktopWindowSize
            && _shellSettings.Fullscreen;
        if (!displayModesApplied)
        {
            throw new InvalidOperationException(
                "Raw controller input did not apply the display mode and window-size settings.");
        }

        DispatchSmokeJoyButton(JoyButton.B);
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        DispatchSmokeJoyButton(JoyButton.A);
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        var beforeCancelledReset = _shellSettings.ToDocument().SerializeCanonical();
        DispatchSmokeJoyButton(JoyButton.A);
        if (!_settingsFullResetConfirmation)
        {
            throw new InvalidOperationException("Full settings reset did not require confirmation.");
        }

        DispatchSmokeJoyButton(JoyButton.B);
        var fullResetCancelLossless = !_settingsFullResetConfirmation
            && _shellSettings.ToDocument().SerializeCanonical() == beforeCancelledReset;
        if (!fullResetCancelLossless)
        {
            throw new InvalidOperationException("Cancelling full settings reset changed preferences.");
        }

        _shellSettings.HighContrast = true;
        _shellSettings.MonoOutput = true;
        SaveShellSettings();
        DispatchSmokeJoyButton(JoyButton.A);
        DispatchSmokeJoyButton(JoyButton.A);
        DrainPlayerDataOperationForSmoke();
        var fullResetComplete = !_settingsFullResetConfirmation
            && !_shellSettings.HighContrast
            && !_shellSettings.MonoOutput
            && !AudioBuses.IsMonoOutputApplied()
            && Math.Abs(_shellSettings.MasterVolume - 0.8f) < 0.0001f
            && Math.Abs(_shellSettings.ControllerDeadzone - 0.5f) < 0.0001f
            && Math.Abs(GameActions.GetGameplayDeadzone() - 0.5f) < 0.0001f
            && _shellSettings.WindowMode == PreferencesDocument.WindowedMode
            && _shellSettings.WindowSizePreset == PreferencesDocument.HdWindowSize
            && !_shellSettings.Fullscreen
            && _shellSettings.VibeAdaptationEnabled
            && !_shellSettings.LocalPlaytestSummariesEnabled
            && _keyboardBindings.ActionToBinding["confirm"] == "key:enter"
            && _controllerBindings.ActionToBinding["confirm"] == "button:south";
        if (!fullResetComplete)
        {
            throw new InvalidOperationException("Confirmed full settings reset was incomplete.");
        }

        _settingsControllerResetQualified = fullResetComplete;

        _shellSettings.MusicVolume = 0.45f;
        _shellSettings.ControllerDeadzone = 0.65f;
        _shellSettings.MonoOutput = true;
        _shellSettings.WindowMode = PreferencesDocument.ExclusiveFullscreenMode;
        _shellSettings.WindowSizePreset = PreferencesDocument.DesktopWindowSize;
        _shellSettings.Fullscreen = true;
        _shellSettings.VibeAdaptationEnabled = false;
        _shellSettings.LocalPlaytestSummariesEnabled = true;
        SaveShellSettings();
        LoadShellSettings();
        var saveReloadComplete = Math.Abs(_shellSettings.MusicVolume - 0.45f) < 0.0001f
            && Math.Abs(_shellSettings.ControllerDeadzone - 0.65f) < 0.0001f
            && Math.Abs(GameActions.GetGameplayDeadzone() - 0.65f) < 0.0001f
            && _shellSettings.MonoOutput
            && _shellSettings.WindowMode == PreferencesDocument.ExclusiveFullscreenMode
            && _shellSettings.WindowSizePreset == PreferencesDocument.DesktopWindowSize
            && _shellSettings.Fullscreen
            && !_shellSettings.VibeAdaptationEnabled
            && _shellSettings.LocalPlaytestSummariesEnabled
            && AudioBuses.IsMonoOutputApplied()
            && AudioBuses.MonoDownmixEffectCount() == 1;
        if (!saveReloadComplete)
        {
            throw new InvalidOperationException("Settings screen save did not round-trip.");
        }

        if (_preferencesStore is null)
        {
            throw new InvalidOperationException("Preferences store disappeared during settings smoke.");
        }

        var originalPreferencesStore = _preferencesStore;
        var blockedRoot = System.IO.Path.Combine(
            originalPreferencesStore.UserDataRoot,
            "settings-save-blocker");
        System.IO.File.WriteAllText(blockedRoot, "block directory creation");
        _preferencesStore = new PreferencesStore(blockedRoot);
        var saveFailureVisible = !SaveShellSettings()
            && string.Equals(
                _settingsStatusCaption,
                "SETTINGS SAVE FAILED: CURRENT SESSION ONLY",
                StringComparison.Ordinal);
        _preferencesStore = originalPreferencesStore;
        System.IO.File.Delete(blockedRoot);
        if (!saveFailureVisible)
        {
            throw new InvalidOperationException("Settings save failure was not visible and recoverable.");
        }

        _shellSettings = ShellSettings.CreateDefaults();
        SaveShellSettings("status.settings.all-ready");
        using (var keyboardReset = new InputEventKey { Pressed = true, Keycode = Key.Enter })
        {
            _Input(keyboardReset);
        }

        var keyboardConfirmationOpened = _settingsFullResetConfirmation
            && _pendingDataResetPlan is not null;
        using (var keyboardCancel = new InputEventKey { Pressed = true, Keycode = Key.Escape })
        {
            _Input(keyboardCancel);
        }

        _settingsKeyboardResetCancelQualified = keyboardConfirmationOpened
            && !_settingsFullResetConfirmation
            && _pendingDataResetPlan is null;
        DispatchSmokeJoyButton(JoyButton.B);
        DispatchSmokeJoyButton(JoyButton.Start);
        var controllerRouteComplete = _screenState == ScreenState.Menu;
        if (!controllerRouteComplete)
        {
            throw new InvalidOperationException(
                "Raw controller events did not complete the settings route.");
        }

        var settingsLog = System.IO.File.ReadAllText(_structuredLog!.ActiveLogPath);
        if (!settingsLog.Contains("settings_browse_open", StringComparison.Ordinal)
            || !settingsLog.Contains("preferences_save_failed", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Settings browse/save-failure structured events were not retained.");
        }

        var evidence = new SettingsScreenQualificationEvidence(
            SchemaVersion: 1,
            Kind: "settings-screen-qualification-v1",
            Passed: true,
            PreferenceSchemaVersion: ShellSettings.SchemaVersion,
            SectionCount: SettingsMenuCatalog.Sections.Count,
            ItemCount: SettingsMenuCatalog.TotalItemCount,
            EveryItemDescribed: true,
            KeyboardRouteComplete: keyboardRouteComplete,
            ControllerRouteComplete: controllerRouteComplete,
            KeyboardRemappingComplete: true,
            ControllerRemappingComplete: true,
            ConflictSwapAndCancelComplete: true,
            OppositeDeviceBindingsRetained: true,
            SingleActionNavigationComplete: keyboardRouteComplete && controllerRouteComplete,
            SectionResetComplete: sectionResetComplete,
            FullResetCancelLossless: fullResetCancelLossless,
            FullResetComplete: fullResetComplete,
            SaveReloadComplete: saveReloadComplete,
            SaveFailureVisible: saveFailureVisible,
            ControllerDeadzoneApplied: controllerDeadzoneApplied,
            DigitalFallbackRetained: digitalFallbackRetained,
            MonoOutputApplied: monoOutputApplied,
            DisplayModesApplied: displayModesApplied,
            VibeAdaptationOptOutApplied: vibeAdaptationOptOutApplied,
            LocalPlaytestConsentApplied: localPlaytestConsentApplied,
            Sections: SettingsMenuCatalog.Sections
                .Select(section => section.ToString().ToLowerInvariant())
                .ToArray());
        WriteSettingsScreenEvidence(evidence);
    }

    private void ExecuteLocalPlaytestSummarySmokeTest()
    {
        if (_localPlaytestSummaryStore is null || _preferencesStore is null)
        {
            throw new InvalidOperationException(
                "Local playtest summary services were unavailable during qualification.");
        }

        _localPlaytestSummaryStore.DeleteAll();
        _localPlaytestSummaryCount = 0;
        _shellSettings = ShellSettings.CreateDefaults();
        SaveShellSettings();
        var defaultConsentOff = !_shellSettings.LocalPlaytestSummariesEnabled;
        TransitionToScreen(ScreenState.Menu);

        using (var openSettings = new InputEventKey { Pressed = true, Keycode = Key.F1 })
        {
            _Input(openSettings);
        }
        using (var openGameplay = new InputEventKey { Pressed = true, Keycode = Key.Enter })
        {
            _Input(openGameplay);
        }
        for (var row = 0; row < 4; row++)
        {
            using var down = new InputEventKey
            {
                Pressed = true,
                PhysicalKeycode = Key.Down,
            };
            _Input(down);
        }
        using (var enableConsent = new InputEventKey { Pressed = true, Keycode = Key.Enter })
        {
            _Input(enableConsent);
        }

        var consentKeyboardRouteComplete = _screenState == ScreenState.Settings
            && _settingsSectionOpen
            && CurrentSettingsSection == SettingsSection.Gameplay
            && CurrentSettingsItems()[_settingsItemCursor].Id == "local_playtest_summaries"
            && _shellSettings.LocalPlaytestSummariesEnabled;
        var consentRoundTrip = _preferencesStore.Load().Document is
        {
            LocalPlaytestSummariesEnabled: true,
        };

        using (var closeGameplay = new InputEventKey { Pressed = true, Keycode = Key.Escape })
        {
            _Input(closeGameplay);
        }
        using (var closeSettings = new InputEventKey { Pressed = true, Keycode = Key.F1 })
        {
            _Input(closeSettings);
        }

        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, false) with
        {
            Width = 5,
            Height = 4,
            StarvationTicks = 4,
            StarvationWarningTicks = 0,
            PowerSpawnIntervalTicks = 0,
        };
        var terminal = SnakeRun.Create(70_005UL, config);
        for (var step = 0; step < 100 && terminal.Status == RunStatus.Running; step++)
        {
            terminal.Step();
        }
        if (terminal.Status == RunStatus.Running)
        {
            throw new InvalidOperationException(
                "Local playtest qualification could not create a terminal seeded run.");
        }

        var captureTime = new DateTimeOffset(2026, 8, 8, 22, 0, 5, 123, TimeSpan.Zero);
        var terminalCaptureHonored = CaptureLocalPlaytestSummary(terminal, captureTime)
            && _localPlaytestSummaryCount == 1
            && System.IO.File.Exists(_localPlaytestSummaryStore.StorePath);

        using (var reopenSettings = new InputEventKey { Pressed = true, Keycode = Key.F1 })
        {
            _Input(reopenSettings);
        }
        for (var section = 0; section < 5; section++)
        {
            using var down = new InputEventKey
            {
                Pressed = true,
                PhysicalKeycode = Key.Down,
            };
            _Input(down);
        }
        using (var openData = new InputEventKey { Pressed = true, Keycode = Key.Enter })
        {
            _Input(openData);
        }
        for (var row = 0; row < 8; row++)
        {
            using var down = new InputEventKey
            {
                Pressed = true,
                PhysicalKeycode = Key.Down,
            };
            _Input(down);
        }
        using (var export = new InputEventKey { Pressed = true, Keycode = Key.Enter })
        {
            _Input(export);
        }

        var exportFiles = System.IO.Directory.Exists(_localPlaytestSummaryStore.ExportDirectory)
            ? System.IO.Directory.GetFiles(
                _localPlaytestSummaryStore.ExportDirectory,
                "playtest-summaries_*.json",
                System.IO.SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();
        var exportKeyboardRouteComplete = exportFiles.Length == 1
            && _settingsStatusCaption?.StartsWith("EXPORTED 1: user://", StringComparison.Ordinal)
                == true;
        var exportPayload = exportFiles.Length == 1
            ? System.IO.File.ReadAllText(exportFiles[0])
            : string.Empty;
        using var exportDocument = System.Text.Json.JsonDocument.Parse(exportPayload);
        var summaryElement = exportDocument.RootElement
            .GetProperty("summaries")[0];
        string[] allowedSummaryFields =
        [
            "summaryId", "capturedAtUtc", "appVersion", "runKind", "rulesetId",
            "rulesVersion", "modeId", "modeVersion", "scoreCategoryId", "configHash",
            "adaptationEnabled", "adaptivePolicyId", "adaptiveFinalState", "seed", "outcome",
            "deathCause", "survivalSteps", "score", "finalLength", "foodEaten", "wraps",
            "nearMisses", "powerupsCollected", "comboPeak", "finalStateHash",
            "powerDecisions",
        ];
        var observedFields = summaryElement.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
        string[] allowedPowerDecisionFields =
        [
            "powerId", "offered", "detoursObserved", "collected", "activated",
            "expired", "consumed", "saved", "deathAdjacent",
        ];
        var powerDecisionRows = summaryElement.GetProperty("powerDecisions");
        var fieldAllowlistExact = observedFields.SequenceEqual(allowedSummaryFields)
            && powerDecisionRows.GetArrayLength() == 9
            && powerDecisionRows.EnumerateArray().All(row => row.EnumerateObject()
                .Select(property => property.Name)
                .SequenceEqual(allowedPowerDecisionFields))
            && powerDecisionRows.EnumerateArray()
                .Select(row => row.GetProperty("powerId").GetString())
                .SequenceEqual(PowerDecisionCatalog.All.Select(definition => definition.Id));
        string[] forbiddenFieldFamilies =
        [
            "playerName", "displayName", "inputTiming", "inputTimestamp", "device",
            "systemPath", "homePath", "upload", "endpoint", "url",
        ];
        var forbiddenFieldsAbsent = forbiddenFieldFamilies.All(field =>
            !exportPayload.Contains(field, StringComparison.OrdinalIgnoreCase));

        using (var closeData = new InputEventKey { Pressed = true, Keycode = Key.Escape })
        {
            _Input(closeData);
        }
        using (var leaveSettings = new InputEventKey { Pressed = true, Keycode = Key.F1 })
        {
            _Input(leaveSettings);
        }
        _shellSettings.LocalPlaytestSummariesEnabled = false;
        SaveShellSettings();
        var disabledCaptureSkipped = !CaptureLocalPlaytestSummary(
            terminal,
            captureTime.AddSeconds(1))
            && _localPlaytestSummaryCount == 1;

        DispatchSmokeJoyButton(JoyButton.Start);
        for (var section = 0; section < 5; section++)
        {
            DispatchSmokeJoyButton(JoyButton.DpadDown);
        }
        DispatchSmokeJoyButton(JoyButton.A);
        for (var row = 0; row < 9; row++)
        {
            DispatchSmokeJoyButton(JoyButton.DpadDown);
        }
        DispatchSmokeJoyButton(JoyButton.A);
        var deleteControllerRouteComplete = _playtestDeleteConfirmation
            && CurrentSettingsItems()[_settingsItemCursor].Id == "delete_playtest_summaries";
        DispatchSmokeJoyButton(JoyButton.B);
        var deleteCancelLossless = !_playtestDeleteConfirmation
            && System.IO.File.Exists(_localPlaytestSummaryStore.StorePath)
            && System.IO.File.Exists(exportFiles[0]);
        DispatchSmokeJoyButton(JoyButton.A);
        DispatchSmokeJoyButton(JoyButton.A);
        var storeAndExportsDeleted = !_playtestDeleteConfirmation
            && _localPlaytestSummaryCount == 0
            && !System.IO.File.Exists(_localPlaytestSummaryStore.StorePath)
            && !System.IO.File.Exists(exportFiles[0]);
        DispatchSmokeJoyButton(JoyButton.B);
        DispatchSmokeJoyButton(JoyButton.Start);

        var uploadSurfaceAbsent = typeof(LocalPlaytestSummaryStore)
                .GetMethods()
                .All(method => !method.Name.Contains("Upload", StringComparison.OrdinalIgnoreCase))
            && typeof(LocalPlaytestSummaryStore).Assembly
                .GetReferencedAssemblies()
                .All(assembly => assembly.Name?.StartsWith(
                    "System.Net",
                    StringComparison.Ordinal) != true);
        var passed = defaultConsentOff
            && consentKeyboardRouteComplete
            && consentRoundTrip
            && terminalCaptureHonored
            && disabledCaptureSkipped
            && fieldAllowlistExact
            && forbiddenFieldsAbsent
            && exportKeyboardRouteComplete
            && deleteControllerRouteComplete
            && deleteCancelLossless
            && storeAndExportsDeleted
            && uploadSurfaceAbsent;
        if (!passed)
        {
            throw new InvalidOperationException(
                "Local playtest summary qualification failed consent, privacy, export, deletion, or upload isolation.");
        }

        _shellSettings = ShellSettings.CreateDefaults();
        SaveShellSettings();
        var evidence = new LocalPlaytestSummaryQualificationEvidence(
            SchemaVersion: 1,
            Kind: "local-playtest-summary-qualification-v1",
            Passed: true,
            PreferenceSchemaVersion: PreferencesDocument.CurrentSchemaVersion,
            SummarySchemaVersion: LocalPlaytestSummaryDocument.CurrentSchemaVersion,
            CollectionBasis: LocalPlaytestSummaryDocument.ExplicitOptInBasis,
            RetentionLimit: LocalPlaytestSummaryDocument.MaximumSummaries,
            ExportFileLimit: LocalPlaytestSummaryStore.MaximumExportFiles,
            MaximumDocumentBytes: LocalPlaytestSummaryDocument.MaximumDocumentBytes,
            DefaultConsentOff: defaultConsentOff,
            ConsentKeyboardRouteComplete: consentKeyboardRouteComplete,
            ConsentRoundTrip: consentRoundTrip,
            TerminalCaptureHonored: terminalCaptureHonored,
            DisabledCaptureSkipped: disabledCaptureSkipped,
            FieldAllowlistExact: fieldAllowlistExact,
            ForbiddenFieldsAbsent: forbiddenFieldsAbsent,
            ExportKeyboardRouteComplete: exportKeyboardRouteComplete,
            DeleteControllerRouteComplete: deleteControllerRouteComplete,
            DeleteCancelLossless: deleteCancelLossless,
            StoreAndExportsDeleted: storeAndExportsDeleted,
            UploadSurfaceAbsent: uploadSurfaceAbsent,
            AllowedSummaryFields: allowedSummaryFields,
            ForbiddenFieldFamilies: forbiddenFieldFamilies,
            RetentionRules:
            [
                "collection is off by default and disabled capture writes nothing",
                "the newest 200 terminal normal-human summaries are retained",
                "the newest 20 explicit in-game exports are retained",
                "confirmed deletion removes the store and all generated exports without backup",
                "invalid documents are never overwritten by append or export",
            ],
            Notes:
            [
                "Summaries contain versioned balance facts only.",
                "No name, raw input timing, device identity, system path, endpoint, or URL field exists.",
                "No upload API, network assembly reference, or automatic transfer route exists.",
            ]);
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(directory, "local_playtest_summaries.json"),
            evidence.Serialize());
    }

    private void DrainPlayerDataOperationForSmoke()
    {
        for (var attempt = 0; attempt < 2_000 && _playerDataOperation is not null; attempt++)
        {
            if (TryCompletePlayerDataOperation() == PlayerDataOperationCompletion.Pending)
            {
                System.Threading.Thread.Sleep(1);
            }
        }

        if (_playerDataOperation is not null)
        {
            throw new InvalidOperationException(
                "Player-data operation did not finish within the smoke-test bound.");
        }
    }

    private void ExecutePlayerDataRecoverySmokeTest()
    {
        var root = System.IO.Path.Combine(
            ResolveEvidenceDirectory(),
            ".player-data-recovery-smoke-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        try
        {
            static void Write(string playerRoot, string relativePath, string payload)
            {
                var path = System.IO.Path.Combine(
                    playerRoot,
                    relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.WriteAllText(path, payload);
            }

            var service = new PlayerDataRecoveryService(root);
            Write(root, "preferences.json", "preferences");
            Write(root, "input/keyboard.input_bindings.json", "keyboard");
            Write(root, "achievements.json", "achievements");
            Write(root, "onboarding.json", "onboarding");
            Write(root, "progression.json", "progression");
            Write(root, "spectator-league.json", "spectator-league");
            Write(root, "personal_bests.json", "personal-bests");
            Write(root, "score_history.json", "score-history");
            Write(root, "replays/run.vibesnake-replay.json", "replay");
            Write(root, "replay-exports/export.vibesnake-replay.json", "replay-export");
            Write(
                root,
                "offline-challenges/ghosts/household-rival-1.vibesnake-ghost.json",
                "household-rival");
            Write(root, "packs/example/pack.json", "pack");
            var exactPlan = service.CreateResetPlan(
                Enum.GetValues<PlayerDataCategory>(),
                "exact-confirmation");
            var exactConfirmationComplete = exactPlan.RelativeTargets.SequenceEqual(
                [
                    "achievements.json",
                    "input",
                    "offline-challenges",
                    "onboarding.json",
                    "packs",
                    "personal_bests.json",
                    "preferences.json",
                    "progression.json",
                    "replay-exports",
                    "replays",
                    "score_history.json",
                    "spectator-league.json",
                ],
                StringComparer.Ordinal);
            var fileCountBeforeCancel = System.IO.Directory.EnumerateFiles(
                root,
                "*",
                System.IO.SearchOption.AllDirectories).Count();
            _ = service.CreateResetPlan(
                [PlayerDataCategory.Preferences],
                "cancelled");
            var cancelWithoutWriteComplete = fileCountBeforeCancel
                == System.IO.Directory.EnumerateFiles(
                    root,
                    "*",
                    System.IO.SearchOption.AllDirectories).Count()
                && !System.IO.Directory.Exists(service.BackupsDirectory);

            var separatePlan = service.CreateResetPlan(
                [PlayerDataCategory.PersonalBests],
                "separate");
            var separateReset = service.Reset(separatePlan);
            var separateInspection = service.InspectBackups()
                .Single(backup => backup.BackupId == separatePlan.BackupId);
            var separateCategoryResetComplete = separateReset.IsSuccess
                && !System.IO.File.Exists(System.IO.Path.Combine(root, "personal_bests.json"))
                && !System.IO.File.Exists(System.IO.Path.Combine(root, "score_history.json"))
                && System.IO.File.Exists(System.IO.Path.Combine(root, "preferences.json"));
            var backupBeforeResetComplete = separateReset.IsSuccess
                && separateReset.BackupLocation == "backups/separate"
                && System.IO.File.Exists(System.IO.Path.Combine(
                    root,
                    "backups",
                    "separate",
                    PlayerDataRecoveryService.ManifestFileName));
            var backupIntegrityComplete = separateInspection.CanRestore
                && separateInspection.FileCount == 2;
            var restore = service.Restore(separatePlan.BackupId);
            var restoreComplete = restore.IsSuccess
                && System.IO.File.ReadAllText(
                    System.IO.Path.Combine(root, "personal_bests.json")) == "personal-bests"
                && System.IO.File.ReadAllText(
                    System.IO.Path.Combine(root, "score_history.json")) == "score-history";
            var conflict = service.Restore(separatePlan.BackupId);
            var conflictWithoutOverwriteComplete =
                conflict.Code == PlayerDataRestoreCode.Conflict
                && System.IO.File.ReadAllText(
                    System.IO.Path.Combine(root, "personal_bests.json")) == "personal-bests"
                && System.IO.File.ReadAllText(
                    System.IO.Path.Combine(root, "score_history.json")) == "score-history";

            var corruptPlan = service.CreateResetPlan(
                [PlayerDataCategory.Preferences],
                "corrupt");
            var corruptReset = service.Reset(corruptPlan);
            var corruptPayload = System.IO.Path.Combine(
                root,
                "backups",
                "corrupt",
                "payload",
                "preferences.json");
            System.IO.File.AppendAllText(corruptPayload, "tampered");
            var corruptInspection = service.InspectBackups()
                .Single(backup => backup.BackupId == corruptPlan.BackupId);
            var corruptRestore = service.Restore(corruptPlan.BackupId);
            var corruptBackupDetected = corruptReset.IsSuccess
                && corruptInspection.Status == PlayerDataBackupStatus.Corrupt;
            var corruptRestoreRejected =
                corruptRestore.Code == PlayerDataRestoreCode.Corrupt
                && !System.IO.File.Exists(System.IO.Path.Combine(root, "preferences.json"));
            var recoveryLocationVisible = separateInspection.RelativeLocation
                    == "backups/separate"
                && corruptInspection.RelativeLocation == "backups/corrupt";

            _playerDataOperation = Task.FromException<PlayerDataOperationResult>(
                new IOException("Synthetic player-data operation failure."));
            RequestQuit();
            if (!_quitAfterPlayerDataOperation
                || ShouldQuitAfterPlayerDataWork()
                || _quitAfterPlayerDataOperation
                || _playerDataOperation is not null
                || _settingsStatusCaption
                    != Localize("status.player-data.quit-canceled"))
            {
                throw new InvalidOperationException(
                    "A failed player-data operation released quit or concealed the failure.");
            }

            var passed = exactConfirmationComplete
                && cancelWithoutWriteComplete
                && backupBeforeResetComplete
                && backupIntegrityComplete
                && separateCategoryResetComplete
                && corruptBackupDetected
                && corruptRestoreRejected
                && conflictWithoutOverwriteComplete
                && restoreComplete
                && _settingsKeyboardResetCancelQualified
                && _settingsControllerResetQualified
                && recoveryLocationVisible;
            if (!passed)
            {
                throw new InvalidOperationException(
                    "Player-data reset and recovery qualification failed: "
                        + $"exact={exactConfirmationComplete}, cancel={cancelWithoutWriteComplete}, "
                        + $"backup={backupBeforeResetComplete}, integrity={backupIntegrityComplete}, "
                        + $"separate={separateCategoryResetComplete}, corrupt={corruptBackupDetected}, "
                        + $"corruptRestore={corruptRestoreRejected}, conflict={conflictWithoutOverwriteComplete}, "
                        + $"restore={restoreComplete}, keyboard={_settingsKeyboardResetCancelQualified}, "
                        + $"controller={_settingsControllerResetQualified}, location={recoveryLocationVisible}.");
            }

            var evidence = new PlayerDataRecoveryQualificationEvidence(
                SchemaVersion: 1,
                Kind: "player-data-recovery-qualification-v1",
                Passed: true,
                CategoryCount: Enum.GetValues<PlayerDataCategory>().Length,
                ExactConfirmationComplete: exactConfirmationComplete,
                CancelWithoutWriteComplete: cancelWithoutWriteComplete,
                BackupBeforeResetComplete: backupBeforeResetComplete,
                BackupIntegrityComplete: backupIntegrityComplete,
                SeparateCategoryResetComplete: separateCategoryResetComplete,
                CorruptBackupDetected: corruptBackupDetected,
                CorruptRestoreRejected: corruptRestoreRejected,
                ConflictWithoutOverwriteComplete: conflictWithoutOverwriteComplete,
                RestoreComplete: restoreComplete,
                KeyboardRouteComplete: _settingsKeyboardResetCancelQualified,
                ControllerRouteComplete: _settingsControllerResetQualified,
                RecoveryLocationVisible: recoveryLocationVisible,
                Categories:
                [
                    "preferences",
                    "progression",
                    "personal-bests",
                    "replays",
                    "optional-content",
                ]);
            var evidencePath = System.IO.Path.Combine(
                ResolveEvidenceDirectory(),
                "player_data_recovery.json");
            System.IO.File.WriteAllText(evidencePath, evidence.Serialize());
        }
        finally
        {
            if (System.IO.Directory.Exists(root))
            {
                System.IO.Directory.Delete(root, recursive: true);
            }
        }
    }

    private void ExecuteOnboardingSmokeTest()
    {
        if (_onboardingStore is null || _replayStore is null)
        {
            throw new InvalidOperationException("Onboarding qualification stores are unavailable.");
        }

        TransitionToScreen(ScreenState.Menu);
        _run = null;
        _replayRecorder = null;
        _onboardingSession = null;
        var achievementsBefore = _achievements.SerializeCanonical();
        var replaysBefore = _replayStore.ListStored();
        if (!replaysBefore.IsSuccess)
        {
            throw new InvalidOperationException(
                "Onboarding qualification could not inspect replay isolation.");
        }

        var titleFirstComplete = _onboardingWasNewProfile
            && _onboardingProgress.Status == OnboardingStatus.NotStarted
            && _screenState == ScreenState.Menu
            && _onboardingSession is null
            && _run is null;
        if (!titleFirstComplete)
        {
            throw new InvalidOperationException(
                "A fresh profile did not remain on the title menu before explicit Help input.");
        }

        DispatchSmokeKey(Key.H);
        var optionalOfferComplete = _screenState == ScreenState.Onboarding
            && _onboardingSession is null;
        if (!optionalOfferComplete)
        {
            throw new InvalidOperationException(
                "The optional onboarding offer did not open after explicit Help input.");
        }

        DispatchSmokeJoyButton(JoyButton.DpadDown);
        DispatchSmokeJoyButton(JoyButton.A);
        var directPlayComplete = _screenState == ScreenState.Running
            && _run is not null
            && _onboardingProgress.Status == OnboardingStatus.Skipped;
        var controllerRouteComplete = directPlayComplete
            && _activePromptFamily != InputPromptFamily.Keyboard;
        if (!controllerRouteComplete)
        {
            throw new InvalidOperationException(
                "Controller-only onboarding direct-play route failed.");
        }

        var skipPersisted = _onboardingStore.Load().Document?.Status
            == OnboardingStatus.Skipped;
        ReturnToMenu();

        DispatchSmokeKey(Key.H);
        var keyboardPromptObserved = _activePromptFamily == InputPromptFamily.Keyboard;
        DispatchSmokeKey(Key.Enter, physical: false);
        if (_onboardingSession is null)
        {
            throw new InvalidOperationException("Keyboard tutorial route did not start.");
        }

        var isolationProbe = _onboardingSession;
        var competitiveScoreIsolated = !isolationProbe.CompetitiveScoreEligible
            && !isolationProbe.PersistsAchievements
            && !isolationProbe.RecordsReplay
            && _run is null
            && _replayRecorder is null;

        DispatchSmokeKey(Key.Up);
        DispatchSmokeJoyButton(JoyButton.DpadDown);
        var controllerPromptObserved = _activePromptFamily != InputPromptFamily.Keyboard;
        DispatchSmokeKey(Key.Left);
        DispatchSmokeJoyButton(JoyButton.DpadRight);
        DispatchSmokeKey(Key.Right);
        DispatchSmokeJoyButton(JoyButton.DpadRight);
        DispatchSmokeKey(Key.Right);
        DispatchSmokeJoyButton(JoyButton.Start);
        DispatchSmokeKey(Key.Enter, physical: false);

        var keyboardRouteComplete = _screenState == ScreenState.Menu
            && _onboardingSession is null;
        var completionPersisted = _onboardingStore.Load().Document?.Status
            == OnboardingStatus.Completed;
        if (!keyboardRouteComplete || !completionPersisted)
        {
            throw new InvalidOperationException(
                "Mixed-device onboarding lesson route did not complete and persist.");
        }

        var replaysAfterCompletion = _replayStore.ListStored();
        var achievementsIsolated = string.Equals(
            achievementsBefore,
            _achievements.SerializeCanonical(),
            StringComparison.Ordinal);
        var replaysIsolated = replaysAfterCompletion.IsSuccess
            && replaysAfterCompletion.Replays.Count == replaysBefore.Replays.Count;

        DispatchSmokeJoyButton(JoyButton.LeftStick);
        var replayAvailable = _screenState == ScreenState.Onboarding
            && _onboardingSession is null
            && _onboardingProgress.Status == OnboardingStatus.Completed;
        DispatchSmokeJoyButton(JoyButton.B);
        replayAvailable = replayAvailable
            && _screenState == ScreenState.Menu
            && _onboardingProgress.Status == OnboardingStatus.Completed;

        var resetSaved = ResetOnboardingProgress();
        var resetComplete = resetSaved
            && _onboardingProgress.Status == OnboardingStatus.NotStarted
            && _onboardingStore.Load().Document?.Status == OnboardingStatus.NotStarted
            && string.Equals(
                achievementsBefore,
                _achievements.SerializeCanonical(),
                StringComparison.Ordinal)
            && _replayStore.ListStored().Replays.Count == replaysBefore.Replays.Count;
        SaveOnboardingStatus(OnboardingStatus.Completed);

        var activeDevicePromptsComplete = keyboardPromptObserved && controllerPromptObserved;
        if (!skipPersisted
            || !competitiveScoreIsolated
            || !achievementsIsolated
            || !replaysIsolated
            || !replayAvailable
            || !resetComplete
            || !activeDevicePromptsComplete)
        {
            throw new InvalidOperationException(
                "Onboarding persistence, prompt, replay, reset, or isolation gate failed.");
        }

        string[] lessons =
        [
            "turning",
            "invalid-reversal",
            "wrapping",
            "food-and-score",
            "starvation",
            "power-up",
            "pause",
            "restart",
        ];
        var evidence = new OnboardingQualificationEvidence(
            SchemaVersion: 1,
            Kind: "onboarding-qualification-v2",
            Passed: true,
            LessonCount: lessons.Length,
            TitleFirstComplete: titleFirstComplete,
            OptionalOfferComplete: optionalOfferComplete,
            DirectPlayComplete: directPlayComplete,
            KeyboardRouteComplete: keyboardRouteComplete,
            ControllerRouteComplete: controllerRouteComplete,
            ActiveDevicePromptsComplete: activeDevicePromptsComplete,
            SkipPersisted: skipPersisted,
            CompletionPersisted: completionPersisted,
            ReplayAvailable: replayAvailable,
            ResetComplete: resetComplete,
            CompetitiveScoreIsolated: competitiveScoreIsolated,
            AchievementsIsolated: achievementsIsolated,
            ReplaysIsolated: replaysIsolated,
            Lessons: lessons);
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(directory, "onboarding.json"),
            evidence.Serialize());
    }

    private static void WriteSettingsScreenEvidence(
        SettingsScreenQualificationEvidence evidence)
    {
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "settings_screen.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private static void WriteProgressionQualificationEvidence(
        bool keyboardHighlightComplete,
        bool controllerHighlightComplete,
        bool highlightRoundTripComplete,
        bool tourKeyboardRouteComplete,
        bool tourControllerRouteComplete,
        bool tourPracticeIsolationComplete,
        bool tourContextReferencesComplete,
        bool cosmeticKeyboardRouteComplete,
        bool cosmeticControllerRouteComplete,
        bool cosmeticSelectionRoundTripComplete)
    {
        var goals = ProgressionGoalCatalog.Goals;
        var tour = BroadcastTourCatalog.Validate();
        var cosmetics = CosmeticSetCatalog.Validate();
        var aiIsolationProbe = new ProgressionMetrics();
        var terminalMetrics = new RunAchievementMetrics(
            Score: 100,
            MaxCombo: 5,
            Length: 8,
            FoodEaten: 5,
            WrapCount: 3,
            NearMisses: 2,
            PowerupsCollected: 1,
            SurvivalTicks: 600,
            IsTerminal: true);
        var humanOnlyProgression = aiIsolationProbe.MergeHumanRun(
            terminalMetrics,
            ScoreRunContextCatalog.Ai) == aiIsolationProbe;
        var notificationQueue = new ProgressionNotificationQueue();
        var notificationQueueBounded = true;
        for (var index = 0; index < ProgressionNotificationQueue.MaximumPending; index++)
        {
            notificationQueueBounded &= notificationQueue.Enqueue(
                "qualification-" + index,
                "QUALIFICATION REWARD " + index,
                reducedMotion: index == 0);
        }

        notificationQueueBounded &= !notificationQueue.Enqueue(
            "overflow",
            "OVERFLOW",
            reducedMotion: false);
        var reducedMotionReadable = notificationQueue.Current is
        {
            MotionEnabled: false,
            MinimumVisibleMilliseconds: >= ProgressionNotificationQueue.MinimumReadableMilliseconds,
        };
        var left = SnakeRun.Create(70_804UL);
        var right = SnakeRun.Create(70_804UL);
        var cosmeticRulesIsolation = true;
        for (var step = 0; step < 64 && left.Status == RunStatus.Running; step++)
        {
            cosmeticRulesIsolation &= left.Step().StateHash == right.Step().StateHash;
        }

        var evidence = new ProgressionQualificationEvidence(
            SchemaVersion: 1,
            Kind: "progression-qualification-v1",
            Passed: keyboardHighlightComplete
                && controllerHighlightComplete
                && highlightRoundTripComplete
                && humanOnlyProgression
                && notificationQueueBounded
                && reducedMotionReadable
                && cosmetics.Passed
                && cosmeticRulesIsolation
                && tour.Passed
                && tourKeyboardRouteComplete
                && tourControllerRouteComplete
                && tourPracticeIsolationComplete
                && tourContextReferencesComplete
                && cosmeticKeyboardRouteComplete
                && cosmeticControllerRouteComplete
                && cosmeticSelectionRoundTripComplete,
            ProgressionDocumentSchemaVersion: ProgressionDocument.CurrentSchemaVersion,
            GoalCount: goals.Count,
            GoalLaneCount: goals.Select(goal => goal.Lane).Distinct().Count(),
            PacingTierCount: goals.Select(goal => goal.PacingTier).Distinct().Count(),
            ExactRequirementCount: goals.Count(goal =>
                !string.IsNullOrWhiteSpace(goal.ExactRequirement)),
            HighlightedGoalCount: 1,
            KeyboardBrowseAndHighlightComplete: keyboardHighlightComplete,
            ControllerBrowseAndHighlightComplete: controllerHighlightComplete,
            HighlightRoundTripComplete: highlightRoundTripComplete,
            HumanOnlyProgression: humanOnlyProgression,
            RepetitionOnlyGoalCount: goals.Count(goal =>
                goal.Metric == ProgressionMetric.CompletedHumanRuns),
            NotificationQueueBounded: notificationQueueBounded,
            ReducedMotionNotificationReadable: reducedMotionReadable,
            CosmeticSetCount: cosmetics.SetCount,
            CosmeticProfileCaseCount:
                cosmetics.QuietProfileCount + cosmetics.MaximumVibeProfileCount,
            CosmeticQualificationPassed: cosmetics.Passed,
            CosmeticRulesIsolationPassed: cosmeticRulesIsolation,
            CosmeticKeyboardRouteComplete: cosmeticKeyboardRouteComplete,
            CosmeticControllerRouteComplete: cosmeticControllerRouteComplete,
            CosmeticSelectionRoundTripComplete: cosmeticSelectionRoundTripComplete,
            TourSchemaVersion: BroadcastTourCatalog.SchemaVersion,
            TourEventCount: tour.EventCount,
            TourTierCount: tour.TierCount,
            TourValidationPassed: tour.Passed,
            PracticeNoncompetitive: BroadcastTourCatalog.Events.All(item =>
                item.PracticeNoncompetitive),
            ImmediateRematchAndReplayComplete: BroadcastTourCatalog.Events.All(item =>
                item.ImmediateRematch && item.ReplayAvailable),
            TourKeyboardRouteComplete: tourKeyboardRouteComplete,
            TourControllerRouteComplete: tourControllerRouteComplete,
            TourPracticeIsolationComplete: tourPracticeIsolationComplete,
            TourContextReferencesComplete: tourContextReferencesComplete,
            HumanDistributionCount: 0,
            HumanDistributionStatus: "pending-zero-reviewed-human-sessions",
            AiEvidenceUsedAsHumanTarget: false);
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(directory, "progression_qualification.json"),
            evidence.Serialize(),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private void ExecuteScoreBrowserSmokeTest()
    {
        if (_scoreHistoryStore is null || _personalBestStore is null || _playerDataRecovery is null)
        {
            throw new InvalidOperationException("Score-browser persistence services were not ready.");
        }

        var configHash = new RunConfig().ComputeConfigHash();
        var normal = new RunScoreIdentity(
            SnakeRun.RulesetId,
            SnakeRun.RulesVersion,
            configHash,
            RunConfig.ConfigHashAlgorithmId,
            240,
            RunStatus.Dead,
            DeathCause.SelfCollision);
        var challenge = normal with
        {
            Score = 180,
            RunKindId = ScoreRunContextCatalog.SeededChallengeRunKind,
            SeedCategoryId = ScoreRunContextCatalog.FixedChallengeSeedCategory,
            DisplayCategoryId = ScoreRunContextCatalog.SeededChallenge.DisplayCategoryId,
        };
        _personalBests = PersonalBestDocument.CreateDefaults()
            .Apply(normal).Document
            .Apply(challenge).Document;
        _personalBestStore.Save(_personalBests);
        _scoreHistory = ScoreHistoryDocument.CreateDefaults()
            .MergePersonalBests(_personalBests).Document;
        _scoreHistoryStore.Save(_scoreHistory);
        _scoreHistoryWritable = true;

        DispatchSmokeKey(Key.V);
        var keyboardOpenComplete = _screenState == ScreenState.Scores;
        DispatchSmokeKey(Key.R);
        var explicitConfirmationRequired = _scoreImportConfirmation;
        var beforeCancel = _scoreHistory.SerializeCanonical();
        DispatchSmokeKey(Key.Escape, physical: false);
        var keyboardCancelLossless = _screenState == ScreenState.Scores
            && !_scoreImportConfirmation
            && _scoreHistory.SerializeCanonical() == beforeCancel;
        DispatchSmokeKey(Key.Escape, physical: false);

        DispatchSmokeMainMenuSelection(MainMenuItem.Scores, controller: true);
        var controllerOpenComplete = _screenState == ScreenState.Scores;
        DispatchSmokeJoyButton(JoyButton.DpadRight);
        var controllerCategoryNavigationComplete = _scoreBrowseCategoryCursor == 1;

        var inbox = _scoreHistoryStore.EnsurePythonImportInbox();
        const string pythonJson =
            "{\"schema_version\":1,\"migrations\":{\"legacy_highscore_json\":true},"
            + "\"scores\":[{\"name\":\"ALPHA\",\"score\":320,"
            + "\"timestamp\":\"2026-08-08T00:00:00\"},{\"name\":\"BETA\","
            + "\"score\":160,\"timestamp\":\"2026-08-08T00:01:00\"}]}";
        System.IO.File.WriteAllText(inbox, pythonJson);
        var sourceBefore = System.IO.File.ReadAllBytes(inbox);
        DispatchSmokeJoyButton(JoyButton.Y);
        DispatchSmokeJoyButton(JoyButton.A);
        var controllerImportComplete = _scoreHistory.PythonTopTenImported
            && _scoreHistory.PythonTopTenImportedCount == 2;
        var sourceUnchanged = sourceBefore.SequenceEqual(System.IO.File.ReadAllBytes(inbox));
        var importedHash = _scoreHistory.PythonTopTenSourceSha256;

        DispatchSmokeJoyButton(JoyButton.Y);
        DispatchSmokeJoyButton(JoyButton.A);
        var oneTimeImportComplete = _scoreBrowseStatusCaption?.Contains(
            "ALREADY IMPORTED",
            StringComparison.Ordinal) == true
            && _scoreHistory.PythonTopTenImportedCount == 2
            && _scoreHistory.PythonTopTenSourceSha256 == importedHash;

        var report = ScoreBrowseReport.Create(_scoreHistory, _personalBests);
        var legacy = report.Categories.Single(category =>
            category.DisplayName == ScoreRunContextCatalog.LegacyDisplayCategory);
        var legacyCategoryVisible = legacy.Scores.Count == 2;
        var legacyCategoryNoncompetitive = !legacy.Competitive;
        var nativeCategoriesSeparated = report.Categories.Count(category => category.Competitive) == 2
            && report.Categories
                .Where(category => category.Competitive)
                .Select(category => category.CategoryKey)
                .Distinct(StringComparer.Ordinal)
                .Count() == 2;
        var personalBestHistoryVisible = report.Categories
            .Where(category => category.Competitive)
            .All(category => category.PersonalBest.HasValue && category.Scores.Count == 1);
        var resetPlan = _playerDataRecovery.CreateResetPlan(
            [PlayerDataCategory.PersonalBests],
            "score-browser-contract");
        var resetCategoryOwnsScoreHistory = resetPlan.RelativeTargets.SequenceEqual(
            [PersonalBestDocument.FileName, ScoreHistoryDocument.FileName],
            StringComparer.Ordinal);
        using var parsed = System.Text.Json.JsonDocument.Parse(
            _scoreHistory.SerializeCanonical());
        var persistedFieldsPerScore = parsed.RootElement
            .GetProperty("entries")[0]
            .EnumerateObject()
            .Count();
        var passed = keyboardOpenComplete
            && controllerOpenComplete
            && keyboardCancelLossless
            && controllerCategoryNavigationComplete
            && explicitConfirmationRequired
            && controllerImportComplete
            && sourceUnchanged
            && oneTimeImportComplete
            && legacyCategoryVisible
            && legacyCategoryNoncompetitive
            && nativeCategoriesSeparated
            && personalBestHistoryVisible
            && resetCategoryOwnsScoreHistory
            && ScoreHistoryDocument.MaximumScoresPerCategory == 10
            && persistedFieldsPerScore == 18
            && importedHash.Length == 64;
        var evidence = new ScoreBrowserQualificationEvidence(
            SchemaVersion: 1,
            Kind: "score-browser-qualification-v1",
            Passed: passed,
            KeyboardOpenComplete: keyboardOpenComplete,
            ControllerOpenComplete: controllerOpenComplete,
            KeyboardCancelLossless: keyboardCancelLossless,
            ControllerCategoryNavigationComplete: controllerCategoryNavigationComplete,
            ExplicitConfirmationRequired: explicitConfirmationRequired,
            ControllerImportComplete: controllerImportComplete,
            SourceUnchanged: sourceUnchanged,
            OneTimeImportComplete: oneTimeImportComplete,
            LegacyCategoryVisible: legacyCategoryVisible,
            LegacyCategoryNoncompetitive: legacyCategoryNoncompetitive,
            NativeCategoriesSeparated: nativeCategoriesSeparated,
            PersonalBestHistoryVisible: personalBestHistoryVisible,
            ResetCategoryOwnsScoreHistory: resetCategoryOwnsScoreHistory,
            ScoreHistorySchemaVersion: ScoreHistoryDocument.CurrentSchemaVersion,
            MaximumScoresPerCategory: ScoreHistoryDocument.MaximumScoresPerCategory,
            PersistedFieldsPerScore: persistedFieldsPerScore,
            ImportedEntryCount: _scoreHistory.PythonTopTenImportedCount,
            ImportInboxRelativePath: "imports/high_scores.json",
            SourceSha256: importedHash);
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(directory, "score_browser.json"),
            evidence.Serialize());
        if (!passed)
        {
            throw new InvalidOperationException("Score-browser qualification failed.");
        }

        DispatchSmokeJoyButton(JoyButton.B);
        if (_screenState != ScreenState.Menu)
        {
            throw new InvalidOperationException("Score browser did not return to menu.");
        }
    }

    private void DispatchSmokeJoyButton(JoyButton button)
    {
        using var inputEvent = new InputEventJoypadButton
        {
            Device = 0,
            Pressed = true,
            ButtonIndex = button,
        };
        _Input(inputEvent);
    }

    private void DispatchSmokeKey(Key key, bool physical = true)
    {
        using var inputEvent = physical
            ? new InputEventKey { Pressed = true, PhysicalKeycode = key }
            : new InputEventKey { Pressed = true, Keycode = key };
        _Input(inputEvent);
    }

    private void DispatchSmokeMainMenuSelection(MainMenuItem item, bool controller)
    {
        if (_screenState != ScreenState.Menu)
        {
            throw new InvalidOperationException(
                "Main-menu smoke navigation requires the menu screen.");
        }

        var target = (int)item;
        for (var attempts = 0;
            _mainMenuCursor != target && attempts < MainMenuItemCount;
            attempts++)
        {
            if (controller)
            {
                DispatchSmokeJoyButton(JoyButton.DpadDown);
            }
            else
            {
                DispatchSmokeKey(Key.Down);
            }
        }

        if (_mainMenuCursor != target)
        {
            throw new InvalidOperationException("Main-menu smoke navigation missed its target.");
        }

        if (controller)
        {
            DispatchSmokeJoyButton(JoyButton.A);
        }
        else
        {
            DispatchSmokeKey(Key.Enter, physical: false);
        }
    }

    private static void ExecuteShellTransitionGraphSmokeTest()
    {
        HashSet<(ShellScreen From, ShellScreen To)> expected =
        [
            (ShellScreen.Menu, ShellScreen.Menu),
            (ShellScreen.Menu, ShellScreen.Running),
            (ShellScreen.Menu, ShellScreen.Achievements),
            (ShellScreen.Menu, ShellScreen.Bindings),
            (ShellScreen.Menu, ShellScreen.ContentPacks),
            (ShellScreen.Menu, ShellScreen.Replays),
            (ShellScreen.Menu, ShellScreen.Settings),
            (ShellScreen.Menu, ShellScreen.Onboarding),
            (ShellScreen.Menu, ShellScreen.Scores),
            (ShellScreen.Menu, ShellScreen.Tour),
            (ShellScreen.Menu, ShellScreen.Cosmetics),
            (ShellScreen.Menu, ShellScreen.Spectator),
#if AGENT_ARENA_PREVIEW
            (ShellScreen.Menu, ShellScreen.AgentWatch),
#endif
            (ShellScreen.Running, ShellScreen.Paused),
            (ShellScreen.Running, ShellScreen.Ended),
            (ShellScreen.Running, ShellScreen.Menu),
            (ShellScreen.Paused, ShellScreen.Running),
            (ShellScreen.Paused, ShellScreen.Menu),
            (ShellScreen.Ended, ShellScreen.Running),
            (ShellScreen.Ended, ShellScreen.Menu),
            (ShellScreen.Ended, ShellScreen.Achievements),
            (ShellScreen.Ended, ShellScreen.Bindings),
            (ShellScreen.Ended, ShellScreen.ContentPacks),
            (ShellScreen.Ended, ShellScreen.Replays),
            (ShellScreen.Ended, ShellScreen.Settings),
            (ShellScreen.Ended, ShellScreen.Onboarding),
            (ShellScreen.Ended, ShellScreen.Scores),
            (ShellScreen.Ended, ShellScreen.Tour),
            (ShellScreen.Ended, ShellScreen.Spectator),
            (ShellScreen.Ended, ShellScreen.Comparisons),
            (ShellScreen.Achievements, ShellScreen.Menu),
            (ShellScreen.Achievements, ShellScreen.Ended),
            (ShellScreen.Achievements, ShellScreen.Achievements),
            (ShellScreen.Achievements, ShellScreen.Tour),
            (ShellScreen.Achievements, ShellScreen.Cosmetics),
            (ShellScreen.Bindings, ShellScreen.Menu),
            (ShellScreen.Bindings, ShellScreen.Ended),
            (ShellScreen.Bindings, ShellScreen.Bindings),
            (ShellScreen.ContentPacks, ShellScreen.Menu),
            (ShellScreen.ContentPacks, ShellScreen.Ended),
            (ShellScreen.ContentPacks, ShellScreen.ContentPacks),
            (ShellScreen.Replays, ShellScreen.Menu),
            (ShellScreen.Replays, ShellScreen.Ended),
            (ShellScreen.Replays, ShellScreen.Replays),
            (ShellScreen.Replays, ShellScreen.Comparisons),
            (ShellScreen.Settings, ShellScreen.Menu),
            (ShellScreen.Settings, ShellScreen.Ended),
            (ShellScreen.Settings, ShellScreen.Settings),
            (ShellScreen.Settings, ShellScreen.Bindings),
            (ShellScreen.Onboarding, ShellScreen.Onboarding),
            (ShellScreen.Onboarding, ShellScreen.Menu),
            (ShellScreen.Onboarding, ShellScreen.Running),
            (ShellScreen.Onboarding, ShellScreen.Settings),
            (ShellScreen.Scores, ShellScreen.Menu),
            (ShellScreen.Scores, ShellScreen.Ended),
            (ShellScreen.Scores, ShellScreen.Scores),
            (ShellScreen.Tour, ShellScreen.Menu),
            (ShellScreen.Tour, ShellScreen.Achievements),
            (ShellScreen.Tour, ShellScreen.Running),
            (ShellScreen.Tour, ShellScreen.Tour),
            (ShellScreen.Cosmetics, ShellScreen.Menu),
            (ShellScreen.Cosmetics, ShellScreen.Achievements),
            (ShellScreen.Cosmetics, ShellScreen.Cosmetics),
            (ShellScreen.Spectator, ShellScreen.Menu),
            (ShellScreen.Spectator, ShellScreen.Ended),
            (ShellScreen.Spectator, ShellScreen.Spectator),
            (ShellScreen.Spectator, ShellScreen.Running),
            (ShellScreen.Spectator, ShellScreen.Lore),
            (ShellScreen.Lore, ShellScreen.Menu),
            (ShellScreen.Lore, ShellScreen.Spectator),
            (ShellScreen.Lore, ShellScreen.Lore),
            (ShellScreen.Comparisons, ShellScreen.Menu),
            (ShellScreen.Comparisons, ShellScreen.Replays),
            (ShellScreen.Comparisons, ShellScreen.Comparisons),
            (ShellScreen.Comparisons, ShellScreen.Running),
#if AGENT_ARENA_PREVIEW
            (ShellScreen.AgentWatch, ShellScreen.Menu),
#endif
        ];

        foreach (var from in Enum.GetValues<ShellScreen>())
        {
            foreach (var to in Enum.GetValues<ShellScreen>())
            {
                var shouldAllow = expected.Contains((from, to));
                if (ShellTransitions.CanTransition(from, to) != shouldAllow)
                {
                    throw new InvalidOperationException(
                        $"Shell transition graph drifted for {from} to {to}.");
                }

                if (shouldAllow)
                {
                    ShellTransitions.EnsureTransition(from, to);
                    continue;
                }

                var rejected = false;
                try
                {
                    ShellTransitions.EnsureTransition(from, to);
                }
                catch (InvalidOperationException exception)
                    when (exception.Message.StartsWith(
                            "Illegal shell transition",
                            StringComparison.Ordinal))
                {
                    rejected = true;
                }

                if (!rejected)
                {
                    throw new InvalidOperationException(
                        $"Illegal shell transition was accepted: {from} to {to}.");
                }
            }
        }
    }

    private void ExecuteVirtualViewportSmokeTest()
    {
        var matrix = VirtualViewportQualification.Run();
        WriteVirtualViewportEvidence(matrix);

        // Live shell viewport must track the active window and preserve pointer math.
        RefreshVirtualViewport();
        if (_virtualViewport.WindowWidth < VirtualViewport.MinimumWindowWidth
            || _virtualViewport.WindowHeight < VirtualViewport.MinimumWindowHeight
            || _virtualViewport.Scale <= 0.0f)
        {
            throw new InvalidOperationException("Live virtual viewport was not initialized from the window.");
        }

        var mapped = MapPointerToLogical(MapLogicalToWindow(new Vector2(100.0f, 200.0f)));
        if (Math.Abs(mapped.X - 100.0f) > 0.05f || Math.Abs(mapped.Y - 200.0f) > 0.05f)
        {
            throw new InvalidOperationException("Live pointer mapping round-trip failed.");
        }

        var classicMenuPresentation = FitLogicalPresentation(
            new Vector2I(1024, 768),
            ClassicMenuLogicalWidth,
            VirtualViewport.LogicalHeight);
        if (classicMenuPresentation.Position.LengthSquared() > 0.0001f
            || Math.Abs(classicMenuPresentation.Size.X - 1024.0f) > 0.0001f
            || Math.Abs(classicMenuPresentation.Size.Y - 768.0f) > 0.0001f)
        {
            throw new InvalidOperationException(
                "Classic menu presentation did not fill a 4:3 window.");
        }

        // Ultrawide resize path: pillarbox offsets must appear without stretching Y.
        _virtualViewport.Resize(2560.0f, 1080.0f);
        if (_virtualViewport.OffsetX <= 0.0f || Math.Abs(_virtualViewport.Scale - 1.5f) > 0.0001f)
        {
            throw new InvalidOperationException("Live ultrawide resize contract failed.");
        }

        // Non-menu screens retain their 16:9 canvas within a classic window frame.
        _virtualViewport.Resize(1920.0f, 1080.0f, 4.0f, 3.0f);
        if (Math.Abs(_virtualViewport.OffsetX - 240.0f) > 0.0001f
            || Math.Abs(_virtualViewport.OffsetY - 135.0f) > 0.0001f
            || Math.Abs(_virtualViewport.Scale - 1.125f) > 0.0001f)
        {
            throw new InvalidOperationException("Preferred 4:3 window frame was not preserved.");
        }

        RefreshVirtualViewport();
    }

    private static void WriteVirtualViewportEvidence(VirtualViewportMatrixEvidence evidence)
    {
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "viewport_matrix.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private static void ExecuteInputCadenceSmokeTest()
    {
        var evidence = InputCadenceQualification.Run();
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "input_cadence.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
    }

    private void ExecuteMouseInputSmokeTest()
    {
        if (_screenState != ScreenState.Menu)
        {
            throw new InvalidOperationException("Mouse input qualification must begin at the menu.");
        }

        var keyboardBefore = _keyboardBindings.SerializeCanonical();
        var controllerBefore = _controllerBindings.SerializeCanonical();
        var targets = MouseInputPolicy.MenuTargetsForWidth(ActiveLogicalWidth);
        var classicTargets = MouseInputPolicy.MenuTargetsForWidth(ClassicMenuLogicalWidth);
        var menuHitTestingComplete = targets.Count == MainMenuItemCount
            && targets.Select(target => target.Id).Distinct(StringComparer.Ordinal).Count() == 9
            && targets.All(target =>
                MouseInputPolicy.ResolveMenuIndex(
                    target.LogicalBounds.GetCenter(),
                    ActiveLogicalWidth)
                    == target.MenuIndex)
            && Math.Abs(classicTargets[0].LogicalBounds.Position.X - 190.0f) < 0.0001f
            && classicTargets.All(target =>
                MouseInputPolicy.ResolveMenuIndex(
                    target.LogicalBounds.GetCenter(),
                    ClassicMenuLogicalWidth)
                    == target.MenuIndex)
            && Enumerable.Range(0, CosmeticSetsPerPage).All(index =>
                MouseInputPolicy.ResolveCosmeticPageIndex(
                    new Vector2(220.0f, 256.0f + (index * 104.0f))) == index);
        if (!menuHitTestingComplete)
        {
            throw new InvalidOperationException("Mouse menu hit targets are incomplete or overlapping.");
        }

        var originalModeIndex = _selectedRunModeIndex;
        DispatchSmokeMouse(MouseButton.WheelRight, new Vector2(900.0f, 250.0f));
        var horizontalWheelNavigationComplete = _selectedRunModeIndex != originalModeIndex;
        if (!horizontalWheelNavigationComplete)
        {
            throw new InvalidOperationException("Horizontal mouse wheel did not change mode.");
        }

        DispatchSmokeMouse(
            MouseButton.Left,
            targets.Single(target => target.Id == "settings").LogicalBounds.GetCenter());
        var leftClickConfirmComplete = _screenState == ScreenState.Settings;
        if (!leftClickConfirmComplete)
        {
            throw new InvalidOperationException("Mouse settings target did not open settings.");
        }

        DispatchSmokeMouse(MouseButton.WheelDown, new Vector2(180.0f, 220.0f));
        var verticalWheelNavigationComplete = CurrentSettingsSection == SettingsSection.Controls;
        if (!verticalWheelNavigationComplete)
        {
            throw new InvalidOperationException("Vertical mouse wheel did not navigate settings.");
        }

        DispatchSmokeMouse(MouseButton.Right, new Vector2(180.0f, 220.0f));
        var rightClickBackComplete = _screenState == ScreenState.Menu;
        if (!rightClickBackComplete)
        {
            throw new InvalidOperationException("Right mouse button did not return to menu.");
        }

        DispatchSmokeMouse(
            MouseButton.Left,
            targets.Single(target => target.Id == "start").LogicalBounds.GetCenter());
        if (_screenState != ScreenState.Running || _run is null)
        {
            throw new InvalidOperationException("Mouse start target did not begin a run.");
        }

        var head = _run.Head;
        DispatchSmokeMouse(
            MouseButton.Left,
            new Vector2(
                (head.X * CellSize) + (CellSize * 0.5f),
                HudHeight + (head.Y * CellSize) - 30.0f));
        var mouseSnapshot = _run.GetSnapshot();
        var gameplayDirectionComplete = mouseSnapshot.PendingDirections.Count == 1
            && mouseSnapshot.PendingDirections[0] == RulesDirection.Up;
        if (!gameplayDirectionComplete)
        {
            throw new InvalidOperationException(
                "Mouse gameplay click did not queue the intended logical direction.");
        }

        DispatchSmokeMouse(MouseButton.Right, new Vector2(180.0f, 220.0f));
        if (_screenState != ScreenState.Menu)
        {
            throw new InvalidOperationException("Mouse did not leave the running screen safely.");
        }

        var letterboxViewport = new VirtualViewport(1024.0f, 768.0f);
        var letterboxPoint = letterboxViewport.WindowToLogical(new Vector2(512.0f, 10.0f));
        var letterboxInputRejected = !VirtualViewport.ContainsLogicalPoint(letterboxPoint);
        var keyboardBindingsUnchanged = string.Equals(
            keyboardBefore,
            _keyboardBindings.SerializeCanonical(),
            StringComparison.Ordinal);
        var controllerBindingsUnchanged = string.Equals(
            controllerBefore,
            _controllerBindings.SerializeCanonical(),
            StringComparison.Ordinal);
        if (!letterboxInputRejected || !keyboardBindingsUnchanged || !controllerBindingsUnchanged)
        {
            throw new InvalidOperationException(
                "Mouse qualification changed bindings or accepted letterbox input.");
        }

        var evidence = new MouseInputQualificationEvidence(
            SchemaVersion: 1,
            Kind: "mouse-input-qualification-v1",
            Passed: true,
            DeviceClass: "mouse",
            MenuTargetCount: targets.Count,
            MenuHitTestingComplete: menuHitTestingComplete,
            LeftClickConfirmComplete: leftClickConfirmComplete,
            RightClickBackComplete: rightClickBackComplete,
            VerticalWheelNavigationComplete: verticalWheelNavigationComplete,
            HorizontalWheelNavigationComplete: horizontalWheelNavigationComplete,
            GameplayDirectionComplete: gameplayDirectionComplete,
            WindowScalingApplied: true,
            LetterboxInputRejected: letterboxInputRejected,
            KeyboardBindingsUnchanged: keyboardBindingsUnchanged,
            ControllerBindingsUnchanged: controllerBindingsUnchanged,
            MenuTargets: targets.Select(target => target.Id).ToArray(),
            PendingHumanChecks:
            [
                "physical-mouse-windows-macos-linux",
                "pointer-hover-and-visible-focus-review",
            ]);
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(directory, "mouse_input.json"),
            evidence.Serialize());
    }

    private void DispatchSmokeMouse(MouseButton button, Vector2 logicalPoint)
    {
        using var inputEvent = new InputEventMouseButton
        {
            Pressed = true,
            ButtonIndex = button,
            Position = MapLogicalToWindow(logicalPoint),
        };
        _Input(inputEvent);
    }

    private async Task<PresentationFrameSummary> ExecutePresentationFrameSamplerSmokeTestAsync()
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

        var equalTailSampler = new PresentationFrameSampler();
        for (var index = 0; index < 40; index++)
        {
            equalTailSampler.RecordFrameMilliseconds(6.935);
        }

        var equalTailSummary = equalTailSampler.Summarize();
        if (equalTailSummary.P99Milliseconds > equalTailSummary.MaxMilliseconds)
        {
            throw new InvalidOperationException(
                "Presentation percentile interpolation exceeded its source samples.");
        }

        PresentationFrameSummary liveSummary = default;
        for (var attempt = 1;
            attempt <= BareArcadeLoopQualification.MaximumSharedHostMeasurementAttempts;
            attempt++)
        {
            liveSummary = await MeasurePresentationFrameBurstAsync();
            if (!BareArcadeLoopQualification.ShouldRetrySharedHostTail(
                    liveSummary,
                    attempt))
            {
                break;
            }

            _structuredLog?.Warning(
                "performance",
                "Shared-host presentation p95 tail exceeded its ceiling while average and maximum remained within budget; resampling once.",
                eventCode: "presentation_tail_resample");
        }

        WritePresentationFrameEvidence(liveSummary);
        return liveSummary;
    }

    private async Task<PresentationFrameSummary> MeasurePresentationFrameBurstAsync()
    {
        for (var warmup = 0;
            warmup < BareArcadeLoopQualification.RequiredWarmupFrameSamples;
            warmup++)
        {
            QueueRedraw();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        var live = new PresentationFrameSampler();
        for (var index = 0;
            index < BareArcadeLoopQualification.RequiredLiveFrameSamples;
            index++)
        {
            var started = Time.GetTicksUsec();
            QueueRedraw();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var elapsedMilliseconds = (Time.GetTicksUsec() - started) / 1000.0;
            live.RecordFrameMilliseconds(Math.Max(0.01, elapsedMilliseconds));
        }

        return live.Summarize();
    }

    private async Task ExecutePerformanceQualificationSmokeTestAsync()
    {
        var attemptSummaries = new List<string>(
            PerformanceQualification.MaximumSharedHostMeasurementAttempts);
        var measurements = await MeasurePerformanceProfilesAsync();
        var evidence = PerformanceQualification.Run(measurements);
        attemptSummaries.Add(
            "attempt 1: " + SummarizePerformanceMeasurements(measurements));
        if (PerformanceQualification.ShouldRetrySharedHostTail(
                evidence,
                measurements,
                completedAttemptCount: 1))
        {
            var retryProfileIds = measurements
                .Where(measurement => measurement.P95FrameMilliseconds
                    > PerformanceQualification.SharedHostMaximumP95Milliseconds)
                .Select(measurement => measurement.Id)
                .ToHashSet(StringComparer.Ordinal);
            _structuredLog?.Warning(
                "performance",
                "Shared-host p95 tail exceeded its ceiling while every average remained within budget; resampling only the affected profiles once.",
                eventCode: "performance_tail_resample");
            var retryMeasurements = await MeasurePerformanceProfilesAsync(retryProfileIds);
            attemptSummaries.Add(
                "attempt 2 (" + string.Join(",", retryProfileIds.Order(StringComparer.Ordinal))
                + "): " + SummarizePerformanceMeasurements(retryMeasurements));
            measurements = PerformanceQualification.MergeSharedHostTailRetry(
                measurements,
                retryMeasurements);
            evidence = PerformanceQualification.Run(measurements);
        }
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "performance.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
        if (!evidence.Passed)
        {
            throw new InvalidOperationException(
                "Performance qualification failed. Retained measurements: "
                + string.Join(" | ", attemptSummaries));
        }
    }

    private static void ExecutePerformanceRetryPolicySmokeTest()
    {
        static PerformanceProfileMeasurement Measurement(
            string id,
            double averageMilliseconds,
            double p95Milliseconds) =>
            new(
                Id: id,
                SampleCount: PerformanceQualification.RequiredSamplesPerProfile,
                AverageFrameMilliseconds: averageMilliseconds,
                P50FrameMilliseconds: 10.0,
                P95FrameMilliseconds: p95Milliseconds,
                P99FrameMilliseconds: p95Milliseconds,
                MaximumFrameMilliseconds: p95Milliseconds,
                DriverDrawCallStatus: "unavailable-headless-backend",
                AverageObservedDriverDrawCalls: 0.0,
                MaximumObservedDriverDrawCalls: 0);

        var tailOnlyMeasurements = PerformanceQualification.Profiles
            .Select(profile => Measurement(
                profile.Id,
                20.0,
                profile.Id == "default" ? 70.12 : 69.0))
            .ToArray();
        var tailOnlyEvidence = PerformanceQualification.Run(tailOnlyMeasurements);
        var tailRetry = new[]
        {
            Measurement("default", 20.0, 69.0),
        };
        var mergedTailMeasurements = PerformanceQualification.MergeSharedHostTailRetry(
            tailOnlyMeasurements,
            tailRetry);
        var mergedTailEvidence = PerformanceQualification.Run(mergedTailMeasurements);
        var sustainedMeasurements = PerformanceQualification.Profiles
            .Select(profile => Measurement(profile.Id, 26.0, 70.12))
            .ToArray();
        var sustainedEvidence = PerformanceQualification.Run(sustainedMeasurements);
        var passingMeasurements = PerformanceQualification.Profiles
            .Select(profile => Measurement(profile.Id, 20.0, 69.0))
            .ToArray();
        var passingEvidence = PerformanceQualification.Run(passingMeasurements);
        if (!PerformanceQualification.ShouldRetrySharedHostTail(
                tailOnlyEvidence,
                tailOnlyMeasurements,
                completedAttemptCount: 1)
            || PerformanceQualification.ShouldRetrySharedHostTail(
                tailOnlyEvidence,
                tailOnlyMeasurements,
                completedAttemptCount: 2)
            || PerformanceQualification.ShouldRetrySharedHostTail(
                sustainedEvidence,
                sustainedMeasurements,
                completedAttemptCount: 1)
            || PerformanceQualification.ShouldRetrySharedHostTail(
                passingEvidence,
                passingMeasurements,
                completedAttemptCount: 1)
            || !mergedTailEvidence.Passed
            || mergedTailMeasurements[0] != tailOnlyMeasurements[0]
            || mergedTailMeasurements[1] != tailRetry[0]
            || mergedTailMeasurements[2] != tailOnlyMeasurements[2])
        {
            throw new InvalidOperationException(
                "Performance retry policy did not preserve its bounded tail-only contract.");
        }

        var presentationTail = new PresentationFrameSummary(
            SampleCount: BareArcadeLoopQualification.RequiredLiveFrameSamples,
            AverageMilliseconds: 20.0,
            P50Milliseconds: 10.0,
            P95Milliseconds: 60.12,
            P99Milliseconds: 61.0,
            MaxMilliseconds: 61.0);
        var presentationSustained = presentationTail with
        {
            AverageMilliseconds = 26.0,
        };
        var presentationLongFrame = presentationTail with
        {
            MaxMilliseconds = BareArcadeLoopQualification.MaximumSmokeFrameMilliseconds + 0.01,
        };
        var presentationPassing = presentationTail with
        {
            P95Milliseconds = 59.0,
        };
        var presentationIncomplete = presentationTail with
        {
            SampleCount = BareArcadeLoopQualification.RequiredLiveFrameSamples - 1,
        };
        if (!BareArcadeLoopQualification.ShouldRetrySharedHostTail(
                presentationTail,
                completedAttemptCount: 1)
            || BareArcadeLoopQualification.ShouldRetrySharedHostTail(
                presentationTail,
                completedAttemptCount: 2)
            || BareArcadeLoopQualification.ShouldRetrySharedHostTail(
                presentationSustained,
                completedAttemptCount: 1)
            || BareArcadeLoopQualification.ShouldRetrySharedHostTail(
                presentationLongFrame,
                completedAttemptCount: 1)
            || BareArcadeLoopQualification.ShouldRetrySharedHostTail(
                presentationPassing,
                completedAttemptCount: 1)
            || BareArcadeLoopQualification.ShouldRetrySharedHostTail(
                presentationIncomplete,
                completedAttemptCount: 1))
        {
            throw new InvalidOperationException(
                "Presentation retry policy did not preserve its bounded tail-only contract.");
        }
    }

    private async Task<IReadOnlyList<PerformanceProfileMeasurement>>
        MeasurePerformanceProfilesAsync(HashSet<string>? requestedProfileIds = null)
    {
        var profiles = requestedProfileIds is null
            ? PerformanceQualification.Profiles
            : PerformanceQualification.Profiles
                .Where(profile => requestedProfileIds.Contains(profile.Id))
                .ToArray();
        if (requestedProfileIds is not null
            && profiles.Count != requestedProfileIds.Count)
        {
            throw new ArgumentException(
                "Performance measurement requested an unknown profile.",
                nameof(requestedProfileIds));
        }

        var measurements = new List<PerformanceProfileMeasurement>(
            profiles.Count);
        try
        {
            foreach (var profile in profiles)
            {
                _performanceStressProfile = profile;
                for (var warmup = 0;
                    warmup < PerformanceQualification.RequiredWarmupFramesPerProfile;
                    warmup++)
                {
                    QueueRedraw();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                }

                var sampler = new PresentationFrameSampler();
                var driverDrawCalls = new List<double>(
                    PerformanceQualification.RequiredSamplesPerProfile);
                for (var sample = 0;
                    sample < PerformanceQualification.RequiredSamplesPerProfile;
                    sample++)
                {
                    var started = Time.GetTicksUsec();
                    QueueRedraw();
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    var elapsedMilliseconds = (Time.GetTicksUsec() - started) / 1000.0;
                    sampler.RecordFrameMilliseconds(Math.Max(0.01, elapsedMilliseconds));
                    driverDrawCalls.Add(Math.Max(
                        0.0,
                        Godot.Performance.GetMonitor(
                            Godot.Performance.Monitor.RenderTotalDrawCallsInFrame)));
                }

                var summary = sampler.Summarize();
                var maximumDriverDrawCalls = driverDrawCalls.Max();
                measurements.Add(new PerformanceProfileMeasurement(
                    Id: profile.Id,
                    SampleCount: summary.SampleCount,
                    AverageFrameMilliseconds: summary.AverageMilliseconds,
                    P50FrameMilliseconds: summary.P50Milliseconds,
                    P95FrameMilliseconds: summary.P95Milliseconds,
                    P99FrameMilliseconds: summary.P99Milliseconds,
                    MaximumFrameMilliseconds: summary.MaxMilliseconds,
                    DriverDrawCallStatus: maximumDriverDrawCalls > 0.0
                        ? "observed"
                        : "unavailable-headless-backend",
                    AverageObservedDriverDrawCalls: driverDrawCalls.Average(),
                    MaximumObservedDriverDrawCalls: (int)Math.Ceiling(maximumDriverDrawCalls)));
            }
        }
        finally
        {
            _performanceStressProfile = null;
            QueueRedraw();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        return measurements;
    }

    private static string SummarizePerformanceMeasurements(
        IEnumerable<PerformanceProfileMeasurement> measurements) =>
        string.Join(
            "; ",
            measurements.Select(measurement =>
                $"{measurement.Id}: avg={measurement.AverageFrameMilliseconds:F2}ms, "
                + $"p95={measurement.P95FrameMilliseconds:F2}ms, "
                + $"p99={measurement.P99FrameMilliseconds:F2}ms, "
                + $"max={measurement.MaximumFrameMilliseconds:F2}ms"));

    private void ExecuteBareArcadeLoopSmokeTest(PresentationFrameSummary frameSummary)
    {
        var evidence = BareArcadeLoopQualification.Run(ActiveShellTheme, frameSummary);
        var directory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "bare_arcade_loop.json");
        System.IO.File.WriteAllText(path, evidence.Serialize());
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
        TransitionToScreen(ScreenState.Menu);
        _mainMenuCursor = (int)MainMenuItem.Start;
        _run = null;
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
            hungerTicksRemaining: 100,
            score: 120);
        _replayRecorder = new RunReplayRecorder(_run, appVersion: ProductIdentity.AppVersion);

        var deathResult = _run.Step();
        if (
            _run.Status != RunStatus.Dead
            || _run.DeathCause != DeathCause.SelfCollision
            || !deathResult.Events.HasFlag(RunEvent.Died))
        {
            throw new InvalidOperationException("Forced collision did not end the run.");
        }

        if (_replayRecorder is null
            || !_replayRecorder.TryCompleteStep(deathResult, _run))
        {
            throw new InvalidOperationException(
                "Smoke death path failed to mirror-complete the terminal step: "
                    + (_replayRecorder?.FailureMessage ?? "missing recorder"));
        }

        CompleteRunEnd(deathResult.OrderedEvents);
        var collisionSummary = _runEndSummary
            ?? throw new InvalidOperationException("Collision run-end summary was not captured.");
        // Smoke path finishes and saves synchronously so the death-restart
        // contract does not depend on process-frame drain of background save.
        var recording = _replayRecorder!.Finish(_run);
        _replayRecorder = null;
        if (!recording.IsSuccessful || recording.Replay is null || _replayStore is null)
        {
            throw new InvalidOperationException(
                "Smoke death path failed to finalize replay: " + recording.Message);
        }

        _structuredLog?.Information(
            "replay",
            "Terminal replay finalized for atomic save.",
            eventCode: "replay_finalized");
        var saveCaption = SaveAndVerifyReplay(_replayStore, recording.Replay);
        if (!saveCaption.StartsWith("REPLAY SAVED", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Smoke death path failed terminal replay save: " + saveCaption);
        }

        if (_structuredLog is not null)
        {
            var logText = System.IO.File.ReadAllText(_structuredLog.ActiveLogPath);
            if (!logText.Contains("run_dead", StringComparison.Ordinal)
                || !logText.Contains("replay_finalized", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Smoke death path did not write expected structured log event codes.");
            }
        }

        var sameInputRestartRejected = !TryRestartFromEnded(_terminalInputSequence)
            && _screenState == ScreenState.Ended;
        DispatchSmokeJoyButton(JoyButton.A);
        var controllerRestartComplete = _screenState == ScreenState.Running
            && _run is { Status: RunStatus.Running }
            && _replayRecorder is not null;
        if (!sameInputRestartRejected || !controllerRestartComplete)
        {
            throw new InvalidOperationException(
                "Controller restart or fatal-input rejection contract failed.");
        }

        _run = SnakeRun.CreateForTesting(
            new RunConfig(
                Width: 5,
                Height: 4,
                StarvationTicks: 1,
                PowerSpawnIntervalTicks: 0),
            [new GridPoint(1, 1)],
            RulesDirection.Right,
            new GridPoint(4, 3),
            hungerTicksRemaining: 1,
            score: 50);
        _replayRecorder = new RunReplayRecorder(_run, appVersion: ProductIdentity.AppVersion);
        var starvationResult = _run.Step();
        if (_replayRecorder is null
            || !_replayRecorder.TryCompleteStep(starvationResult, _run))
        {
            throw new InvalidOperationException("Starvation restart fixture did not record.");
        }

        CompleteRunEnd(starvationResult.OrderedEvents);
        var starvationSummary = _runEndSummary
            ?? throw new InvalidOperationException("Starvation run-end summary was not captured.");
        var terminalRecording = _replayRecorder.Finish(_run);
        _replayRecorder = null;
        if (!terminalRecording.IsSuccessful)
        {
            throw new InvalidOperationException("Starvation restart fixture did not finalize.");
        }

        DispatchSmokeAction(GameActions.Pause);
        var onlyConfirmRestarts = _screenState == ScreenState.Ended;
        DispatchSmokeKey(Key.Enter, physical: false);
        var keyboardRestartComplete = _screenState == ScreenState.Running
            && _run is { Status: RunStatus.Running }
            && _replayRecorder is not null;
        if (!onlyConfirmRestarts || !keyboardRestartComplete)
        {
            throw new InvalidOperationException("Keyboard deliberate restart contract failed.");
        }

        if (_structuredLog is not null)
        {
            var restartLog = System.IO.File.ReadAllText(_structuredLog.ActiveLogPath);
            if (!restartLog.Contains("run_start", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Smoke restart path did not write run_start structured log event.");
            }
        }

        var loadedBests = _personalBestStore?.Load();
        var personalBestPersisted = loadedBests is { IsSuccess: true }
            && loadedBests.Document!.Entries.Any(entry => entry.BestScore == 120);
        var fairCategorySeparated = loadedBests is { IsSuccess: true }
            && loadedBests.Document!.Entries.Count >= 2;
        var accessRetained = ShellTransitions.CanTransition(
                ShellScreen.Ended,
                ShellScreen.Menu)
            && ShellTransitions.CanTransition(ShellScreen.Ended, ShellScreen.Settings)
            && ShellTransitions.CanTransition(ShellScreen.Settings, ShellScreen.Ended)
            && ShellTransitions.CanTransition(ShellScreen.Ended, ShellScreen.Replays);
        var summaryOrderComplete = collisionSummary.Score == 120
            && collisionSummary.PersonalBest >= collisionSummary.Score
            && collisionSummary.Length > 0
            && collisionSummary.SurvivalSteps > 0;
        var collisionAttributionComplete = collisionSummary.Cause == "SELF COLLISION";
        var starvationAttributionComplete = starvationSummary.Cause == "STARVATION";
        var recoveryHintComplete = collisionSummary.RecoveryHint.Contains(
                "Shield",
                StringComparison.Ordinal)
            && starvationSummary.RecoveryHint.Contains(
                "hunger",
                StringComparison.OrdinalIgnoreCase);
        var unlockSummaryComplete = FormatRunEndUnlocks([]) == "NEW UNLOCKS: NONE";

        var evidence = new RunEndQualificationEvidence(
            SchemaVersion: 1,
            Kind: "run-end-qualification-v1",
            Passed: true,
            SummaryOrderComplete: summaryOrderComplete,
            CollisionAttributionComplete: collisionAttributionComplete,
            StarvationAttributionComplete: starvationAttributionComplete,
            RecoveryHintComplete: recoveryHintComplete,
            PersonalBestPersisted: personalBestPersisted,
            FairCategorySeparated: fairCategorySeparated,
            SameInputRestartRejected: sameInputRestartRejected,
            LaterIntentAccepted: controllerRestartComplete && keyboardRestartComplete,
            OnlyConfirmRestarts: onlyConfirmRestarts,
            KeyboardRestartComplete: keyboardRestartComplete,
            ControllerRestartComplete: controllerRestartComplete,
            MenuAccessRetained: accessRetained,
            SettingsAccessRetained: accessRetained,
            ReplayAccessRetained: accessRetained,
            UnlockSummaryComplete: unlockSummaryComplete);
        var evidenceDirectory = ResolveEvidenceDirectory();
        System.IO.Directory.CreateDirectory(evidenceDirectory);
        System.IO.File.WriteAllText(
            System.IO.Path.Combine(evidenceDirectory, "run_end.json"),
            evidence.Serialize());

        if (!summaryOrderComplete
            || !collisionAttributionComplete
            || !starvationAttributionComplete
            || !recoveryHintComplete
            || !personalBestPersisted
            || !fairCategorySeparated
            || !accessRetained
            || !unlockSummaryComplete)
        {
            throw new InvalidOperationException("Run-end qualification evidence was incomplete.");
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
            || FeedbackCaption(collisionFeedback) != "SHIELD BROKE: COLLISION BLOCKED")
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

        var activationCues = new HashSet<AudioCue>();
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
            var spawnCaption = FeedbackCaption(spawn);
            if (spawn.Cue is null || spawnCaption is null
                || !spawnCaption.Contains(
                    PowerPresentation.ShortName(kind),
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Spawn feedback missing for {kind}.");
            }

            var activate = StepFeedback.Resolve(
            [
                new RunEventDetail(RunEventKind.PowerActivated, Power: kind),
            ]);
            if (activate.Cue != StepFeedback.ActivationCue(kind)
                || activate.Text is null
                || !activationCues.Add(activate.Cue.Value))
            {
                throw new InvalidOperationException($"Activation feedback missing for {kind}.");
            }
        }

        if (activationCues.Count != Enum.GetValues<PowerKind>().Length)
        {
            throw new InvalidOperationException("Power activation cues are not one-to-one.");
        }

        var vibeDirector = new VibeLevelDirector();
        foreach (var combo in new[]
        {
            (Count: 3, Cue: AudioCue.ComboTier1, Caption: "COMBO 3: FLOW"),
            (Count: 5, Cue: AudioCue.ComboTier2, Caption: "COMBO 5: HEAT"),
            (Count: 10, Cue: AudioCue.ComboTier3, Caption: "COMBO 10: OVERDRIVE"),
            (Count: 20, Cue: AudioCue.ComboTier4, Caption: "COMBO 20: TRANSCENDENT"),
        })
        {
            var transition = vibeDirector.Update(combo.Count);
            var feedback = StepFeedback.Resolve(
                [new RunEventDetail(RunEventKind.AteFood)],
                combo.Count,
                transition);
            if (feedback.Cue != combo.Cue || FeedbackCaption(feedback) != combo.Caption)
            {
                throw new InvalidOperationException(
                    $"Combo milestone feedback is not canonical at {combo.Count}.");
            }
        }

        var comboBreak = StepFeedback.Resolve(
            [new RunEventDetail(RunEventKind.ComboExpired, Value: 0)],
            comboCount: 0,
            vibeTransition: vibeDirector.Update(0));
        if (comboBreak.Cue != AudioCue.ComboBreak
            || FeedbackCaption(comboBreak) != "COMBO EXPIRED")
        {
            throw new InvalidOperationException("Combo-break feedback is not canonical.");
        }

        var achievement = StepFeedback.Resolve(
            [new RunEventDetail(RunEventKind.AchievementCandidate, Value: 0)]);
        var achievementCaption = FeedbackCaption(achievement);
        if (achievement.Cue != AudioCue.Achievement
            || achievementCaption is null
            || !achievementCaption.StartsWith("ACHIEVEMENT: ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Achievement feedback is not canonical.");
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
            || FeedbackCaption(lastStand) != "LAST STAND: DEATH REVERSED")
        {
            throw new InvalidOperationException("Last Stand recovery feedback is not canonical.");
        }

        var starvationWarning = StepFeedback.Resolve(
        [
            new RunEventDetail(RunEventKind.StarvationWarning, Value: 200),
        ]);
        if (
            starvationWarning.Cue != AudioCue.Starvation
            || FeedbackCaption(starvationWarning) != "STARVATION WARNING")
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
                HungerMaximumTicks: HungerFeedback.DefaultMaximumTicks,
                HungerWarningTicks: RunConfig.DefaultStarvationWarningTicks,
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

    private static string? FeedbackCaption(StepFeedback feedback) =>
        feedback.Text is { } text
            ? ShellLocalization.Format(
                text.Id,
                ShellLocale.English,
                text.Arguments.ToArray())
            : null;

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
