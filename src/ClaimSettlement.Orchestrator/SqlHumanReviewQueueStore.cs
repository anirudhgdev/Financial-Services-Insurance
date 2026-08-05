using ClaimSettlement.Agents.Models;
using ClaimSettlement.Agents.Pipeline;
using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClaimSettlement.Orchestrator;

public sealed class SqlHumanReviewQueueStore : IHumanReviewQueueStore
{
    private readonly ClaimSettlementDbContext _dbContext;

    public SqlHumanReviewQueueStore(ClaimSettlementDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HumanReviewQueueEntry> EnqueueAsync(ClaimAgentContext context, string reason, CancellationToken ct)
    {
        var claim = await _dbContext.Claims
            .FirstOrDefaultAsync(x => x.ClaimId == context.ClaimId && x.ProviderId == context.ProviderConfig.ProviderId, ct);

        if (claim is null)
        {
            throw new InvalidOperationException($"Claim {context.ClaimId} was not found for provider {context.ProviderConfig.ProviderId}.");
        }

        var assignedAdjuster = await SelectAdjusterAsync(context.ProviderConfig.ProviderId, claim.ClaimType, ct);
        var now = DateTime.UtcNow;

        var assignment = new AdjusterAssignment
        {
            AssignmentId = Guid.NewGuid(),
            ClaimId = claim.ClaimId,
            ProviderId = claim.ProviderId,
            AdjusterId = assignedAdjuster ?? string.Empty,
            AssignedAt = now,
            Decision = null,
            Rationale = null,
            SettlementOverride = null
        };

        _dbContext.AdjusterAssignments.Add(assignment);

        if (assignedAdjuster is null)
        {
            claim.Status = "MANUAL_REVIEW_PENDING";
        }
        else
        {
            claim.Status = "MANUAL_REVIEW_ASSIGNED";
        }

        claim.UpdatedAt = now;

        _dbContext.AgentOutputs.Add(new AgentOutput
        {
            OutputId = Guid.NewGuid(),
            ClaimId = claim.ClaimId,
            AgentId = "HumanReviewQueue",
            OutputPayload = System.Text.Json.JsonSerializer.Serialize(new
            {
                reason,
                assignedAdjuster,
                queuedAtUtc = now,
                pendingAssignment = assignedAdjuster is null,
                notificationEventType = "HUMAN_REVIEW_ASSIGNED",
                message = assignedAdjuster is null
                    ? "Your claim requires manual review and is waiting for adjuster assignment."
                    : "Your claim has been assigned to an adjuster for manual review.",
                responseDeadlineUtc = now.AddHours(context.ProviderConfig.AdjusterSlaPeriodHours)
            }),
            CreatedAt = now,
            SchemaVersion = "1.0"
        });

        await _dbContext.SaveChangesAsync(ct);

        return new HumanReviewQueueEntry
        {
            ClaimId = claim.ClaimId,
            ProviderId = claim.ProviderId,
            AssignedAdjusterId = assignedAdjuster,
            AssignedAtUtc = now,
            PendingAssignment = assignedAdjuster is null
        };
    }

    private async Task<string?> SelectAdjusterAsync(string providerId, string claimType, CancellationToken ct)
    {
        if (string.Equals(claimType, "unassigned", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var windowStart = DateTime.UtcNow.AddDays(-30);
        var assignmentLoads = await _dbContext.AdjusterAssignments
            .AsNoTracking()
            .Where(x => x.ProviderId == providerId && x.AssignedAt >= windowStart)
            .GroupBy(x => x.AdjusterId)
            .Select(group => new { AdjusterId = group.Key, Count = group.Count() })
            .OrderBy(x => x.Count)
            .ThenBy(x => x.AdjusterId)
            .ToListAsync(ct);

        if (assignmentLoads.Count > 0)
        {
            return assignmentLoads[0].AdjusterId;
        }

        return claimType switch
        {
            "auto" => "adjuster-auto-1",
            "property" => "adjuster-property-1",
            "health" => "adjuster-health-1",
            _ => "adjuster-general-1"
        };
    }
}
