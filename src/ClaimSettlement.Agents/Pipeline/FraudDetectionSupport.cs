using ClaimSettlement.Agents.Models;
using ClaimSettlement.Domain.Entities;
using System.Text.Json;

namespace ClaimSettlement.Agents.Pipeline;

public sealed class FraudScoreResponse
{
    public decimal Score { get; init; }

    public IReadOnlyList<string> Signals { get; init; } = Array.Empty<string>();
}

public interface IFraudScoringClient
{
    Task<FraudScoreResponse> ScoreAsync(ClaimAgentContext context, ClaimPipelineInput input, CancellationToken ct);
}

public sealed class SimulatedFraudScoringClient : IFraudScoringClient
{
    public Task<FraudScoreResponse> ScoreAsync(ClaimAgentContext context, ClaimPipelineInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.UpstreamOutputs.TryGetValue("fraudService", out var payload) &&
            payload.RootElement.TryGetProperty("fail", out var failElement) &&
            failElement.ValueKind == JsonValueKind.True)
        {
            throw new TimeoutException("Simulated Fraud Detection Service timeout.");
        }

        var baseline = input.ClaimRecord.LossAmount >= context.ProviderConfig.ManualReviewClaimAmountThreshold ? 0.62m : 0.18m;
        return Task.FromResult(new FraudScoreResponse
        {
            Score = baseline,
            Signals = baseline >= 0.50m ? new[] { "SERVICE_HIGH_RISK" } : new[] { "SERVICE_LOW_RISK" }
        });
    }
}

public sealed class DuplicateClaimMatch
{
    public Guid ClaimId { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}

public interface IClaimHistorySignalProvider
{
    Task<(IReadOnlyList<DuplicateClaimMatch> duplicates, int claimsInPastYear)> GetHistoryAsync(
        ClaimAgentContext context,
        ClaimPipelineInput input,
        CancellationToken ct);
}

public sealed class UpstreamHistorySignalProvider : IClaimHistorySignalProvider
{
    public Task<(IReadOnlyList<DuplicateClaimMatch> duplicates, int claimsInPastYear)> GetHistoryAsync(
        ClaimAgentContext context,
        ClaimPipelineInput input,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var duplicates = new List<DuplicateClaimMatch>();
        var claimsInPastYear = 0;

        if (context.UpstreamOutputs.TryGetValue("historicalClaims", out var historyDoc) &&
            historyDoc.RootElement.TryGetProperty("claims", out var claimsElement) &&
            claimsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in claimsElement.EnumerateArray())
            {
                if (!item.TryGetProperty("claimId", out var claimIdElement))
                {
                    continue;
                }

                if (!Guid.TryParse(claimIdElement.GetString(), out var claimId))
                {
                    continue;
                }

                var isDuplicate = item.TryGetProperty("isDuplicate", out var duplicateElement) &&
                    duplicateElement.ValueKind == JsonValueKind.True;
                if (isDuplicate)
                {
                    duplicates.Add(new DuplicateClaimMatch
                    {
                        ClaimId = claimId,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }

                claimsInPastYear++;
            }
        }

        return Task.FromResult<(IReadOnlyList<DuplicateClaimMatch>, int)>((duplicates, claimsInPastYear));
    }
}

public interface IFraudExplainabilityGenerator
{
    string BuildExplanation(decimal overallScore, string verdict, IReadOnlyDictionary<string, decimal> signalWeights, IReadOnlyDictionary<string, string> evidence);
}

public sealed class TemplateFraudExplainabilityGenerator : IFraudExplainabilityGenerator
{
    public string BuildExplanation(decimal overallScore, string verdict, IReadOnlyDictionary<string, decimal> signalWeights, IReadOnlyDictionary<string, string> evidence)
    {
        var orderedSignals = signalWeights.OrderByDescending(x => x.Value)
            .Select(x => $"{x.Key}:{x.Value:0.00} ({evidence.GetValueOrDefault(x.Key, "n/a")})");

        return $"Fraud verdict {verdict} with composite score {overallScore:0.00}. " +
            $"Signal breakdown => {string.Join("; ", orderedSignals)}.";
    }
}

public interface IFraudCircuitBreaker
{
    bool IsOpen { get; }

    void RecordSuccess();

    void RecordFailure();
}

public sealed class TimeWindowFraudCircuitBreaker : IFraudCircuitBreaker
{
    private readonly TimeSpan _openDuration;
    private DateTime _openUntilUtc = DateTime.MinValue;
    private readonly object _sync = new();

    public TimeWindowFraudCircuitBreaker() : this(TimeSpan.FromSeconds(30))
    {
    }

    public TimeWindowFraudCircuitBreaker(TimeSpan openDuration)
    {
        _openDuration = openDuration;
    }

    public bool IsOpen
    {
        get
        {
            lock (_sync)
            {
                return DateTime.UtcNow < _openUntilUtc;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_sync)
        {
            _openUntilUtc = DateTime.MinValue;
        }
    }

    public void RecordFailure()
    {
        lock (_sync)
        {
            _openUntilUtc = DateTime.UtcNow.Add(_openDuration);
        }
    }
}
