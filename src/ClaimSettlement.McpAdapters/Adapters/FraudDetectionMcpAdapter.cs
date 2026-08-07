using System.Net.Http.Json;
using ClaimSettlement.McpAdapters.Abstractions;
using Polly;
using Polly.CircuitBreaker;

namespace ClaimSettlement.McpAdapters.Adapters;

public sealed record FraudScoreRequest(
    string ClaimId,
    string PolicyNumber,
    string ClaimantId,
    string ClaimType,
    decimal LossAmount,
    DateTimeOffset DateOfLoss,
    string? Description
);

public sealed record FraudScoreResponse(
    decimal Score,
    string RiskLevel,
    IReadOnlyList<FraudSignalDto> Signals,
    bool ServiceAvailable
);

public sealed record FraudSignalDto(string SignalType, string Description, decimal Weight);

public sealed class FraudDetectionMcpAdapter : IMcpTool<FraudScoreRequest, FraudScoreResponse>
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FraudDetectionMcpAdapter> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public string ToolName => "FraudDetection";
    public string Description => "Adapter to fetch fraud score from the Fraud Detection API.";

    public FraudDetectionMcpAdapter(HttpClient httpClient, ILogger<FraudDetectionMcpAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(response => !response.IsSuccessStatusCode),
                FailureRatio = 0.5,
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            .Build();
    }

    public async Task<McpToolResult<FraudScoreResponse>> InvokeAsync(FraudScoreRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _pipeline.ExecuteAsync(async ct =>
                await _httpClient.PostAsJsonAsync("/fraud/score", request, ct), cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var score = await response.Content.ReadFromJsonAsync<FraudScoreResponse>(cancellationToken: cancellationToken);
                return new McpToolResult<FraudScoreResponse>(true, score, null, null);
            }

            return new McpToolResult<FraudScoreResponse>(false, null, $"API returned {response.StatusCode}", McpErrorCode.ServiceUnavailable);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogError(ex, "Circuit open for Fraud Detection API.");
            return new McpToolResult<FraudScoreResponse>(false, null, ex.Message, McpErrorCode.CircuitOpen);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout connecting to Fraud Detection API.");
            return new McpToolResult<FraudScoreResponse>(false, null, ex.Message, McpErrorCode.Timeout);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Service unavailable: Fraud Detection API.");
            return new McpToolResult<FraudScoreResponse>(false, null, ex.Message, McpErrorCode.ServiceUnavailable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unknown error connecting to Fraud Detection API.");
            return new McpToolResult<FraudScoreResponse>(false, null, ex.Message, McpErrorCode.Unknown);
        }
    }
}
