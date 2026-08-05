using ClaimSettlement.Agents.Models;
using ClaimSettlement.Agents.Pipeline;
using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;

namespace ClaimSettlement.Orchestrator;

public sealed class ClaimPipelineOrchestrator : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new("ClaimSettlement.Orchestrator");
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ClaimPipelineOrchestrator> _logger;
    private readonly OrchestratorOptions _options;
    private readonly ConcurrentDictionary<string, ProviderDispatchQueue> _providerQueues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Guid, byte> _queuedOrRunningClaims = new();

    private readonly IReadOnlyList<PipelineStep> _pipelineSteps =
    [
        new(
            "DocumentAnalysisAgent",
            async (provider, context, claim, ct) =>
            {
                var agent = provider.GetRequiredService<DocumentAnalysisAgent>();
                var output = await agent.InvokeAsync(context, new ClaimPipelineInput(claim), ct);
                return JsonSerializer.Serialize(output, SerializerOptions);
            }),
        new(
            "PolicyValidationAgent",
            async (provider, context, claim, ct) =>
            {
                var agent = provider.GetRequiredService<PolicyValidationAgent>();
                var output = await agent.InvokeAsync(context, new ClaimPipelineInput(claim), ct);
                return JsonSerializer.Serialize(output, SerializerOptions);
            }),
        new(
            "FraudDetectionAgent",
            async (provider, context, claim, ct) =>
            {
                var agent = provider.GetRequiredService<FraudDetectionAgent>();
                var output = await agent.InvokeAsync(context, new ClaimPipelineInput(claim), ct);
                return JsonSerializer.Serialize(output, SerializerOptions);
            }),
        new(
            "SettlementDecisionAgent",
            async (provider, context, claim, ct) =>
            {
                var agent = provider.GetRequiredService<SettlementDecisionAgent>();
                var output = await agent.InvokeAsync(context, new ClaimPipelineInput(claim), ct);
                return JsonSerializer.Serialize(output, SerializerOptions);
            })
    ];

    public ClaimPipelineOrchestrator(
        IServiceScopeFactory scopeFactory,
        IOptions<OrchestratorOptions> options,
        ILogger<ClaimPipelineOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Claim pipeline orchestrator is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnqueuePendingClaimsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enqueue pending claims.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Claim pipeline orchestrator is stopping.");
    }

    private async Task EnqueuePendingClaimsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClaimSettlementDbContext>();

        var pendingClaims = await dbContext.Claims
            .AsNoTracking()
            .Where(c => c.Status == "INTAKE_COMPLETE" || c.Status == "PIPELINE_IN_PROGRESS")
            .OrderBy(c => c.CreatedAt)
            .Take(_options.PendingBatchSize)
            .Select(c => new { c.ClaimId, c.ProviderId })
            .ToListAsync(ct);

        foreach (var pending in pendingClaims)
        {
            if (!_queuedOrRunningClaims.TryAdd(pending.ClaimId, 0))
            {
                continue;
            }

            var queue = await GetOrCreateProviderQueueAsync(pending.ProviderId, ct);
            await queue.Writer.WriteAsync(pending.ClaimId, ct);
        }
    }

    private async Task<ProviderDispatchQueue> GetOrCreateProviderQueueAsync(string providerId, CancellationToken ct)
    {
        if (_providerQueues.TryGetValue(providerId, out var existingQueue))
        {
            return existingQueue;
        }

        var limit = await ResolveProviderConcurrencyLimitAsync(providerId, ct);
        var createdQueue = new ProviderDispatchQueue(
            providerId,
            limit,
            StartClaimExecutionAsync,
            _logger,
            _queuedOrRunningClaims,
            ct);

        return _providerQueues.GetOrAdd(providerId, createdQueue);
    }

    private async Task<int> ResolveProviderConcurrencyLimitAsync(string providerId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var providerConfigurationService = scope.ServiceProvider.GetRequiredService<IProviderConfigurationService>();
        var configuration = await providerConfigurationService.GetConfigurationAsync(providerId, ct);

        var resolved = configuration.PipelineConcurrencyLimit <= 0
            ? _options.DefaultProviderConcurrencyLimit
            : configuration.PipelineConcurrencyLimit;
        return Math.Max(1, resolved);
    }

    private Task StartClaimExecutionAsync(string providerId, Guid claimId, CancellationToken ct)
        => Task.Run(() => ProcessClaimAsync(providerId, claimId, ct), ct);

    private async Task ProcessClaimAsync(string providerId, Guid claimId, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ClaimSettlementDbContext>();
            var providerConfigurationService = scope.ServiceProvider.GetRequiredService<IProviderConfigurationService>();

            var claim = await dbContext.Claims
                .Include(x => x.PipelineState)
                .FirstOrDefaultAsync(x => x.ProviderId == providerId && x.ClaimId == claimId, ct);

            if (claim is null)
            {
                _logger.LogWarning("Claim {ClaimId} was not found for provider {ProviderId}.", claimId, providerId);
                return;
            }

            var providerConfig = await providerConfigurationService.GetConfigurationAsync(providerId, ct);
            var pipelineState = EnsurePipelineState(claim, providerId);
            pipelineState.ProviderConfigSnapshot = JsonSerializer.Serialize(providerConfig, SerializerOptions);

            claim.Status = "PIPELINE_IN_PROGRESS";
            claim.UpdatedAt = DateTime.UtcNow;
            pipelineState.Status = "PIPELINE_IN_PROGRESS";
            AddLifecycleNotificationEvent(
                dbContext,
                claim,
                "PROCESSING_MILESTONE",
                "Claim processing has started.",
                claim.Status);
            await dbContext.SaveChangesAsync(ct);

            var completedSteps = DeserializeCompletedSteps(pipelineState.CompletedSteps);
            var persistedOutputs = DeserializeAgentOutputs(pipelineState.AgentOutputs);

            foreach (var step in _pipelineSteps)
            {
                if (completedSteps.Contains(step.Name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var context = BuildAgentContext(claim, providerConfig, persistedOutputs);
                var invocationResult = await InvokeAgentWithRetryAsync(step, scope.ServiceProvider, context, claim, ct);
                DisposeContextJson(context);

                if (!invocationResult.Success || invocationResult.SerializedOutput is null)
                {
                    await RouteToHumanReviewAsync(
                        scope.ServiceProvider,
                        dbContext,
                        claim,
                        pipelineState,
                        completedSteps,
                        persistedOutputs,
                        invocationResult.FailureReason ?? $"{step.Name} failed after retries.",
                        ct);
                    return;
                }

                completedSteps.Add(step.Name);
                persistedOutputs[step.Name] = invocationResult.SerializedOutput;

                dbContext.AgentOutputs.Add(new AgentOutput
                {
                    OutputId = Guid.NewGuid(),
                    ClaimId = claim.ClaimId,
                    AgentId = step.Name,
                    OutputPayload = invocationResult.SerializedOutput,
                    CreatedAt = DateTime.UtcNow,
                    SchemaVersion = "1.0"
                });

                pipelineState.CurrentStep = step.Name;
                pipelineState.CompletedSteps = JsonSerializer.Serialize(completedSteps, SerializerOptions);
                pipelineState.AgentOutputs = JsonSerializer.Serialize(persistedOutputs, SerializerOptions);
                claim.UpdatedAt = DateTime.UtcNow;

                AddLifecycleNotificationEvent(
                    dbContext,
                    claim,
                    "PROCESSING_MILESTONE",
                    $"Processing milestone completed: {step.Name}.",
                    claim.Status);

                if (string.Equals(step.Name, "DocumentAnalysisAgent", StringComparison.Ordinal) &&
                    TryGetBlockingMissingItems(invocationResult.SerializedOutput, out var blockingItems) &&
                    blockingItems.Count > 0)
                {
                    AddLifecycleNotificationEvent(
                        dbContext,
                        claim,
                        "INFO_REQUESTED",
                        "Additional documents or details are required to continue claim processing.",
                        claim.Status,
                        DateTime.UtcNow.AddDays(providerConfig.InformationRequestDeadlineDays),
                        blockingItems);
                }

                if (string.Equals(step.Name, "SettlementDecisionAgent", StringComparison.Ordinal))
                {
                    var recommendation = invocationResult.Recommendation ?? "UNKNOWN";
                    AddLifecycleNotificationEvent(
                        dbContext,
                        claim,
                        "DECISION_READY",
                        $"A settlement decision recommendation is available: {recommendation}.",
                        claim.Status);
                }

                await dbContext.SaveChangesAsync(ct);

                if (string.Equals(step.Name, "SettlementDecisionAgent", StringComparison.Ordinal) &&
                    string.Equals(invocationResult.Recommendation, "MANUAL_REVIEW", StringComparison.OrdinalIgnoreCase))
                {
                    await RouteToHumanReviewAsync(
                        scope.ServiceProvider,
                        dbContext,
                        claim,
                        pipelineState,
                        completedSteps,
                        persistedOutputs,
                        "Settlement decision recommended MANUAL_REVIEW.",
                        ct);
                    return;
                }
            }

            pipelineState.Status = "PIPELINE_COMPLETE";
            pipelineState.CompletedAt = DateTime.UtcNow;
            claim.Status = "PIPELINE_COMPLETE";
            claim.UpdatedAt = DateTime.UtcNow;
            AddLifecycleNotificationEvent(
                dbContext,
                claim,
                "PIPELINE_COMPLETED",
                "Claim processing is complete.",
                claim.Status);
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while processing claim {ClaimId} for provider {ProviderId}.", claimId, providerId);
        }
    }

    private static ClaimPipelineState EnsurePipelineState(Claim claim, string providerId)
    {
        if (claim.PipelineState is not null)
        {
            return claim.PipelineState;
        }

        var state = new ClaimPipelineState
        {
            ClaimId = claim.ClaimId,
            ProviderId = providerId,
            CurrentStep = "NONE",
            CompletedSteps = "[]",
            AgentOutputs = "{}",
            ProviderConfigSnapshot = "{}",
            Status = "PIPELINE_IN_PROGRESS",
            StartedAt = DateTime.UtcNow
        };

        claim.PipelineState = state;
        return state;
    }

    private static List<string> DeserializeCompletedSteps(string completedStepsJson)
    {
        if (string.IsNullOrWhiteSpace(completedStepsJson))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<string>>(completedStepsJson, SerializerOptions) ?? [];
    }

    private static Dictionary<string, string> DeserializeAgentOutputs(string agentOutputsJson)
    {
        if (string.IsNullOrWhiteSpace(agentOutputsJson))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        return JsonSerializer.Deserialize<Dictionary<string, string>>(agentOutputsJson, SerializerOptions)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static ClaimAgentContext BuildAgentContext(
        Claim claim,
        ProviderConfiguration providerConfig,
        IReadOnlyDictionary<string, string> outputs)
    {
        var upstreamOutputs = new Dictionary<string, JsonDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in outputs)
        {
            upstreamOutputs[item.Key] = JsonDocument.Parse(item.Value);
        }

        var identity = new ClaimsIdentity("orchestrator");
        identity.AddClaim(new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, "orchestrator-system"));
        identity.AddClaim(new System.Security.Claims.Claim("provider_id", claim.ProviderId));

        return new ClaimAgentContext
        {
            ClaimId = claim.ClaimId,
            ClaimRecord = claim,
            UpstreamOutputs = upstreamOutputs,
            ProviderConfig = providerConfig,
            UserIdentity = identity
        };
    }

    private static void DisposeContextJson(ClaimAgentContext context)
    {
        foreach (var output in context.UpstreamOutputs.Values)
        {
            output.Dispose();
        }
    }

    private async Task<AgentInvocationResult> InvokeAgentWithRetryAsync(
        PipelineStep step,
        IServiceProvider serviceProvider,
        ClaimAgentContext context,
        Claim claim,
        CancellationToken ct)
    {
        const int retries = 2;
        var attempts = retries + 1;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var activity = ActivitySource.StartActivity($"Agent:{step.Name}", ActivityKind.Internal);
            activity?.SetTag("agent.name", step.Name);
            activity?.SetTag("claim.id", claim.ClaimId.ToString());
            activity?.SetTag("provider.id", claim.ProviderId);
            activity?.SetTag("attempt", attempt);

            var stepStart = Stopwatch.GetTimestamp();
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.AgentTimeoutSeconds));

                var outputJson = await step.Executor(serviceProvider, context, claim, timeoutCts.Token);
                var recommendation = ValidateOutputSchema(step.Name, outputJson);

                activity?.SetTag("outcome", "success");
                activity?.SetTag("duration.ms", Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds);

                return AgentInvocationResult.SuccessResult(outputJson, recommendation);
            }
            catch (Exception ex)
            {
                activity?.SetTag("outcome", "error");
                activity?.SetTag("error.message", ex.Message);

                _logger.LogWarning(
                    ex,
                    "Agent {AgentName} failed for claim {ClaimId} on attempt {Attempt}/{TotalAttempts}.",
                    step.Name,
                    claim.ClaimId,
                    attempt,
                    attempts);

                if (attempt >= attempts)
                {
                    return AgentInvocationResult.FailedResult(ex.Message);
                }

                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        return AgentInvocationResult.FailedResult($"Agent {step.Name} failed.");
    }

    private static string? ValidateOutputSchema(string stepName, string serializedOutput)
    {
        using var document = JsonDocument.Parse(serializedOutput);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Agent output for {stepName} must be a JSON object.");
        }

        if (!string.Equals(stepName, "SettlementDecisionAgent", StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var propertyName in new[] { "Recommendation", "recommendation" })
        {
            if (document.RootElement.TryGetProperty(propertyName, out var recommendation) &&
                recommendation.ValueKind == JsonValueKind.String)
            {
                return recommendation.GetString();
            }
        }

        throw new InvalidOperationException("SettlementDecisionAgent output must include Recommendation.");
    }

    private async Task RouteToHumanReviewAsync(
        IServiceProvider serviceProvider,
        ClaimSettlementDbContext dbContext,
        Claim claim,
        ClaimPipelineState pipelineState,
        List<string> completedSteps,
        Dictionary<string, string> persistedOutputs,
        string reason,
        CancellationToken ct)
    {
        var providerConfigurationService = serviceProvider.GetRequiredService<IProviderConfigurationService>();
        var providerConfig = await providerConfigurationService.GetConfigurationAsync(claim.ProviderId, ct);
        var context = BuildAgentContext(claim, providerConfig, persistedOutputs);
        var humanReviewAgent = serviceProvider.GetRequiredService<HumanReviewAgent>();
        var humanReviewResult = await humanReviewAgent.InvokeAsync(context, new HumanReviewInput(reason), ct);
        DisposeContextJson(context);

        var serializedReviewOutput = JsonSerializer.Serialize(humanReviewResult, SerializerOptions);

        persistedOutputs["HumanReviewAgent"] = serializedReviewOutput;
        if (!completedSteps.Contains("HumanReviewAgent", StringComparer.OrdinalIgnoreCase))
        {
            completedSteps.Add("HumanReviewAgent");
        }

        dbContext.AgentOutputs.Add(new AgentOutput
        {
            OutputId = Guid.NewGuid(),
            ClaimId = claim.ClaimId,
            AgentId = "HumanReviewAgent",
            OutputPayload = serializedReviewOutput,
            CreatedAt = DateTime.UtcNow,
            SchemaVersion = "1.0"
        });

        pipelineState.CurrentStep = "HumanReviewAgent";
        pipelineState.Status = "MANUAL_REVIEW";
        pipelineState.CompletedSteps = JsonSerializer.Serialize(completedSteps, SerializerOptions);
        pipelineState.AgentOutputs = JsonSerializer.Serialize(persistedOutputs, SerializerOptions);
        claim.Status = "MANUAL_REVIEW";
        claim.UpdatedAt = DateTime.UtcNow;

        AddLifecycleNotificationEvent(
            dbContext,
            claim,
            "MANUAL_REVIEW_ROUTED",
            "Claim has been routed for manual adjuster review.",
            claim.Status);

        await dbContext.SaveChangesAsync(ct);
    }

    private static bool TryGetBlockingMissingItems(string? serializedOutput, out List<string> blockingItems)
    {
        blockingItems = [];
        if (string.IsNullOrWhiteSpace(serializedOutput))
        {
            return false;
        }

        using var document = JsonDocument.Parse(serializedOutput);
        if (!document.RootElement.TryGetProperty("BlockingMissingFields", out var blockingElement) &&
            !document.RootElement.TryGetProperty("blockingMissingFields", out blockingElement))
        {
            return false;
        }

        if (blockingElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        blockingItems = blockingElement
            .EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToList();

        return true;
    }

    private static void AddLifecycleNotificationEvent(
        ClaimSettlementDbContext dbContext,
        Claim claim,
        string eventType,
        string message,
        string claimStatus,
        DateTime? responseDeadlineUtc = null,
        IReadOnlyList<string>? missingItems = null)
    {
        dbContext.AgentOutputs.Add(new AgentOutput
        {
            OutputId = Guid.NewGuid(),
            ClaimId = claim.ClaimId,
            AgentId = "NotificationLifecycle",
            OutputPayload = JsonSerializer.Serialize(new
            {
                notificationEventType = eventType,
                message,
                claimStatus,
                responseDeadlineUtc,
                missingItems = missingItems ?? Array.Empty<string>(),
                eventTimestampUtc = DateTime.UtcNow
            }),
            CreatedAt = DateTime.UtcNow,
            SchemaVersion = "1.0"
        });
    }

    private sealed record PipelineStep(
        string Name,
        Func<IServiceProvider, ClaimAgentContext, Claim, CancellationToken, Task<string>> Executor);

    private sealed record AgentInvocationResult(bool Success, string? SerializedOutput, string? Recommendation, string? FailureReason)
    {
        public static AgentInvocationResult SuccessResult(string serializedOutput, string? recommendation)
            => new(true, serializedOutput, recommendation, null);

        public static AgentInvocationResult FailedResult(string reason)
            => new(false, null, null, reason);
    }

    private sealed class ProviderDispatchQueue
    {
        private readonly Channel<Guid> _channel;
        private readonly CancellationToken _stoppingToken;
        private readonly SemaphoreSlim _semaphore;
        private readonly Func<string, Guid, CancellationToken, Task> _executor;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<Guid, byte> _queuedOrRunningClaims;

        public ProviderDispatchQueue(
            string providerId,
            int maxConcurrency,
            Func<string, Guid, CancellationToken, Task> executor,
            ILogger logger,
            ConcurrentDictionary<Guid, byte> queuedOrRunningClaims,
            CancellationToken stoppingToken)
        {
            ProviderId = providerId;
            _stoppingToken = stoppingToken;
            _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            _executor = executor;
            _logger = logger;
            _queuedOrRunningClaims = queuedOrRunningClaims;
            _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

            _ = Task.Run(() => DispatchLoopAsync(maxConcurrency));
        }

        public string ProviderId { get; }

        public ChannelWriter<Guid> Writer => _channel.Writer;

        private async Task DispatchLoopAsync(int maxConcurrency)
        {
            _logger.LogInformation(
                "Initialized provider queue for {ProviderId} with max concurrency {MaxConcurrency}.",
                ProviderId,
                maxConcurrency);

            await foreach (var claimId in _channel.Reader.ReadAllAsync(_stoppingToken))
            {
                await _semaphore.WaitAsync(_stoppingToken);
                _ = ExecuteClaimAsync(claimId);
            }
        }

        private async Task ExecuteClaimAsync(Guid claimId)
        {
            try
            {
                await _executor(ProviderId, claimId, _stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider queue execution failed for claim {ClaimId}.", claimId);
            }
            finally
            {
                _queuedOrRunningClaims.TryRemove(claimId, out _);
                _semaphore.Release();
            }
        }
    }
}