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

public sealed record AgentViewerFrameV5(
    string Schema,
    long Sequence,
    AgentViewerOperationKind Operation,
    int StartTick,
    string StartStateHash,
    int StepsAdvanced,
    AgentBurstStopReason? BurstStopReason,
    RunEventKind? BurstStopEvent,
    AgentObservationV3 Observation,
    AgentMatchEndReason EndReason,
    bool VerifiedResultAvailable)
{
    public const string Contract = "vibesnake-agent-viewer-frame-v5";
}

public interface IAgentViewerSink
{
    bool TryPublish(AgentViewerFrameV5 frame);
}
