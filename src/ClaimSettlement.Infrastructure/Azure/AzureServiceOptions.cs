namespace ClaimSettlement.Infrastructure.Azure;

public sealed class AzureOpenAIOptions
{
    public const string SectionName = "Azure:OpenAI";

    public string Endpoint { get; set; } = string.Empty;

    public string DeploymentName { get; set; } = string.Empty;
}

public sealed class AzureAiFoundryOptions
{
    public const string SectionName = "AzureAiFoundry";

    public string Endpoint { get; set; } = string.Empty;

    public string DeploymentName { get; set; } = string.Empty;

    public string Authentication { get; set; } = "ManagedIdentity";
}

public sealed class AzureSearchOptions
{
    public const string SectionName = "Azure:Search";

    public string Endpoint { get; set; } = string.Empty;

    public string IndexName { get; set; } = string.Empty;
}

public sealed class AzureStorageOptions
{
    public const string SectionName = "Azure:Storage";

    public string BlobServiceUri { get; set; } = string.Empty;

    public string DocumentsContainerName { get; set; } = "documents";
}
