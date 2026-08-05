using ClaimSettlement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ClaimSettlement.Infrastructure.Persistence;

public interface IProviderConfigurationService
{
    Task<ProviderConfiguration> GetConfigurationAsync(string providerId, CancellationToken ct);

    Task<ProviderConfiguration> UpsertConfigurationAsync(ProviderConfiguration configuration, CancellationToken ct);
}

public sealed class ProviderConfigurationService : IProviderConfigurationService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ClaimSettlementDbContext _dbContext;
    private readonly IMemoryCache _memoryCache;

    public ProviderConfigurationService(ClaimSettlementDbContext dbContext, IMemoryCache memoryCache)
    {
        _dbContext = dbContext;
        _memoryCache = memoryCache;
    }

    public async Task<ProviderConfiguration> GetConfigurationAsync(string providerId, CancellationToken ct)
    {
        var cacheKey = BuildCacheKey(providerId);
        if (_memoryCache.TryGetValue(cacheKey, out ProviderConfiguration? cached) && cached is not null)
        {
            return cached;
        }

        var configuration = await _dbContext.ProviderConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProviderId == providerId && x.IsActive, ct)
            ?? BuildDefault(providerId);

        _memoryCache.Set(cacheKey, configuration, CacheTtl);
        return configuration;
    }

    public async Task<ProviderConfiguration> UpsertConfigurationAsync(ProviderConfiguration configuration, CancellationToken ct)
    {
        var existing = await _dbContext.ProviderConfigurations
            .FirstOrDefaultAsync(x => x.ProviderId == configuration.ProviderId, ct);

        if (existing is null)
        {
            _dbContext.ProviderConfigurations.Add(configuration);
        }
        else
        {
            existing.ProviderName = configuration.ProviderName;
            existing.ManualReviewFraudThreshold = configuration.ManualReviewFraudThreshold;
            existing.ManualReviewClaimAmountThreshold = configuration.ManualReviewClaimAmountThreshold;
            existing.DeduplicationWindowDays = configuration.DeduplicationWindowDays;
            existing.InformationRequestDeadlineDays = configuration.InformationRequestDeadlineDays;
            existing.AdjusterSlaPeriodHours = configuration.AdjusterSlaPeriodHours;
            existing.SupportedClaimTypes = configuration.SupportedClaimTypes;
            existing.SupportedNotificationChannels = configuration.SupportedNotificationChannels;
            existing.PipelineConcurrencyLimit = configuration.PipelineConcurrencyLimit;
            existing.IsActive = configuration.IsActive;
            existing.ClaimTypeMandatoryFields = configuration.ClaimTypeMandatoryFields;
            existing.CoverageMappingRules = configuration.CoverageMappingRules;
            existing.ExclusionSets = configuration.ExclusionSets;
            existing.AlwaysManualClaimTypes = configuration.AlwaysManualClaimTypes;
        }

        await _dbContext.SaveChangesAsync(ct);

        var updated = await _dbContext.ProviderConfigurations
            .AsNoTracking()
            .FirstAsync(x => x.ProviderId == configuration.ProviderId, ct);

        _memoryCache.Set(BuildCacheKey(configuration.ProviderId), updated, CacheTtl);
        return updated;
    }

    private static string BuildCacheKey(string providerId) => $"provider-config:{providerId}";

    private static ProviderConfiguration BuildDefault(string providerId)
    {
        return new ProviderConfiguration
        {
            ProviderId = providerId,
            ProviderName = "Default",
            ManualReviewFraudThreshold = 0.70m,
            ManualReviewClaimAmountThreshold = decimal.MaxValue,
            DeduplicationWindowDays = 90,
            InformationRequestDeadlineDays = 7,
            AdjusterSlaPeriodHours = 48,
            SupportedClaimTypes = "[]",
            SupportedNotificationChannels = "[]",
            PipelineConcurrencyLimit = 100,
            IsActive = true,
            ClaimTypeMandatoryFields = "{}",
            CoverageMappingRules = "{}",
            ExclusionSets = "{}",
            AlwaysManualClaimTypes = "[]"
        };
    }
}
