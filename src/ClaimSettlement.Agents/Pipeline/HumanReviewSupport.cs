using ClaimSettlement.Agents.Models;
using ClaimSettlement.Domain.Entities;
using System.Text.Json;

namespace ClaimSettlement.Agents.Pipeline;

public sealed class HumanReviewQueueEntry
{
    public Guid ClaimId { get; init; }

    public string ProviderId { get; init; } = string.Empty;

    public string? AssignedAdjusterId { get; init; }

    public DateTime AssignedAtUtc { get; init; }

    public bool PendingAssignment { get; init; }
}

public interface IHumanReviewQueueStore
{
    Task<HumanReviewQueueEntry> EnqueueAsync(ClaimAgentContext context, string reason, CancellationToken ct);
}

public sealed class InMemoryHumanReviewQueueStore : IHumanReviewQueueStore
{
    private static readonly string[] DefaultAdjusters = ["adjuster-alpha", "adjuster-bravo", "adjuster-charlie"];

    public Task<HumanReviewQueueEntry> EnqueueAsync(ClaimAgentContext context, string reason, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var index = Math.Abs(context.ClaimId.GetHashCode()) % DefaultAdjusters.Length;
        var assignedAdjusterId = DefaultAdjusters[index];

        return Task.FromResult(new HumanReviewQueueEntry
        {
            ClaimId = context.ClaimId,
            ProviderId = context.ProviderConfig.ProviderId,
            AssignedAdjusterId = assignedAdjusterId,
            AssignedAtUtc = DateTime.UtcNow,
            PendingAssignment = false
        });
    }
}

public interface IReviewPackageAssembler
{
    HumanReviewPackage Build(ClaimAgentContext context);
}

public sealed class ReviewPackageAssembler : IReviewPackageAssembler
{
    public HumanReviewPackage Build(ClaimAgentContext context)
    {
        var missingSections = new List<string>();

        var claimSummary = ExtractString(context, "DocumentAnalysisAgent", "Summary", "summary", missingSections);
        var policySummary = ExtractPolicySummary(context, missingSections);
        var fraudSummary = ExtractFraudSummary(context, missingSections);
        var documentHighlights = ExtractDocumentHighlights(context, missingSections);
        var settlementReasoning = ExtractString(context, "SettlementDecisionAgent", "Reasoning", "reasoning", missingSections);
        var recommendedAmount = ExtractDecimal(context, "SettlementDecisionAgent", "RecommendedSettlementAmount", "recommendedSettlementAmount");

        return new HumanReviewPackage
        {
            ClaimSummary = claimSummary,
            PolicyValidationSummary = policySummary,
            FraudSummary = fraudSummary,
            DocumentHighlights = documentHighlights,
            SettlementReasoning = settlementReasoning,
            RecommendedSettlementAmount = recommendedAmount,
            MissingSections = missingSections.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    private static string ExtractPolicySummary(ClaimAgentContext context, ICollection<string> missingSections)
    {
        if (!context.UpstreamOutputs.TryGetValue("PolicyValidationAgent", out var policyDoc))
        {
            missingSections.Add("PolicyValidationAgent");
            return "Policy validation output missing.";
        }

        var verdict = policyDoc.RootElement.TryGetProperty("Verdict", out var verdictElement)
            ? verdictElement.GetString() ?? "UNKNOWN"
            : "UNKNOWN";
        var netPayable = policyDoc.RootElement.TryGetProperty("NetPayable", out var payableElement) && payableElement.TryGetDecimal(out var payable)
            ? payable
            : 0m;

        return $"Policy verdict {verdict}; net payable estimate {netPayable:0.00}.";
    }

    private static string ExtractFraudSummary(ClaimAgentContext context, ICollection<string> missingSections)
    {
        if (!context.UpstreamOutputs.TryGetValue("FraudDetectionAgent", out var fraudDoc))
        {
            missingSections.Add("FraudDetectionAgent");
            return "Fraud output missing.";
        }

        var verdict = fraudDoc.RootElement.TryGetProperty("Verdict", out var verdictElement)
            ? verdictElement.GetString() ?? "UNKNOWN"
            : "UNKNOWN";
        var score = fraudDoc.RootElement.TryGetProperty("RiskScore", out var riskElement) && riskElement.TryGetDecimal(out var risk)
            ? risk
            : 0m;

        return $"Fraud verdict {verdict} with score {score:0.00}.";
    }

    private static string ExtractDocumentHighlights(ClaimAgentContext context, ICollection<string> missingSections)
    {
        if (!context.UpstreamOutputs.TryGetValue("DocumentAnalysisAgent", out var docAnalysis))
        {
            missingSections.Add("DocumentAnalysisAgent");
            return "Document analysis output missing.";
        }

        if (!docAnalysis.RootElement.TryGetProperty("Documents", out var documentsElement) ||
            documentsElement.ValueKind != JsonValueKind.Array)
        {
            return "No extracted document details available.";
        }

        var extractedCount = documentsElement.EnumerateArray().Count();
        return $"{extractedCount} document entries available for review.";
    }

    private static string ExtractString(ClaimAgentContext context, string outputName, string primaryProperty, string alternateProperty, ICollection<string> missingSections)
    {
        if (!context.UpstreamOutputs.TryGetValue(outputName, out var doc))
        {
            missingSections.Add(outputName);
            return $"{outputName} output missing.";
        }

        if (doc.RootElement.TryGetProperty(primaryProperty, out var primary) && primary.ValueKind == JsonValueKind.String)
        {
            return primary.GetString() ?? string.Empty;
        }

        if (doc.RootElement.TryGetProperty(alternateProperty, out var alternate) && alternate.ValueKind == JsonValueKind.String)
        {
            return alternate.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static decimal ExtractDecimal(ClaimAgentContext context, string outputName, string primaryProperty, string alternateProperty)
    {
        if (!context.UpstreamOutputs.TryGetValue(outputName, out var doc))
        {
            return 0m;
        }

        if (doc.RootElement.TryGetProperty(primaryProperty, out var primary) && primary.TryGetDecimal(out var value))
        {
            return value;
        }

        if (doc.RootElement.TryGetProperty(alternateProperty, out var alternate) && alternate.TryGetDecimal(out var alternateValue))
        {
            return alternateValue;
        }

        return 0m;
    }
}

public sealed class HumanReviewSlaAction
{
    public required Guid ClaimId { get; init; }

    public required string ProviderId { get; init; }

    public required string EventType { get; init; }

    public required string NewStatus { get; init; }
}

public interface IHumanReviewSlaEvaluator
{
    IReadOnlyList<HumanReviewSlaAction> Evaluate(IReadOnlyList<AdjusterAssignment> assignments, DateTime utcNow, int slaHours);
}

public sealed class HumanReviewSlaEvaluator : IHumanReviewSlaEvaluator
{
    public IReadOnlyList<HumanReviewSlaAction> Evaluate(IReadOnlyList<AdjusterAssignment> assignments, DateTime utcNow, int slaHours)
    {
        var threshold = utcNow.AddHours(-Math.Max(1, slaHours));
        var actions = new List<HumanReviewSlaAction>();

        foreach (var assignment in assignments)
        {
            if (assignment.DecidedAt.HasValue)
            {
                continue;
            }

            if (assignment.AssignedAt <= threshold)
            {
                actions.Add(new HumanReviewSlaAction
                {
                    ClaimId = assignment.ClaimId,
                    ProviderId = assignment.ProviderId,
                    EventType = "SLA_BREACHED",
                    NewStatus = "SLA_BREACHED"
                });
            }
        }

        return actions;
    }
}
