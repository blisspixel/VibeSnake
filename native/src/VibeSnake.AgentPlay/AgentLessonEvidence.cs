using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

public enum AgentLessonEvidenceSource : byte
{
    ReplayTrace = 0,
    AttemptWitness = 1,
}

public enum AgentLessonEvidenceState : byte
{
    Live = 0,
    Verified = 1,
    FailedClosed = 2,
}

public enum AgentLessonReviewCode : byte
{
    TargetReached = 0,
    ReplayRequirementUnmet = 1,
    InsufficientAttemptEvidence = 2,
}

internal enum AgentLessonAttemptOperation : byte
{
    Step = 0,
    Burst = 1,
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentLessonRequirementDefinitionV2(
    string Id,
    string DisplayName,
    string Description,
    AgentLessonEvidenceSource EvidenceSource,
    int Target);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentSignalLessonDefinitionV2(
    string Id,
    string Title,
    string Instruction,
    string ModeId,
    ulong PracticeSeed,
    int MaximumSteps,
    string EvaluationPolicyId,
    IReadOnlyList<AgentLessonRequirementDefinitionV2> Requirements);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentLessonRequirementProgressV2(
    string RequirementId,
    string DisplayName,
    AgentLessonEvidenceSource EvidenceSource,
    int Current,
    int Target,
    bool Satisfied);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentLessonRetryDescriptorV1(
    string Schema,
    string Tool,
    string LessonId,
    string ActionProfile,
    bool FreshSessionRequired)
{
    public const string Contract = "vibesnake-agent-lesson-retry-v1";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentLessonProgressV2(
    string Schema,
    string LessonId,
    string Title,
    string Instruction,
    string EvaluationPolicyId,
    IReadOnlyList<AgentLessonRequirementProgressV2> Requirements,
    int RequirementsSatisfied,
    bool AllRequirementsSatisfied,
    string? FirstUnmetRequirementId,
    AgentLessonEvidenceState EvidenceState,
    int AttemptEvidenceCount,
    string AttemptEvidenceHash,
    AgentLessonRetryDescriptorV1? RetryDescriptor)
{
    public const string Contract = "vibesnake-agent-lesson-progress-v2";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentLessonProgressDeltaV2(
    string Schema,
    string LessonId,
    IReadOnlyList<string> NewlySatisfiedRequirementIds,
    int PreviousRequirementsSatisfied,
    int CurrentRequirementsSatisfied,
    bool AllRequirementsReachedThisMutation,
    int AttemptEvidenceCount,
    string AttemptEvidenceHash)
{
    public const string Contract = "vibesnake-agent-lesson-progress-delta-v2";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentLessonOutcomeV2(
    string Schema,
    string LessonId,
    string EvaluationPolicyId,
    IReadOnlyList<AgentLessonRequirementProgressV2> Requirements,
    int RequirementsSatisfied,
    bool AllRequirementsSatisfied,
    string? FirstUnmetRequirementId,
    AgentLessonReviewCode ReviewCode,
    AgentMatchEndReason EndReason,
    int AttemptEvidenceCount,
    string ReplayPayloadHash,
    string AttemptEvidenceHash,
    string EvidenceHash,
    AgentLessonRetryDescriptorV1 RetryDescriptor)
{
    public const string Contract = "vibesnake-agent-lesson-outcome-v2";
}

public static class AgentSignalSchoolCatalog
{
    public const int MaximumAttemptWitnesses = 32;
    public const string EmptyAttemptEvidenceHash =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    public const string EvaluationPolicyId = "ordered-replay-attempt-evidence-v2";
    public const string FirstTurnId = "first-turn";
    public const string WrapLineId = "wrap-line";
    public const string HungerRouteId = "hunger-route";
    public const string ExitRouteId = "exit-route";
    public const string PowerRouteId = "power-route";
    public const string RecoverRouteId = "recover-route";
    public const string ComboRouteId = "combo-route";
    public const string DeathReadId = "death-read";

    public static IReadOnlyList<AgentSignalLessonDefinitionV2> All { get; } =
        Array.AsReadOnly(new[]
        {
            Lesson(
                FirstTurnId,
                "First Signal",
                "Observe one rejected opposite reversal, then make a legal turn.",
                RunModeCatalog.ClassicId,
                7UL,
                16,
                Requirement(
                    "opposite_reversal_rejected",
                    "Opposite reversal rejected",
                    "One valid-state opposite reversal is rejected without advancing rules.",
                    AgentLessonEvidenceSource.AttemptWitness),
                Requirement(
                    "legal_turn_after_rejection",
                    "Legal turn after rejection",
                    "A later verified rules step emits DirectionChanged.",
                    AgentLessonEvidenceSource.ReplayTrace)),
            Lesson(
                WrapLineId,
                "Open Circuit",
                "Cross one board edge and remain in the running state.",
                RunModeCatalog.ClassicId,
                65_535UL,
                160,
                Requirement(
                    "wrapped_event",
                    "Typed wrap",
                    "One verified rules step emits Wrapped.",
                    AgentLessonEvidenceSource.ReplayTrace),
                Requirement(
                    "running_after_wrap",
                    "Running after wrap",
                    "The same wrapped step ends in the Running state.",
                    AgentLessonEvidenceSource.ReplayTrace)),
            Lesson(
                HungerRouteId,
                "Feed the Signal",
                "Collect food before a starvation ending.",
                RunModeCatalog.VibeId,
                4_294_967_291UL,
                180,
                Requirement(
                    "food_eaten",
                    "Food eaten",
                    "One verified rules step emits AteFood.",
                    AgentLessonEvidenceSource.ReplayTrace),
                Requirement(
                    "food_before_starvation",
                    "Food before starvation",
                    "That food step does not end in starvation death.",
                    AgentLessonEvidenceSource.ReplayTrace)),
            Lesson(
                ExitRouteId,
                "Keep Two Doors",
                "Grow once while retaining at least two structural next-step exits.",
                RunModeCatalog.VibeId,
                20_260_814UL,
                240,
                Requirement(
                    "food_growth",
                    "Growth step",
                    "One verified rules step emits AteFood and grows the body by one segment.",
                    AgentLessonEvidenceSource.ReplayTrace),
                Requirement(
                    "two_structural_exits_after_growth",
                    "Two exits after growth",
                    "That food step ends Running with at least two structural non-reversing exits.",
                    AgentLessonEvidenceSource.ReplayTrace)),
            Lesson(
                PowerRouteId,
                "Tune the Current",
                "Collect and activate the same visible power kind.",
                RunModeCatalog.VibeId,
                32_452_843UL,
                320,
                Requirement(
                    "power_collected",
                    "Power collected",
                    "One verified event identifies a collected power kind.",
                    AgentLessonEvidenceSource.ReplayTrace),
                Requirement(
                    "same_power_activated",
                    "Same power activated",
                    "A verified activation for that collected power kind follows in event order.",
                    AgentLessonEvidenceSource.ReplayTrace)),
            Lesson(
                RecoverRouteId,
                "Return from Static",
                "Use a named protection resource to prevent a collision and remain running.",
                RunModeCatalog.VibeId,
                0UL,
                600,
                Requirement(
                    "collision_prevented",
                    "Collision prevented",
                    "One verified CollisionPrevented event names a cause and power.",
                    AgentLessonEvidenceSource.ReplayTrace),
                Requirement(
                    "running_after_recovery",
                    "Running after recovery",
                    "That protected step ends in the Running state.",
                    AgentLessonEvidenceSource.ReplayTrace)),
            Lesson(
                ComboRouteId,
                "Hold the Chorus",
                "Collect three food and reach a verified peak combo of three.",
                RunModeCatalog.VibeId,
                49_979_687UL,
                480,
                Requirement(
                    "three_food",
                    "Three food",
                    "Verified replay events contain at least three AteFood steps.",
                    AgentLessonEvidenceSource.ReplayTrace,
                    target: 3),
                Requirement(
                    "peak_combo_three",
                    "Peak combo three",
                    "A verified post-step state reaches combo three.",
                    AgentLessonEvidenceSource.ReplayTrace,
                    target: 3)),
            Lesson(
                DeathReadId,
                "Read the End",
                "Reach a typed death whose event cause matches the terminal state.",
                RunModeCatalog.VibeId,
                20_260_815UL,
                600,
                Requirement(
                    "terminal_death",
                    "Terminal death",
                    "The verified replay ends Dead with a non-none death cause.",
                    AgentLessonEvidenceSource.ReplayTrace),
                Requirement(
                    "matching_death_event",
                    "Matching death event",
                    "The terminal Died event reports the same death cause.",
                    AgentLessonEvidenceSource.ReplayTrace)),
        });

    public static AgentSignalLessonDefinitionV2 Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return All.SingleOrDefault(value => string.Equals(value.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown Signal School lesson {id}.", nameof(id));
    }

    public static AgentLessonProgressDeltaV2 Delta(
        AgentLessonProgressV2 previous,
        AgentLessonProgressV2 current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        if (!IsValidProgress(previous)
            || !IsValidProgress(current)
            || !string.Equals(previous.LessonId, current.LessonId, StringComparison.Ordinal)
            || previous.Requirements.Count != current.Requirements.Count
            || !previous.Requirements.Select(item => item.RequirementId)
                .SequenceEqual(current.Requirements.Select(item => item.RequirementId))
            || current.RequirementsSatisfied < previous.RequirementsSatisfied
            || previous.AllRequirementsSatisfied && !current.AllRequirementsSatisfied
            || current.AttemptEvidenceCount < previous.AttemptEvidenceCount
            || (current.AttemptEvidenceCount == previous.AttemptEvidenceCount)
                != string.Equals(
                    current.AttemptEvidenceHash,
                    previous.AttemptEvidenceHash,
                    StringComparison.Ordinal)
            || previous.Requirements.Zip(current.Requirements).Any(pair =>
                pair.Second.Current < pair.First.Current
                || pair.First.Satisfied && !pair.Second.Satisfied))
        {
            throw new ArgumentException(
                "Lesson progress values must describe one monotonic requirement sequence.");
        }

        var newlySatisfied = previous.Requirements
            .Zip(current.Requirements)
            .Where(pair => !pair.First.Satisfied && pair.Second.Satisfied)
            .Select(pair => pair.Second.RequirementId)
            .ToArray();
        return new AgentLessonProgressDeltaV2(
            AgentLessonProgressDeltaV2.Contract,
            current.LessonId,
            Array.AsReadOnly(newlySatisfied),
            previous.RequirementsSatisfied,
            current.RequirementsSatisfied,
            !previous.AllRequirementsSatisfied && current.AllRequirementsSatisfied,
            current.AttemptEvidenceCount,
            current.AttemptEvidenceHash);
    }

    public static bool IsValidProgress(AgentLessonProgressV2? progress)
    {
        if (progress is null
            || !string.Equals(progress.Schema, AgentLessonProgressV2.Contract, StringComparison.Ordinal)
            || !Enum.IsDefined(progress.EvidenceState)
            || progress.AttemptEvidenceCount is < 0 or > AgentLessonEvidenceTracker.MaximumAttemptWitnesses
            || !IsLowerHex(progress.AttemptEvidenceHash, 64))
        {
            return false;
        }

        AgentSignalLessonDefinitionV2 definition;
        try
        {
            definition = Get(progress.LessonId);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!string.Equals(progress.Title, definition.Title, StringComparison.Ordinal)
            || !string.Equals(progress.Instruction, definition.Instruction, StringComparison.Ordinal)
            || !string.Equals(
                progress.EvaluationPolicyId,
                definition.EvaluationPolicyId,
                StringComparison.Ordinal)
            || !HasValidRequirements(
                definition,
                progress.Requirements,
                progress.RequirementsSatisfied,
                progress.AllRequirementsSatisfied,
                progress.FirstUnmetRequirementId)
            || !HasValidAttemptEvidence(
                progress.LessonId,
                progress.AttemptEvidenceCount,
                progress.AttemptEvidenceHash,
                progress.Requirements))
        {
            return false;
        }

        return progress.EvidenceState == AgentLessonEvidenceState.Live
            ? progress.RetryDescriptor is null
            : IsValidRetryDescriptor(progress.RetryDescriptor, progress.LessonId);
    }

    public static bool IsValidOutcome(AgentLessonOutcomeV2? outcome)
    {
        if (outcome is null
            || !string.Equals(outcome.Schema, AgentLessonOutcomeV2.Contract, StringComparison.Ordinal)
            || !Enum.IsDefined(outcome.ReviewCode)
            || !Enum.IsDefined(outcome.EndReason)
            || outcome.EndReason is AgentMatchEndReason.None or AgentMatchEndReason.ReplayFailure
            || outcome.AttemptEvidenceCount is < 0 or > AgentLessonEvidenceTracker.MaximumAttemptWitnesses
            || !IsLowerHex(outcome.ReplayPayloadHash, 64)
            || !IsLowerHex(outcome.AttemptEvidenceHash, 64)
            || !IsLowerHex(outcome.EvidenceHash, 64)
            || !string.Equals(
                outcome.EvidenceHash,
                AgentLessonEvidenceReplayEvaluator.ComputeEvidenceHash(
                    outcome.ReplayPayloadHash,
                    outcome.AttemptEvidenceHash),
                StringComparison.Ordinal))
        {
            return false;
        }

        AgentSignalLessonDefinitionV2 definition;
        try
        {
            definition = Get(outcome.LessonId);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!string.Equals(
                outcome.EvaluationPolicyId,
                definition.EvaluationPolicyId,
                StringComparison.Ordinal)
            || !HasValidRequirements(
                definition,
                outcome.Requirements,
                outcome.RequirementsSatisfied,
                outcome.AllRequirementsSatisfied,
                outcome.FirstUnmetRequirementId)
            || !HasValidAttemptEvidence(
                outcome.LessonId,
                outcome.AttemptEvidenceCount,
                outcome.AttemptEvidenceHash,
                outcome.Requirements)
            || !IsValidRetryDescriptor(outcome.RetryDescriptor, outcome.LessonId))
        {
            return false;
        }

        var expectedReview = outcome.AllRequirementsSatisfied
            ? AgentLessonReviewCode.TargetReached
            : outcome.Requirements
                .First(item => !item.Satisfied)
                .EvidenceSource == AgentLessonEvidenceSource.AttemptWitness
                ? AgentLessonReviewCode.InsufficientAttemptEvidence
                : AgentLessonReviewCode.ReplayRequirementUnmet;
        return outcome.ReviewCode == expectedReview;
    }

    private static bool HasValidAttemptEvidence(
        string lessonId,
        int count,
        string hash,
        IReadOnlyList<AgentLessonRequirementProgressV2> requirements) =>
        (count == 0
            ? string.Equals(hash, EmptyAttemptEvidenceHash, StringComparison.Ordinal)
            : lessonId == FirstTurnId
                && !string.Equals(hash, EmptyAttemptEvidenceHash, StringComparison.Ordinal))
        && (lessonId == FirstTurnId
            ? requirements.Count == 2
                && requirements[0].Current == Math.Min(1, count)
            : count == 0);

    internal static AgentLessonRetryDescriptorV1 CreateRetryDescriptor(
        string lessonId,
        string actionProfile) =>
        new(
            AgentLessonRetryDescriptorV1.Contract,
            "start_lesson",
            Get(lessonId).Id,
            actionProfile,
            FreshSessionRequired: true);

    private static AgentSignalLessonDefinitionV2 Lesson(
        string id,
        string title,
        string instruction,
        string modeId,
        ulong practiceSeed,
        int maximumSteps,
        AgentLessonRequirementDefinitionV2 first,
        AgentLessonRequirementDefinitionV2 second) =>
        new(
            id,
            title,
            instruction,
            modeId,
            practiceSeed,
            maximumSteps,
            EvaluationPolicyId,
            Array.AsReadOnly(new[] { first, second }));

    private static AgentLessonRequirementDefinitionV2 Requirement(
        string id,
        string displayName,
        string description,
        AgentLessonEvidenceSource source,
        int target = 1) =>
        new(id, displayName, description, source, target);

    private static bool HasValidRequirements(
        AgentSignalLessonDefinitionV2 definition,
        IReadOnlyList<AgentLessonRequirementProgressV2>? requirements,
        int requirementsSatisfied,
        bool allRequirementsSatisfied,
        string? firstUnmetRequirementId)
    {
        if (requirements is not { Count: 2 }
            || definition.Requirements.Count != requirements.Count)
        {
            return false;
        }

        var satisfied = 0;
        string? firstUnmet = null;
        var encounteredUnmet = false;
        for (var index = 0; index < requirements.Count; index++)
        {
            var expected = definition.Requirements[index];
            var actual = requirements[index];
            if (actual is null
                || !string.Equals(actual.RequirementId, expected.Id, StringComparison.Ordinal)
                || !string.Equals(actual.DisplayName, expected.DisplayName, StringComparison.Ordinal)
                || actual.EvidenceSource != expected.EvidenceSource
                || actual.Target != expected.Target
                || actual.Current < 0
                || actual.Current > actual.Target
                || actual.Satisfied != (actual.Current >= actual.Target))
            {
                return false;
            }

            if (actual.Satisfied)
            {
                if (encounteredUnmet)
                {
                    return false;
                }
                satisfied++;
            }
            else
            {
                encounteredUnmet = true;
                firstUnmet ??= actual.RequirementId;
            }
        }

        return requirementsSatisfied == satisfied
            && allRequirementsSatisfied == (satisfied == requirements.Count)
            && string.Equals(firstUnmetRequirementId, firstUnmet, StringComparison.Ordinal);
    }

    private static bool IsValidRetryDescriptor(
        AgentLessonRetryDescriptorV1? retry,
        string lessonId) =>
        retry is not null
        && string.Equals(retry.Schema, AgentLessonRetryDescriptorV1.Contract, StringComparison.Ordinal)
        && string.Equals(retry.Tool, "start_lesson", StringComparison.Ordinal)
        && string.Equals(retry.LessonId, lessonId, StringComparison.Ordinal)
        && AgentPassportV4.IsSupportedActionProfile(retry.ActionProfile)
        && retry.FreshSessionRequired;

    private static bool IsLowerHex(string? value, int length) =>
        value is not null
        && value.Length == length
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

internal sealed record AgentLessonAttemptWitnessV1(
    int Ordinal,
    AgentLessonAttemptOperation Operation,
    string IdempotencyKeyHash,
    int Tick,
    string StateHash,
    AgentAction Action);

internal sealed class AgentLessonEvidenceTracker
{
    public const int MaximumAttemptWitnesses = AgentSignalSchoolCatalog.MaximumAttemptWitnesses;

    private readonly AgentSignalLessonDefinitionV2 _definition;
    private readonly RunConfig _config;
    private readonly Dictionary<string, int> _values = new(StringComparer.Ordinal);
    private readonly List<AgentLessonAttemptWitnessV1> _attemptWitnesses = new();
    private readonly HashSet<PowerKind> _collectedPowerKinds = new();
    private int? _firstReversalWitnessTick;

    public AgentLessonEvidenceTracker(string lessonId, RunConfig config)
    {
        _definition = AgentSignalSchoolCatalog.Get(lessonId);
        _config = config ?? throw new ArgumentNullException(nameof(config));
        foreach (var requirement in _definition.Requirements)
        {
            _values.Add(requirement.Id, 0);
        }
    }

    public IReadOnlyList<AgentLessonAttemptWitnessV1> AttemptWitnesses =>
        _attemptWitnesses.AsReadOnly();

    public bool TryRecordOppositeReversal(
        AgentLessonAttemptOperation operation,
        string idempotencyKey,
        RunSnapshot snapshot,
        AgentAction action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_definition.Id != AgentSignalSchoolCatalog.FirstTurnId
            || _attemptWitnesses.Count >= MaximumAttemptWitnesses
            || !TryMapDirection(action, out var direction))
        {
            return false;
        }

        var effectiveDirection = snapshot.PendingDirections.Count > 0
            ? snapshot.PendingDirections[^1]
            : snapshot.Direction;
        if (direction != effectiveDirection.Opposite())
        {
            return false;
        }

        _attemptWitnesses.Add(new AgentLessonAttemptWitnessV1(
            _attemptWitnesses.Count + 1,
            operation,
            Hash(idempotencyKey),
            snapshot.Tick,
            snapshot.StateHash,
            action));
        _firstReversalWitnessTick ??= snapshot.Tick;
        Set("opposite_reversal_rejected", 1);
        return true;
    }

    internal void RecordVerifiedWitness(AgentLessonAttemptWitnessV1 witness)
    {
        ArgumentNullException.ThrowIfNull(witness);
        if (_definition.Id != AgentSignalSchoolCatalog.FirstTurnId
            || _attemptWitnesses.Count >= MaximumAttemptWitnesses
            || witness.Ordinal != _attemptWitnesses.Count + 1)
        {
            throw new InvalidOperationException("Lesson attempt witness order was invalid.");
        }

        _attemptWitnesses.Add(witness);
        _firstReversalWitnessTick ??= witness.Tick;
        Set("opposite_reversal_rejected", 1);
    }

    public void RecordStep(RunSnapshot before, RunStepResult result, RunSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        switch (_definition.Id)
        {
            case AgentSignalSchoolCatalog.FirstTurnId:
                if (_firstReversalWitnessTick is { } witnessedTick
                    && before.Tick >= witnessedTick
                    && HasEvent(result, RunEventKind.DirectionChanged))
                {
                    Set("legal_turn_after_rejection", 1);
                }
                break;
            case AgentSignalSchoolCatalog.WrapLineId:
                if (HasEvent(result, RunEventKind.Wrapped))
                {
                    Set("wrapped_event", 1);
                    if (after.Status == RunStatus.Running)
                    {
                        Set("running_after_wrap", 1);
                    }
                }
                break;
            case AgentSignalSchoolCatalog.HungerRouteId:
                if (HasEvent(result, RunEventKind.AteFood))
                {
                    Set("food_eaten", 1);
                    if (!(after.Status == RunStatus.Dead
                        && after.DeathCause == DeathCause.Starvation))
                    {
                        Set("food_before_starvation", 1);
                    }
                }
                break;
            case AgentSignalSchoolCatalog.ExitRouteId:
                if (HasEvent(result, RunEventKind.AteFood)
                    && after.Body.Count == before.Body.Count + 1)
                {
                    Set("food_growth", 1);
                    if (after.Status == RunStatus.Running
                        && AgentStyleEvidenceMath.StructuralOpenExitCount(_config, after) >= 2)
                    {
                        Set("two_structural_exits_after_growth", 1);
                    }
                }
                break;
            case AgentSignalSchoolCatalog.PowerRouteId:
                RecordPowerEvents(result);
                break;
            case AgentSignalSchoolCatalog.RecoverRouteId:
                if (result.OrderedEvents.Any(item =>
                    item.Kind == RunEventKind.CollisionPrevented
                    && item.Cause is DeathCause.SelfCollision or DeathCause.Starvation
                    && item.Power is { } power
                    && Enum.IsDefined(power)))
                {
                    Set("collision_prevented", 1);
                    if (after.Status == RunStatus.Running)
                    {
                        Set("running_after_recovery", 1);
                    }
                }
                break;
            case AgentSignalSchoolCatalog.ComboRouteId:
                if (HasEvent(result, RunEventKind.AteFood))
                {
                    Increment("three_food");
                }
                Set("peak_combo_three", after.ComboCount);
                break;
            case AgentSignalSchoolCatalog.DeathReadId:
                if (after.Status == RunStatus.Dead && after.DeathCause != DeathCause.None)
                {
                    Set("terminal_death", 1);
                    if (result.OrderedEvents.Any(item =>
                        item.Kind == RunEventKind.Died
                        && item.Cause == after.DeathCause))
                    {
                        Set("matching_death_event", 1);
                    }
                }
                break;
            default:
                throw new InvalidOperationException("Unknown Signal School evaluator identity.");
        }
    }

    public AgentLessonProgressV2 Snapshot(
        AgentLessonEvidenceState evidenceState,
        string actionProfile)
    {
        var requirements = _definition.Requirements
            .Select(requirement =>
            {
                var current = Math.Min(requirement.Target, _values[requirement.Id]);
                return new AgentLessonRequirementProgressV2(
                    requirement.Id,
                    requirement.DisplayName,
                    requirement.EvidenceSource,
                    current,
                    requirement.Target,
                    current >= requirement.Target);
            })
            .ToArray();
        var satisfied = requirements.Count(item => item.Satisfied);
        return new AgentLessonProgressV2(
            AgentLessonProgressV2.Contract,
            _definition.Id,
            _definition.Title,
            _definition.Instruction,
            _definition.EvaluationPolicyId,
            Array.AsReadOnly(requirements),
            satisfied,
            satisfied == requirements.Length,
            requirements.FirstOrDefault(item => !item.Satisfied)?.RequirementId,
            evidenceState,
            _attemptWitnesses.Count,
            ComputeAttemptEvidenceHash(_attemptWitnesses),
            evidenceState == AgentLessonEvidenceState.Live
                ? null
                : AgentSignalSchoolCatalog.CreateRetryDescriptor(
                    _definition.Id,
                    actionProfile));
    }

    internal static string ComputeAttemptEvidenceHash(
        IReadOnlyList<AgentLessonAttemptWitnessV1> witnesses)
    {
        ArgumentNullException.ThrowIfNull(witnesses);
        var builder = new StringBuilder();
        foreach (var witness in witnesses)
        {
            builder.Append(witness.Ordinal.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(((byte)witness.Operation).ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(witness.IdempotencyKeyHash)
                .Append('|').Append(witness.Tick.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(witness.StateHash)
                .Append('|').Append(((byte)witness.Action).ToString(CultureInfo.InvariantCulture))
                .Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private void RecordPowerEvents(RunStepResult result)
    {
        foreach (var item in result.OrderedEvents)
        {
            if (item.Kind == RunEventKind.PowerCollected
                && item.Power is { } collected
                && Enum.IsDefined(collected))
            {
                _collectedPowerKinds.Add(collected);
                Set("power_collected", 1);
            }
            else if (item.Kind == RunEventKind.PowerActivated
                && item.Power is { } activated
                && _collectedPowerKinds.Contains(activated))
            {
                Set("same_power_activated", 1);
            }
        }
    }

    private void Set(string id, int value)
    {
        var target = _definition.Requirements.Single(item => item.Id == id).Target;
        _values[id] = Math.Min(target, Math.Max(_values[id], value));
    }

    private void Increment(string id) => Set(id, checked(_values[id] + 1));

    private static bool HasEvent(RunStepResult result, RunEventKind kind) =>
        result.OrderedEvents.Any(item => item.Kind == kind);

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool TryMapDirection(AgentAction action, out Direction direction)
    {
        direction = action switch
        {
            AgentAction.Up => Direction.Up,
            AgentAction.Right => Direction.Right,
            AgentAction.Down => Direction.Down,
            AgentAction.Left => Direction.Left,
            _ => default,
        };
        return action != AgentAction.Continue && Enum.IsDefined(action);
    }
}

internal static class AgentLessonEvidenceReplayEvaluator
{
    private const string EvidenceDomain = "vibesnake-agent-lesson-evidence-v2";

    public static AgentLessonProgressV2 Evaluate(
        string lessonId,
        string actionProfile,
        RunReplay replay,
        IReadOnlyList<AgentLessonAttemptWitnessV1> witnesses)
    {
        ArgumentNullException.ThrowIfNull(replay);
        ArgumentNullException.ThrowIfNull(witnesses);
        var playback = new RunReplayPlayback(replay);
        var tracker = new AgentLessonEvidenceTracker(lessonId, playback.Configuration);
        var expectedOperation = actionProfile switch
        {
            AgentPassportV4.FourDirectionActionProfile => AgentLessonAttemptOperation.Step,
            AgentPassportV4.FourDirectionBurstActionProfile => AgentLessonAttemptOperation.Burst,
            _ => throw new ArgumentException("The lesson action profile is unsupported.", nameof(actionProfile)),
        };
        var witnessKeys = new HashSet<string>(StringComparer.Ordinal);
        var previousWitnessTick = -1;
        for (var index = 0; index < witnesses.Count; index++)
        {
            var witness = witnesses[index];
            if (witness.Ordinal != index + 1 || witness.Tick < previousWitnessTick)
            {
                throw new InvalidOperationException("Lesson attempt witness order was invalid.");
            }
            if (witness.Operation != expectedOperation
                || !witnessKeys.Add(witness.IdempotencyKeyHash))
            {
                throw new InvalidOperationException(
                    "Lesson attempt witness profile or idempotency identity was invalid.");
            }
            VerifyWitness(playback, witness);
            tracker.RecordVerifiedWitness(witness);
            previousWitnessTick = witness.Tick;
        }

        playback.Reset();
        while (!playback.IsComplete)
        {
            var before = playback.CurrentSnapshot;
            if (!playback.TryAdvance(out var frame) || frame is null)
            {
                throw new InvalidOperationException("Verified replay ended before its declared step count.");
            }
            tracker.RecordStep(before, frame.Result, frame.Snapshot);
        }
        return tracker.Snapshot(AgentLessonEvidenceState.Verified, actionProfile);
    }

    public static AgentLessonOutcomeV2 CreateOutcome(
        AgentLessonProgressV2 progress,
        AgentMatchEndReason endReason,
        string replayPayloadHash)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var review = progress.AllRequirementsSatisfied
            ? AgentLessonReviewCode.TargetReached
            : progress.Requirements
                .First(item => !item.Satisfied)
                .EvidenceSource == AgentLessonEvidenceSource.AttemptWitness
                ? AgentLessonReviewCode.InsufficientAttemptEvidence
                : AgentLessonReviewCode.ReplayRequirementUnmet;
        var retry = progress.RetryDescriptor
            ?? throw new InvalidOperationException("Verified lesson progress requires fresh retry guidance.");
        return new AgentLessonOutcomeV2(
            AgentLessonOutcomeV2.Contract,
            progress.LessonId,
            progress.EvaluationPolicyId,
            progress.Requirements,
            progress.RequirementsSatisfied,
            progress.AllRequirementsSatisfied,
            progress.FirstUnmetRequirementId,
            review,
            endReason,
            progress.AttemptEvidenceCount,
            replayPayloadHash,
            progress.AttemptEvidenceHash,
            ComputeEvidenceHash(replayPayloadHash, progress.AttemptEvidenceHash),
            retry);
    }

    public static bool Equivalent(AgentLessonProgressV2 left, AgentLessonProgressV2 right) =>
        string.Equals(left.LessonId, right.LessonId, StringComparison.Ordinal)
        && left.Requirements.SequenceEqual(right.Requirements)
        && left.RequirementsSatisfied == right.RequirementsSatisfied
        && left.AllRequirementsSatisfied == right.AllRequirementsSatisfied
        && string.Equals(
            left.FirstUnmetRequirementId,
            right.FirstUnmetRequirementId,
            StringComparison.Ordinal)
        && left.AttemptEvidenceCount == right.AttemptEvidenceCount
        && string.Equals(
            left.AttemptEvidenceHash,
            right.AttemptEvidenceHash,
            StringComparison.Ordinal);

    public static string ComputeEvidenceHash(
        string replayPayloadHash,
        string attemptEvidenceHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(replayPayloadHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptEvidenceHash);
        var payload = string.Join('\n', EvidenceDomain, replayPayloadHash, attemptEvidenceHash);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private static void VerifyWitness(
        RunReplayPlayback playback,
        AgentLessonAttemptWitnessV1 witness)
    {
        if (witness.Ordinal <= 0
            || witness.Ordinal > AgentLessonEvidenceTracker.MaximumAttemptWitnesses
            || !Enum.IsDefined(witness.Operation)
            || witness.IdempotencyKeyHash.Length != 64
            || witness.IdempotencyKeyHash.Any(character =>
                !(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new InvalidOperationException("Lesson attempt witness shape was invalid.");
        }

        var snapshot = SnapshotAt(playback, witness.Tick);
        if (snapshot.Status != RunStatus.Running
            || !string.Equals(snapshot.StateHash, witness.StateHash, StringComparison.Ordinal)
            || !TryMapDirection(witness.Action, out var direction))
        {
            throw new InvalidOperationException("Lesson attempt witness did not match replay state.");
        }
        var effectiveDirection = snapshot.PendingDirections.Count > 0
            ? snapshot.PendingDirections[^1]
            : snapshot.Direction;
        if (direction != effectiveDirection.Opposite())
        {
            throw new InvalidOperationException("Lesson attempt witness was not an opposite reversal.");
        }
    }

    private static RunSnapshot SnapshotAt(RunReplayPlayback playback, int tick)
    {
        if (tick < 0 || tick > playback.StepCount)
        {
            throw new InvalidOperationException("Lesson attempt witness tick was outside the replay.");
        }
        playback.Seek(tick);
        return playback.CurrentSnapshot;
    }

    private static bool TryMapDirection(AgentAction action, out Direction direction)
    {
        direction = action switch
        {
            AgentAction.Up => Direction.Up,
            AgentAction.Right => Direction.Right,
            AgentAction.Down => Direction.Down,
            AgentAction.Left => Direction.Left,
            _ => default,
        };
        return action != AgentAction.Continue && Enum.IsDefined(action);
    }
}
