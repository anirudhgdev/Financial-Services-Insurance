using ClaimSettlement.Api.Claims;
using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClaimSettlement.Api.Tests;

public sealed class ClaimDuplicateGuardTests
{
    [Fact]
    public async Task FindsDuplicateWithin24Hours()
    {
        var options = new DbContextOptionsBuilder<ClaimSettlementDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new ClaimSettlementDbContext(options);
        var recentClaimId = Guid.NewGuid();

        dbContext.Claims.Add(new Claim
        {
            ClaimId = recentClaimId,
            ProviderId = "provider-1",
            ClaimantId = "user-1",
            PolicyNumber = "POL-007",
            DateOfLoss = new DateTime(2026, 8, 1),
            ClaimType = "auto",
            LossAmount = 1000,
            Status = "INTAKE_COMPLETE",
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var guard = new ClaimDuplicateGuard(dbContext);
        var duplicate = await guard.FindDuplicateClaimAsync("provider-1", "user-1", "POL-007", new DateTime(2026, 8, 1), CancellationToken.None);

        Assert.Equal(recentClaimId, duplicate);
    }

    [Fact]
    public async Task IgnoresOlderThan24Hours()
    {
        var options = new DbContextOptionsBuilder<ClaimSettlementDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new ClaimSettlementDbContext(options);

        dbContext.Claims.Add(new Claim
        {
            ClaimId = Guid.NewGuid(),
            ProviderId = "provider-1",
            ClaimantId = "user-1",
            PolicyNumber = "POL-007",
            DateOfLoss = new DateTime(2026, 8, 1),
            ClaimType = "auto",
            LossAmount = 1000,
            Status = "INTAKE_COMPLETE",
            CreatedAt = DateTime.UtcNow.AddHours(-30),
            UpdatedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var guard = new ClaimDuplicateGuard(dbContext);
        var duplicate = await guard.FindDuplicateClaimAsync("provider-1", "user-1", "POL-007", new DateTime(2026, 8, 1), CancellationToken.None);

        Assert.Null(duplicate);
    }
}
