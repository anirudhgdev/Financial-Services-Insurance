namespace ClaimSettlement.EvalHarness;

internal static class ToolInvocationValidation
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ExpectedTools =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["DocumentAnalysisAgent"] = new HashSet<string>(["DocumentIntelligence"], StringComparer.OrdinalIgnoreCase),
            ["PolicyValidationAgent"] = new HashSet<string>(["PolicyManagement"], StringComparer.OrdinalIgnoreCase),
            ["FraudDetectionAgent"] = new HashSet<string>(["FraudDetection"], StringComparer.OrdinalIgnoreCase)
        };

    public static IReadOnlyList<ToolInvocationDiscrepancy> Validate(IEnumerable<EvaluationToolInvocation> actualInvocations)
    {
        var actualByAgent = actualInvocations
            .Where(invocation => string.Equals(invocation.Outcome, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            .GroupBy(invocation => invocation.AgentId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(invocation => invocation.ToolName).ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
        var discrepancies = new List<ToolInvocationDiscrepancy>();

        foreach (var expected in ExpectedTools)
        {
            actualByAgent.TryGetValue(expected.Key, out var actualTools);
            actualTools ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var missingTool in expected.Value.Except(actualTools, StringComparer.OrdinalIgnoreCase))
            {
                discrepancies.Add(new ToolInvocationDiscrepancy("TOOL_MISSING", expected.Key, missingTool));
            }
        }

        foreach (var actual in actualByAgent)
        {
            var expectedTools = ExpectedTools.GetValueOrDefault(actual.Key) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var unexpectedTool in actual.Value.Except(expectedTools, StringComparer.OrdinalIgnoreCase))
            {
                discrepancies.Add(new ToolInvocationDiscrepancy("TOOL_UNEXPECTED", actual.Key, unexpectedTool));
            }
        }

        return discrepancies;
    }
}

internal sealed record EvaluationToolInvocation(string AgentId, string ToolName, string Outcome);

internal sealed record ToolInvocationDiscrepancy(string Kind, string AgentId, string ToolName);