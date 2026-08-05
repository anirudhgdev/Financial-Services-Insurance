using ClaimSettlement.Domain.Entities;

namespace ClaimSettlement.Agents.Models;

public sealed record ClaimPipelineInput(Claim ClaimRecord);

public sealed record HumanReviewInput(string Reason);

public sealed class DocumentAnalysisResult
{
    public string Summary { get; init; } = string.Empty;

    public decimal Confidence { get; init; }

    public IReadOnlyList<string> MissingFields { get; init; } = Array.Empty<string>();
}

public sealed class PolicyValidationResult
{
    public string Verdict { get; init; } = "POLICY_VALID";

    public decimal CoverageLimit { get; init; }

    public decimal Deductible { get; init; }

    public decimal NetPayable { get; init; }
}

public sealed class FraudDetectionResult
{
    public string Verdict { get; init; } = "FRAUD_LOW";

    public decimal RiskScore { get; init; }

    public IReadOnlyList<string> Signals { get; init; } = Array.Empty<string>();
}

public sealed class SettlementDecisionResult
{
    public string Recommendation { get; init; } = "APPROVE";

    public decimal ConfidenceScore { get; init; }

    public decimal RecommendedSettlementAmount { get; init; }

    public string Reasoning { get; init; } = string.Empty;
}

public sealed class HumanReviewResult
{
    public string QueueStatus { get; init; } = "QUEUED";

    public DateTime QueuedAtUtc { get; init; }

    public string Reason { get; init; } = string.Empty;
}