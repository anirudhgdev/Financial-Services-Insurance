namespace ClaimSettlement.EvalHarness;

internal static class HumanReviewMetrics
{
    public static IReadOnlyList<HumanReviewRate> Compute(IEnumerable<HumanReviewEvaluationResult> results) =>
        results.GroupBy(result => result.ScenarioType, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var expectedRate = group.Average(result => result.ExpectedDecision == "MANUAL_REVIEW" ? 1d : 0d);
                var actualRate = group.Average(result => result.ActualDecision == "MANUAL_REVIEW" ? 1d : 0d);
                var difference = Math.Abs(actualRate - expectedRate);
                return new HumanReviewRate(group.Key, expectedRate, actualRate, difference > 0.10d);
            })
            .ToList();
}

internal sealed record HumanReviewEvaluationResult(string ScenarioType, string ExpectedDecision, string? ActualDecision);

internal sealed record HumanReviewRate(string ScenarioType, double ExpectedRate, double ActualRate, bool IsAnomalous);