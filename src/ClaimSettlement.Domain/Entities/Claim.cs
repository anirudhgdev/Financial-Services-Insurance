namespace ClaimSettlement.Domain.Entities;

public sealed class Claim
{
    public Guid ClaimId { get; set; }

    public string ProviderId { get; set; } = string.Empty;

    public string PolicyNumber { get; set; } = string.Empty;

    public string ClaimantId { get; set; } = string.Empty;

    public DateTime DateOfLoss { get; set; }

    public string ClaimType { get; set; } = string.Empty;

    public decimal LossAmount { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ClaimPipelineState? PipelineState { get; set; }

    public ICollection<AgentOutput> AgentOutputs { get; set; } = new List<AgentOutput>();

    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();

    public ICollection<AdjusterAssignment> AdjusterAssignments { get; set; } = new List<AdjusterAssignment>();
}
