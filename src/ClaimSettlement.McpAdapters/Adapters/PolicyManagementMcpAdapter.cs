using System.Net.Http.Json;
using ClaimSettlement.McpAdapters.Abstractions;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace ClaimSettlement.McpAdapters.Adapters;

public sealed record PolicyLookupRequest(string PolicyNumber);

public sealed record PolicyLookupResponse(
    bool Found,
    PolicyDetailsDto? Policy,
    string? ErrorMessage
);

public sealed record PolicyDetailsDto(
    string PolicyNumber,
    string PolicyholderName,
    string PolicyholderId,
    DateTimeOffset EffectiveDate,
    DateTimeOffset ExpirationDate,
    string Status,
    string PolicyType,
    IReadOnlyList<CoverageDetailDto> Coverages,
    IReadOnlyList<string> Exclusions,
    decimal Deductible,
    decimal PremiumAmount,
    DateTimeOffset LastPremiumPaidDate,
    DateTimeOffset WaitingPeriodEnd
);

public sealed record CoverageDetailDto(
    string CoverageType,
    decimal CoverageLimit,
    decimal UsedAmount
);

public sealed class PolicyManagementMcpAdapter : IMcpTool<PolicyLookupRequest, PolicyLookupResponse>
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PolicyManagementMcpAdapter> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public string ToolName => "PolicyManagement";
    public string Description => "Adapter to fetch policy details from the Policy Management API.";

    public PolicyManagementMcpAdapter(HttpClient httpClient, ILogger<PolicyManagementMcpAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(response => !response.IsSuccessStatusCode),
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(response => !response.IsSuccessStatusCode),
                FailureRatio = 0.5,
                MinimumThroughput = 3,
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            .Build();
    }

    public async Task<McpToolResult<PolicyLookupResponse>> InvokeAsync(PolicyLookupRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PolicyNumber))
        {
            return new McpToolResult<PolicyLookupResponse>(false, null, "Policy number is required.", McpErrorCode.InvalidInput);
        }

        try
        {
            var response = await _pipeline.ExecuteAsync(async ct =>
                await _httpClient.GetAsync($"/policy/{request.PolicyNumber}", ct), cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var policy = await response.Content.ReadFromJsonAsync<PolicyDetailsDto>(cancellationToken: cancellationToken);
                return new McpToolResult<PolicyLookupResponse>(true, new PolicyLookupResponse(true, policy, null), null, null);
            }
            
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new McpToolResult<PolicyLookupResponse>(true, new PolicyLookupResponse(false, null, "Policy not found"), null, null);
            }

            return new McpToolResult<PolicyLookupResponse>(false, null, $"API returned {response.StatusCode}", McpErrorCode.ServiceUnavailable);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Circuit open for Policy Management API.");
            return new McpToolResult<PolicyLookupResponse>(false, null, ex.Message, McpErrorCode.CircuitOpen);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout connecting to Policy Management API.");
            return new McpToolResult<PolicyLookupResponse>(false, null, ex.Message, McpErrorCode.Timeout);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Service unavailable: Policy Management API.");
            return new McpToolResult<PolicyLookupResponse>(false, null, ex.Message, McpErrorCode.ServiceUnavailable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unknown error connecting to Policy Management API.");
            return new McpToolResult<PolicyLookupResponse>(false, null, ex.Message, McpErrorCode.Unknown);
        }
    }
}
