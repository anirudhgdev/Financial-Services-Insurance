namespace ClaimSettlement.Api.Claims;

public sealed class AdjusterDecisionRequest
{
    public string Decision { get; init; } = string.Empty;

    public string Rationale { get; init; } = string.Empty;

    public decimal? SettlementOverride { get; init; }
}

public sealed class AdjusterDecisionResponse
{
    public Guid ClaimId { get; init; }

    public string Decision { get; init; } = string.Empty;

    public string Rationale { get; init; } = string.Empty;

    public decimal? SettlementOverride { get; init; }

    public DateTime DecidedAtUtc { get; init; }
}
