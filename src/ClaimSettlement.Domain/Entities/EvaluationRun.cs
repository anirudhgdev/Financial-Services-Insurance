using ClaimSettlement.Domain.Identity;

namespace ClaimSettlement.Domain.Entities;

public sealed class EvaluationRun : IProviderScoped
{
    public Guid RunId { get; set; }

    public string ProviderId { get; set; } = string.Empty;

    public string CreatedByUserId { get; set; } = string.Empty;

    public string DatasetVersion { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<Claim> Claims { get; set; } = new List<Claim>();
}