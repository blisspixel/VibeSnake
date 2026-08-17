using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

public static class AgentViewerTransport
{
    // .NET maps named pipes to Unix-domain sockets on Unix. A short portable
    // name leaves room for the platform temporary-directory and CoreFxPipe prefixes.
    public const int MaximumPipeNameLength = 24;

    public static bool IsValidPipeName(string? pipeName) =>
        !string.IsNullOrWhiteSpace(pipeName)
        && pipeName.Length <= MaximumPipeNameLength
        && pipeName.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_');
}

public enum AgentViewerOperationKind : byte
{
    Initial = 0,
    Step = 1,
    Burst = 2,
    Finish = 3,
}

/// <summary>
/// How many non-reversal exits the head structurally has right now. These are
/// threshold crossings of <see cref="AgentSurvivalStateV1.StructuralOpenExits"/>,
/// not grades, predictions, or advice about which exit to take.
/// </summary>
public enum AgentExitPressureV1 : byte
{
    NotRunning = 0,
    Open = 1,
    Narrow = 2,
    Pinned = 3,
    Trapped = 4,
}

/// <summary>
/// The closed set of held resources that can survive a mistake the agent has
/// already made. Boost, Magnet, and Gluttony change scoring or routing rather
/// than recovery, so they are deliberately outside this catalog.
/// </summary>
public enum AgentRecoveryResourceKind : byte
{
    Shield = 0,
    PhaseShift = 1,
    LastStand = 2,
    SlowMo = 3,
}

public sealed record AgentRecoveryResourceV1(
    AgentRecoveryResourceKind Kind,
    bool Held,
    int TicksRemaining);

/// <summary>
/// Observed danger and recovery facts for one presented frame. Every value is
/// derived from public board state the same frame already carries, so a viewer
/// can recompute all of it and reject a frame that disagrees with itself.
/// It names what is true now. It never names a direction to take.
/// </summary>
public sealed record AgentSurvivalStateV1(
    string Schema,
    int CandidateExits,
    int StructuralOpenExits,
    AgentExitPressureV1 ExitPressure,
    int HeldRecoveryCount,
    IReadOnlyList<AgentRecoveryResourceV1> RecoveryResources)
{
    public const string Contract = "vibesnake-agent-survival-state-v1";

    // A running head can always try three directions: the reversal is not a
    // legal turn, so it is never counted as an exit that was available.
    public const int RunningCandidateExits = 3;

    public static readonly IReadOnlyList<AgentRecoveryResourceKind> RecoveryOrder =
        Array.AsReadOnly(new[]
        {
            AgentRecoveryResourceKind.Shield,
            AgentRecoveryResourceKind.PhaseShift,
            AgentRecoveryResourceKind.LastStand,
            AgentRecoveryResourceKind.SlowMo,
        });

    public static AgentExitPressureV1 Pressure(bool running, int structuralOpenExits) =>
        !running
            ? AgentExitPressureV1.NotRunning
            : structuralOpenExits switch
            {
                >= 3 => AgentExitPressureV1.Open,
                2 => AgentExitPressureV1.Narrow,
                1 => AgentExitPressureV1.Pinned,
                _ => AgentExitPressureV1.Trapped,
            };

    public static AgentSurvivalStateV1 Create(
        bool running,
        int structuralOpenExits,
        int shieldTicksRemaining,
        int phaseShiftTicksRemaining,
        bool lastStandHeld,
        int lastStandRecoveryTicksRemaining,
        int slowMoTicksRemaining)
    {
        var resources = new[]
        {
            new AgentRecoveryResourceV1(
                AgentRecoveryResourceKind.Shield,
                shieldTicksRemaining > 0,
                shieldTicksRemaining),
            new AgentRecoveryResourceV1(
                AgentRecoveryResourceKind.PhaseShift,
                phaseShiftTicksRemaining > 0,
                phaseShiftTicksRemaining),
            new AgentRecoveryResourceV1(
                AgentRecoveryResourceKind.LastStand,
                lastStandHeld,
                lastStandRecoveryTicksRemaining),
            new AgentRecoveryResourceV1(
                AgentRecoveryResourceKind.SlowMo,
                slowMoTicksRemaining > 0,
                slowMoTicksRemaining),
        };
        return new AgentSurvivalStateV1(
            Contract,
            running ? RunningCandidateExits : 0,
            running ? structuralOpenExits : 0,
            Pressure(running, structuralOpenExits),
            resources.Count(resource => resource.Held),
            Array.AsReadOnly(resources));
    }
}

public sealed record AgentViewerFrameV9(
    string Schema,
    long Sequence,
    AgentViewerOperationKind Operation,
    int StartTick,
    string StartStateHash,
    int StepsAdvanced,
    AgentBurstStopReason? BurstStopReason,
    RunEventKind? BurstStopEvent,
    AgentObservationV5 Observation,
    // Observed danger and recovery facts for the presented step. A spectator can
    // read the same danger the agent can compute, without being told a route.
    AgentSurvivalStateV1 SurvivalState,
    AgentMatchEndReason EndReason,
    bool VerifiedResultAvailable,
    // Present only with a verified result. The spectator overlay shows a bounded
    // prefix so a human can match the window against the host's replay identity.
    string? VerifiedReplayPayloadHash = null,
    AgentStyleOutcomeV3? StyleOutcome = null,
    AgentLessonOutcomeV3? LessonOutcome = null)
{
    public const string Contract = "vibesnake-agent-viewer-frame-v9";
}

public interface IAgentViewerSink
{
    bool TryPublish(AgentViewerFrameV9 frame);
}
