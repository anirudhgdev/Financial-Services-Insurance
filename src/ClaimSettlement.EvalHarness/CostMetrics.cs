namespace ClaimSettlement.EvalHarness;

internal static class CostMetrics
{
    public static CostMetricsResult Compute(
        IEnumerable<EvaluationTokenUsage> usages,
        AzureOpenAiTokenPricing pricing,
        int completedCaseCount)
    {
        var perAgent = usages
            .GroupBy(usage => usage.AgentId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var inputTokens = group.Sum(usage => usage.InputTokenCount);
                var outputTokens = group.Sum(usage => usage.OutputTokenCount);
                var cost = inputTokens / 1_000_000m * pricing.InputUsdPerMillionTokens +
                    outputTokens / 1_000_000m * pricing.OutputUsdPerMillionTokens;
                return new AgentCost(group.Key, inputTokens, outputTokens, cost);
            })
            .ToList();
        var totalCost = perAgent.Sum(cost => cost.CostUsd);
        return new CostMetricsResult(perAgent, totalCost, completedCaseCount == 0 ? null : totalCost / completedCaseCount);
    }
}

internal sealed record AzureOpenAiTokenPricing(decimal InputUsdPerMillionTokens, decimal OutputUsdPerMillionTokens);

internal sealed record EvaluationTokenUsage(string AgentId, long InputTokenCount, long OutputTokenCount);

internal sealed record AgentCost(string AgentId, long InputTokenCount, long OutputTokenCount, decimal CostUsd);

internal sealed record CostMetricsResult(IReadOnlyList<AgentCost> PerAgent, decimal TotalCostUsd, decimal? CostPerCompletedClaimUsd);