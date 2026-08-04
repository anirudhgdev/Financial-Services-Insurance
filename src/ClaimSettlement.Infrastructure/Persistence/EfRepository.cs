using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace ClaimSettlement.Infrastructure.Persistence;

public sealed class EfRepository<T> : IRepository<T> where T : class, IProviderScoped
{
    private readonly ClaimSettlementDbContext _dbContext;

    public EfRepository(ClaimSettlementDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IQueryable<T> Query(string providerId) => _dbContext
        .Set<T>()
        .ForProvider(providerId);

    public Task<T?> GetAsync(string providerId, Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Query(providerId).FirstOrDefaultAsync(predicate, ct);

    public async Task<IReadOnlyList<T>> ListAsync(string providerId, Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default)
    {
        var query = Query(providerId);
        if (predicate is not null)
        {
            query = query.Where(predicate);
        }

        return await query.ToListAsync(ct);
    }

    public void Add(T entity) => _dbContext.Set<T>().Add(entity);

    public void Update(T entity) => _dbContext.Set<T>().Update(entity);

    public void Delete(T entity) => _dbContext.Set<T>().Remove(entity);
}
