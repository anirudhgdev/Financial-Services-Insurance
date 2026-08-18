using ClaimSettlement.Agents.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimSettlement.Agents;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddClaimSettlementAgents(this IServiceCollection services, IConfiguration configuration)
    {
        var foundrySection = configuration.GetSection(AzureAiFoundryOptions.SectionName);
        services.Configure<AzureAiFoundryOptions>(options =>
        {
            options.Endpoint = foundrySection["Endpoint"] ?? string.Empty;
            options.DeploymentName = foundrySection["DeploymentName"] ?? string.Empty;
            options.Authentication = foundrySection["Authentication"] ?? "ManagedIdentity";
        });
        services.AddScoped<IClaimIntakeConversationService, AzureAiFoundryClaimIntakeConversationService>();
        services.AddScoped<IDocumentExtractionClient, SimulatedDocumentExtractionClient>();
        services.AddScoped<IDocumentDeduplicationService, DocumentDeduplicationService>();
        services.AddScoped<IGapClassificationService, GapClassificationService>();
        services.AddScoped<IClaimSummaryGenerator, TemplateClaimSummaryGenerator>();
        services.AddScoped<IPolicyManagementClient, SimulatedPolicyManagementClient>();
        services.AddScoped<IPolicyValidationEngine, PolicyValidationEngine>();
        services.AddScoped<IFraudScoringClient, SimulatedFraudScoringClient>();
        services.AddScoped<IClaimHistorySignalProvider, UpstreamHistorySignalProvider>();
        services.AddScoped<IFraudExplainabilityGenerator, TemplateFraudExplainabilityGenerator>();
        services.AddSingleton<IFraudCircuitBreaker, TimeWindowFraudCircuitBreaker>();
        services.AddScoped<IHumanReviewQueueStore, InMemoryHumanReviewQueueStore>();
        services.AddScoped<IReviewPackageAssembler, ReviewPackageAssembler>();
        services.AddSingleton<IHumanReviewSlaEvaluator, HumanReviewSlaEvaluator>();
        services.AddScoped<INotificationServiceClient, SimulatedNotificationServiceClient>();
        services.AddSingleton<INotificationIdGenerator, HashNotificationIdGenerator>();
        services.AddSingleton<INotificationDedupStore, InMemoryNotificationDedupStore>();
        services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
        services.AddSingleton<IDeadLetterNotificationSink, InMemoryDeadLetterNotificationSink>();
        services.AddSingleton<INotificationEventFactory, NotificationEventFactory>();

        services.AddScoped<ClaimIntakeAgent>();
        services.AddScoped<DocumentAnalysisAgent>();
        services.AddScoped<PolicyValidationAgent>();
        services.AddScoped<FraudDetectionAgent>();
        services.AddScoped<SettlementDecisionAgent>();
        services.AddScoped<HumanReviewAgent>();
        services.AddScoped<NotificationAgent>();

        return services;
    }
}