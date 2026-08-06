using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Infrastructure.Observability;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ClaimSettlement.Orchestrator;

public sealed class InformationRequestReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InformationRequestReminderService> _logger;
    private readonly IAuditLogger _auditLogger;
    private readonly IClaimMetrics _claimMetrics;

    public InformationRequestReminderService(
        IServiceScopeFactory scopeFactory,
        ILogger<InformationRequestReminderService> logger,
        IAuditLogger auditLogger,
        IClaimMetrics claimMetrics)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _auditLogger = auditLogger;
        _claimMetrics = claimMetrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessRemindersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Information-request reminder cycle failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
        }
    }

    private async Task ProcessRemindersAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClaimSettlementDbContext>();

        var infoRequestEvents = await dbContext.AgentOutputs
            .AsNoTracking()
            .Where(x => x.AgentId == "NotificationLifecycle" && x.OutputPayload.Contains("\"notificationEventType\":\"INFO_REQUESTED\""))
            .OrderBy(x => x.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        if (infoRequestEvents.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var sourceEvent in infoRequestEvents)
        {
            if (!TryReadDeadline(sourceEvent.OutputPayload, out var deadlineUtc) || deadlineUtc is null)
            {
                continue;
            }

            var reminderDue = now >= deadlineUtc.Value.AddHours(-24) && now < deadlineUtc.Value;
            var timeoutDue = now >= deadlineUtc.Value;

            if (!reminderDue && !timeoutDue)
            {
                continue;
            }

            var stage = timeoutDue ? "INFO_TIMEOUT" : "INFO_REQUEST_REMINDER_24H";
            var alreadyEmitted = await dbContext.AgentOutputs
                .AsNoTracking()
                .AnyAsync(
                    x => x.ClaimId == sourceEvent.ClaimId
                        && x.AgentId == "NotificationReminder"
                        && x.OutputPayload.Contains(sourceEvent.OutputId.ToString())
                        && x.OutputPayload.Contains(stage),
                    ct);

            if (alreadyEmitted)
            {
                continue;
            }

            dbContext.AgentOutputs.Add(new AgentOutput
            {
                OutputId = Guid.NewGuid(),
                ClaimId = sourceEvent.ClaimId,
                AgentId = "NotificationReminder",
                OutputPayload = JsonSerializer.Serialize(new
                {
                    sourceOutputId = sourceEvent.OutputId,
                    notificationEventType = timeoutDue ? "INFO_TIMEOUT" : "INFO_REQUEST_REMINDER",
                    message = timeoutDue
                        ? "Requested claim information was not received before the deadline."
                        : "Reminder: additional claim information is due within 24 hours.",
                    responseDeadlineUtc = deadlineUtc,
                    eventTimestampUtc = now
                }),
                CreatedAt = now,
                SchemaVersion = "1.0"
            });

            if (timeoutDue)
            {
                var claim = await dbContext.Claims.FirstOrDefaultAsync(x => x.ClaimId == sourceEvent.ClaimId, ct);
                if (claim is not null)
                {
                    claim.Status = "INFO_TIMEOUT";
                    claim.UpdatedAt = now;
                    _claimMetrics.RecordClaimOutcome(claim.Status);

                    await _auditLogger.AppendAsync(new AuditLogEntry
                    {
                        ProviderId = claim.ProviderId,
                        EventType = "INFO_TIMEOUT",
                        ActorId = "info-reminder",
                        ActorType = "System",
                        ClaimId = claim.ClaimId,
                        Payload = new
                        {
                            claim.Status,
                            deadlineUtc,
                            timedOutAtUtc = now
                        }
                    }, ct);
                }
            }
        }

        await dbContext.SaveChangesAsync(ct);
    }

    private static bool TryReadDeadline(string payload, out DateTime? deadlineUtc)
    {
        deadlineUtc = null;

        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("responseDeadlineUtc", out var deadlineElement) ||
            deadlineElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = deadlineElement.GetString();
        if (!DateTime.TryParse(raw, out var parsed))
        {
            return false;
        }

        deadlineUtc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return true;
    }
}
