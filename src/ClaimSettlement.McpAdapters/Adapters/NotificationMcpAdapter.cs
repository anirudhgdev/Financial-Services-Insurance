using System.Net.Http.Json;
using ClaimSettlement.McpAdapters.Abstractions;
using Polly;
using Polly.Retry;

namespace ClaimSettlement.McpAdapters.Adapters;

public sealed record NotifyRequest(
    string RecipientId,
    string Channel,
    string TemplateId,
    IReadOnlyDictionary<string, string> TemplateParameters,
    string? IdempotencyKey
);

public sealed record NotifyResponse(
    bool Success,
    string? MessageId,
    string? ErrorMessage
);

public sealed class NotificationMcpAdapter : IMcpTool<NotifyRequest, NotifyResponse>
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NotificationMcpAdapter> _logger;
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public string ToolName => "Notification";
    public string Description => "Adapter to send notifications via Notification Service.";

    public NotificationMcpAdapter(HttpClient httpClient, ILogger<NotificationMcpAdapter> logger)
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
            .Build();
    }

    public async Task<McpToolResult<NotifyResponse>> InvokeAsync(NotifyRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _pipeline.ExecuteAsync(async ct =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, "/notify")
                {
                    Content = JsonContent.Create(request)
                };
                if (!string.IsNullOrEmpty(request.IdempotencyKey))
                {
                    req.Headers.Add("X-Idempotency-Key", request.IdempotencyKey);
                }
                return await _httpClient.SendAsync(req, ct);
            }, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var notifyResponse = await response.Content.ReadFromJsonAsync<NotifyResponse>(cancellationToken: cancellationToken);
                return new McpToolResult<NotifyResponse>(true, notifyResponse, null, null);
            }

            return new McpToolResult<NotifyResponse>(false, null, $"API returned {response.StatusCode}", McpErrorCode.ServiceUnavailable);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout connecting to Notification API.");
            return new McpToolResult<NotifyResponse>(false, null, ex.Message, McpErrorCode.Timeout);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Service unavailable: Notification API.");
            return new McpToolResult<NotifyResponse>(false, null, ex.Message, McpErrorCode.ServiceUnavailable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unknown error connecting to Notification API.");
            return new McpToolResult<NotifyResponse>(false, null, ex.Message, McpErrorCode.Unknown);
        }
    }
}
