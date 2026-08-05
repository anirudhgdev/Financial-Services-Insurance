using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClaimSettlement.Api.Claims;

public sealed class ClaimDuplicateGuard : IClaimDuplicateGuard
{
    private readonly ClaimSettlementDbContext _dbContext;

    public ClaimDuplicateGuard(ClaimSettlementDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Guid?> FindDuplicateClaimAsync(
        string providerId,
        string claimantId,
        string policyNumber,
        DateTime dateOfLoss,
        CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        return await _dbContext.Claims
            .AsNoTracking()
            .Where(x => x.ProviderId == providerId)
            .Where(x => x.ClaimantId == claimantId)
            .Where(x => x.PolicyNumber == policyNumber)
            .Where(x => x.DateOfLoss.Date == dateOfLoss.Date)
            .Where(x => x.CreatedAt >= cutoff)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => (Guid?)x.ClaimId)
            .FirstOrDefaultAsync(ct);
    }
}