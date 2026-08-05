using ClaimSettlement.Agents.Abstractions;
using ClaimSettlement.Agents.Models;

namespace ClaimSettlement.Agents.Pipeline;

public sealed class HumanReviewAgent : IClaimAgent<HumanReviewInput, HumanReviewResult>
{
    private readonly IHumanReviewQueueStore _queueStore;
    private readonly IReviewPackageAssembler _reviewPackageAssembler;

    public HumanReviewAgent(IHumanReviewQueueStore queueStore, IReviewPackageAssembler reviewPackageAssembler)
    {
        _queueStore = queueStore;
        _reviewPackageAssembler = reviewPackageAssembler;
    }

    public async Task<HumanReviewResult> InvokeAsync(ClaimAgentContext context, HumanReviewInput input, CancellationToken ct)
    {
        var queueEntry = await _queueStore.EnqueueAsync(context, input.Reason, ct);
        var reviewPackage = _reviewPackageAssembler.Build(context);

        var result = new HumanReviewResult
        {
            QueueStatus = queueEntry.PendingAssignment ? "PENDING_ASSIGNMENT" : "QUEUED",
            QueuedAtUtc = queueEntry.AssignedAtUtc,
            Reason = input.Reason,
            AssignedAdjusterId = queueEntry.AssignedAdjusterId,
            NextAssignmentRetryAtUtc = queueEntry.PendingAssignment ? queueEntry.AssignedAtUtc.AddMinutes(15) : null,
            NotificationRequired = true,
            NotificationEventType = queueEntry.PendingAssignment ? "CUSTOMER_DELAY_NOTICE" : "ADJUSTER_ASSIGNED",
            ReviewPackage = reviewPackage
        };

        return result;
    }
}