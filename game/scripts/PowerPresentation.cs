using Godot;
using VibeSnake.Rules;

namespace VibeSnake.Game;

/// <summary>
/// Presentation tokens for the nine power contracts. Letter icons are the
/// primary channel. Signal colours are qualified gameplay hues grouped by
/// meaning: protection is cool, tempo is blue and red, food runs from gold
/// to teal, and Segment Detach is the warning red. They are not illustration art.
/// </summary>
internal static class PowerPresentation
{
    public static char Marker(PowerKind kind) =>
        PowerFeedbackCatalog.Find(kind).StableIcon;

    public static string ShortName(PowerKind kind) =>
        PowerFeedbackCatalog.Find(kind).Name;

    public static Color SignalColor(PowerKind kind)
    {
        var (red, green, blue) = SignalBytes(kind);
        return new Color(red / 255f, green / 255f, blue / 255f);
    }

    public static string SignalHex(PowerKind kind)
    {
        var (red, green, blue) = SignalBytes(kind);
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    public static Color PhaseBody => Spectral(SignalColor(PowerKind.PhaseShift));

    public static Color GhostOutline => Spectral(SignalColor(PowerKind.PhaseShift));

    public static Color GluttonyBody => SignalColor(PowerKind.Gluttony);

    private static (byte R, byte G, byte B) SignalBytes(PowerKind kind) => kind switch
    {
        PowerKind.Shield => (0x3C, 0xD0, 0xCC),
        PowerKind.PhaseShift => (0xA0, 0x9A, 0xDF),
        PowerKind.LastStand => (0x44, 0x8E, 0xA8),
        PowerKind.SlowMo => (0x3F, 0x86, 0xDD),
        PowerKind.Boost => (0xF7, 0x76, 0x71),
        PowerKind.Magnet => (0x40, 0xB4, 0xA0),
        PowerKind.Bait => (0xFF, 0xE0, 0x4D),
        PowerKind.Gluttony => (0xB1, 0xCC, 0x46),
        PowerKind.SegmentDetach => (0xD8, 0x5C, 0x68),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown power kind."),
    };

    private static Color Spectral(Color color) => new(color.R, color.G, color.B, 0.72f);

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
