using Godot;
using VibeSnake.Rules;

namespace VibeSnake.Game;

/// <summary>
/// Presentation tokens for the nine power contracts. These are engineering
/// fallback markers, not final authored art.
/// </summary>
internal static class PowerPresentation
{
    public static char Marker(PowerKind kind) => kind switch
    {
        PowerKind.Shield => 'S',
        PowerKind.PhaseShift => 'P',
        PowerKind.LastStand => 'L',
        PowerKind.SlowMo => 'W',
        PowerKind.Boost => 'B',
        PowerKind.Magnet => 'M',
        PowerKind.Bait => 'T',
        PowerKind.Gluttony => 'G',
        PowerKind.SegmentDetach => 'D',
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown power kind."),
    };

    public static string ShortName(PowerKind kind) => kind switch
    {
        PowerKind.Shield => "SHIELD",
        PowerKind.PhaseShift => "PHASE",
        PowerKind.LastStand => "LAST STAND",
        PowerKind.SlowMo => "SLOW-MO",
        PowerKind.Boost => "BOOST",
        PowerKind.Magnet => "MAGNET",
        PowerKind.Bait => "BAIT",
        PowerKind.Gluttony => "GLUTTONY",
        PowerKind.SegmentDetach => "DETACH",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown power kind."),
    };

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

        if (snapshot.HasShield)
        {
            parts.Add(ActiveTimer("SHIELD", snapshot.ShieldTicksRemaining));
        }

        if (snapshot.HasPhaseShift)
        {
            parts.Add(ActiveTimer("PHASE", snapshot.PhaseShiftTicksRemaining));
        }

        if (snapshot.LastStandHeld)
        {
            parts.Add("LAST STAND HELD");
        }

        if (snapshot.HasLastStandRecovery)
        {
            parts.Add(ActiveTimer("RECOVERY", snapshot.LastStandRecoveryTicksRemaining));
        }

        if (snapshot.HasSlowMo)
        {
            parts.Add(ActiveTimer("SLOW-MO", snapshot.SlowMoTicksRemaining));
        }

        if (snapshot.HasBoost)
        {
            parts.Add(ActiveTimer("BOOST", snapshot.BoostTicksRemaining));
        }

        if (snapshot.HasMagnet)
        {
            parts.Add(ActiveTimer("MAGNET", snapshot.MagnetTicksRemaining));
        }

        if (snapshot.HasGluttony)
        {
            parts.Add(ActiveTimer("GLUTTONY", snapshot.GluttonyTicksRemaining));
        }

        if (snapshot.HasBait && snapshot.BaitPosition is { } bait)
        {
            parts.Add($"BAIT MARK {bait.X},{bait.Y}");
        }

        if (snapshot.HasDetachedObstacles)
        {
            parts.Add(
                ActiveTimer(
                    $"DETACH x{snapshot.DetachedObstacles.Count}",
                    snapshot.DetachedObstacleTicksRemaining));
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

        if (snapshot.PowerPickup is { } pickup)
        {
            var seconds = Seconds(pickup.VisibilityTicksRemaining);
            return $"{ShortName(pickup.Kind)} SIGNAL    {seconds:0.0}s    ROUTE TO {Marker(pickup.Kind)}";
        }

        return "POWER SIGNAL QUIET";
    }

    private static string ActiveTimer(string label, int ticksRemaining) =>
        $"{label} {Seconds(ticksRemaining):0.0}s";

    private static double Seconds(int ticksRemaining) =>
        ticksRemaining * RunConfig.RulesTickMilliseconds / 1000.0;
}
