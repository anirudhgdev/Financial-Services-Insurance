using ClaimSettlement.Domain.Identity;
using System.Linq.Expressions;

namespace ClaimSettlement.Infrastructure.Persistence;

/// <summary>
/// Generic read/write repository that enforces provider-scoped queries.
/// </summary>
public interface IRepository<T> where T : class, IProviderScoped
{
    IQueryable<T> Query(string providerId);

    Task<T?> GetAsync(string providerId, Expression<Func<T, bool>> predicate, CancellationToken ct = default);

    Task<IReadOnlyList<T>> ListAsync(string providerId, Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);

    void Add(T entity);

    void Update(T entity);

    void Delete(T entity);
}
