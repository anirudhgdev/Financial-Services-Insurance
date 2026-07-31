namespace ClaimSettlement.Domain.Entities;

public sealed class AdjusterAssignment
{
    public Guid AssignmentId { get; set; }

    public Guid ClaimId { get; set; }

    public string AdjusterId { get; set; } = string.Empty;

    public DateTime AssignedAt { get; set; }

    public DateTime? DecidedAt { get; set; }

    public string? Decision { get; set; }

    public string? Rationale { get; set; }

    public decimal? SettlementOverride { get; set; }

    public Claim? Claim { get; set; }
}
