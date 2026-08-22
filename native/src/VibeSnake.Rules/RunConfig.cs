using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VibeSnake.Rules;

public sealed record RunConfig(
    int Width = 64,
    int Height = 33,
    int StarvationTicks = 600,
    int MaximumDirectionQueue = 3,
    int FoodScore = 10,
    int ComboWindowTicks = 60,
    int SpeedBonusTicks = 30,
    int PowerSpawnIntervalTicks = 300,
    int PowerVisibleTicks = 120,
    int ShieldDurationTicks = 100,
    int PhaseShiftDurationTicks = 100,
    int LastStandRecoveryTicks = 60,
    int SlowMoDurationTicks = 120,
    int BoostDurationTicks = 80,
    int MagnetDurationTicks = 120,
    int GluttonyDurationTicks = 100,
    int SegmentDetachObstacleTicks = 200,
    int SegmentDetachMaxSegments = 5,
    bool EnableNearMiss = true,
    bool EnableComboExpiredEvent = true,
    bool EnableAchievementCandidates = false,
    int StarvationWarningTicks = 200,
    string ModeId = RunModeCatalog.VibeId,
    int ModeVersion = RunModeCatalog.CurrentModeVersion,
    bool EnableStarvation = true,
    bool EnableComboScoring = true,
    bool EnableSpeedScoreBonus = true,
    bool EnableLengthScoreBonus = true,
    bool EnableAdaptation = false,
    string AdaptivePolicyId = AdaptiveDifficultyPolicy.DisabledPolicyId,
    bool EnablePowerDecisionOffers = false,
    bool AvoidFoodGeodesicPowerOffers = false)
{
    /// <summary>
    /// Algorithm id for <see cref="ComputeConfigHash"/>. Distinct from the run
    /// state hash so score and replay metadata can identify rules without
    /// participating in step-by-step state identity.
    /// </summary>
    public const string ConfigHashAlgorithmId = "sha256-canonical-runconfig-v3";

    public const int RulesTickMilliseconds = 50;
    /// <summary>Default remaining hunger ticks that trigger a starvation warning (10s at 50ms).</summary>
    public const int DefaultStarvationWarningTicks = 200;
    public const int MaximumGridDimension = 4_096;
    public const int MaximumGridCells = 262_144;
    public const int MaximumConfiguredTicks = 1_000_000;
    public const int MaximumDirectionQueueCapacity = 64;
    public const int MaximumFoodScore = 1_000_000;
    public const int MinimumPowerVisibleTicks = 2;
    public const int MinimumShieldDurationTicks = 2;
    public const int MinimumPhaseShiftDurationTicks = 2;
    public const int MinimumLastStandRecoveryTicks = 1;
    public const int MinimumSlowMoDurationTicks = 2;
    public const int MinimumBoostDurationTicks = 2;
    public const int MinimumMagnetDurationTicks = 2;
    public const int MinimumGluttonyDurationTicks = 2;
    public const int MinimumSegmentDetachObstacleTicks = 2;
    public const int MinimumSegmentDetachMaxSegments = 1;
    public const int MaximumSegmentDetachMaxSegments = 64;

    internal void Validate()
    {
        if (!RunModeCatalog.IsSupportedIdentity(ModeId, ModeVersion))
        {
            throw new ArgumentException(
                $"Unsupported run mode identity {ModeId}@{ModeVersion}.",
                nameof(ModeId));
        }

        if (string.IsNullOrWhiteSpace(AdaptivePolicyId))
        {
            throw new ArgumentException(
                "The adaptive policy identity must not be blank.",
                nameof(AdaptivePolicyId));
        }

        if (string.Equals(ModeId, RunModeCatalog.ClassicId, StringComparison.Ordinal))
        {
            if (EnableAdaptation
                || !string.Equals(
                    AdaptivePolicyId,
                    AdaptiveDifficultyPolicy.DisabledPolicyId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Classic requires adaptation to remain disabled with policy 'none'.",
                    nameof(EnableAdaptation));
            }

            if (EnableStarvation
                || EnableComboScoring
                || EnableSpeedScoreBonus
                || EnableLengthScoreBonus
                || EnableNearMiss
                || EnableComboExpiredEvent
                || EnableAchievementCandidates
                || EnablePowerDecisionOffers
                || AvoidFoodGeodesicPowerOffers
                || PowerSpawnIntervalTicks != 0)
            {
                throw new ArgumentException(
                    "Classic cannot enable starvation, bonus scoring, near misses, or powers.",
                    nameof(ModeId));
            }
        }
        else if (EnableAdaptation)
        {
            if (!EnableStarvation
                || !string.Equals(
                    AdaptivePolicyId,
                    AdaptiveDifficultyPolicy.CurrentPolicyId,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Enabled Vibe adaptation requires starvation and the current adaptive policy.",
                    nameof(EnableAdaptation));
            }
        }
        else if (!string.Equals(
            AdaptivePolicyId,
            AdaptiveDifficultyPolicy.DisabledPolicyId,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Disabled adaptation requires policy 'none'.",
                nameof(AdaptivePolicyId));
        }

        if (Width < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(Width), "The grid must be at least two cells wide.");
        }

        if (Height < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(Height), "The grid must be at least two cells high.");
        }

        if (Width > MaximumGridDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Width),
                $"The grid width cannot exceed {MaximumGridDimension} cells.");
        }

        if (Height > MaximumGridDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Height),
                $"The grid height cannot exceed {MaximumGridDimension} cells.");
        }

        if ((long)Width * Height > MaximumGridCells)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Height),
                $"The grid cannot contain more than {MaximumGridCells} cells.");
        }

        if (StarvationTicks <= 0 || StarvationTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StarvationTicks),
                $"Starvation cannot exceed {MaximumConfiguredTicks} ticks.");
        }

        // Warning ticks at or above StarvationTicks never fire (short test configs
        // keep the production default of 200 while using smaller starvation budgets).
        if (StarvationWarningTicks < 0 || StarvationWarningTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StarvationWarningTicks),
                $"Starvation warning cannot exceed {MaximumConfiguredTicks} ticks.");
        }

        if (
            MaximumDirectionQueue <= 0
            || MaximumDirectionQueue > MaximumDirectionQueueCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumDirectionQueue),
                $"The direction queue cannot exceed {MaximumDirectionQueueCapacity} entries.");
        }

        if (FoodScore <= 0 || FoodScore > MaximumFoodScore)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FoodScore),
                $"The base food score cannot exceed {MaximumFoodScore}.");
        }

        if (ComboWindowTicks <= 0 || ComboWindowTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ComboWindowTicks),
                $"The combo window cannot exceed {MaximumConfiguredTicks} ticks.");
        }

        if (SpeedBonusTicks <= 0 || SpeedBonusTicks > ComboWindowTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(SpeedBonusTicks));
        }

        if (PowerSpawnIntervalTicks < 0 || PowerSpawnIntervalTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(PowerSpawnIntervalTicks));
        }

        if (PowerVisibleTicks < MinimumPowerVisibleTicks || PowerVisibleTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(PowerVisibleTicks));
        }

        if (
            ShieldDurationTicks < MinimumShieldDurationTicks
            || ShieldDurationTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(ShieldDurationTicks));
        }

        if (
            PhaseShiftDurationTicks < MinimumPhaseShiftDurationTicks
            || PhaseShiftDurationTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(PhaseShiftDurationTicks));
        }

        if (
            LastStandRecoveryTicks < MinimumLastStandRecoveryTicks
            || LastStandRecoveryTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(LastStandRecoveryTicks));
        }

        if (
            SlowMoDurationTicks < MinimumSlowMoDurationTicks
            || SlowMoDurationTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(SlowMoDurationTicks));
        }

        if (
            BoostDurationTicks < MinimumBoostDurationTicks
            || BoostDurationTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(BoostDurationTicks));
        }

        if (
            MagnetDurationTicks < MinimumMagnetDurationTicks
            || MagnetDurationTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(MagnetDurationTicks));
        }

        if (
            GluttonyDurationTicks < MinimumGluttonyDurationTicks
            || GluttonyDurationTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(GluttonyDurationTicks));
        }

        if (
            SegmentDetachObstacleTicks < MinimumSegmentDetachObstacleTicks
            || SegmentDetachObstacleTicks > MaximumConfiguredTicks)
        {
            throw new ArgumentOutOfRangeException(nameof(SegmentDetachObstacleTicks));
        }

        if (
            SegmentDetachMaxSegments < MinimumSegmentDetachMaxSegments
            || SegmentDetachMaxSegments > MaximumSegmentDetachMaxSegments)
        {
            throw new ArgumentOutOfRangeException(nameof(SegmentDetachMaxSegments));
        }
    }

    /// <summary>
    /// Lowercase SHA-256 of the full effective rules configuration, including
    /// ruleset identity and every field that can change scored behavior.
    /// Serializes every established field. The compatibility-off power offer
    /// flag is omitted to preserve frozen fixture hashes; enabling it writes an
    /// explicit true value. Does not include seeds or live run state.
    /// </summary>
    public string ComputeConfigHash()
    {
        Validate();
        var payload = SerializeCanonicalConfigBytes();
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    /// <summary>
    /// Canonical JSON document for the effective rules configuration.
    /// Stable field order; suitable for inspection and hashing.
    /// </summary>
    public string SerializeCanonicalConfig()
    {
        Validate();
        return Encoding.UTF8.GetString(SerializeCanonicalConfigBytes());
    }

    private byte[] SerializeCanonicalConfigBytes()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("algorithm", ConfigHashAlgorithmId);
            writer.WriteString("rulesetId", RulesetIdentity.CurrentId);
            writer.WriteNumber("rulesVersion", RulesetIdentity.CurrentVersion);
            writer.WriteString("modeId", ModeId);
            writer.WriteNumber("modeVersion", ModeVersion);
            writer.WriteNumber("width", Width);
            writer.WriteNumber("height", Height);
            writer.WriteNumber("rulesTickMilliseconds", RulesTickMilliseconds);
            writer.WriteNumber("starvationTicks", StarvationTicks);
            writer.WriteNumber("starvationWarningTicks", StarvationWarningTicks);
            writer.WriteNumber("maximumDirectionQueue", MaximumDirectionQueue);
            writer.WriteNumber("foodScore", FoodScore);
            writer.WriteNumber("comboWindowTicks", ComboWindowTicks);
            writer.WriteNumber("speedBonusTicks", SpeedBonusTicks);
            writer.WriteNumber("powerSpawnIntervalTicks", PowerSpawnIntervalTicks);
            writer.WriteNumber("powerVisibleTicks", PowerVisibleTicks);
            writer.WriteNumber("shieldDurationTicks", ShieldDurationTicks);
            writer.WriteNumber("phaseShiftDurationTicks", PhaseShiftDurationTicks);
            writer.WriteNumber("lastStandRecoveryTicks", LastStandRecoveryTicks);
            writer.WriteNumber("slowMoDurationTicks", SlowMoDurationTicks);
            writer.WriteNumber("boostDurationTicks", BoostDurationTicks);
            writer.WriteNumber("magnetDurationTicks", MagnetDurationTicks);
            writer.WriteNumber("gluttonyDurationTicks", GluttonyDurationTicks);
            writer.WriteNumber("segmentDetachObstacleTicks", SegmentDetachObstacleTicks);
            writer.WriteNumber("segmentDetachMaxSegments", SegmentDetachMaxSegments);
            writer.WriteBoolean("enableNearMiss", EnableNearMiss);
            writer.WriteBoolean("enableComboExpiredEvent", EnableComboExpiredEvent);
            writer.WriteBoolean("enableAchievementCandidates", EnableAchievementCandidates);
            writer.WriteBoolean("enableStarvation", EnableStarvation);
            writer.WriteBoolean("enableComboScoring", EnableComboScoring);
            writer.WriteBoolean("enableSpeedScoreBonus", EnableSpeedScoreBonus);
            writer.WriteBoolean("enableLengthScoreBonus", EnableLengthScoreBonus);
            writer.WriteBoolean("enableAdaptation", EnableAdaptation);
            writer.WriteString("adaptivePolicyId", AdaptivePolicyId);
            if (EnablePowerDecisionOffers)
            {
                writer.WriteBoolean("enablePowerDecisionOffers", true);
            }
            if (AvoidFoodGeodesicPowerOffers)
            {
                writer.WriteBoolean("avoidFoodGeodesicPowerOffers", true);
            }
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
