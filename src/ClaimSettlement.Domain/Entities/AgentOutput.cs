namespace ClaimSettlement.Domain.Entities;

public sealed class AgentOutput
{
    public Guid OutputId { get; set; }

    public Guid ClaimId { get; set; }

    public string AgentId { get; set; } = string.Empty;

    public string OutputPayload { get; set; } = "{}";

    public DateTime CreatedAt { get; set; }

    public string SchemaVersion { get; set; } = "1.0";

    public Claim? Claim { get; set; }
}
