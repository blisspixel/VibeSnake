using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

internal enum AgentReplayLane : byte
{
    Agent = 0,
    Rival = 1,
}

internal enum AgentReplayFinalizationFailure : byte
{
    None = 0,
    Recording = 1,
    Verification = 2,
}

internal sealed record AgentReplayFinalization(
    AgentReplayFinalizationFailure Failure,
    RunReplay? Replay,
    ReplayVerificationResult? Verification)
{
    public static AgentReplayFinalization Failed(AgentReplayFinalizationFailure failure)
    {
        if (failure == AgentReplayFinalizationFailure.None)
        {
            throw new ArgumentOutOfRangeException(nameof(failure));
        }

        return new AgentReplayFinalization(failure, Replay: null, Verification: null);
    }
}

internal interface IAgentReplayFinalizer
{
    AgentReplayFinalization Finalize(
        AgentReplayLane lane,
        RunReplayRecorder recorder,
        SnakeRun run);
}

internal sealed class AgentReplayFinalizer : IAgentReplayFinalizer
{
    public static AgentReplayFinalizer Instance { get; } = new();

    private AgentReplayFinalizer()
    {
    }

    public AgentReplayFinalization Finalize(
        AgentReplayLane lane,
        RunReplayRecorder recorder,
        SnakeRun run)
    {
        _ = lane;
        var recording = recorder.Finish(run);
        if (!recording.IsSuccessful || recording.Replay is null)
        {
            return AgentReplayFinalization.Failed(AgentReplayFinalizationFailure.Recording);
        }

        var verification = recording.Replay.Verify();
        return verification.IsValid
            ? new AgentReplayFinalization(
                AgentReplayFinalizationFailure.None,
                recording.Replay,
                verification)
            : AgentReplayFinalization.Failed(AgentReplayFinalizationFailure.Verification);
    }
}
