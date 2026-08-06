using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Xunit;

namespace ClaimSettlement.Api.Tests;

public sealed class AuditAndHealthIntegrationTests
{
    [Fact]
    public async Task AuditLog_RejectsUpdateAndDeleteOperations()
    {
        await using var dbContext = BuildDbContext();

        var entry = new AuditLog
        {
            EntryId = Guid.NewGuid(),
            ProviderId = "provider-1",
            EventType = "TEST_EVENT",
            ActorId = "tester",
            ActorType = "System",
            Payload = "{}",
            Timestamp = DateTime.UtcNow
        };

        dbContext.AuditLogs.Add(entry);
        await dbContext.SaveChangesAsync();

        entry.Payload = "{\"updated\":true}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());

        dbContext.Entry(entry).State = EntityState.Unchanged;
        dbContext.AuditLogs.Remove(entry);
        await Assert.ThrowsAsync<InvalidOperationException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task HealthEndpoints_ReturnExpectedResponses()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        var ready = await client.GetAsync("/health/ready");
        Assert.True(ready.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable);

        var readyBody = await ready.Content.ReadAsStringAsync();
        Assert.Contains("status", readyBody, StringComparison.OrdinalIgnoreCase);
    }

    private static ClaimSettlementDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<ClaimSettlementDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ClaimSettlementDbContext(options);
    }
}
