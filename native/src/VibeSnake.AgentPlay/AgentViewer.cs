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

public sealed record AgentViewerFrameV7(
    string Schema,
    long Sequence,
    AgentViewerOperationKind Operation,
    int StartTick,
    string StartStateHash,
    int StepsAdvanced,
    AgentBurstStopReason? BurstStopReason,
    RunEventKind? BurstStopEvent,
    AgentObservationV5 Observation,
    AgentMatchEndReason EndReason,
    bool VerifiedResultAvailable,
    AgentStyleOutcomeV2? StyleOutcome = null,
    AgentLessonOutcomeV2? LessonOutcome = null)
{
    public const string Contract = "vibesnake-agent-viewer-frame-v7";
}

public interface IAgentViewerSink
{
    bool TryPublish(AgentViewerFrameV7 frame);
}
