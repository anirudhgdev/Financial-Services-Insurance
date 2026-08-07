using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using ClaimSettlement.McpAdapters.Abstractions;
using ClaimSettlement.McpAdapters.Configuration;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace ClaimSettlement.McpAdapters.Adapters;

public sealed record DocumentExtractRequest(
    byte[] DocumentContent,
    string FileName
);

public sealed record DocumentExtractResponse(
    bool Success,
    IReadOnlyDictionary<string, ExtractedFieldDto> Fields,
    string RawText,
    decimal OverallConfidence,
    string? ErrorMessage
);

public sealed record ExtractedFieldDto(string Value, decimal Confidence);

public sealed class DocumentIntelligenceMcpAdapter : IMcpTool<DocumentExtractRequest, DocumentExtractResponse>
{
    private readonly DocumentAnalysisClient _client;
    private readonly ExternalServiceSettings _settings;
    private readonly ILogger<DocumentIntelligenceMcpAdapter> _logger;
    private readonly ResiliencePipeline _pipeline;

    public string ToolName => "DocumentIntelligence";
    public string Description => "Adapter to extract fields from documents via Azure Document Intelligence.";

    public DocumentIntelligenceMcpAdapter(
        DocumentAnalysisClient client, 
        IOptions<ExternalServiceSettings> settings, 
        ILogger<DocumentIntelligenceMcpAdapter> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential
            })
            .Build();
    }

    public async Task<McpToolResult<DocumentExtractResponse>> InvokeAsync(DocumentExtractRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            using var stream = new MemoryStream(request.DocumentContent);
            
            var operation = await _pipeline.ExecuteAsync(async ct => 
                await _client.AnalyzeDocumentAsync(WaitUntil.Completed, _settings.DocumentIntelligence.DeploymentId, stream, cancellationToken: ct),
                cancellationToken);
                
            var result = operation.Value;
            var fields = new Dictionary<string, ExtractedFieldDto>();
            
            foreach (var doc in result.Documents)
            {
                foreach (var field in doc.Fields)
                {
                    fields[field.Key] = new ExtractedFieldDto(field.Value.Content, (decimal)(field.Value.Confidence ?? 0));
                }
            }

            return new McpToolResult<DocumentExtractResponse>(true, new DocumentExtractResponse(
                true,
                fields,
                result.Content,
                (decimal)1.0, 
                null
            ), null, null);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Timeout connecting to Document Intelligence API.");
            return new McpToolResult<DocumentExtractResponse>(false, null, ex.Message, McpErrorCode.Timeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unknown error connecting to Document Intelligence API.");
            return new McpToolResult<DocumentExtractResponse>(false, null, ex.Message, McpErrorCode.Unknown);
        }
    }
}
