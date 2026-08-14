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

public sealed record AgentViewerFrameV3(
    string Schema,
    long Sequence,
    AgentObservationV2 Observation,
    AgentMatchEndReason EndReason,
    bool VerifiedResultAvailable)
{
    public const string Contract = "vibesnake-agent-viewer-frame-v3";
}

public interface IAgentViewerSink
{
    bool TryPublish(AgentViewerFrameV3 frame);
}
