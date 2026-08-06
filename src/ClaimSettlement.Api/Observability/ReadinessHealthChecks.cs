using Azure.Storage.Blobs;
using ClaimSettlement.Infrastructure.Azure;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ClaimSettlement.Api.Observability;

public sealed class NotificationServiceOptions
{
    public const string SectionName = "ExternalServices:NotificationService";

    public string BaseUrl { get; set; } = string.Empty;
}

public sealed class SqlReadinessHealthCheck : IHealthCheck
{
    private readonly ClaimSettlementDbContext _dbContext;

    public SqlReadinessHealthCheck(ClaimSettlementDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var connectionString = _dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString.Contains('<'))
        {
            return HealthCheckResult.Healthy("SQL readiness check skipped due to placeholder or missing connection string.");
        }

        var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
        return canConnect
            ? HealthCheckResult.Healthy("SQL reachable.")
            : HealthCheckResult.Unhealthy("SQL is not reachable.");
    }
}

public sealed class BlobStorageReadinessHealthCheck : IHealthCheck
{
    private readonly IServiceProvider _serviceProvider;

    public BlobStorageReadinessHealthCheck(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var blobServiceClient = _serviceProvider.GetService<BlobServiceClient>();
        if (blobServiceClient is null)
        {
            return HealthCheckResult.Healthy("Blob storage readiness check skipped because BlobServiceClient is not configured.");
        }

        var host = blobServiceClient.Uri.Host;
        if (host.Contains('<'))
        {
            return HealthCheckResult.Healthy("Blob storage readiness check skipped due to placeholder configuration.");
        }

        await blobServiceClient.GetPropertiesAsync(cancellationToken: cancellationToken);
        return HealthCheckResult.Healthy("Blob storage reachable.");
    }
}

public sealed class OpenAiReadinessHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<AzureOpenAIOptions> _options;

    public OpenAiReadinessHealthCheck(IHttpClientFactory httpClientFactory, IOptions<AzureOpenAIOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Value.Endpoint) || _options.Value.Endpoint.Contains('<'))
        {
            return HealthCheckResult.Healthy("Azure OpenAI readiness check skipped due to placeholder configuration.");
        }

        var client = _httpClientFactory.CreateClient(nameof(OpenAiReadinessHealthCheck));
        client.Timeout = TimeSpan.FromSeconds(5);

        using var request = new HttpRequestMessage(HttpMethod.Get, _options.Value.Endpoint);
        using var response = await client.SendAsync(request, cancellationToken);

        return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Unauthorized
            ? HealthCheckResult.Healthy("Azure OpenAI endpoint reachable.")
            : HealthCheckResult.Unhealthy($"Azure OpenAI endpoint returned {(int)response.StatusCode}.");
    }
}

public sealed class NotificationServiceReadinessHealthCheck : IHealthCheck
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<NotificationServiceOptions> _options;

    public NotificationServiceReadinessHealthCheck(IHttpClientFactory httpClientFactory, IOptions<NotificationServiceOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Value.BaseUrl) || _options.Value.BaseUrl.Contains('<'))
        {
            return HealthCheckResult.Healthy("Notification service readiness check skipped due to placeholder configuration.");
        }

        var client = _httpClientFactory.CreateClient(nameof(NotificationServiceReadinessHealthCheck));
        client.Timeout = TimeSpan.FromSeconds(5);

        using var response = await client.GetAsync(_options.Value.BaseUrl, cancellationToken);
        return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Unauthorized
            ? HealthCheckResult.Healthy("Notification service reachable.")
            : HealthCheckResult.Unhealthy($"Notification service returned {(int)response.StatusCode}.");
    }
}
