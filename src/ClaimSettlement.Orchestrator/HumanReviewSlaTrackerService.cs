using ClaimSettlement.Agents.Pipeline;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClaimSettlement.Orchestrator;

public sealed class HumanReviewSlaTrackerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HumanReviewSlaTrackerService> _logger;

    public HumanReviewSlaTrackerService(IServiceScopeFactory scopeFactory, ILogger<HumanReviewSlaTrackerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateSlaAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed during human-review SLA evaluation.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task EvaluateSlaAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClaimSettlementDbContext>();
        var evaluator = scope.ServiceProvider.GetRequiredService<IHumanReviewSlaEvaluator>();

        var assignments = await dbContext.AdjusterAssignments
            .AsNoTracking()
            .Where(x => !x.DecidedAt.HasValue)
            .ToListAsync(ct);

        if (assignments.Count == 0)
        {
            return;
        }

        var providerSlaMap = await dbContext.ProviderConfigurations
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToDictionaryAsync(x => x.ProviderId, x => x.AdjusterSlaPeriodHours, ct);

        var now = DateTime.UtcNow;
        var actions = new List<HumanReviewSlaAction>();

        foreach (var providerGroup in assignments.GroupBy(x => x.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            var slaHours = providerSlaMap.TryGetValue(providerGroup.Key, out var configured)
                ? configured
                : 48;

            actions.AddRange(evaluator.Evaluate(providerGroup.ToList(), now, slaHours));
        }

        if (actions.Count == 0)
        {
            return;
        }

        var breachedClaimIds = actions.Select(x => x.ClaimId).Distinct().ToList();
        var breachedClaims = await dbContext.Claims
            .Where(x => breachedClaimIds.Contains(x.ClaimId))
            .ToListAsync(ct);

        foreach (var claim in breachedClaims)
        {
            claim.Status = "SLA_BREACHED";
            claim.UpdatedAt = now;

            dbContext.AgentOutputs.Add(new ClaimSettlement.Domain.Entities.AgentOutput
            {
                OutputId = Guid.NewGuid(),
                ClaimId = claim.ClaimId,
                AgentId = "HumanReviewSlaTracker",
                OutputPayload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    eventType = "SLA_BREACHED",
                    updatedAtUtc = now,
                    status = "SLA_BREACHED"
                }),
                CreatedAt = now,
                SchemaVersion = "1.0"
            });
        }

        await dbContext.SaveChangesAsync(ct);
    }
}
