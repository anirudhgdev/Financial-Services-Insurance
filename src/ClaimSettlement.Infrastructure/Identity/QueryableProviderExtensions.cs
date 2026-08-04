using ClaimSettlement.Domain.Identity;

namespace ClaimSettlement.Infrastructure.Identity;

/// <summary>
/// Queryable extensions for enforcing provider-scoped data access.
/// </summary>
public static class QueryableProviderExtensions
{
    /// <summary>
    /// Filters a queryable of <see cref="IProviderScoped"/> entities to the specified provider.
    /// </summary>
    public static IQueryable<T> ForProvider<T>(this IQueryable<T> source, string providerId)
        where T : class, IProviderScoped
    {
        ArgumentException.ThrowIfNullOrEmpty(providerId);
        return source.Where(x => x.ProviderId == providerId);
    }
}
