namespace ClaimSettlement.Orchestrator;

public sealed class OrchestratorOptions
{
    public const string SectionName = "Orchestrator";

    public int PollIntervalSeconds { get; set; } = 5;

    public int PendingBatchSize { get; set; } = 200;

    public int DefaultProviderConcurrencyLimit { get; set; } = 100;

    public int AgentTimeoutSeconds { get; set; } = 30;
}