namespace ClaimSettlement.Api.Claims;

public sealed class AdjusterQueueResponse
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public IReadOnlyList<AdjusterQueueClaim> Claims { get; init; } = Array.Empty<AdjusterQueueClaim>();
}

public sealed class AdjusterQueueClaim
{
    public Guid ClaimId { get; init; }
    public string ClaimantId { get; init; } = string.Empty;
    public string ClaimType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Priority { get; init; } = string.Empty;
    public string? AssignedAdjusterId { get; init; }
    public DateTime AssignedAtUtc { get; init; }
}

public sealed class AdjusterReviewPackageResponse
{
    public Guid ClaimId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string ClaimSummary { get; init; } = string.Empty;
    public string PolicyValidationSummary { get; init; } = string.Empty;
    public string FraudSummary { get; init; } = string.Empty;
    public string DocumentHighlights { get; init; } = string.Empty;
    public string SettlementReasoning { get; init; } = string.Empty;
    public decimal RecommendedSettlementAmount { get; init; }
    public IReadOnlyList<string> MissingSections { get; init; } = Array.Empty<string>();
}