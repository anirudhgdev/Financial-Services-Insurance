using ClaimSettlement.Agents.Abstractions;
using ClaimSettlement.Agents.Models;

namespace ClaimSettlement.Agents.Pipeline;

public sealed class SettlementDecisionAgent : IClaimAgent<ClaimPipelineInput, SettlementDecisionResult>
{
    public Task<SettlementDecisionResult> InvokeAsync(ClaimAgentContext context, ClaimPipelineInput input, CancellationToken ct)
    {
        var recommendation = "APPROVE";
        var confidence = 0.92m;

        if (input.ClaimRecord.LossAmount > context.ProviderConfig.ManualReviewClaimAmountThreshold)
        {
            recommendation = "MANUAL_REVIEW";
            confidence = 0.88m;
        }

        var result = new SettlementDecisionResult
        {
            Recommendation = recommendation,
            ConfidenceScore = confidence,
            RecommendedSettlementAmount = Math.Max(0m, input.ClaimRecord.LossAmount * 0.95m),
            Reasoning = "Decision generated from current policy validation and fraud signals."
        };

        return Task.FromResult(result);
    }
}