using ClaimSettlement.Domain.Identity;

namespace ClaimSettlement.Domain.Entities;

public sealed class AuditLog : IProviderScoped
{
    public Guid EntryId { get; set; }

    public Guid? ClaimId { get; set; }

    public string ProviderId { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string ActorId { get; set; } = string.Empty;

    public string ActorType { get; set; } = string.Empty;

    public string Payload { get; set; } = "{}";

    public DateTime Timestamp { get; set; }

    public Claim? Claim { get; set; }
}
