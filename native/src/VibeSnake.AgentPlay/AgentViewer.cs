namespace VibeSnake.AgentPlay;

public sealed record AgentViewerFrameV1(
    string Schema,
    long Sequence,
    AgentObservationV1 Observation)
{
    public const string Contract = "vibesnake-agent-viewer-frame-v1";
}

public interface IAgentViewerSink
{
    bool TryPublish(AgentViewerFrameV1 frame);
}
