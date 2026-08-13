namespace VibeSnake.Rules;

/// <summary>
/// Player-visible state of the bounded Vibe adaptive policy.
/// </summary>
public enum AdaptiveDifficultyState : byte
{
    Disabled = 0,
    Support = 1,
    Standard = 2,
    Pressure = 3,
}

/// <summary>
/// One deterministic adaptive decision. Hunger drain is the only controlled
/// variable, which keeps the policy inspectable and prevents hidden score changes.
/// </summary>
public readonly record struct AdaptiveDifficultyDecision(
    AdaptiveDifficultyState State,
    int HungerDrainTicks,
    string Reason);

/// <summary>
/// Pure, stateless Vibe difficulty policy. It reads only versioned run state,
/// never profile history, wall-clock time, presentation state, or device input.
/// </summary>
public static class AdaptiveDifficultyPolicy
{
    public const string DisabledPolicyId = "none";
    public const string CurrentPolicyId = "vibe-bounded-hunger-v1";
    public const int SupportComboCeiling = 2;
    public const int PressureComboThreshold = 10;
    public const int MinimumHungerDrainTicks = 0;
    public const int MaximumHungerDrainTicks = 2;

    public const string PolicyDescription =
        "Low-hunger runs below combo 3 drain every other step; combo 10+ drains one extra tick every fourth step; all other steps drain normally.";

    public static AdaptiveDifficultyDecision Evaluate(
        RunConfig config,
        int tick,
        int comboCount,
        int hungerTicksRemaining)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentOutOfRangeException.ThrowIfNegative(tick);
        ArgumentOutOfRangeException.ThrowIfNegative(comboCount);

        if (hungerTicksRemaining < 0 || hungerTicksRemaining > config.StarvationTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(hungerTicksRemaining));
        }

        if (!config.EnableStarvation)
        {
            return new AdaptiveDifficultyDecision(
                AdaptiveDifficultyState.Disabled,
                0,
                "Starvation is disabled for this mode.");
        }

        if (!config.EnableAdaptation)
        {
            return new AdaptiveDifficultyDecision(
                AdaptiveDifficultyState.Disabled,
                1,
                "Adaptation is disabled for this run.");
        }

        if (!string.Equals(
                config.AdaptivePolicyId,
                CurrentPolicyId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The enabled adaptive policy does not match the supported contract.",
                nameof(config));
        }

        if (config.StarvationWarningTicks > 0
            && hungerTicksRemaining <= config.StarvationWarningTicks
            && comboCount <= SupportComboCeiling)
        {
            return new AdaptiveDifficultyDecision(
                AdaptiveDifficultyState.Support,
                tick % 2 == 0 ? 1 : 0,
                "Low hunger and combo below 3 slow hunger drain to every other step.");
        }

        if (comboCount >= PressureComboThreshold
            && hungerTicksRemaining > config.StarvationWarningTicks)
        {
            return new AdaptiveDifficultyDecision(
                AdaptiveDifficultyState.Pressure,
                tick % 4 == 0 ? 2 : 1,
                "Combo 10 or higher adds one hunger tick every fourth step.");
        }

        return new AdaptiveDifficultyDecision(
            AdaptiveDifficultyState.Standard,
            1,
            "Normal hunger drain applies.");
    }
}
