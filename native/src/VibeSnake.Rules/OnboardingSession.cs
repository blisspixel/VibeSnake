namespace VibeSnake.Rules;

public enum OnboardingLesson : byte
{
    Turning = 0,
    InvalidReversal = 1,
    Wrapping = 2,
    FoodAndScore = 3,
    Starvation = 4,
    PowerUp = 5,
    Pause = 6,
    Restart = 7,
    Complete = 8,
}

public static class OnboardingCopyIds
{
    public const string MovementRequired = "onboarding.lesson.movement-required";
    public const string PauseLater = "onboarding.lesson.pause-later";
    public const string PauseComplete = "onboarding.lesson.pause-complete";
    public const string RestartLater = "onboarding.lesson.restart-later";
    public const string Complete = "onboarding.lesson.complete";
    public const string TurnUp = "onboarding.lesson.turn-up";
    public const string TurnAccepted = "onboarding.lesson.turn-accepted";
    public const string ReverseDown = "onboarding.lesson.reverse-down";
    public const string ReverseRejected = "onboarding.lesson.reverse-rejected";
    public const string WrapLeft = "onboarding.lesson.wrap-left";
    public const string WrapComplete = "onboarding.lesson.wrap-complete";
    public const string FoodRight = "onboarding.lesson.food-right";
    public const string FoodComplete = "onboarding.lesson.food-complete";
    public const string HungerRight = "onboarding.lesson.hunger-right";
    public const string HungerWarning = "onboarding.lesson.hunger-warning";
    public const string Starved = "onboarding.lesson.starved";
    public const string ShieldRight = "onboarding.lesson.shield-right";
    public const string ShieldCollected = "onboarding.lesson.shield-collected";

    public static IReadOnlyList<string> All { get; } =
    [
        MovementRequired,
        PauseLater,
        PauseComplete,
        RestartLater,
        Complete,
        TurnUp,
        TurnAccepted,
        ReverseDown,
        ReverseRejected,
        WrapLeft,
        WrapComplete,
        FoodRight,
        FoodComplete,
        HungerRight,
        HungerWarning,
        Starved,
        ShieldRight,
        ShieldCollected,
    ];
}

public sealed record OnboardingAdvance(
    OnboardingLesson PreviousLesson,
    OnboardingLesson CurrentLesson,
    bool InputAccepted,
    bool LessonAdvanced,
    string CopyId,
    RunEvent Events = RunEvent.None);

/// <summary>
/// Deterministic unscored micro-scenarios for first-run teaching. The session
/// cannot write scores, achievements, replays, or profile state.
/// </summary>
public sealed class OnboardingSession
{
    public const int ScenarioWidth = 6;
    public const int ScenarioHeight = 5;
    public const string Identity = "vibesnake-onboarding@1-unscored";

    private SnakeRun _scenario;
    private int _starvationMoves;
    private bool _foodScenarioReady;
    private bool _powerScenarioReady;

    public OnboardingSession()
    {
        _scenario = CreateTurningScenario();
    }

    public OnboardingLesson Lesson { get; private set; }

    public RunSnapshot Snapshot => _scenario.GetSnapshot();

    public bool IsComplete => Lesson == OnboardingLesson.Complete;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The established instance property is retained for public API compatibility.")]
    public bool CompetitiveScoreEligible => false;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The established instance property is retained for public API compatibility.")]
    public bool PersistsAchievements => false;

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The established instance property is retained for public API compatibility.")]
    public bool RecordsReplay => false;

    public OnboardingAdvance SubmitDirection(Direction direction)
    {
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        return Lesson switch
        {
            OnboardingLesson.Turning => SubmitTurning(direction),
            OnboardingLesson.InvalidReversal => SubmitInvalidReversal(direction),
            OnboardingLesson.Wrapping => SubmitWrapping(direction),
            OnboardingLesson.FoodAndScore => SubmitFood(direction),
            OnboardingLesson.Starvation => SubmitStarvation(direction),
            OnboardingLesson.PowerUp => SubmitPower(direction),
            _ => Rejected(OnboardingCopyIds.MovementRequired),
        };
    }

    public OnboardingAdvance SubmitPause()
    {
        if (Lesson != OnboardingLesson.Pause)
        {
            return Rejected(OnboardingCopyIds.PauseLater);
        }

        return AdvanceTo(
            OnboardingLesson.Restart,
            OnboardingCopyIds.PauseComplete);
    }

    public OnboardingAdvance SubmitRestart()
    {
        if (Lesson != OnboardingLesson.Restart)
        {
            return Rejected(OnboardingCopyIds.RestartLater);
        }

        return AdvanceTo(
            OnboardingLesson.Complete,
            OnboardingCopyIds.Complete);
    }

    public void Reset()
    {
        Lesson = OnboardingLesson.Turning;
        _starvationMoves = 0;
        _foodScenarioReady = false;
        _powerScenarioReady = false;
        _scenario = CreateTurningScenario();
    }

    private OnboardingAdvance SubmitTurning(Direction direction)
    {
        if (direction != Direction.Up)
        {
            return Rejected(OnboardingCopyIds.TurnUp);
        }

        AssertScenario(
            _scenario.QueueDirection(direction),
            "Tutorial legal turn was rejected.");

        var result = _scenario.Step();
        AssertScenario(
            _scenario.Direction == Direction.Up,
            "Tutorial turn direction diverged.");
        AssertScenario(
            result.Events.HasFlag(RunEvent.Moved),
            "Tutorial turn event diverged.");

        return AdvanceTo(
            OnboardingLesson.InvalidReversal,
            OnboardingCopyIds.TurnAccepted,
            result.Events);
    }

    private OnboardingAdvance SubmitInvalidReversal(Direction direction)
    {
        if (direction != Direction.Down)
        {
            return Rejected(OnboardingCopyIds.ReverseDown);
        }

        var before = _scenario.ComputeStateHash();
        AssertScenario(
            !_scenario.QueueDirection(direction),
            "Tutorial reversal was accepted.");
        AssertScenario(
            _scenario.ComputeStateHash() == before,
            "Tutorial reversal changed state.");

        _scenario = CreateWrapScenario();
        return AdvanceTo(
            OnboardingLesson.Wrapping,
            OnboardingCopyIds.ReverseRejected);
    }

    private OnboardingAdvance SubmitWrapping(Direction direction)
    {
        if (direction != Direction.Left)
        {
            return Rejected(OnboardingCopyIds.WrapLeft);
        }

        var result = _scenario.Step();
        AssertScenario(
            result.Events.HasFlag(RunEvent.Wrapped),
            "Tutorial wrap event diverged.");
        AssertScenario(
            _scenario.Head.X == ScenarioWidth - 1,
            "Tutorial wrap position diverged.");

        _foodScenarioReady = false;
        return AdvanceTo(
            OnboardingLesson.FoodAndScore,
            OnboardingCopyIds.WrapComplete,
            result.Events);
    }

    private OnboardingAdvance SubmitFood(Direction direction)
    {
        if (direction != Direction.Right)
        {
            return Rejected(OnboardingCopyIds.FoodRight);
        }

        if (!_foodScenarioReady)
        {
            _scenario = CreateFoodScenario();
            _foodScenarioReady = true;
        }

        var lengthBefore = _scenario.Body.Count;
        var result = _scenario.Step();
        AssertScenario(
            result.Events.HasFlag(RunEvent.AteFood),
            "Tutorial food event diverged.");
        AssertScenario(_scenario.Score > 0, "Tutorial food score diverged.");
        AssertScenario(
            _scenario.Body.Count == lengthBefore + 1,
            "Tutorial food growth diverged.");

        _scenario = CreateStarvationScenario();
        return AdvanceTo(
            OnboardingLesson.Starvation,
            OnboardingCopyIds.FoodComplete,
            result.Events);
    }

    private OnboardingAdvance SubmitStarvation(Direction direction)
    {
        if (direction != Direction.Right)
        {
            return Rejected(OnboardingCopyIds.HungerRight);
        }

        var result = _scenario.Step();
        _starvationMoves++;
        if (_starvationMoves == 1)
        {
            AssertScenario(
                result.Events.HasFlag(RunEvent.StarvationWarning),
                "Tutorial starvation warning event diverged.");
            AssertScenario(
                _scenario.Status == RunStatus.Running,
                "Tutorial starvation warning ended the run early.");

            return Accepted(
                OnboardingCopyIds.HungerWarning,
                result.Events);
        }

        AssertScenario(_starvationMoves == 2, "Tutorial starvation move count diverged.");
        AssertScenario(
            _scenario.Status == RunStatus.Dead,
            "Tutorial starvation status diverged.");
        AssertScenario(
            _scenario.DeathCause == DeathCause.Starvation,
            "Tutorial starvation cause diverged.");

        _powerScenarioReady = false;
        return AdvanceTo(
            OnboardingLesson.PowerUp,
            OnboardingCopyIds.Starved,
            result.Events);
    }

    private OnboardingAdvance SubmitPower(Direction direction)
    {
        if (direction != Direction.Right)
        {
            return Rejected(OnboardingCopyIds.ShieldRight);
        }

        if (!_powerScenarioReady)
        {
            _scenario = CreatePowerScenario();
            _powerScenarioReady = true;
        }

        var result = _scenario.Step();
        AssertScenario(
            result.Events.HasFlag(RunEvent.PowerCollected),
            "Tutorial power collection event diverged.");
        AssertScenario(_scenario.HasShield, "Tutorial Shield activation diverged.");

        return AdvanceTo(
            OnboardingLesson.Pause,
            OnboardingCopyIds.ShieldCollected,
            result.Events);
    }

    private OnboardingAdvance AdvanceTo(
        OnboardingLesson next,
        string copyId,
        RunEvent events = RunEvent.None)
    {
        var previous = Lesson;
        Lesson = next;
        return new OnboardingAdvance(previous, Lesson, true, true, copyId, events);
    }

    private OnboardingAdvance Accepted(string copyId, RunEvent events) =>
        new(Lesson, Lesson, true, false, copyId, events);

    private OnboardingAdvance Rejected(string copyId) =>
        new(Lesson, Lesson, false, false, copyId);

    private static void AssertScenario(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static RunConfig ScenarioConfig(int starvationTicks = 50, int warningTicks = 10) =>
        new(
            Width: ScenarioWidth,
            Height: ScenarioHeight,
            StarvationTicks: starvationTicks,
            PowerSpawnIntervalTicks: 0,
            StarvationWarningTicks: warningTicks);

    private static SnakeRun CreateTurningScenario() => SnakeRun.CreateForTesting(
        ScenarioConfig(),
        [new GridPoint(2, 2)],
        Direction.Right,
        new GridPoint(5, 4),
        hungerTicksRemaining: 50);

    private static SnakeRun CreateWrapScenario() => SnakeRun.CreateForTesting(
        ScenarioConfig(),
        [new GridPoint(0, 2)],
        Direction.Left,
        new GridPoint(3, 4),
        hungerTicksRemaining: 50);

    private static SnakeRun CreateFoodScenario() => SnakeRun.CreateForTesting(
        ScenarioConfig(),
        [new GridPoint(1, 2)],
        Direction.Right,
        new GridPoint(2, 2),
        hungerTicksRemaining: 20);

    private static SnakeRun CreateStarvationScenario() => SnakeRun.CreateForTesting(
        ScenarioConfig(starvationTicks: 2, warningTicks: 1),
        [new GridPoint(1, 2)],
        Direction.Right,
        new GridPoint(5, 4),
        hungerTicksRemaining: 2);

    private static SnakeRun CreatePowerScenario() => SnakeRun.CreateForTesting(
        ScenarioConfig(),
        [new GridPoint(1, 2)],
        Direction.Right,
        new GridPoint(5, 4),
        hungerTicksRemaining: 50,
        powerPickup: new PowerPickup(
            PowerKind.Shield,
            new GridPoint(2, 2),
            visibilityTicksRemaining: 20));
}
