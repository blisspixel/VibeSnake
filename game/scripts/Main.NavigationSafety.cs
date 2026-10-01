using Godot;
using VibeSnake.Persistence;
using VibeSnake.Rules;
using RulesDirection = VibeSnake.Rules.Direction;

namespace VibeSnake.Game;

public partial class Main
{
    private long _replayNavigationGeneration;
    private long _replayOperationNavigationGeneration;
    private string? _replayOperationPriorDurableMessage;
    private QueuedReplayNavigationOperation? _queuedReplayNavigationOperation;

    private sealed record QueuedReplayNavigationOperation(
        long Generation,
        Func<ReplayOperationResult> Operation,
        string Caption,
        ReplayOperationKind Kind);

    private bool SettingsModalOwnsInput => _playerDataOperation is not null
        || _playerDataRecoveryBrowseOpen
        || _playtestDeleteConfirmation
        || _settingsFullResetConfirmation;

    private static bool IsDurableReplayOperation(ReplayOperationKind kind) => kind is
        ReplayOperationKind.Save or ReplayOperationKind.Export or ReplayOperationKind.Delete
        or ReplayOperationKind.GhostImport or ReplayOperationKind.GhostRunCardExport
        or ReplayOperationKind.GhostDelete;

    private static string ReplayCaptionWithCompletion(string caption, string? completion)
    {
        if (completion is null)
        {
            return caption;
        }

        const string separator = " | ";
        var partLimit = (MaximumReplayStatusCharacters - separator.Length) / 2;
        static string Bound(string value, int limit)
        {
            if (value.Length <= limit)
            {
                return value;
            }

            var length = limit - 3;
            if (char.IsHighSurrogate(value[length - 1]))
            {
                length--;
            }
            return value[..length] + "...";
        }
        return Bound(SanitizeReplayStatus(caption), partLimit) + separator
            + Bound(SanitizeReplayStatus(completion), partLimit);
    }

    private void StartOrQueueReplayNavigationOperation(
        Func<ReplayOperationResult> operation,
        string caption,
        ReplayOperationKind kind)
    {
        if (TryStartReplayResultOperation(operation, caption, kind))
        {
            return;
        }

        _queuedReplayNavigationOperation = new(
            _replayNavigationGeneration, operation, caption, kind);
        ShowReplayStatus(caption + "; WAITING FOR THE CURRENT REPLAY OPERATION");
    }

    private void ExecuteNavigationSafetySmokeTest()
    {
        var originalPromptFamily = _activePromptFamily;
        var originalSettingsCaption = _settingsStatusCaption;
        var originalReplayCaption = _replayStatusCaption;
        var originalSection = _settingsSectionCursor;
        try
        {
            QualifySettingsModalRestoreOwnership();
            QualifyReplayNavigationOwnership();
        }
        finally
        {
            _playerDataOperation = null;
            _playerDataRecoveryBrowseOpen = false;
            _playerDataBackups = [];
            _settingsFullResetConfirmation = false;
            _playtestDeleteConfirmation = false;
            _pendingDataResetPlan = null;
            _replayOperation = null;
            _replayOperationKind = null;
            _replayOperationPriorDurableMessage = null;
            _queuedReplaySave = null;
            _queuedReplayNavigationOperation = null;
            ReturnToMenu();
            _activePromptFamily = originalPromptFamily;
            _settingsSectionCursor = originalSection;
            _settingsStatusCaption = originalSettingsCaption;
            _replayStatusCaption = originalReplayCaption;
        }
        GD.Print("VIBESNAKE_NAVIGATION_SAFETY_OK settings_modals=4 replay_navigation=cancel-current-refresh-durable");
    }

    private void QualifySettingsModalRestoreOwnership()
    {
        TransitionToScreen(ScreenState.Settings);
        _settingsSectionCursor = (int)SettingsSection.Data;
        _settingsSectionOpen = true;
        var plan = new PlayerDataResetPlan("navigation-smoke", [PlayerDataCategory.Progression], []);
        _pendingDataResetPlan = plan;
        _settingsFullResetConfirmation = true;
        DispatchSmokeKey(Key.F8, physical: false);
        AssertPrimaryInput(ReferenceEquals(_pendingDataResetPlan, plan)
            && _settingsFullResetConfirmation,
            "Restore defaults replaced a reset plan under review.");
        _settingsFullResetConfirmation = false;
        _pendingDataResetPlan = null;

        _playtestDeleteConfirmation = true;
        DispatchSmokeJoyButton(JoyButton.Back);
        AssertPrimaryInput(_playtestDeleteConfirmation && !_settingsFullResetConfirmation
            && _pendingDataResetPlan is null,
            "Restore defaults opened a data reset behind local-summary deletion review.");
        _playtestDeleteConfirmation = false;

        _playerDataBackups = [new("navigation-smoke", "backups/navigation-smoke",
            PlayerDataBackupStatus.Valid, "Verified smoke backup", [PlayerDataCategory.Progression], 0, 0)];
        _playerDataRecoveryBrowseOpen = true;
        DispatchSmokeKey(Key.F8, physical: false);
        AssertPrimaryInput(_playerDataRecoveryBrowseOpen && !_settingsFullResetConfirmation
            && _pendingDataResetPlan is null,
            "Restore defaults opened a reset behind recovery browsing.");
        _playerDataRecoveryBrowseOpen = false;
        _playerDataBackups = [];

        var operation = new TaskCompletionSource<PlayerDataOperationResult>();
        _playerDataOperation = operation.Task;
        DispatchSmokeJoyButton(JoyButton.Back);
        AssertPrimaryInput(ReferenceEquals(_playerDataOperation, operation.Task)
            && !_settingsFullResetConfirmation && _pendingDataResetPlan is null
            && _settingsStatusCaption == Localize("settings.player-data.operation"),
            "Restore defaults bypassed an active player-data operation.");
        _playerDataOperation = null;
        ReturnToMenu();
    }

    private void QualifyReplayNavigationOwnership()
    {
        IReadOnlyList<RulesDirection>[] commands = [[RulesDirection.Up]];
        var config = RunModeCatalog.CreateConfig(RunModeCatalog.Vibe, enableAdaptation: false);
        var replay = RunReplay.Capture(SnakeRun.Create(SmokeSeed + 205, config), commands,
            capturedAtUtc: "2026-08-08T01:01:01.205Z");
        var playback = new RunReplayPlayback(replay);
        var race = new GhostRaceSession(SeedChallengeDescriptor.Create(replay), replay);

        TransitionToScreen(ScreenState.Replays);
        var canceledPlayback = StageControlledReplayOperation(ReplayOperationKind.PlaybackLoad);
        DispatchSmokeAction(GameActions.Back);
        OpenReplaysBrowse();
        AssertPrimaryInput(_queuedReplayNavigationOperation is not null,
            "Reopening the replay library did not retain a fresh read behind in-flight work.");
        canceledPlayback.SetResult(new("CANCELED PLAYBACK", Playback: playback));
        TryCompleteReplayOperation();
        AssertPrimaryInput(_replayPlayback is null && _screenState == ScreenState.Replays,
            "A canceled playback load activated in a reopened replay library.");
        CompleteControlledNavigationRefresh(ReplayOperationKind.BrowserLoad);
        ReturnToMenu();

        TransitionToScreen(ScreenState.Replays);
        TransitionToScreen(ScreenState.Comparisons);
        var canceledRace = StageControlledReplayOperation(ReplayOperationKind.GhostRaceLoad);
        DispatchSmokeAction(GameActions.Back);
        TransitionToScreen(ScreenState.Replays);
        OpenOfflineComparisons();
        canceledRace.SetResult(new("CANCELED GHOST RACE", GhostRace: race));
        TryCompleteReplayOperation();
        AssertPrimaryInput(_activeGhostRace is null && _run is null
            && _screenState == ScreenState.Comparisons,
            "A canceled ghost load launched a run in a reopened comparison library.");
        CompleteControlledNavigationRefresh(ReplayOperationKind.GhostList);
        ReturnToMenu();

        TransitionToScreen(ScreenState.Replays);
        var currentPlayback = StageControlledReplayOperation(ReplayOperationKind.PlaybackLoad);
        currentPlayback.SetResult(new("CURRENT PLAYBACK", Playback: playback));
        TryCompleteReplayOperation();
        AssertPrimaryInput(ReferenceEquals(_replayPlayback, playback),
            "A current playback request was rejected.");
        ReturnToMenu();

        TransitionToScreen(ScreenState.Replays);
        TransitionToScreen(ScreenState.Comparisons);
        var currentRace = StageControlledReplayOperation(ReplayOperationKind.GhostRaceLoad);
        currentRace.SetResult(new("CURRENT GHOST RACE", GhostRace: race));
        TryCompleteReplayOperation();
        AssertPrimaryInput(ReferenceEquals(_activeGhostRace, race)
            && _screenState == ScreenState.Running,
            "A current ghost request failed to launch its prepared run.");
        ReturnToMenu();

        foreach (var kind in new[] { ReplayOperationKind.Save, ReplayOperationKind.Export,
            ReplayOperationKind.Delete, ReplayOperationKind.GhostImport,
            ReplayOperationKind.GhostRunCardExport, ReplayOperationKind.GhostDelete })
        {
            TransitionToScreen(ScreenState.Replays);
            var durable = StageControlledReplayOperation(kind);
            ReturnToMenu();
            durable.SetResult(new("DURABLE " + kind));
            TryCompleteReplayOperation();
            AssertPrimaryInput(_replayStatusCaption == "DURABLE " + kind,
                "Navigation discarded durable replay-operation completion evidence.");
        }

        foreach (var kind in new[] { ReplayOperationKind.Save, ReplayOperationKind.Export, ReplayOperationKind.Delete })
        {
            foreach (var refreshFailed in new[] { false, true })
            {
                TransitionToScreen(ScreenState.Replays);
                var durable = StageControlledReplayOperation(kind);
                ReturnToMenu();
                TransitionToScreen(ScreenState.Replays);
                var refreshedCaption = refreshFailed
                    ? "REPLAY BROWSER UNAVAILABLE [IoError]: " + new string('X', 200)
                    : "REPLAY LIBRARY VERIFIED";
                StartOrQueueReplayNavigationOperation(
                    () => new(refreshedCaption, BrowserEntries: []),
                    "REPLAY LIBRARY VERIFICATION IN PROGRESS", ReplayOperationKind.BrowserLoad);
                var durableCaption = "DURABLE " + kind + " COMPLETED";
                durable.SetResult(new(durableCaption));
                TryCompleteReplayOperation();
                AssertPrimaryInput(_replayStatusCaption!.Contains(durableCaption, StringComparison.Ordinal)
                    && _replayStatusCaption.Contains("VERIFICATION IN PROGRESS", StringComparison.Ordinal),
                    "A deferred refresh concealed durable completion while loading.");
                CompleteControlledNavigationRefresh(ReplayOperationKind.BrowserLoad);
                AssertPrimaryInput(_replayStatusCaption!.Contains(durableCaption, StringComparison.Ordinal)
                    && _replayStatusCaption.StartsWith(refreshFailed
                        ? "REPLAY BROWSER UNAVAILABLE [IoError]"
                        : "REPLAY LIBRARY VERIFIED", StringComparison.Ordinal)
                    && _replayStatusCaption.Length <= MaximumReplayStatusCharacters,
                    "A refreshed library hid durable completion or its own failure.");
                ReturnToMenu();
            }
        }

        TransitionToScreen(ScreenState.Replays);
        var delayed = StageControlledReplayOperation(ReplayOperationKind.PlaybackLoad);
        ReturnToMenu();
        OpenReplaysBrowse();
        QueueReplaySave(() => "PRIORITY SAVE COMPLETE", "PRIORITY SAVE");
        delayed.SetResult(new("CANCELED PLAYBACK", Playback: playback));
        TryCompleteReplayOperation();
        AssertPrimaryInput(_replayOperationKind == ReplayOperationKind.Save
            && _queuedReplayNavigationOperation is not null,
            "A library refresh displaced a queued terminal replay save.");
        _replayOperation!.GetAwaiter().GetResult();
        TryCompleteReplayOperation();
        CompleteControlledNavigationRefresh(ReplayOperationKind.BrowserLoad);
        ReturnToMenu();

        TransitionToScreen(ScreenState.Replays);
        var departed = StageControlledReplayOperation(ReplayOperationKind.PlaybackLoad);
        ReturnToMenu();
        OpenReplaysBrowse();
        ReturnToMenu();
        departed.SetResult(new("CANCELED PLAYBACK", Playback: playback));
        TryCompleteReplayOperation();
        AssertPrimaryInput(_replayOperation is null && _queuedReplayNavigationOperation is null
            && _replayPlayback is null,
            "A deferred library read survived leaving its screen.");
    }

    private TaskCompletionSource<ReplayOperationResult> StageControlledReplayOperation(ReplayOperationKind kind)
    {
        AssertPrimaryInput(_replayOperation is null, "A controlled replay operation overlapped prior work.");
        var completion = new TaskCompletionSource<ReplayOperationResult>();
        _replayOperation = completion.Task;
        _replayOperationKind = kind;
        _replayOperationNavigationGeneration = _replayNavigationGeneration;
        return completion;
    }

    private void CompleteControlledNavigationRefresh(ReplayOperationKind kind)
    {
        AssertPrimaryInput(_replayOperation is not null && _replayOperationKind == kind
            && _queuedReplayNavigationOperation is null,
            "A deferred library refresh did not start after prior work completed.");
        _replayOperation!.GetAwaiter().GetResult();
        TryCompleteReplayOperation();
        AssertPrimaryInput(_replayOperation is null && _replayPlayback is null,
            "A library refresh installed canceled playback or remained in progress.");
    }
}
