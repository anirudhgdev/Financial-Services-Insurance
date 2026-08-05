using ClaimSettlement.Agents.Abstractions;
using ClaimSettlement.Agents.Models;

namespace ClaimSettlement.Agents.Pipeline;

public sealed class DocumentAnalysisAgent : IClaimAgent<ClaimPipelineInput, DocumentAnalysisResult>
{
    public Task<DocumentAnalysisResult> InvokeAsync(ClaimAgentContext context, ClaimPipelineInput input, CancellationToken ct)
    {
        var result = new DocumentAnalysisResult
        {
            Summary = $"Document analysis completed for claim {context.ClaimId}.",
            Confidence = 0.95m,
            MissingFields = Array.Empty<string>()
        };

        return Task.FromResult(result);
    }
}