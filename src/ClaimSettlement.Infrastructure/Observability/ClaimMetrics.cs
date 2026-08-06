using System.Diagnostics.Metrics;

namespace ClaimSettlement.Infrastructure.Observability;

public static class ClaimTelemetry
{
    public const string ActivitySourceName = "ClaimSettlement.Agents";

    public const string MeterName = "ClaimSettlement.Metrics";
}

public interface IClaimMetrics
{
    void RecordClaimOutcome(string outcome);

    void RecordPipelineDuration(TimeSpan duration, string outcome);

    void RecordFraudScore(decimal score);

    void RecordAgentExecution(string agentName, bool success);

    void RecordNotificationDelivery(string eventType, bool delivered);
}

public sealed class ClaimMetrics : IClaimMetrics
{
    private readonly Counter<long> _claimsPerHour;
    private readonly Histogram<double> _pipelineDurationSeconds;
    private readonly Histogram<double> _fraudScoreDistribution;
    private readonly Counter<long> _agentExecutions;
    private readonly Counter<long> _agentErrors;
    private readonly Counter<long> _notificationDelivered;
    private readonly Counter<long> _notificationFailed;

    public ClaimMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(ClaimTelemetry.MeterName);
        _claimsPerHour = meter.CreateCounter<long>("claims_per_hour", unit: "claims", description: "Claim outcomes over time.");
        _pipelineDurationSeconds = meter.CreateHistogram<double>("pipeline_duration_seconds", unit: "s", description: "End-to-end pipeline duration.");
        _fraudScoreDistribution = meter.CreateHistogram<double>("fraud_score", description: "Fraud score distribution.");
        _agentExecutions = meter.CreateCounter<long>("agent_executions_total", unit: "count", description: "Agent execution count.");
        _agentErrors = meter.CreateCounter<long>("agent_errors_total", unit: "count", description: "Agent failure count.");
        _notificationDelivered = meter.CreateCounter<long>("notification_delivered_total", unit: "count", description: "Successful notification deliveries.");
        _notificationFailed = meter.CreateCounter<long>("notification_failed_total", unit: "count", description: "Failed notification deliveries.");
    }

    public void RecordClaimOutcome(string outcome)
    {
        _claimsPerHour.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void RecordPipelineDuration(TimeSpan duration, string outcome)
    {
        _pipelineDurationSeconds.Record(duration.TotalSeconds, new KeyValuePair<string, object?>("outcome", outcome));
    }

    public void RecordFraudScore(decimal score)
    {
        _fraudScoreDistribution.Record((double)score);
    }

    public void RecordAgentExecution(string agentName, bool success)
    {
        _agentExecutions.Add(1, new KeyValuePair<string, object?>("agent", agentName));
        if (!success)
        {
            _agentErrors.Add(1, new KeyValuePair<string, object?>("agent", agentName));
        }
    }

    public void RecordNotificationDelivery(string eventType, bool delivered)
    {
        if (delivered)
        {
            _notificationDelivered.Add(1, new KeyValuePair<string, object?>("eventType", eventType));
            return;
        }

        _notificationFailed.Add(1, new KeyValuePair<string, object?>("eventType", eventType));
    }
}
