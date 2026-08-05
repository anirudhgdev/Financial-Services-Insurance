using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure.Azure;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimSettlement.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers persistence, Azure service clients, and data-access services.
    /// </summary>
    public static IServiceCollection AddClaimSettlementInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ClaimSettlementDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("ClaimSettlementDb")));

        services.AddMemoryCache();

        services.AddClaimSettlementAzureClients(configuration);

        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IProviderConfigurationService, ProviderConfigurationService>();

        return services;
    }
}
