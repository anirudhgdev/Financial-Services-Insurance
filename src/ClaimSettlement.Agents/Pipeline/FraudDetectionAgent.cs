using ClaimSettlement.Agents.Abstractions;
using ClaimSettlement.Agents.Models;

namespace ClaimSettlement.Agents.Pipeline;

public sealed class FraudDetectionAgent : IClaimAgent<ClaimPipelineInput, FraudDetectionResult>
{
    public Task<FraudDetectionResult> InvokeAsync(ClaimAgentContext context, ClaimPipelineInput input, CancellationToken ct)
    {
        var score = input.ClaimRecord.LossAmount > context.ProviderConfig.ManualReviewClaimAmountThreshold ? 0.72m : 0.18m;

        var result = new FraudDetectionResult
        {
            Verdict = score >= 0.70m ? "FRAUD_HIGH" : "FRAUD_LOW",
            RiskScore = score,
            Signals = score >= 0.70m ? new[] { "HIGH_VALUE_CLAIM" } : Array.Empty<string>()
        };

        return Task.FromResult(result);
    }
}