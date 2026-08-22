namespace VibeSnake.Rules.Tests;

/// <summary>
/// Runs independent work items on a bounded worker set and returns results in
/// input order. One SnakeRun stays single-threaded; separate runs may share cores.
/// </summary>
internal static class IndependentWork
{
    public static int WorkerCount => Math.Max(1, Environment.ProcessorCount);

    public static TResult[] Map<TItem, TResult>(
        IReadOnlyList<TItem> items,
        Func<TItem, TResult> map)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(map);
        if (items.Count == 0)
        {
            return [];
        }

        var results = new TResult[items.Count];
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = WorkerCount,
        };
        Parallel.For(0, items.Count, options, index =>
        {
            results[index] = map(items[index]);
        });
        return results;
    }
}
