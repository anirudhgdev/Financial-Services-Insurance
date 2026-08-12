namespace ClaimSettlement.EvalHarness;

internal static class FraudMetrics
{
    public static FraudMetricsReport Compute(IEnumerable<FraudEvaluationResult> results)
    {
        var cases = results.ToList();
        var knownFraud = cases.Where(result => result.ExpectedFraudVerdict == "FRAUD_HIGH").ToList();
        var genuineCases = cases.Where(result => result.ExpectedFraudVerdict == "FRAUD_LOW" && result.ActualFraudVerdict is not null).ToList();
        var scoredCases = cases.Where(result => result.RiskScore.HasValue).ToList();

        return new FraudMetricsReport(
            DetectionRate: Divide(knownFraud.Count(result => result.ActualFraudVerdict == "FRAUD_HIGH"), knownFraud.Count),
            FalsePositiveRate: Divide(genuineCases.Count(result => result.ActualFraudVerdict != "FRAUD_LOW"), genuineCases.Count),
            AucRoc: ComputeAucRoc(scoredCases),
            MeanScoreByScenarioType: scoredCases
                .GroupBy(result => result.ScenarioType, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key)
                .Select(group => new ScenarioFraudScore(group.Key, group.Average(result => result.RiskScore!.Value)))
                .ToList());
    }

    private static double? ComputeAucRoc(IReadOnlyList<FraudEvaluationResult> cases)
    {
        var positiveCount = cases.Count(result => result.ExpectedFraudVerdict == "FRAUD_HIGH");
        var negativeCount = cases.Count - positiveCount;
        if (positiveCount == 0 || negativeCount == 0)
        {
            return null;
        }

        var ranked = cases.OrderBy(result => result.RiskScore).ToList();
        var positiveRankSum = 0d;
        for (var index = 0; index < ranked.Count;)
        {
            var tieEnd = index;
            while (tieEnd + 1 < ranked.Count && ranked[tieEnd + 1].RiskScore == ranked[index].RiskScore)
            {
                tieEnd++;
            }

            var averageRank = (index + 1 + tieEnd + 1) / 2d;
            for (var tiedIndex = index; tiedIndex <= tieEnd; tiedIndex++)
            {
                if (ranked[tiedIndex].ExpectedFraudVerdict == "FRAUD_HIGH")
                {
                    positiveRankSum += averageRank;
                }
            }

            index = tieEnd + 1;
        }

        return (positiveRankSum - (positiveCount * (positiveCount + 1) / 2d)) / (positiveCount * negativeCount);
    }

    private static double Divide(double numerator, double denominator) => denominator == 0 ? 0 : numerator / denominator;
}

internal sealed record FraudEvaluationResult(string ScenarioType, string ExpectedFraudVerdict, string? ActualFraudVerdict, decimal? RiskScore);

internal sealed record ScenarioFraudScore(string ScenarioType, decimal MeanRiskScore);

internal sealed record FraudMetricsReport(
    double DetectionRate,
    double FalsePositiveRate,
    double? AucRoc,
    IReadOnlyList<ScenarioFraudScore> MeanScoreByScenarioType);