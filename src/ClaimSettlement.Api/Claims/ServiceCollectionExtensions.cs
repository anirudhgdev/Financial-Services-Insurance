using ClaimSettlement.Infrastructure.Azure;
using Microsoft.Extensions.Options;

namespace ClaimSettlement.Api.Claims;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddClaimIntakeServices(this IServiceCollection services)
    {
        services.AddScoped<IClaimIntakeValidationService, ClaimIntakeValidationService>();
        services.AddScoped<IClaimDuplicateGuard, ClaimDuplicateGuard>();
        services.AddSingleton<IDocumentUploadPolicy, DocumentUploadPolicy>();
        services.AddScoped<IClaimIntakeService, ClaimIntakeService>();

        services.AddOptions<AzureStorageOptions>()
            .BindConfiguration(AzureStorageOptions.SectionName)
            .Validate(_ => true);

        return services;
    }
}