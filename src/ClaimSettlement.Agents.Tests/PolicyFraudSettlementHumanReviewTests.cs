using ClaimSettlement.Agents.Models;
using ClaimSettlement.Agents.Pipeline;
using ClaimSettlement.Domain.Entities;
using System.Security.Claims;
using System.Text.Json;
using Xunit;
using DomainClaim = ClaimSettlement.Domain.Entities.Claim;
using SecurityClaim = System.Security.Claims.Claim;

namespace ClaimSettlement.Agents.Tests;

public sealed class PolicyFraudSettlementHumanReviewTests
{
    [Fact]
    public async Task PolicyValidation_ReturnsValidVerdict_WhenPolicyIsActiveAndEligible()
    {
        var agent = new PolicyValidationAgent(
            new StaticPolicyClient(BuildPolicy()),
            new PolicyValidationEngine());

        var claim = BuildClaim(lossAmount: 1200m, claimType: "auto", policyNumber: "POL-100");
        var result = await agent.InvokeAsync(BuildContext(claim), new ClaimPipelineInput(claim), CancellationToken.None);

        Assert.Equal("POLICY_VALID", result.PolicyVerdict);
        Assert.Equal("COVERAGE_VALID", result.CoverageVerdict);
        Assert.Equal("ELIGIBLE", result.EligibilityVerdict);
        Assert.Equal(700m, result.NetPayable);
    }

    [Fact]
    public async Task PolicyValidation_ReturnsExpiredVerdict_WhenDateOfLossOutsideTerm()
    {
        var policy = BuildPolicy() with { EffectiveTo = new DateTime(2026, 6, 30) };
        var agent = new PolicyValidationAgent(new StaticPolicyClient(policy), new PolicyValidationEngine());

        var claim = BuildClaim(dateOfLoss: new DateTime(2026, 7, 1));
        var result = await agent.InvokeAsync(BuildContext(claim), new ClaimPipelineInput(claim), CancellationToken.None);

        Assert.Equal("POLICY_EXPIRED", result.PolicyVerdict);
        Assert.Equal("POLICY_INVALID", result.FailureCode);
    }

    [Fact]
    public async Task PolicyValidation_ReturnsUnavailable_WhenApiFailsAllRetries()
    {
        var agent = new PolicyValidationAgent(new ThrowingPolicyClient(), new PolicyValidationEngine());

        var claim = BuildClaim();
        var result = await agent.InvokeAsync(BuildContext(claim), new ClaimPipelineInput(claim), CancellationToken.None);

        Assert.Equal("POLICY_CHECK_UNAVAILABLE", result.PolicyVerdict);
        Assert.True(result.RequiresManualReview);
    }

    [Fact]
    public async Task PolicyValidation_ReturnsNotFound_WhenPolicyMissing()
    {
        var agent = new PolicyValidationAgent(new NullPolicyClient(), new PolicyValidationEngine());
        var claim = BuildClaim(policyNumber: "NF-001");

        var result = await agent.InvokeAsync(BuildContext(claim), new ClaimPipelineInput(claim), CancellationToken.None);

        Assert.Equal("POLICY_NOT_FOUND", result.PolicyVerdict);
        Assert.True(result.RequiresManualReview);
    }

    [Fact]
    public async Task PolicyValidation_ReturnsCoverageExcluded_WhenExclusionApplies()
    {
        var policy = BuildPolicy() with
        {
            ExclusionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["auto"] = "EXC-900"
            }
        };
        var agent = new PolicyValidationAgent(new StaticPolicyClient(policy), new PolicyValidationEngine());
        var claim = BuildClaim(claimType: "auto");

        var result = await agent.InvokeAsync(BuildContext(claim), new ClaimPipelineInput(claim), CancellationToken.None);

        Assert.Equal("COVERAGE_EXCLUDED", result.CoverageVerdict);
        Assert.Equal("EXC-900", result.ExclusionReference);
    }

    [Fact]
    public async Task PolicyValidation_ReturnsPartialCoverage_WhenAmountExceedsLimit()
    {
        var policy = BuildPolicy() with { CoverageLimit = 1000m, Deductible = 100m };
        var agent = new PolicyValidationAgent(new StaticPolicyClient(policy), new PolicyValidationEngine());
        var claim = BuildClaim(lossAmount: 1800m);

        var result = await agent.InvokeAsync(BuildContext(claim), new ClaimPipelineInput(claim), CancellationToken.None);

        Assert.Equal("PARTIAL_COVERAGE", result.CoverageVerdict);
        Assert.Equal(800m, result.ExcessAmount);
        Assert.Equal(900m, result.NetPayable);
    }

    [Fact]
    public async Task PolicyValidation_ReturnsEligibilityFailure_WhenClaimantNotAuthorized()
    {
        var policy = BuildPolicy() with { AuthorizedInsuredIds = ["other-user"] };
        var agent = new PolicyValidationAgent(new StaticPolicyClient(policy), new PolicyValidationEngine());
        var claim = BuildClaim();

        var result = await agent.InvokeAsync(BuildContext(claim), new ClaimPipelineInput(claim), CancellationToken.None);

        Assert.Equal("INELIGIBLE", result.EligibilityVerdict);
        Assert.Equal("IDENTITY_MISMATCH", result.FailureCode);
    }

    [Fact]
    public async Task FraudDetection_FlagsDuplicateAndHighRisk()
    {
        var duplicateId = Guid.NewGuid();
        var fraudAgent = new FraudDetectionAgent(
            new ConstantFraudClient(0.55m, ["SERVICE_HIGH_RISK"]),
            new StaticHistoryProvider([new DuplicateClaimMatch { ClaimId = duplicateId, CreatedAtUtc = DateTime.UtcNow }], 5),
            new TemplateFraudExplainabilityGenerator(),
            new TimeWindowFraudCircuitBreaker(TimeSpan.FromSeconds(30)));

        var claim = BuildClaim(lossAmount: 10000m);
        var result = await fraudAgent.InvokeAsync(BuildContext(claim), new ClaimPipelineInput(claim), CancellationToken.None);

        Assert.Equal("FRAUD_HIGH", result.Verdict);
        Assert.True(result.DuplicateDetected);
        Assert.Equal(duplicateId, result.DuplicateClaimId);
        Assert.Contains("duplicate", result.SignalWeights.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FraudDetection_FallsBackToRules_WhenServiceUnavailable()
    {
        var fraudAgent = new FraudDetectionAgent(
            new ThrowingFraudClient(),
            new StaticHistoryProvider([], 1),
            new TemplateFraudExplainabilityGenerator(),
            new TimeWindowFraudCircuitBreaker(TimeSpan.FromSeconds(30)));

        var claim = BuildClaim(lossAmount: 3000m);
        var result = await fraudAgent.InvokeAsync(BuildContext(claim), new ClaimPipelineInput(claim), CancellationToken.None);

        Assert.True(result.ServiceUnavailable);
        Assert.Contains("FRAUD_SERVICE_UNAVAILABLE", result.Signals);
    }

    [Fact]
    public async Task FraudDetection_ReturnsLowRiskScenario()
    {
        var claim = BuildClaim(lossAmount: 1200m);
        var agent = new FraudDetectionAgent(
            new ConstantFraudClient(0.10m, ["SERVICE_LOW_RISK"]),
            new StaticHistoryProvider([], 0),
            new TemplateFraudExplainabilityGenerator(),
            new TimeWindowFraudCircuitBreaker(TimeSpan.FromSeconds(30)));

        var result = await agent.InvokeAsync(BuildContext(claim), new ClaimPipelineInput(claim), CancellationToken.None);

        Assert.Equal("FRAUD_LOW", result.Verdict);
    }

    [Fact]
    public async Task FraudDetection_ReturnsMediumRiskScenario()
    {
        var claim = BuildClaim(lossAmount: 4200m);
        var agent = new FraudDetectionAgent(
            new ConstantFraudClient(0.65m, ["SERVICE_MEDIUM_RISK"]),
            new StaticHistoryProvider([], 1),
            new TemplateFraudExplainabilityGenerator(),
            new TimeWindowFraudCircuitBreaker(TimeSpan.FromSeconds(30)));

        var result = await agent.InvokeAsync(BuildContext(claim), new ClaimPipelineInput(claim), CancellationToken.None);

        Assert.Equal("FRAUD_MEDIUM", result.Verdict);
        Assert.Contains("service", result.SignalWeights.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SettlementDecision_RejectsExpiredPolicy()
    {
        var claim = BuildClaim();
        var context = BuildContext(claim, new Dictionary<string, object>
        {
            ["DocumentAnalysisAgent"] = new DocumentAnalysisResult { Confidence = 0.95m, Summary = "summary" },
            ["PolicyValidationAgent"] = new PolicyValidationResult { PolicyVerdict = "POLICY_EXPIRED", CoverageVerdict = "COVERAGE_UNKNOWN", EligibilityVerdict = "INELIGIBLE", Verdict = "POLICY_EXPIRED" },
            ["FraudDetectionAgent"] = new FraudDetectionResult { Verdict = "FRAUD_LOW", RiskScore = 0.1m }
        });

        var result = await new SettlementDecisionAgent().InvokeAsync(context, new ClaimPipelineInput(claim), CancellationToken.None);

        Assert.Equal("REJECT", result.Recommendation);
        Assert.Equal("POLICY_INVALID", result.RejectionReasonCode);
        Assert.True(result.IsImmutable);
    }

    [Fact]
    public async Task SettlementDecision_EscalatesLowConfidenceToManualReview()
    {
        var claim = BuildClaim(lossAmount: 1800m);
        var context = BuildContext(claim, new Dictionary<string, object>
        {
            ["DocumentAnalysisAgent"] = new DocumentAnalysisResult { Confidence = 0.05m, Summary = "summary" },
            ["PolicyValidationAgent"] = new PolicyValidationResult { PolicyVerdict = "POLICY_VALID", CoverageVerdict = "COVERAGE_VALID", EligibilityVerdict = "ELIGIBLE", Verdict = "POLICY_VALID", CoverageLimit = 5000m, Deductible = 200m },
            ["FraudDetectionAgent"] = new FraudDetectionResult { Verdict = "FRAUD_LOW", RiskScore = 0.65m }
        });

        var result = await new SettlementDecisionAgent().InvokeAsync(context, new ClaimPipelineInput(claim), CancellationToken.None);

        Assert.Equal("MANUAL_REVIEW", result.Recommendation);
        Assert.True(result.ConfidenceScore < 0.70m);
    }

    [Fact]
    public async Task SettlementDecision_AutoApprovesValidLowRiskClaim()
    {
        var claim = BuildClaim(lossAmount: 1500m);
        var context = BuildContext(claim, new Dictionary<string, object>
        {
            ["DocumentAnalysisAgent"] = new DocumentAnalysisResult { Confidence = 0.95m, Summary = "complete" },
            ["PolicyValidationAgent"] = new PolicyValidationResult { PolicyVerdict = "POLICY_VALID", CoverageVerdict = "COVERAGE_VALID", EligibilityVerdict = "ELIGIBLE", Verdict = "POLICY_VALID", CoverageLimit = 2500m, Deductible = 250m },
            ["FraudDetectionAgent"] = new FraudDetectionResult { Verdict = "FRAUD_LOW", RiskScore = 0.1m }
        });

        var result = await new SettlementDecisionAgent().InvokeAsync(context, new ClaimPipelineInput(claim), CancellationToken.None);
        Assert.Equal("APPROVE", result.Recommendation);
    }

    [Fact]
    public async Task SettlementDecision_RejectsCoverageExcludedClaim()
    {
        var claim = BuildClaim(lossAmount: 1500m);
        var context = BuildContext(claim, new Dictionary<string, object>
        {
            ["DocumentAnalysisAgent"] = new DocumentAnalysisResult { Confidence = 0.95m, Summary = "complete" },
            ["PolicyValidationAgent"] = new PolicyValidationResult { PolicyVerdict = "POLICY_VALID", CoverageVerdict = "COVERAGE_EXCLUDED", EligibilityVerdict = "INELIGIBLE", Verdict = "COVERAGE_EXCLUDED" },
            ["FraudDetectionAgent"] = new FraudDetectionResult { Verdict = "FRAUD_LOW", RiskScore = 0.1m }
        });

        var result = await new SettlementDecisionAgent().InvokeAsync(context, new ClaimPipelineInput(claim), CancellationToken.None);
        Assert.Equal("REJECT", result.Recommendation);
        Assert.Equal("COVERAGE_EXCLUDED", result.RejectionReasonCode);
    }

    [Fact]
    public async Task SettlementDecision_RoutesManualReviewForHighFraud()
    {
        var claim = BuildClaim(lossAmount: 9000m);
        var context = BuildContext(claim, new Dictionary<string, object>
        {
            ["DocumentAnalysisAgent"] = new DocumentAnalysisResult { Confidence = 0.95m, Summary = "complete" },
            ["PolicyValidationAgent"] = new PolicyValidationResult { PolicyVerdict = "POLICY_VALID", CoverageVerdict = "COVERAGE_VALID", EligibilityVerdict = "ELIGIBLE", Verdict = "POLICY_VALID", CoverageLimit = 12000m, Deductible = 500m },
            ["FraudDetectionAgent"] = new FraudDetectionResult { Verdict = "FRAUD_HIGH", RiskScore = 0.85m }
        });

        var result = await new SettlementDecisionAgent().InvokeAsync(context, new ClaimPipelineInput(claim), CancellationToken.None);
        Assert.Equal("MANUAL_REVIEW", result.Recommendation);
    }

    [Fact]
    public async Task SettlementDecision_RoutesManualReviewForPolicyCheckUnavailable()
    {
        var claim = BuildClaim(lossAmount: 900m);
        var context = BuildContext(claim, new Dictionary<string, object>
        {
            ["DocumentAnalysisAgent"] = new DocumentAnalysisResult { Confidence = 0.95m, Summary = "complete" },
            ["PolicyValidationAgent"] = new PolicyValidationResult { PolicyVerdict = "POLICY_CHECK_UNAVAILABLE", CoverageVerdict = "COVERAGE_UNKNOWN", EligibilityVerdict = "INELIGIBLE", Verdict = "POLICY_CHECK_UNAVAILABLE" },
            ["FraudDetectionAgent"] = new FraudDetectionResult { Verdict = "FRAUD_LOW", RiskScore = 0.1m }
        });

        var result = await new SettlementDecisionAgent().InvokeAsync(context, new ClaimPipelineInput(claim), CancellationToken.None);
        Assert.Equal("MANUAL_REVIEW", result.Recommendation);
    }

    [Fact]
    public async Task SettlementDecision_RoutesManualReview_WhenClaimTypeAlwaysManualConfigured()
    {
        var claim = BuildClaim(lossAmount: 1500m, claimType: "property");
        var context = BuildContext(claim, new Dictionary<string, object>
        {
            ["DocumentAnalysisAgent"] = new DocumentAnalysisResult { Confidence = 0.95m, Summary = "complete" },
            ["PolicyValidationAgent"] = new PolicyValidationResult { PolicyVerdict = "POLICY_VALID", CoverageVerdict = "COVERAGE_VALID", EligibilityVerdict = "ELIGIBLE", Verdict = "POLICY_VALID", CoverageLimit = 2500m, Deductible = 250m },
            ["FraudDetectionAgent"] = new FraudDetectionResult { Verdict = "FRAUD_LOW", RiskScore = 0.1m }
        });

        context.ProviderConfig.AlwaysManualClaimTypes = "[\"property\"]";

        var result = await new SettlementDecisionAgent().InvokeAsync(context, new ClaimPipelineInput(claim), CancellationToken.None);
        Assert.Equal("MANUAL_REVIEW", result.Recommendation);
    }

    [Fact]
    public async Task HumanReviewAgent_AssignsClaimAndBuildsReviewPackage()
    {
        var claim = BuildClaim();
        var context = BuildContext(claim, new Dictionary<string, object>
        {
            ["DocumentAnalysisAgent"] = new DocumentAnalysisResult { Confidence = 0.95m, Summary = "Doc summary" },
            ["PolicyValidationAgent"] = new PolicyValidationResult { Verdict = "POLICY_VALID", PolicyVerdict = "POLICY_VALID", CoverageVerdict = "COVERAGE_VALID", EligibilityVerdict = "ELIGIBLE", NetPayable = 1200m },
            ["FraudDetectionAgent"] = new FraudDetectionResult { Verdict = "FRAUD_MEDIUM", RiskScore = 0.40m },
            ["SettlementDecisionAgent"] = new SettlementDecisionResult { Recommendation = "MANUAL_REVIEW", Reasoning = "Reasoning narrative", RecommendedSettlementAmount = 1100m }
        });

        var agent = new HumanReviewAgent(new FakeQueueStore(), new ReviewPackageAssembler());
        var result = await agent.InvokeAsync(context, new HumanReviewInput("High fraud"), CancellationToken.None);

        Assert.Equal("QUEUED", result.QueueStatus);
        Assert.Equal("adjuster-007", result.AssignedAdjusterId);
        Assert.NotNull(result.ReviewPackage);
        Assert.Empty(result.ReviewPackage!.MissingSections);
    }

    [Fact]
    public void HumanReviewSlaEvaluator_FlagsBreaches()
    {
        var evaluator = new HumanReviewSlaEvaluator();
        var assignments = new List<AdjusterAssignment>
        {
            new()
            {
                AssignmentId = Guid.NewGuid(),
                ClaimId = Guid.NewGuid(),
                ProviderId = "provider-1",
                AdjusterId = "adjuster-1",
                AssignedAt = DateTime.UtcNow.AddHours(-60)
            }
        };

        var actions = evaluator.Evaluate(assignments, DateTime.UtcNow, 48);

        Assert.Single(actions);
        Assert.Equal("SLA_BREACHED", actions[0].EventType);
    }

    private static ClaimAgentContext BuildContext(DomainClaim claim, IDictionary<string, object>? outputs = null)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new SecurityClaim(ClaimTypes.NameIdentifier, "user-1"));

        var upstream = new Dictionary<string, JsonDocument>(StringComparer.OrdinalIgnoreCase);
        if (outputs is not null)
        {
            foreach (var output in outputs)
            {
                upstream[output.Key] = JsonDocument.Parse(JsonSerializer.Serialize(output.Value));
            }
        }

        return new ClaimAgentContext
        {
            ClaimId = claim.ClaimId,
            ClaimRecord = claim,
            UpstreamOutputs = upstream,
            ProviderConfig = new ProviderConfiguration
            {
                ProviderId = "provider-1",
                ProviderName = "Provider",
                ManualReviewFraudThreshold = 0.70m,
                ManualReviewClaimAmountThreshold = 5000m,
                DeduplicationWindowDays = 90,
                InformationRequestDeadlineDays = 7,
                AdjusterSlaPeriodHours = 48,
                SupportedClaimTypes = "[\"auto\",\"property\"]",
                SupportedNotificationChannels = "[\"email\"]",
                PipelineConcurrencyLimit = 100,
                IsActive = true,
                ClaimTypeMandatoryFields = "{}",
                CoverageMappingRules = "{}",
                ExclusionSets = "{}",
                AlwaysManualClaimTypes = "[]"
            },
            UserIdentity = identity
        };
    }

    private static DomainClaim BuildClaim(
        decimal lossAmount = 1200m,
        string claimType = "auto",
        string policyNumber = "POL-1",
        DateTime? dateOfLoss = null)
    {
        return new DomainClaim
        {
            ClaimId = Guid.NewGuid(),
            ProviderId = "provider-1",
            ClaimantId = "user-1",
            PolicyNumber = policyNumber,
            DateOfLoss = dateOfLoss ?? new DateTime(2026, 7, 1),
            ClaimType = claimType,
            LossAmount = lossAmount,
            Status = "INTAKE_COMPLETE",
            CreatedAt = new DateTime(2026, 1, 1),
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static PolicyRecord BuildPolicy()
    {
        return new PolicyRecord
        {
            PolicyNumber = "POL-100",
            EffectiveFrom = new DateTime(2026, 1, 1),
            EffectiveTo = new DateTime(2026, 12, 31),
            IsCancelled = false,
            IsLapsed = false,
            CoverageLimit = 1500m,
            Deductible = 500m,
            CoveredClaimTypes = ["auto", "property"],
            ExclusionMap = new Dictionary<string, string>(),
            AuthorizedInsuredIds = ["user-1"],
            PolicyInceptionDate = new DateTime(2026, 1, 1),
            WaitingPeriodDays = 30,
            PremiumPaidThroughDate = new DateTime(2026, 12, 31)
        };
    }

    private sealed class StaticPolicyClient : IPolicyManagementClient
    {
        private readonly PolicyRecord _policy;

        public StaticPolicyClient(PolicyRecord policy)
        {
            _policy = policy;
        }

        public Task<PolicyRecord?> GetPolicyAsync(string providerId, string policyNumber, CancellationToken ct)
            => Task.FromResult<PolicyRecord?>(_policy);
    }

    private sealed class ThrowingPolicyClient : IPolicyManagementClient
    {
        public Task<PolicyRecord?> GetPolicyAsync(string providerId, string policyNumber, CancellationToken ct)
            => throw new TimeoutException("Simulated policy timeout");
    }

    private sealed class NullPolicyClient : IPolicyManagementClient
    {
        public Task<PolicyRecord?> GetPolicyAsync(string providerId, string policyNumber, CancellationToken ct)
            => Task.FromResult<PolicyRecord?>(null);
    }

    private sealed class ConstantFraudClient : IFraudScoringClient
    {
        private readonly decimal _score;
        private readonly IReadOnlyList<string> _signals;

        public ConstantFraudClient(decimal score, IReadOnlyList<string> signals)
        {
            _score = score;
            _signals = signals;
        }

        public Task<FraudScoreResponse> ScoreAsync(ClaimAgentContext context, ClaimPipelineInput input, CancellationToken ct)
            => Task.FromResult(new FraudScoreResponse { Score = _score, Signals = _signals });
    }

    private sealed class ThrowingFraudClient : IFraudScoringClient
    {
        public Task<FraudScoreResponse> ScoreAsync(ClaimAgentContext context, ClaimPipelineInput input, CancellationToken ct)
            => throw new TimeoutException("Simulated fraud timeout");
    }

    private sealed class StaticHistoryProvider : IClaimHistorySignalProvider
    {
        private readonly IReadOnlyList<DuplicateClaimMatch> _duplicates;
        private readonly int _claimsInPastYear;

        public StaticHistoryProvider(IReadOnlyList<DuplicateClaimMatch> duplicates, int claimsInPastYear)
        {
            _duplicates = duplicates;
            _claimsInPastYear = claimsInPastYear;
        }

        public Task<(IReadOnlyList<DuplicateClaimMatch> duplicates, int claimsInPastYear)> GetHistoryAsync(
            ClaimAgentContext context,
            ClaimPipelineInput input,
            CancellationToken ct)
        {
            return Task.FromResult((_duplicates, _claimsInPastYear));
        }
    }

    private sealed class FakeQueueStore : IHumanReviewQueueStore
    {
        public Task<HumanReviewQueueEntry> EnqueueAsync(ClaimAgentContext context, string reason, CancellationToken ct)
        {
            return Task.FromResult(new HumanReviewQueueEntry
            {
                ClaimId = context.ClaimId,
                ProviderId = context.ProviderConfig.ProviderId,
                AssignedAdjusterId = "adjuster-007",
                AssignedAtUtc = DateTime.UtcNow,
                PendingAssignment = false
            });
        }
    }
}
