namespace ClaimSettlement.McpAdapters.Configuration;

public sealed class ExternalServiceSettings
{
    public ServiceEndpoint PolicyManagementApi { get; set; } = new();
    public ServiceEndpoint FraudDetectionService { get; set; } = new();
    public ServiceEndpoint NotificationService { get; set; } = new();
    public DocumentIntelligenceSettings DocumentIntelligence { get; set; } = new();
}

public sealed class ServiceEndpoint
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
}

public sealed class DocumentIntelligenceSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string DeploymentId { get; set; } = "prebuilt-document";
}
