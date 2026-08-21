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

    public double[] SnapshotSamples() => [.. _frameMilliseconds];

    /// <summary>
    /// Estimates the intrinsic frame distribution from identical replicates.
    /// Shared-host scheduling can add elapsed time but cannot reduce it, so the
    /// pointwise minimum removes only one-sided external delay. A rendering cost
    /// present at the same workload position in every replicate remains.
    /// </summary>
    public static PresentationFrameSummary SummarizePointwiseMinimum(
        IReadOnlyList<IReadOnlyList<double>> replicates)
    {
        ArgumentNullException.ThrowIfNull(replicates);
        if (replicates.Count == 0)
        {
            throw new ArgumentException(
                "At least one presentation replicate is required.",
                nameof(replicates));
        }

        var sampleCount = replicates[0]?.Count
            ?? throw new ArgumentException(
                "Presentation replicates cannot contain null samples.",
                nameof(replicates));
        if (sampleCount == 0)
        {
            throw new ArgumentException(
                "Presentation replicates cannot be empty.",
                nameof(replicates));
        }

        var reduced = new PresentationFrameSampler();
        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var minimum = double.PositiveInfinity;
            foreach (var replicate in replicates)
            {
                if (replicate is null || replicate.Count != sampleCount)
                {
                    throw new ArgumentException(
                        "Presentation replicates must have identical sample counts.",
                        nameof(replicates));
                }

                var sample = replicate[sampleIndex];
                if (!double.IsFinite(sample) || sample < 0.0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(replicates),
                        "Presentation replicate samples must be finite and non-negative.");
                }

                minimum = Math.Min(minimum, sample);
            }

            reduced.RecordFrameMilliseconds(minimum);
        }

        return reduced.Summarize();
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

internal sealed record PresentationFrameBurst(
    IReadOnlyList<double> Samples,
    PresentationFrameSummary Summary);
