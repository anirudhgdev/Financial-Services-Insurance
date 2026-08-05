using ClaimSettlement.Agents.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimSettlement.Agents;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddClaimSettlementAgents(this IServiceCollection services)
    {
        services.AddScoped<ClaimIntakeAgent>();
        services.AddScoped<DocumentAnalysisAgent>();
        services.AddScoped<PolicyValidationAgent>();
        services.AddScoped<FraudDetectionAgent>();
        services.AddScoped<SettlementDecisionAgent>();
        services.AddScoped<HumanReviewAgent>();

        return services;
    }
}