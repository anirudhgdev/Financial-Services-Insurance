using ClaimSettlement.Domain.Entities;

namespace ClaimSettlement.Agents.Models;

public sealed class ClaimIntakeInput
{
    public string SessionId { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> CollectedFields { get; init; } = new Dictionary<string, string>();
}

public sealed class ClaimIntakeResult
{
    public string Prompt { get; init; } = string.Empty;

    public IReadOnlyList<string> MissingFields { get; init; } = Array.Empty<string>();

    public bool ReadyForSubmission { get; init; }
}

public sealed record ClaimPipelineInput(Claim ClaimRecord);

public sealed record HumanReviewInput(string Reason);

public sealed class DocumentAnalysisResult
{
    public string Summary { get; init; } = string.Empty;

    public decimal Confidence { get; init; }

    public IReadOnlyList<string> MissingFields { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> BlockingMissingFields { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> NonBlockingMissingFields { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> DuplicateDocumentIds { get; init; } = Array.Empty<string>();

    public IReadOnlyList<DocumentExtractionResult> Documents { get; init; } = Array.Empty<DocumentExtractionResult>();

    public bool NotificationRequired { get; init; }

    public string? NotificationEventType { get; init; }
}

public sealed class DocumentExtractionResult
{
    public string DocumentId { get; init; } = string.Empty;

    public string Status { get; init; } = "EXTRACTED";

    public string RawExtractedText { get; init; } = string.Empty;

    public decimal Confidence { get; init; }

    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
}

public sealed class PolicyValidationResult
{
    public string Verdict { get; init; } = "POLICY_VALID";

    public string PolicyVerdict { get; init; } = "POLICY_VALID";

    public string CoverageVerdict { get; init; } = "COVERAGE_VALID";

    public string EligibilityVerdict { get; init; } = "ELIGIBLE";

    public string? FailureCode { get; init; }

    public bool RequiresManualReview { get; init; }

    public bool IsPolicyFound { get; init; }

    public bool IsPolicyActiveOnLossDate { get; init; }

    public decimal CoverageLimit { get; init; }

    public decimal Deductible { get; init; }

    public decimal NetPayable { get; init; }

    public decimal ExcessAmount { get; init; }

    public string? ExclusionReference { get; init; }

    public string? WaitingPeriodEndDateIso { get; init; }
}

public sealed class FraudDetectionResult
{
    public string Verdict { get; init; } = "FRAUD_LOW";

    public decimal RiskScore { get; init; }

    public IReadOnlyList<string> Signals { get; init; } = Array.Empty<string>();

    public bool ServiceUnavailable { get; init; }

    public bool DuplicateDetected { get; init; }

    public Guid? DuplicateClaimId { get; init; }

    public IReadOnlyDictionary<string, decimal> SignalWeights { get; init; } = new Dictionary<string, decimal>();

    public string Explanation { get; init; } = string.Empty;
}

public sealed class SettlementDecisionResult
{
    public string Recommendation { get; init; } = "APPROVE";

    public decimal ConfidenceScore { get; init; }

    public decimal RecommendedSettlementAmount { get; init; }

    public string Reasoning { get; init; } = string.Empty;

    public string? RejectionReasonCode { get; init; }

    public IReadOnlyList<string> MissingInputs { get; init; } = Array.Empty<string>();

    public decimal AppliedCoverageLimit { get; init; }

    public decimal AppliedDeductible { get; init; }

    public bool IsImmutable { get; init; }

    public int DecisionVersion { get; init; }
}

public sealed class HumanReviewResult
{
    public string QueueStatus { get; init; } = "QUEUED";

    public DateTime QueuedAtUtc { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string? AssignedAdjusterId { get; init; }

    public DateTime? NextAssignmentRetryAtUtc { get; init; }

    public bool NotificationRequired { get; init; }

    public string? NotificationEventType { get; init; }

    public HumanReviewPackage? ReviewPackage { get; init; }
}

public sealed class HumanReviewPackage
{
    public string ClaimSummary { get; init; } = string.Empty;

    public string PolicyValidationSummary { get; init; } = string.Empty;

    public string FraudSummary { get; init; } = string.Empty;

    public string DocumentHighlights { get; init; } = string.Empty;

    public string SettlementReasoning { get; init; } = string.Empty;

    public decimal RecommendedSettlementAmount { get; init; }

    public IReadOnlyList<string> MissingSections { get; init; } = Array.Empty<string>();
}