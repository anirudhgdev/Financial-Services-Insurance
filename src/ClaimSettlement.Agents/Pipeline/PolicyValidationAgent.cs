using ClaimSettlement.Agents.Abstractions;
using ClaimSettlement.Agents.Models;

namespace ClaimSettlement.Agents.Pipeline;

public sealed class PolicyValidationAgent : IClaimAgent<ClaimPipelineInput, PolicyValidationResult>
{
    private readonly IPolicyManagementClient _policyManagementClient;
    private readonly IPolicyValidationEngine _policyValidationEngine;

    public PolicyValidationAgent(
        IPolicyManagementClient policyManagementClient,
        IPolicyValidationEngine policyValidationEngine)
    {
        _policyManagementClient = policyManagementClient;
        _policyValidationEngine = policyValidationEngine;
    }

    public async Task<PolicyValidationResult> InvokeAsync(ClaimAgentContext context, ClaimPipelineInput input, CancellationToken ct)
    {
        const int attempts = 3;
        var delayMs = 500;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

                var policy = await _policyManagementClient.GetPolicyAsync(
                    context.ProviderConfig.ProviderId,
                    input.ClaimRecord.PolicyNumber,
                    timeoutCts.Token);

                return _policyValidationEngine.Validate(input.ClaimRecord, policy, input.ClaimRecord.ClaimantId);
            }
            catch (Exception) when (attempt < attempts)
            {
                await Task.Delay(delayMs, ct);
                delayMs *= 2;
            }
            catch (Exception)
            {
                break;
            }
        }

        return new PolicyValidationResult
        {
            Verdict = "POLICY_CHECK_UNAVAILABLE",
            PolicyVerdict = "POLICY_CHECK_UNAVAILABLE",
            CoverageVerdict = "COVERAGE_UNKNOWN",
            EligibilityVerdict = "INELIGIBLE",
            FailureCode = "POLICY_CHECK_UNAVAILABLE",
            RequiresManualReview = true,
            IsPolicyFound = false,
            IsPolicyActiveOnLossDate = false
        };
    }
}