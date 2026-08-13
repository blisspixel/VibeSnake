namespace VibeSnake.Game;

/// <summary>
/// Lightweight presentation frame-time sampler for decision-gate evidence.
/// Host-dependent; does not claim declared-hardware acceptance by itself.
/// </summary>
internal sealed class PresentationFrameSampler
{
    private readonly List<double> _frameMilliseconds = [];

    public int SampleCount => _frameMilliseconds.Count;

    public void RecordFrameMilliseconds(double frameMilliseconds)
    {
        if (double.IsNaN(frameMilliseconds)
            || double.IsInfinity(frameMilliseconds)
            || frameMilliseconds < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameMilliseconds));
        }

        _frameMilliseconds.Add(frameMilliseconds);
    }

    public PresentationFrameSummary Summarize()
    {
        if (_frameMilliseconds.Count == 0)
        {
            throw new InvalidOperationException("No frame samples were recorded.");
        }

        var ordered = _frameMilliseconds.OrderBy(value => value).ToArray();
        return new PresentationFrameSummary(
            SampleCount: ordered.Length,
            AverageMilliseconds: ordered.Average(),
            P50Milliseconds: Percentile(ordered, 0.50),
            P95Milliseconds: Percentile(ordered, 0.95),
            P99Milliseconds: Percentile(ordered, 0.99),
            MaxMilliseconds: ordered[^1]);
    }

    private static double Percentile(double[] orderedAscending, double percentile)
    {
        if (orderedAscending.Length == 1)
        {
            return orderedAscending[0];
        }

        var rank = percentile * (orderedAscending.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return orderedAscending[lower];
        }

        var weight = rank - lower;
        var interpolated = (orderedAscending[lower] * (1.0 - weight))
            + (orderedAscending[upper] * weight);
        return Math.Clamp(
            interpolated,
            orderedAscending[lower],
            orderedAscending[upper]);
    }
}

internal readonly record struct PresentationFrameSummary(
    int SampleCount,
    double AverageMilliseconds,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double MaxMilliseconds);
