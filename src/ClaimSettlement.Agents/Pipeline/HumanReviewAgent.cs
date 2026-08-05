using ClaimSettlement.Agents.Abstractions;
using ClaimSettlement.Agents.Models;

namespace ClaimSettlement.Agents.Pipeline;

public sealed class HumanReviewAgent : IClaimAgent<HumanReviewInput, HumanReviewResult>
{
    public Task<HumanReviewResult> InvokeAsync(ClaimAgentContext context, HumanReviewInput input, CancellationToken ct)
    {
        var result = new HumanReviewResult
        {
            QueueStatus = "QUEUED",
            QueuedAtUtc = DateTime.UtcNow,
            Reason = input.Reason
        };

        return Task.FromResult(result);
    }
}