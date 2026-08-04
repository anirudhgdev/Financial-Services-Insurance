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
        if (openAiOptions is not null && !string.IsNullOrEmpty(openAiOptions.Endpoint))
        {
            services.AddSingleton(_ => new AzureOpenAIClient(
                new Uri(openAiOptions.Endpoint),
                credential));
        }

        var searchOptions = configuration.GetSection(AzureSearchOptions.SectionName).Get<AzureSearchOptions>();
        if (searchOptions is not null && !string.IsNullOrEmpty(searchOptions.Endpoint))
        {
            services.AddSingleton(_ => new SearchIndexClient(
                new Uri(searchOptions.Endpoint),
                credential));

            if (!string.IsNullOrEmpty(searchOptions.IndexName))
            {
                services.AddSingleton(serviceProvider => new SearchClient(
                    new Uri(searchOptions.Endpoint),
                    searchOptions.IndexName,
                    credential));
            }
        }

        var storageOptions = configuration.GetSection(AzureStorageOptions.SectionName).Get<AzureStorageOptions>();
        if (storageOptions is not null && !string.IsNullOrEmpty(storageOptions.BlobServiceUri))
        {
            services.AddSingleton(_ => new BlobServiceClient(
                new Uri(storageOptions.BlobServiceUri),
                credential));
        }

        return services;
    }
}
