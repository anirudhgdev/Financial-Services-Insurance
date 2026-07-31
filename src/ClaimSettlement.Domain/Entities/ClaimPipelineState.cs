namespace ClaimSettlement.Domain.Entities;

public sealed class ClaimPipelineState
{
    public Guid ClaimId { get; set; }

    public string ProviderId { get; set; } = string.Empty;

    public string CurrentStep { get; set; } = string.Empty;

    public string CompletedSteps { get; set; } = "[]";

    public string AgentOutputs { get; set; } = "{}";

    public string Status { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public Claim? Claim { get; set; }
}
