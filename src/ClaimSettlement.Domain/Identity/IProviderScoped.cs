namespace ClaimSettlement.Domain.Identity;

/// <summary>
/// Marker for entities that are partitioned by provider and must be filtered
/// by the caller's <see cref="IProviderContextAccessor.ProviderId"/>.
/// </summary>
public interface IProviderScoped
{
    string ProviderId { get; }
}
