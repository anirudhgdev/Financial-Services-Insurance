namespace ClaimSettlement.EvalHarness;

internal static class LatencyMetrics
{
    public static IReadOnlyList<LatencyPercentiles> Compute(IEnumerable<PipelineLatency> results) =>
        results.SelectMany(result => result.Stages)
            .GroupBy(stage => stage.StageName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key)
            .Select(group => new LatencyPercentiles(
                group.Key,
                Percentile(group.Select(stage => stage.Duration).ToList(), 0.50),
                Percentile(group.Select(stage => stage.Duration).ToList(), 0.95),
                Percentile(group.Select(stage => stage.Duration).ToList(), 0.99)))
            .ToList();

    private static TimeSpan Percentile(IReadOnlyList<TimeSpan> values, double percentile)
    {
        if (values.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var ordered = values.OrderBy(value => value).ToList();
        var index = (int)Math.Ceiling(percentile * ordered.Count) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Count - 1)];
    }
}

internal sealed record PipelineStageLatency(string StageName, TimeSpan Duration);

internal sealed record PipelineLatency(IReadOnlyList<PipelineStageLatency> Stages);

internal sealed record LatencyPercentiles(string StageName, TimeSpan P50, TimeSpan P95, TimeSpan P99);