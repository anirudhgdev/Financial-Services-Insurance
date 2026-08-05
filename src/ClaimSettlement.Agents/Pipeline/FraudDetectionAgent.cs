using ClaimSettlement.Agents.Abstractions;
using ClaimSettlement.Agents.Models;

namespace ClaimSettlement.Agents.Pipeline;

public sealed class FraudDetectionAgent : IClaimAgent<ClaimPipelineInput, FraudDetectionResult>
{
    private readonly IFraudScoringClient _fraudScoringClient;
    private readonly IClaimHistorySignalProvider _historySignalProvider;
    private readonly IFraudExplainabilityGenerator _explainabilityGenerator;
    private readonly IFraudCircuitBreaker _circuitBreaker;

    public FraudDetectionAgent(
        IFraudScoringClient fraudScoringClient,
        IClaimHistorySignalProvider historySignalProvider,
        IFraudExplainabilityGenerator explainabilityGenerator,
        IFraudCircuitBreaker circuitBreaker)
    {
        _fraudScoringClient = fraudScoringClient;
        _historySignalProvider = historySignalProvider;
        _explainabilityGenerator = explainabilityGenerator;
        _circuitBreaker = circuitBreaker;
    }

    public async Task<FraudDetectionResult> InvokeAsync(ClaimAgentContext context, ClaimPipelineInput input, CancellationToken ct)
    {
        var signalWeights = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var evidence = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var serviceUnavailable = false;
        IReadOnlyList<string> externalSignals = Array.Empty<string>();
        var externalScore = 0m;

        if (!_circuitBreaker.IsOpen)
        {
            var externalScored = await TryExternalScoreAsync(context, input, ct);
            serviceUnavailable = !externalScored.success;
            externalScore = externalScored.score;
            externalSignals = externalScored.signals;
        }
        else
        {
            serviceUnavailable = true;
        }

        signalWeights["service"] = serviceUnavailable ? 0m : externalScore * 0.50m;
        evidence["service"] = serviceUnavailable ? "Fraud service unavailable" : string.Join(',', externalSignals);

        var history = await _historySignalProvider.GetHistoryAsync(context, input, ct);
        var duplicateDetected = history.duplicates.Count > 0;
        var duplicateId = history.duplicates.FirstOrDefault()?.ClaimId;

        var historyScore = 0m;
        if (duplicateDetected)
        {
            historyScore += 0.40m;
            signalWeights["duplicate"] = 0.40m;
            evidence["duplicate"] = $"Matched prior claim {duplicateId}";
        }

        if (history.claimsInPastYear > 3)
        {
            historyScore += 0.20m;
            signalWeights["HIGH_FREQUENCY_CLAIMANT"] = 0.20m;
            evidence["HIGH_FREQUENCY_CLAIMANT"] = $"{history.claimsInPastYear} claims in 12 months";
        }

        signalWeights["history"] = historyScore * 0.25m;
        evidence["history"] = $"History score {historyScore:0.00}";

        var patternScore = EvaluatePatternScore(context, input, signalWeights, evidence);

        var riskScore = Math.Clamp(signalWeights.Values.Sum() + (patternScore * 0.25m), 0m, 1m);
        var verdict = ResolveVerdict(riskScore);

        var signals = signalWeights
            .Where(x => x.Value > 0m)
            .Select(x => x.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (serviceUnavailable)
        {
            signals.Add("FRAUD_SERVICE_UNAVAILABLE");
        }

        var explanation = _explainabilityGenerator.BuildExplanation(riskScore, verdict, signalWeights, evidence);

        return new FraudDetectionResult
        {
            Verdict = verdict,
            RiskScore = riskScore,
            Signals = signals,
            ServiceUnavailable = serviceUnavailable,
            DuplicateDetected = duplicateDetected,
            DuplicateClaimId = duplicateId,
            SignalWeights = signalWeights,
            Explanation = explanation
        };
    }

    private async Task<(bool success, decimal score, IReadOnlyList<string> signals)> TryExternalScoreAsync(
        ClaimAgentContext context,
        ClaimPipelineInput input,
        CancellationToken ct)
    {
        const int maxAttempts = 3;
        var delayMs = 500;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var scored = await _fraudScoringClient.ScoreAsync(context, input, ct);
                _circuitBreaker.RecordSuccess();
                return (true, scored.Score, scored.Signals);
            }
            catch when (attempt < maxAttempts)
            {
                await Task.Delay(delayMs, ct);
                delayMs *= 2;
            }
            catch
            {
                _circuitBreaker.RecordFailure();
                return (false, 0m, Array.Empty<string>());
            }
        }

        _circuitBreaker.RecordFailure();
        return (false, 0m, Array.Empty<string>());
    }

    private static decimal EvaluatePatternScore(
        ClaimAgentContext context,
        ClaimPipelineInput input,
        IDictionary<string, decimal> signalWeights,
        IDictionary<string, string> evidence)
    {
        var score = 0m;

        if (input.ClaimRecord.LossAmount > 5000m && input.ClaimRecord.LossAmount % 1000m == 0m)
        {
            score += 0.25m;
            signalWeights["ROUND_AMOUNT"] = 0.25m;
            evidence["ROUND_AMOUNT"] = $"Loss amount {input.ClaimRecord.LossAmount:0.00}";
        }

        var policyAgeDays = (input.ClaimRecord.DateOfLoss.Date - input.ClaimRecord.CreatedAt.Date).TotalDays;
        if (policyAgeDays <= 30)
        {
            score += 0.20m;
            signalWeights["NEW_POLICY_CLAIM"] = 0.20m;
            evidence["NEW_POLICY_CLAIM"] = $"Claim filed {Math.Max(0, policyAgeDays):0} days from policy inception";
        }

        if (input.ClaimRecord.LossAmount >= context.ProviderConfig.ManualReviewClaimAmountThreshold)
        {
            score += 0.20m;
            signalWeights["HIGH_VALUE_CLAIM"] = 0.20m;
            evidence["HIGH_VALUE_CLAIM"] = $"Amount {input.ClaimRecord.LossAmount:0.00}";
        }

        return Math.Clamp(score, 0m, 1m);
    }

    private static string ResolveVerdict(decimal score)
    {
        if (score >= 0.70m)
        {
            return "FRAUD_HIGH";
        }

        if (score >= 0.30m)
        {
            return "FRAUD_MEDIUM";
        }

        return "FRAUD_LOW";
    }
}