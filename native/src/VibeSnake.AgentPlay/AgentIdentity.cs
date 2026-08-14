using System.Text.Json.Serialization;
using VibeSnake.Rules;

namespace VibeSnake.AgentPlay;

public sealed record AgentAccentDefinition(
    string Id,
    string DisplayName,
    AiDisplayColor Color);

public static class AgentAccentCatalog
{
    public const string SignalCyanId = "signal-cyan";

    public static IReadOnlyList<AgentAccentDefinition> All { get; } =
        Array.AsReadOnly(new[]
        {
            new AgentAccentDefinition(SignalCyanId, "Signal Cyan", new AiDisplayColor(100, 255, 255)),
            new AgentAccentDefinition("coil-gold", "Coil Gold", new AiDisplayColor(255, 225, 80)),
            new AgentAccentDefinition("pit-orange", "Pit Orange", new AiDisplayColor(255, 170, 45)),
            new AgentAccentDefinition("archive-magenta", "Archive Magenta", new AiDisplayColor(255, 80, 245)),
            new AgentAccentDefinition("flow-seafoam", "Flow Seafoam", new AiDisplayColor(150, 255, 210)),
            new AgentAccentDefinition("bureau-ivory", "Bureau Ivory", new AiDisplayColor(245, 245, 230)),
            new AgentAccentDefinition("strike-red", "Strike Red", new AiDisplayColor(255, 92, 72)),
            new AgentAccentDefinition("underground-violet", "Underground Violet", new AiDisplayColor(190, 140, 255)),
        });

    public static AgentAccentDefinition Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return All.SingleOrDefault(value => string.Equals(value.Id, id, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Unknown agent accent {id}.", nameof(id));
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AgentPassportV4
{
    public const string Contract = "vibesnake-agent-passport-v4";
    public const string SymbolicStepObservationProfile = "symbolic-step-v4";
    public const string FourDirectionActionProfile = "four-direction-step-v1";
    public const string FourDirectionBurstActionProfile = "four-direction-burst-v1";
    public const int MaximumDisplayNameLength = 48;

    public AgentPassportV4(
        string schema,
        string agentId,
        string policyVersion,
        string displayName,
        string avatarId,
        string accentId,
        string stationId,
        string observationProfile = SymbolicStepObservationProfile,
        string actionProfile = FourDirectionActionProfile)
    {
        if (schema != Contract)
        {
            throw new ArgumentException("The agent passport schema is unsupported.", nameof(schema));
        }

        AgentMatchOptions.ValidateToken(agentId, 64, nameof(agentId));
        AgentMatchOptions.ValidateToken(policyVersion, 64, nameof(policyVersion));
        ValidateDisplayName(displayName);
        _ = CosmeticSetCatalog.Find(avatarId)
            ?? throw new ArgumentException($"Unknown agent avatar {avatarId}.", nameof(avatarId));
        _ = AgentAccentCatalog.Get(accentId);
        _ = StationIdentityCatalog.Get(stationId);
        if (observationProfile != SymbolicStepObservationProfile)
        {
            throw new ArgumentException(
                "The host supports only symbolic-step-v4 observations.",
                nameof(observationProfile));
        }

        if (!IsSupportedActionProfile(actionProfile))
        {
            throw new ArgumentException(
                "The host supports only four-direction-step-v1 or four-direction-burst-v1 actions.",
                nameof(actionProfile));
        }

        Schema = schema;
        AgentId = agentId;
        PolicyVersion = policyVersion;
        DisplayName = displayName;
        AvatarId = avatarId;
        AccentId = accentId;
        StationId = stationId;
        ObservationProfile = observationProfile;
        ActionProfile = actionProfile;
    }

    public string Schema { get; }

    public string AgentId { get; }

    public string PolicyVersion { get; }

    public string DisplayName { get; }

    public string AvatarId { get; }

    public string AccentId { get; }

    public string StationId { get; }

    public string ObservationProfile { get; }

    public string ActionProfile { get; }

    public static AgentPassportV4 Anonymous { get; } = new(
        Contract,
        "anonymous-agent",
        "unversioned",
        "External Agent",
        "classic-signal",
        AgentAccentCatalog.SignalCyanId,
        "global_coil");

    public static AgentPassportV4 CreateAnonymous(string actionProfile)
    {
        if (actionProfile == FourDirectionActionProfile)
        {
            return Anonymous;
        }

        if (!IsSupportedActionProfile(actionProfile))
        {
            throw new ArgumentException(
                "The action profile is unsupported.",
                nameof(actionProfile));
        }

        return new AgentPassportV4(
            Contract,
            "anonymous-agent",
            "unversioned",
            "External Agent",
            "classic-signal",
            AgentAccentCatalog.SignalCyanId,
            "global_coil",
            SymbolicStepObservationProfile,
            actionProfile);
    }

    public static bool IsSupportedActionProfile(string actionProfile) =>
        actionProfile is FourDirectionActionProfile or FourDirectionBurstActionProfile;

    private static void ValidateDisplayName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > MaximumDisplayNameLength
            || value != value.Trim()
            || value.Any(character => char.IsControl(character)))
        {
            throw new ArgumentException(
                $"Agent display names must be trimmed, contain no controls, and use at most {MaximumDisplayNameLength} characters.",
                nameof(value));
        }
    }
}
