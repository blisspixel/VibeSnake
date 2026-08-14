using System.Collections.ObjectModel;
using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

internal static class AgentStyleEvidenceMath
{
    public const int BasisPointScale = 10_000;

    public static int BasisPoints(long numerator, long denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(numerator);
        ArgumentOutOfRangeException.ThrowIfNegative(denominator);
        if (numerator > denominator)
        {
            throw new ArgumentOutOfRangeException(
                nameof(numerator),
                "A rate numerator cannot exceed its denominator.");
        }

        if (denominator == 0)
        {
            return 0;
        }

        return checked((int)(checked(numerator * BasisPointScale) / denominator));
    }

    public static int StructuralOpenExitCount(RunConfig config, RunSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Status != RunStatus.Running || snapshot.Body.Count == 0)
        {
            return 0;
        }

        var effectiveDirection = snapshot.PendingDirections.Count == 0
            ? snapshot.Direction
            : snapshot.PendingDirections[^1];
        var openExits = 0;
        foreach (var candidateDirection in Enum.GetValues<Direction>())
        {
            if (candidateDirection == effectiveDirection.Opposite())
            {
                continue;
            }

            var candidate = snapshot.Head
                .Add(candidateDirection.Offset())
                .Wrap(config.Width, config.Height);
            var grows = snapshot.Food == candidate && !snapshot.HasGluttony;
            var movesOntoDepartingTail = !grows
                && candidate == snapshot.Body[0]
                && !snapshot.Body.Skip(1).Contains(candidate);
            var structurallyBlocked =
                snapshot.Body.Contains(candidate) && !movesOntoDepartingTail
                || snapshot.DetachedObstacles.Contains(candidate);
            if (!structurallyBlocked)
            {
                openExits++;
            }
        }

        return openExits;
    }

    public static int WrappedManhattanDistance(
        GridPoint left,
        GridPoint right,
        int width,
        int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var deltaX = Math.Abs(left.X - right.X);
        var deltaY = Math.Abs(left.Y - right.Y);
        if (deltaX >= width || deltaY >= height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(right),
                "Distance points must be inside the wrapped board.");
        }

        return Math.Min(deltaX, width - deltaX)
            + Math.Min(deltaY, height - deltaY);
    }
}

internal readonly record struct AgentStyleEvidenceFacts(
    int AcceptedSteps,
    int StructuralOpenExitSteps,
    int FoodEaten,
    int PeakCombo,
    int CurrentCombo,
    bool ContinuityFrozen,
    int ContinuityNumerator,
    int ContinuityDenominator,
    int BodyProximityNearMisses,
    int WrappedBodyProximityNearMisses,
    int ActivatedPowerKindMask,
    int MaximumConcurrentActivePowerKinds,
    int FoodProgressSteps,
    int SafeFoodProgressSteps);

internal readonly record struct AgentStyleReplayEvidence(
    AgentStyleEvidenceFacts Facts,
    AgentStyleProgressV2 Progress);

internal sealed class AgentStyleEvidenceTracker
{
    private readonly AgentStyleContractDefinitionV2 _definition;
    private readonly RunConfig _config;
    private readonly HashSet<PowerKind> _activatedPowerKinds = [];
    private int _lastTick;
    private string _lastStateHash;
    private int _rulesAdvancedSteps;
    private int _structuralOpenExitSteps;
    private int _foodEaten;
    private int _peakCombo;
    private int _currentCombo;
    private bool _continuityFrozen;
    private int _continuityNumerator;
    private int _continuityDenominator;
    private int _bodyProximityNearMisses;
    private int _wrappedBodyProximityNearMisses;
    private int _maximumConcurrentActivePowerKinds;
    private int _foodProgressSteps;
    private int _safeFoodProgressSteps;

    public AgentStyleEvidenceTracker(
        string styleContractId,
        string modeId,
        RunConfig config,
        RunSnapshot initialSnapshot)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(initialSnapshot);
        AgentStyleContractCatalog.ValidateMode(styleContractId, modeId);
        if (!string.Equals(config.ModeId, modeId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Style evidence mode must match the fixed run configuration.",
                nameof(modeId));
        }

        if (initialSnapshot.Tick != 0
            || initialSnapshot.Status != RunStatus.Running
            || initialSnapshot.Body.Count == 0
            || initialSnapshot.ComboCount != 0
            || string.IsNullOrWhiteSpace(initialSnapshot.StateHash))
        {
            throw new ArgumentException(
                "Style evidence requires a fresh, running agent replay state.",
                nameof(initialSnapshot));
        }

        _definition = AgentStyleContractCatalog.Get(styleContractId);
        _config = config;
        _lastTick = initialSnapshot.Tick;
        _lastStateHash = initialSnapshot.StateHash;
    }

    public void Record(
        RunSnapshot before,
        RunStepResult result,
        RunSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.Tick != _lastTick
            || !string.Equals(before.StateHash, _lastStateHash, StringComparison.Ordinal)
            || before.Status != RunStatus.Running
            || after.Tick != checked(before.Tick + 1)
            || result.Tick != after.Tick
            || result.Status != after.Status
            || result.DeathCause != after.DeathCause
            || !string.Equals(result.StateHash, after.StateHash, StringComparison.Ordinal)
            || after.Body.Count == 0)
        {
            throw new InvalidOperationException(
                "Style evidence received a noncontiguous or contradictory rules-advanced step.");
        }

        _rulesAdvancedSteps = checked(_rulesAdvancedSteps + 1);
        var structuralExits = AgentStyleEvidenceMath.StructuralOpenExitCount(_config, after);
        if (after.Status == RunStatus.Running)
        {
            if (structuralExits >= 2)
            {
                _structuralOpenExitSteps = checked(_structuralOpenExitSteps + 1);
            }
        }

        var ateFood = false;
        var wrapped = false;
        foreach (var item in result.OrderedEvents)
        {
            if (!Enum.IsDefined(item.Kind))
            {
                throw new InvalidOperationException(
                    "Style evidence received an unknown public event kind.");
            }

            switch (item.Kind)
            {
                case RunEventKind.AteFood:
                    if (ateFood)
                    {
                        throw new InvalidOperationException(
                            "One rules-advanced step cannot contain multiple food events.");
                    }

                    ateFood = true;
                    _foodEaten = checked(_foodEaten + 1);
                    break;
                case RunEventKind.Wrapped:
                    wrapped = true;
                    break;
                case RunEventKind.PowerActivated:
                    if (item.Power is not { } power || !Enum.IsDefined(power))
                    {
                        throw new InvalidOperationException(
                            "Power activation evidence requires one known power kind.");
                    }

                    _activatedPowerKinds.Add(power);
                    break;
            }
        }

        _currentCombo = after.ComboCount;
        _peakCombo = Math.Max(_peakCombo, _currentCombo);
        if (!_continuityFrozen && after.ComboCount >= 4)
        {
            _continuityNumerator = _currentCombo;
            _continuityDenominator = _foodEaten;
            if (_continuityNumerator > _continuityDenominator)
            {
                throw new InvalidOperationException(
                    "Combo continuity exceeded the accepted food evidence.");
            }

            _continuityFrozen = true;
        }

        var bodyNeighborCount = BodyNeighborCount(after);
        foreach (var item in result.OrderedEvents)
        {
            if (item.Kind != RunEventKind.NearMiss
                || item.Value is not > 0
                || item.Position != after.Head
                || bodyNeighborCount < 3)
            {
                continue;
            }

            _bodyProximityNearMisses = checked(_bodyProximityNearMisses + 1);
            if (wrapped)
            {
                _wrappedBodyProximityNearMisses = checked(
                    _wrappedBodyProximityNearMisses + 1);
            }
        }

        _maximumConcurrentActivePowerKinds = Math.Max(
            _maximumConcurrentActivePowerKinds,
            ActivePowerKindCount(after));

        if (before.Food is { } target)
        {
            _foodProgressSteps = checked(_foodProgressSteps + 1);
            var madeProgress = ateFood
                || AgentStyleEvidenceMath.WrappedManhattanDistance(
                    after.Head,
                    target,
                    _config.Width,
                    _config.Height)
                < AgentStyleEvidenceMath.WrappedManhattanDistance(
                    before.Head,
                    target,
                    _config.Width,
                    _config.Height);
            var retainedSafeStructure = after.Status == RunStatus.Won
                || after.Status == RunStatus.Running && structuralExits >= 1;
            if (madeProgress && retainedSafeStructure)
            {
                _safeFoodProgressSteps = checked(_safeFoodProgressSteps + 1);
            }
        }

        _lastTick = after.Tick;
        _lastStateHash = after.StateHash;
    }

    public AgentStyleProgressV2 Snapshot()
    {
        var criteria = BuildCriteria();
        var progress = new AgentStyleProgressV2(
            AgentStyleProgressV2.Contract,
            _definition.Id,
            _definition.DisplayName,
            _definition.EvaluationPolicyId,
            criteria,
            criteria.Count(value => value.Satisfied),
            criteria.All(value => value.Satisfied));
        return AgentStyleContractCatalog.IsValidProgress(progress)
            ? progress
            : throw new InvalidOperationException(
                "Style progress contradicted its closed catalog definition.");
    }

    public AgentStyleEvidenceFacts Facts => new(
        _rulesAdvancedSteps,
        _structuralOpenExitSteps,
        _foodEaten,
        _peakCombo,
        _currentCombo,
        _continuityFrozen,
        _continuityNumerator,
        _continuityDenominator,
        _bodyProximityNearMisses,
        _wrappedBodyProximityNearMisses,
        _activatedPowerKinds.Aggregate(
            0,
            (mask, power) => mask | 1 << checked((int)power - 1)),
        _maximumConcurrentActivePowerKinds,
        _foodProgressSteps,
        _safeFoodProgressSteps);

    public AgentStyleOutcomeV2 CreateOutcome(string replayPayloadHash)
    {
        return AgentStyleEvidenceReplayEvaluator.CreateOutcome(
            Snapshot(),
            replayPayloadHash);
    }

    private ReadOnlyCollection<AgentStyleCriterionProgressV2> BuildCriteria()
    {
        var definitions = _definition.Criteria;
        if (definitions.Count != 2)
        {
            throw new InvalidOperationException(
                "A style contract must contain exactly two ordered criteria.");
        }

        AgentStyleCriterionProgressV2[] criteria = _definition.Id switch
        {
            AgentStyleContractCatalog.StillwaterId =>
            [
                Count(definitions[0], _rulesAdvancedSteps),
                Rate(
                    definitions[1],
                    _structuralOpenExitSteps,
                    _rulesAdvancedSteps),
            ],
            AgentStyleContractCatalog.CrownchaserId =>
            [
                Count(definitions[0], _peakCombo),
                Rate(
                    definitions[1],
                    _continuityFrozen
                        ? _continuityNumerator
                        : Math.Min(_currentCombo, _foodEaten),
                    _continuityFrozen ? _continuityDenominator : _foodEaten),
            ],
            AgentStyleContractCatalog.EdgeProphetId =>
            [
                Count(definitions[0], _bodyProximityNearMisses),
                Count(definitions[1], _wrappedBodyProximityNearMisses),
            ],
            AgentStyleContractCatalog.MutagenistId =>
            [
                Count(definitions[0], _activatedPowerKinds.Count),
                Count(definitions[1], _maximumConcurrentActivePowerKinds),
            ],
            AgentStyleContractCatalog.RedlineId =>
            [
                Count(definitions[0], _foodEaten),
                Rate(definitions[1], _safeFoodProgressSteps, _foodProgressSteps),
            ],
            _ => throw new InvalidOperationException("The style contract is unsupported."),
        };
        return Array.AsReadOnly(criteria);
    }

    private static AgentStyleCriterionProgressV2 Count(
        AgentStyleCriterionDefinitionV2 definition,
        int current) =>
        Progress(definition, current, numerator: null, denominator: null);

    private static AgentStyleCriterionProgressV2 Rate(
        AgentStyleCriterionDefinitionV2 definition,
        long numerator,
        long denominator) =>
        Progress(
            definition,
            AgentStyleEvidenceMath.BasisPoints(numerator, denominator),
            numerator,
            denominator);

    private static AgentStyleCriterionProgressV2 Progress(
        AgentStyleCriterionDefinitionV2 definition,
        int current,
        long? numerator,
        long? denominator)
    {
        if (definition.Comparator != AgentStyleCriterionComparator.AtLeast
            || definition.Unit == AgentStyleCriterionUnit.Count
                && (numerator is not null || denominator is not null)
            || definition.Unit == AgentStyleCriterionUnit.BasisPoints
                && (numerator is null || denominator is null))
        {
            throw new InvalidOperationException(
                "Style criterion progress did not match its closed definition.");
        }

        return new AgentStyleCriterionProgressV2(
            definition.Id,
            definition.DisplayName,
            definition.Comparator,
            definition.Unit,
            current,
            definition.Target,
            numerator,
            denominator,
            current >= definition.Target);
    }

    private static int BodyNeighborCount(RunSnapshot snapshot)
    {
        if (snapshot.Body.Count < NearMissDetector.MinimumSnakeLength)
        {
            return 0;
        }

        var occupied = snapshot.Body.ToHashSet();
        var count = 0;
        for (var deltaX = -1; deltaX <= 1; deltaX++)
        {
            for (var deltaY = -1; deltaY <= 1; deltaY++)
            {
                if ((deltaX != 0 || deltaY != 0)
                    && occupied.Contains(new GridPoint(
                        snapshot.Head.X + deltaX,
                        snapshot.Head.Y + deltaY)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int ActivePowerKindCount(RunSnapshot snapshot)
    {
        var count = 0;
        count += snapshot.HasShield ? 1 : 0;
        count += snapshot.HasPhaseShift ? 1 : 0;
        count += snapshot.LastStandHeld || snapshot.HasLastStandRecovery ? 1 : 0;
        count += snapshot.HasSlowMo ? 1 : 0;
        count += snapshot.HasBoost ? 1 : 0;
        count += snapshot.HasMagnet ? 1 : 0;
        count += snapshot.HasBait ? 1 : 0;
        count += snapshot.HasGluttony ? 1 : 0;
        count += snapshot.HasDetachedObstacles ? 1 : 0;
        return count;
    }

}

internal static class AgentStyleEvidenceReplayEvaluator
{
    public static AgentStyleReplayEvidence Evaluate(
        string styleContractId,
        string modeId,
        RunReplay replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        var playback = new RunReplayPlayback(replay);
        var tracker = new AgentStyleEvidenceTracker(
            styleContractId,
            modeId,
            playback.Configuration,
            playback.CurrentSnapshot);
        while (!playback.IsComplete)
        {
            var before = playback.CurrentSnapshot;
            if (!playback.TryAdvance(out var frame) || frame is null)
            {
                throw new InvalidOperationException(
                    "Verified replay playback ended before its declared step count.");
            }

            tracker.Record(before, frame.Result, frame.Snapshot);
        }

        return new AgentStyleReplayEvidence(tracker.Facts, tracker.Snapshot());
    }

    public static AgentStyleProgressV2 EvaluateProgress(
        string styleContractId,
        string modeId,
        RunReplay replay)
    {
        return Evaluate(styleContractId, modeId, replay).Progress;
    }

    public static AgentStyleOutcomeV2 EvaluateOutcome(
        string styleContractId,
        string modeId,
        RunReplay replay)
    {
        var progress = EvaluateProgress(styleContractId, modeId, replay);
        return CreateOutcome(progress, replay.PayloadHash);
    }

    public static AgentStyleOutcomeV2 CreateOutcome(
        AgentStyleProgressV2 progress,
        string replayPayloadHash)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (replayPayloadHash is null
            || replayPayloadHash.Length != 64
            || replayPayloadHash.Any(character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "Style outcomes require the verified replay payload hash.",
                nameof(replayPayloadHash));
        }

        var outcome = new AgentStyleOutcomeV2(
            AgentStyleOutcomeV2.Contract,
            progress.ContractId,
            progress.DisplayName,
            progress.EvaluationPolicyId,
            progress.Criteria,
            progress.CriteriaSatisfied,
            progress.AllCriteriaSatisfied,
            replayPayloadHash);
        return AgentStyleContractCatalog.IsValidOutcome(outcome)
            ? outcome
            : throw new InvalidOperationException(
                "Style outcome contradicted its closed catalog definition.");
    }

    public static bool Equivalent(
        AgentStyleProgressV2 expected,
        AgentStyleProgressV2 actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        return string.Equals(expected.Schema, actual.Schema, StringComparison.Ordinal)
            && string.Equals(expected.ContractId, actual.ContractId, StringComparison.Ordinal)
            && string.Equals(expected.DisplayName, actual.DisplayName, StringComparison.Ordinal)
            && string.Equals(
                expected.EvaluationPolicyId,
                actual.EvaluationPolicyId,
                StringComparison.Ordinal)
            && expected.CriteriaSatisfied == actual.CriteriaSatisfied
            && expected.AllCriteriaSatisfied == actual.AllCriteriaSatisfied
            && expected.Criteria.SequenceEqual(actual.Criteria);
    }
}
