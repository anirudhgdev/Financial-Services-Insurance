namespace ClaimSettlement.Domain.Entities;

public sealed class ToolInvocationAudit
{
    public Guid InvocationId { get; set; }

    public Guid ClaimId { get; set; }

    public string ProviderId { get; set; } = string.Empty;

    public string AgentId { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public DateTime InvokedAtUtc { get; set; }

    public string Outcome { get; set; } = string.Empty;

    public Claim? Claim { get; set; }
}