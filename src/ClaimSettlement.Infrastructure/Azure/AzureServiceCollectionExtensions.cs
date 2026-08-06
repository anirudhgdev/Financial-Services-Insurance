using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClaimSettlement.Infrastructure.Azure;

public static class AzureServiceCollectionExtensions
{
    /// <summary>
    /// Registers Azure service clients that authenticate using the managed identity
    /// of the hosting compute (DefaultAzureCredential).
    /// </summary>
    public static IServiceCollection AddClaimSettlementAzureClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var credential = new DefaultAzureCredential();

        var openAiOptions = configuration.GetSection(AzureOpenAIOptions.SectionName).Get<AzureOpenAIOptions>();
        if (openAiOptions is not null &&
            TryBuildServiceUri(openAiOptions.Endpoint, out var openAiEndpointUri))
        {
            services.AddSingleton(_ => new AzureOpenAIClient(
                openAiEndpointUri,
                credential));
        }

        var searchOptions = configuration.GetSection(AzureSearchOptions.SectionName).Get<AzureSearchOptions>();
        if (searchOptions is not null &&
            TryBuildServiceUri(searchOptions.Endpoint, out var searchEndpointUri))
        {
            services.AddSingleton(_ => new SearchIndexClient(
                searchEndpointUri,
                credential));

            if (!string.IsNullOrEmpty(searchOptions.IndexName))
            {
                services.AddSingleton(serviceProvider => new SearchClient(
                    searchEndpointUri,
                    searchOptions.IndexName,
                    credential));
            }
        }

        var storageOptions = configuration.GetSection(AzureStorageOptions.SectionName).Get<AzureStorageOptions>();
        if (storageOptions is not null &&
            TryBuildServiceUri(storageOptions.BlobServiceUri, out var blobServiceUri))
        {
            services.AddSingleton(_ => new BlobServiceClient(
                blobServiceUri,
                credential));
        }

        return services;
    }

    private static bool TryBuildServiceUri(string? rawEndpoint, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(rawEndpoint) || rawEndpoint.Contains('<'))
        {
            return false;
        }

        if (!Uri.TryCreate(rawEndpoint, UriKind.Absolute, out var parsedUri))
        {
            return false;
        }

        uri = parsedUri;
        return true;
    }
}
