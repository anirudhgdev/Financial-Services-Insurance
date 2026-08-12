namespace ClaimSettlement.EvalHarness;

internal static class DecisionAccuracyMetrics
{
    private static readonly string[] DecisionClasses = ["APPROVE", "REJECT", "MANUAL_REVIEW"];

    public static DecisionAccuracyReport Compute(IEnumerable<DecisionEvaluationResult> results)
    {
        var cases = results.ToList();
        var perClass = DecisionClasses.Select(decisionClass =>
        {
            var truePositive = cases.Count(result => result.ExpectedDecision == decisionClass && result.ActualDecision == decisionClass);
            var falsePositive = cases.Count(result => result.ExpectedDecision != decisionClass && result.ActualDecision == decisionClass);
            var falseNegative = cases.Count(result => result.ExpectedDecision == decisionClass && result.ActualDecision != decisionClass);
            var precision = Divide(truePositive, truePositive + falsePositive);
            var recall = Divide(truePositive, truePositive + falseNegative);

            return new PerClassDecisionMetrics(
                decisionClass,
                precision,
                recall,
                Divide(2 * precision * recall, precision + recall));
        }).ToList();

        return new DecisionAccuracyReport(
            cases.Count,
            Divide(cases.Count(result => result.ExpectedDecision == result.ActualDecision), cases.Count),
            perClass);
    }

    private static double Divide(double numerator, double denominator) => denominator == 0 ? 0 : numerator / denominator;
}

internal sealed record DecisionEvaluationResult(string ExpectedDecision, string? ActualDecision);

internal sealed record PerClassDecisionMetrics(string DecisionClass, double Precision, double Recall, double F1);

internal sealed record DecisionAccuracyReport(int CaseCount, double OverallAccuracy, IReadOnlyList<PerClassDecisionMetrics> PerClass);