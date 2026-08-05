using ClaimSettlement.Agents.Abstractions;
using ClaimSettlement.Agents.Models;

namespace ClaimSettlement.Agents.Pipeline;

public sealed class PolicyValidationAgent : IClaimAgent<ClaimPipelineInput, PolicyValidationResult>
{
    public Task<PolicyValidationResult> InvokeAsync(ClaimAgentContext context, ClaimPipelineInput input, CancellationToken ct)
    {
        var deductible = Math.Round(input.ClaimRecord.LossAmount * 0.05m, 2);
        var coverageLimit = input.ClaimRecord.LossAmount;
        var netPayable = Math.Max(0m, coverageLimit - deductible);

        var result = new PolicyValidationResult
        {
            Verdict = "POLICY_VALID",
            CoverageLimit = coverageLimit,
            Deductible = deductible,
            NetPayable = netPayable
        };

        return Task.FromResult(result);
    }
}