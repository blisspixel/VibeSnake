using Godot;
using VibeSnake.Rules;

namespace VibeSnake.Game;

/// <summary>
/// Presentation tokens for the nine power contracts. These are engineering
/// fallback markers, not final authored art.
/// </summary>
internal static class PowerPresentation
{
    public static char Marker(PowerKind kind) =>
        PowerFeedbackCatalog.Find(kind).StableIcon;

    public static string ShortName(PowerKind kind) =>
        PowerFeedbackCatalog.Find(kind).Name;

    public static Color SignalColor(PowerKind kind) => kind switch
    {
        PowerKind.Shield => new Color(0.45f, 0.96f, 1.0f),
        PowerKind.PhaseShift => new Color(0.78f, 0.55f, 1.0f),
        PowerKind.LastStand => new Color(1.0f, 0.72f, 0.28f),
        PowerKind.SlowMo => new Color(0.42f, 0.72f, 1.0f),
        PowerKind.Boost => new Color(1.0f, 0.42f, 0.28f),
        PowerKind.Magnet => new Color(0.95f, 0.55f, 0.95f),
        PowerKind.Bait => new Color(1.0f, 0.82f, 0.25f),
        PowerKind.Gluttony => new Color(0.95f, 0.55f, 0.2f),
        PowerKind.SegmentDetach => new Color(0.85f, 0.35f, 0.4f),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown power kind."),
    };

    public static string DescribeStatus(RunSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var parts = new List<string>(8);

        var interval = snapshot.EffectiveRulesStepMilliseconds;
        if (snapshot.HasShield)
        {
            parts.Add(ActiveTimer(PowerKind.Shield, snapshot.ShieldTicksRemaining, interval));
        }

        if (snapshot.HasPhaseShift)
        {
            parts.Add(ActiveTimer(PowerKind.PhaseShift, snapshot.PhaseShiftTicksRemaining, interval));
        }

        if (snapshot.LastStandHeld)
        {
            parts.Add("[L] LAST STAND HELD");
        }

        if (snapshot.HasLastStandRecovery)
        {
            parts.Add(
                ActiveTimer(
                    PowerKind.LastStand,
                    snapshot.LastStandRecoveryTicksRemaining,
                    interval,
                    "RECOVERY IMMUNITY"));
        }

        if (snapshot.HasSlowMo)
        {
            parts.Add(ActiveTimer(PowerKind.SlowMo, snapshot.SlowMoTicksRemaining, interval));
        }

        if (snapshot.HasBoost)
        {
            parts.Add(ActiveTimer(PowerKind.Boost, snapshot.BoostTicksRemaining, interval));
        }

        if (snapshot.HasMagnet)
        {
            parts.Add(ActiveTimer(PowerKind.Magnet, snapshot.MagnetTicksRemaining, interval));
        }

        if (snapshot.HasGluttony)
        {
            parts.Add(ActiveTimer(PowerKind.Gluttony, snapshot.GluttonyTicksRemaining, interval));
        }

        if (snapshot.HasBait && snapshot.BaitPosition is { } bait)
        {
            parts.Add($"[T] BAIT ARMED AT {bait.X},{bait.Y}: EAT CURRENT FOOD TO TRIGGER");
        }

        if (snapshot.HasDetachedObstacles)
        {
            parts.Add(
                ActiveTimer(
                    PowerKind.SegmentDetach,
                    snapshot.DetachedObstacleTicksRemaining,
                    interval,
                    $"DETACH x{snapshot.DetachedObstacles.Count}"));
        }

        if (snapshot.PowerPickup is { } pickup)
        {
            var definition = PowerFeedbackCatalog.Find(pickup.Kind);
            var decision = PowerDecisionCatalog.Get(pickup.Kind);
            var seconds = Seconds(pickup.VisibilityTicksRemaining, interval);
            parts.Add(
                $"OFFER {decision.Family.ToString().ToUpperInvariant()} "
                    + $"[{definition.StableIcon}] {definition.Name} {seconds:0.0}s "
                    + definition.PickupTelegraph);
        }

        if (parts.Count > 0)
        {
            if (
                snapshot.MovementCadenceNumerator != 1
                || snapshot.MovementCadenceDenominator != 1)
            {
                parts.Add(
                    $"CADENCE {snapshot.MovementCadenceNumerator}/{snapshot.MovementCadenceDenominator}");
            }

            return string.Join("  |  ", parts);
        }

        return "POWER SIGNAL QUIET";
    }

    private static string ActiveTimer(
        PowerKind kind,
        int ticksRemaining,
        int stepIntervalMilliseconds,
        string? label = null) =>
        $"[{Marker(kind)}] {label ?? ShortName(kind)} {Seconds(ticksRemaining, stepIntervalMilliseconds):0.0}s";

    private static double Seconds(int ticksRemaining, int stepIntervalMilliseconds) =>
        RulesCadenceClock.RemainingWallClockSeconds(ticksRemaining, stepIntervalMilliseconds);
}
