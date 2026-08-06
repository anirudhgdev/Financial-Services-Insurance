using ClaimSettlement.Api.Providers;
using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure.Observability;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace ClaimSettlement.Api.Tests;

public sealed class ProviderConfigurationControllerTests
{
    [Fact]
    public async Task RejectsFraudThresholdOutsideAllowedRange()
    {
        await using var dbContext = BuildDbContext();
        using var memoryCache = BuildMemoryCache();
        var service = new ProviderConfigurationService(dbContext, memoryCache);

        var controller = new ProvidersController(
            service,
            new TestProviderContextAccessor("provider-1"),
            dbContext,
            new NoOpAuditLogger());

        var response = await controller.UpdateConfiguration(
            "provider-1",
            new UpdateProviderConfigurationRequest
            {
                ProviderName = "Provider One",
                ManualReviewFraudThreshold = 0.95m,
                ManualReviewClaimAmountThreshold = 6000m,
                DeduplicationWindowDays = 30,
                InformationRequestDeadlineDays = 5,
                AdjusterSlaPeriodHours = 48,
                SupportedClaimTypes = "[\"auto\"]",
                SupportedNotificationChannels = "[\"email\"]",
                PipelineConcurrencyLimit = 50,
                ClaimTypeMandatoryFields = "{}",
                CoverageMappingRules = "{}",
                ExclusionSets = "{}",
                AlwaysManualClaimTypes = "[]",
                IsActive = true
            },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response.Result);
    }

    [Fact]
    public async Task SavesAndReadsProviderConfiguration()
    {
        await using var dbContext = BuildDbContext();
        using var memoryCache = BuildMemoryCache();
        var service = new ProviderConfigurationService(dbContext, memoryCache);

        var controller = new ProvidersController(
            service,
            new TestProviderContextAccessor("provider-1"),
            dbContext,
            new NoOpAuditLogger());

        var update = await controller.UpdateConfiguration(
            "provider-1",
            new UpdateProviderConfigurationRequest
            {
                ProviderName = "Provider One",
                ManualReviewFraudThreshold = 0.55m,
                ManualReviewClaimAmountThreshold = 5000m,
                DeduplicationWindowDays = 45,
                InformationRequestDeadlineDays = 3,
                AdjusterSlaPeriodHours = 36,
                SupportedClaimTypes = "[\"auto\",\"property\"]",
                SupportedNotificationChannels = "[\"email\",\"sms\"]",
                PipelineConcurrencyLimit = 25,
                ClaimTypeMandatoryFields = "{}",
                CoverageMappingRules = "{}",
                ExclusionSets = "{}",
                AlwaysManualClaimTypes = "[\"property\"]",
                IsActive = true
            },
            CancellationToken.None);

        var updateOk = Assert.IsType<OkObjectResult>(update.Result);
        var updated = Assert.IsType<ProviderConfigurationResponse>(updateOk.Value);
        Assert.Equal(0.55m, updated.ManualReviewFraudThreshold);

        var get = await controller.GetConfiguration("provider-1", CancellationToken.None);
        var getOk = Assert.IsType<OkObjectResult>(get.Result);
        var read = Assert.IsType<ProviderConfigurationResponse>(getOk.Value);

        Assert.Equal("provider-1", read.ProviderId);
        Assert.Equal("Provider One", read.ProviderName);
        Assert.Equal("[\"property\"]", read.AlwaysManualClaimTypes);
        Assert.Equal(25, read.PipelineConcurrencyLimit);
    }

    private static ClaimSettlementDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<ClaimSettlementDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ClaimSettlementDbContext(options);
    }

    private static IMemoryCache BuildMemoryCache()
    {
        return new MemoryCache(new MemoryCacheOptions());
    }

    private sealed class TestProviderContextAccessor : IProviderContextAccessor
    {
        private readonly string _providerId;

        public TestProviderContextAccessor(string providerId)
        {
            _providerId = providerId;
        }

        public string ProviderId => _providerId;

        public IReadOnlyCollection<string> Roles => [AppRoles.ProviderAdmin];

        public string UserId => "admin-1";

        public string? Email => "admin@example.com";

        public System.Security.Claims.ClaimsIdentity Identity => new("test");

        public bool IsAuthenticated => true;
    }

    private sealed class NoOpAuditLogger : IAuditLogger
    {
        public Task AppendAsync(AuditLogEntry entry, CancellationToken ct) => Task.CompletedTask;

        public Task UpdateAsync(Guid entryId, object payload, CancellationToken ct)
            => throw new InvalidOperationException();

        public Task DeleteAsync(Guid entryId, CancellationToken ct)
            => throw new InvalidOperationException();
    }
}
