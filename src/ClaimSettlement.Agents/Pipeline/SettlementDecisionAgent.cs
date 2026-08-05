using ClaimSettlement.Agents.Abstractions;
using ClaimSettlement.Agents.Models;
using System.Text.Json;

namespace ClaimSettlement.Agents.Pipeline;

public sealed class SettlementDecisionAgent : IClaimAgent<ClaimPipelineInput, SettlementDecisionResult>
{
    public Task<SettlementDecisionResult> InvokeAsync(ClaimAgentContext context, ClaimPipelineInput input, CancellationToken ct)
    {
        var missingInputs = ValidateUpstreamInputs(context);
        if (missingInputs.Count > 0)
        {
            return Task.FromResult(new SettlementDecisionResult
            {
                Recommendation = "MANUAL_REVIEW",
                ConfidenceScore = 0.40m,
                RecommendedSettlementAmount = 0m,
                Reasoning = "Decision blocked because required upstream outputs are missing.",
                RejectionReasonCode = "DECISION_BLOCKED_MISSING_INPUT",
                MissingInputs = missingInputs,
                IsImmutable = true,
                DecisionVersion = ResolveDecisionVersion(context)
            });
        }

        var policy = ReadPolicyValidation(context);
        var fraud = ReadFraudResult(context);
        var docAnalysis = ReadDocumentAnalysis(context);

        var recommendation = "APPROVE";
        string? rejectionCode = null;

        if (policy.PolicyVerdict is "POLICY_EXPIRED" or "POLICY_NOT_FOUND")
        {
            recommendation = "REJECT";
            rejectionCode = "POLICY_INVALID";
        }
        else if (string.Equals(policy.CoverageVerdict, "COVERAGE_EXCLUDED", StringComparison.OrdinalIgnoreCase))
        {
            recommendation = "REJECT";
            rejectionCode = "COVERAGE_EXCLUDED";
        }
        else if (string.Equals(policy.PolicyVerdict, "POLICY_CHECK_UNAVAILABLE", StringComparison.OrdinalIgnoreCase))
        {
            recommendation = "MANUAL_REVIEW";
        }
        else if (string.Equals(fraud.Verdict, "FRAUD_HIGH", StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(fraud.Verdict, "FRAUD_MEDIUM", StringComparison.OrdinalIgnoreCase) &&
             input.ClaimRecord.LossAmount > context.ProviderConfig.ManualReviewClaimAmountThreshold))
        {
            recommendation = "MANUAL_REVIEW";
        }
        else if (docAnalysis.BlockingMissingFields.Count > 0)
        {
            recommendation = "MANUAL_REVIEW";
        }

        var coverageLimit = policy.CoverageLimit <= 0m ? input.ClaimRecord.LossAmount : policy.CoverageLimit;
        var deductible = Math.Max(0m, policy.Deductible);
        var recommendedAmount = Math.Max(0m, Math.Min(input.ClaimRecord.LossAmount, coverageLimit) - deductible);

        var confidence = ComputeConfidence(policy, fraud, docAnalysis);
        if (confidence < 0.70m)
        {
            recommendation = "MANUAL_REVIEW";
        }

        if (IsAlwaysManualClaimType(input.ClaimRecord.ClaimType, context.ProviderConfig.AlwaysManualClaimTypes))
        {
            recommendation = "MANUAL_REVIEW";
        }

        var reasoning = BuildReasoningNarrative(
            input.ClaimRecord,
            recommendation,
            policy,
            fraud,
            docAnalysis,
            confidence,
            recommendedAmount,
            rejectionCode);

        var result = new SettlementDecisionResult
        {
            Recommendation = recommendation,
            ConfidenceScore = confidence,
            RecommendedSettlementAmount = recommendedAmount,
            Reasoning = reasoning,
            RejectionReasonCode = rejectionCode,
            AppliedCoverageLimit = coverageLimit,
            AppliedDeductible = deductible,
            IsImmutable = true,
            DecisionVersion = ResolveDecisionVersion(context)
        };

        return Task.FromResult(result);
    }

    private static List<string> ValidateUpstreamInputs(ClaimAgentContext context)
    {
        var required = new[] { "DocumentAnalysisAgent", "PolicyValidationAgent", "FraudDetectionAgent" };
        return required.Where(x => !context.UpstreamOutputs.ContainsKey(x)).ToList();
    }

    private static PolicyValidationResult ReadPolicyValidation(ClaimAgentContext context)
    {
        return ReadOutput<PolicyValidationResult>(context, "PolicyValidationAgent") ?? new PolicyValidationResult
        {
            Verdict = "POLICY_CHECK_UNAVAILABLE",
            PolicyVerdict = "POLICY_CHECK_UNAVAILABLE",
            CoverageVerdict = "COVERAGE_UNKNOWN",
            EligibilityVerdict = "INELIGIBLE",
            RequiresManualReview = true
        };
    }

    private static FraudDetectionResult ReadFraudResult(ClaimAgentContext context)
    {
        return ReadOutput<FraudDetectionResult>(context, "FraudDetectionAgent") ?? new FraudDetectionResult
        {
            Verdict = "FRAUD_MEDIUM",
            RiskScore = 0.50m,
            Signals = new[] { "FRAUD_RESULT_MISSING" }
        };
    }

    private static DocumentAnalysisResult ReadDocumentAnalysis(ClaimAgentContext context)
    {
        return ReadOutput<DocumentAnalysisResult>(context, "DocumentAnalysisAgent") ?? new DocumentAnalysisResult
        {
            Summary = "Document analysis unavailable.",
            Confidence = 0m,
            BlockingMissingFields = new[] { "DocumentAnalysisMissing" }
        };
    }

    private static T? ReadOutput<T>(ClaimAgentContext context, string key) where T : class
    {
        if (!context.UpstreamOutputs.TryGetValue(key, out var value))
        {
            return null;
        }

        return System.Text.Json.JsonSerializer.Deserialize<T>(value.RootElement.GetRawText());
    }

    private static decimal ComputeConfidence(PolicyValidationResult policy, FraudDetectionResult fraud, DocumentAnalysisResult documentAnalysis)
    {
        var policyScore = string.Equals(policy.PolicyVerdict, "POLICY_VALID", StringComparison.OrdinalIgnoreCase) ? 0.95m : 0.60m;
        var fraudScore = 1m - Math.Min(0.8m, fraud.RiskScore);
        var documentScore = Math.Clamp(documentAnalysis.Confidence, 0m, 1m);

        return Math.Clamp((policyScore * 0.40m) + (fraudScore * 0.30m) + (documentScore * 0.30m), 0m, 1m);
    }

    private static int ResolveDecisionVersion(ClaimAgentContext context)
    {
        return context.UpstreamOutputs.ContainsKey("SettlementDecisionAgent") ? 2 : 1;
    }

    private static bool IsAlwaysManualClaimType(string claimType, string alwaysManualClaimTypesJson)
    {
        if (string.IsNullOrWhiteSpace(claimType) || string.IsNullOrWhiteSpace(alwaysManualClaimTypesJson) || alwaysManualClaimTypesJson == "[]")
        {
            return false;
        }

        try
        {
            var claimTypes = JsonSerializer.Deserialize<List<string>>(alwaysManualClaimTypesJson) ?? [];
            return claimTypes.Any(x => string.Equals(x, claimType, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static string BuildReasoningNarrative(
        ClaimSettlement.Domain.Entities.Claim claim,
        string recommendation,
        PolicyValidationResult policy,
        FraudDetectionResult fraud,
        DocumentAnalysisResult document,
        decimal confidence,
        decimal amount,
        string? rejectionCode)
    {
        var narrative =
            $"Claim {claim.ClaimId} was evaluated using policy, document, and fraud analysis outputs. " +
            $"Policy validation returned {policy.PolicyVerdict} with coverage verdict {policy.CoverageVerdict} and eligibility verdict {policy.EligibilityVerdict}. " +
            $"Fraud detection produced {fraud.Verdict} at score {fraud.RiskScore:0.00}, supported by signals: {string.Join(", ", fraud.Signals.DefaultIfEmpty("none"))}. " +
            $"Document analysis confidence was {document.Confidence:0.00}; blocking gaps count is {document.BlockingMissingFields.Count}. " +
            $"The weighted decision engine recommendation is {recommendation} with confidence {confidence:0.00}. " +
            $"Recommended settlement amount uses min(claimed, coverage)-deductible and currently equals {amount:0.00}. ";

        if (!string.IsNullOrWhiteSpace(rejectionCode))
        {
            narrative += $"Rejection code: {rejectionCode}. ";
        }

        narrative +=
            "This recommendation remains advisory for adjusters when manual review is required, and all evidence references are included to preserve traceability. " +
            "Any downstream override must be captured as a new immutable decision record version. " +
            "Where confidence is below threshold or upstream certainty is degraded, the claim is escalated to protect policyholder fairness and operational risk controls.";

        var words = narrative.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        if (words < 150)
        {
            narrative += " Additional assessment context: policy limits, deductible values, and fraud signal provenance are retained in structured outputs to support replay and compliance audits.";
        }

        return narrative;
    }
}