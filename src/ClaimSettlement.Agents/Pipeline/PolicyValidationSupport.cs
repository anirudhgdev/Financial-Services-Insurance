using ClaimSettlement.Agents.Models;
using ClaimSettlement.Domain.Entities;

namespace ClaimSettlement.Agents.Pipeline;

public sealed record PolicyRecord
{
    public string PolicyNumber { get; init; } = string.Empty;

    public DateTime EffectiveFrom { get; init; }

    public DateTime EffectiveTo { get; init; }

    public bool IsCancelled { get; init; }

    public bool IsLapsed { get; init; }

    public decimal CoverageLimit { get; init; }

    public decimal Deductible { get; init; }

    public IReadOnlyList<string> CoveredClaimTypes { get; init; } = Array.Empty<string>();

    public IReadOnlyDictionary<string, string> ExclusionMap { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<string> AuthorizedInsuredIds { get; init; } = Array.Empty<string>();

    public DateTime PolicyInceptionDate { get; init; }

    public int WaitingPeriodDays { get; init; }

    public DateTime PremiumPaidThroughDate { get; init; }
}

public interface IPolicyManagementClient
{
    Task<PolicyRecord?> GetPolicyAsync(string providerId, string policyNumber, CancellationToken ct);
}

public sealed class SimulatedPolicyManagementClient : IPolicyManagementClient
{
    public Task<PolicyRecord?> GetPolicyAsync(string providerId, string policyNumber, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (policyNumber.StartsWith("NF-", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<PolicyRecord?>(null);
        }

        var policy = new PolicyRecord
        {
            PolicyNumber = policyNumber,
            EffectiveFrom = DateTime.UtcNow.Date.AddYears(-1),
            EffectiveTo = DateTime.UtcNow.Date.AddYears(1),
            IsCancelled = policyNumber.StartsWith("CX-", StringComparison.OrdinalIgnoreCase),
            IsLapsed = policyNumber.StartsWith("LP-", StringComparison.OrdinalIgnoreCase),
            CoverageLimit = 10000m,
            Deductible = 500m,
            CoveredClaimTypes = new[] { "auto", "property", "health", "life" },
            ExclusionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["excluded"] = "EXC-001"
            },
            AuthorizedInsuredIds = new[] { "user-1", "user-2" },
            PolicyInceptionDate = DateTime.UtcNow.Date.AddMonths(-6),
            WaitingPeriodDays = 30,
            PremiumPaidThroughDate = DateTime.UtcNow.Date.AddDays(30)
        };

        return Task.FromResult<PolicyRecord?>(policy);
    }
}

public interface IPolicyValidationEngine
{
    PolicyValidationResult Validate(Claim claim, PolicyRecord? policy, string claimantId);
}

public sealed class PolicyValidationEngine : IPolicyValidationEngine
{
    public PolicyValidationResult Validate(Claim claim, PolicyRecord? policy, string claimantId)
    {
        if (policy is null)
        {
            return new PolicyValidationResult
            {
                Verdict = "POLICY_NOT_FOUND",
                PolicyVerdict = "POLICY_NOT_FOUND",
                CoverageVerdict = "COVERAGE_UNKNOWN",
                EligibilityVerdict = "INELIGIBLE",
                FailureCode = "POLICY_NOT_FOUND",
                RequiresManualReview = true,
                IsPolicyFound = false,
                IsPolicyActiveOnLossDate = false
            };
        }

        var policyActive = !policy.IsCancelled && !policy.IsLapsed &&
            claim.DateOfLoss.Date >= policy.EffectiveFrom.Date &&
            claim.DateOfLoss.Date <= policy.EffectiveTo.Date;

        if (!policyActive)
        {
            return new PolicyValidationResult
            {
                Verdict = "POLICY_EXPIRED",
                PolicyVerdict = "POLICY_EXPIRED",
                CoverageVerdict = "COVERAGE_UNKNOWN",
                EligibilityVerdict = "INELIGIBLE",
                FailureCode = "POLICY_INVALID",
                RequiresManualReview = false,
                IsPolicyFound = true,
                IsPolicyActiveOnLossDate = false,
                CoverageLimit = policy.CoverageLimit,
                Deductible = policy.Deductible
            };
        }

        if (policy.ExclusionMap.TryGetValue(claim.ClaimType, out var exclusionCode))
        {
            return new PolicyValidationResult
            {
                Verdict = "COVERAGE_EXCLUDED",
                PolicyVerdict = "POLICY_VALID",
                CoverageVerdict = "COVERAGE_EXCLUDED",
                EligibilityVerdict = "INELIGIBLE",
                FailureCode = "COVERAGE_EXCLUDED",
                RequiresManualReview = false,
                IsPolicyFound = true,
                IsPolicyActiveOnLossDate = true,
                CoverageLimit = policy.CoverageLimit,
                Deductible = policy.Deductible,
                ExclusionReference = exclusionCode
            };
        }

        if (!policy.CoveredClaimTypes.Contains(claim.ClaimType, StringComparer.OrdinalIgnoreCase))
        {
            return new PolicyValidationResult
            {
                Verdict = "COVERAGE_EXCLUDED",
                PolicyVerdict = "POLICY_VALID",
                CoverageVerdict = "COVERAGE_EXCLUDED",
                EligibilityVerdict = "INELIGIBLE",
                FailureCode = "COVERAGE_EXCLUDED",
                RequiresManualReview = false,
                IsPolicyFound = true,
                IsPolicyActiveOnLossDate = true,
                CoverageLimit = policy.CoverageLimit,
                Deductible = policy.Deductible,
                ExclusionReference = "CLAIM_TYPE_NOT_COVERED"
            };
        }

        var waitingPeriodEnd = policy.PolicyInceptionDate.Date.AddDays(policy.WaitingPeriodDays);
        if (claim.DateOfLoss.Date < waitingPeriodEnd)
        {
            return new PolicyValidationResult
            {
                Verdict = "INELIGIBLE_WAITING_PERIOD",
                PolicyVerdict = "POLICY_VALID",
                CoverageVerdict = "COVERAGE_VALID",
                EligibilityVerdict = "INELIGIBLE_WAITING_PERIOD",
                FailureCode = "INELIGIBLE_WAITING_PERIOD",
                RequiresManualReview = false,
                IsPolicyFound = true,
                IsPolicyActiveOnLossDate = true,
                CoverageLimit = policy.CoverageLimit,
                Deductible = policy.Deductible,
                WaitingPeriodEndDateIso = waitingPeriodEnd.ToString("yyyy-MM-dd")
            };
        }

        if (policy.PremiumPaidThroughDate.Date < claim.DateOfLoss.Date)
        {
            return new PolicyValidationResult
            {
                Verdict = "INELIGIBLE_PREMIUM_ARREARS",
                PolicyVerdict = "POLICY_VALID",
                CoverageVerdict = "COVERAGE_VALID",
                EligibilityVerdict = "INELIGIBLE_PREMIUM_ARREARS",
                FailureCode = "INELIGIBLE_PREMIUM_ARREARS",
                RequiresManualReview = true,
                IsPolicyFound = true,
                IsPolicyActiveOnLossDate = true,
                CoverageLimit = policy.CoverageLimit,
                Deductible = policy.Deductible
            };
        }

        if (!policy.AuthorizedInsuredIds.Contains(claimantId, StringComparer.OrdinalIgnoreCase))
        {
            return new PolicyValidationResult
            {
                Verdict = "INELIGIBLE",
                PolicyVerdict = "POLICY_VALID",
                CoverageVerdict = "COVERAGE_VALID",
                EligibilityVerdict = "INELIGIBLE",
                FailureCode = "IDENTITY_MISMATCH",
                RequiresManualReview = true,
                IsPolicyFound = true,
                IsPolicyActiveOnLossDate = true,
                CoverageLimit = policy.CoverageLimit,
                Deductible = policy.Deductible
            };
        }

        var payableBase = Math.Min(claim.LossAmount, policy.CoverageLimit);
        var netPayable = Math.Max(0m, payableBase - policy.Deductible);
        var partialCoverage = claim.LossAmount > policy.CoverageLimit;

        return new PolicyValidationResult
        {
            Verdict = partialCoverage ? "PARTIAL_COVERAGE" : "POLICY_VALID",
            PolicyVerdict = "POLICY_VALID",
            CoverageVerdict = partialCoverage ? "PARTIAL_COVERAGE" : "COVERAGE_VALID",
            EligibilityVerdict = "ELIGIBLE",
            IsPolicyFound = true,
            IsPolicyActiveOnLossDate = true,
            CoverageLimit = policy.CoverageLimit,
            Deductible = policy.Deductible,
            NetPayable = netPayable,
            ExcessAmount = partialCoverage ? claim.LossAmount - policy.CoverageLimit : 0m
        };
    }
}
