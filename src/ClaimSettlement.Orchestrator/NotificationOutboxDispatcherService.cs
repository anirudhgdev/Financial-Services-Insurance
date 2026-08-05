using ClaimSettlement.Agents.Models;
using ClaimSettlement.Agents.Pipeline;
using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace ClaimSettlement.Orchestrator;

public sealed class NotificationOutboxDispatcherService : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationOutboxDispatcherService> _logger;
    private DateTime _lastSeenUtc = DateTime.UtcNow.AddMinutes(-15);

    public NotificationOutboxDispatcherService(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationOutboxDispatcherService> logger)
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
                await DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification outbox dispatch failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task DispatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ClaimSettlementDbContext>();
        var notificationAgent = scope.ServiceProvider.GetRequiredService<NotificationAgent>();
        var providerConfigurationService = scope.ServiceProvider.GetRequiredService<IProviderConfigurationService>();

        var events = await dbContext.AgentOutputs
            .AsNoTracking()
            .Where(x => x.CreatedAt >= _lastSeenUtc && x.AgentId != "NotificationAgent" && x.OutputPayload.Contains("notificationEventType"))
            .OrderBy(x => x.CreatedAt)
            .Take(500)
            .ToListAsync(ct);

        if (events.Count == 0)
        {
            return;
        }

        var maxSeen = _lastSeenUtc;

        foreach (var output in events)
        {
            if (output.CreatedAt > maxSeen)
            {
                maxSeen = output.CreatedAt;
            }

            if (!TryParseEnvelope(output.OutputPayload, out var envelope))
            {
                continue;
            }

            var alreadyProcessed = await dbContext.AgentOutputs
                .AsNoTracking()
                .AnyAsync(
                    x => x.AgentId == "NotificationAgent"
                        && x.ClaimId == output.ClaimId
                        && x.OutputPayload.Contains(output.OutputId.ToString()),
                    ct);

            if (alreadyProcessed)
            {
                continue;
            }

            var claim = await dbContext.Claims
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ClaimId == output.ClaimId, ct);

            if (claim is null)
            {
                continue;
            }

            var providerConfig = await providerConfigurationService.GetConfigurationAsync(claim.ProviderId, ct);
            var context = BuildContext(claim, providerConfig);

            NotificationResult? notificationResult = null;
            try
            {
                notificationResult = await notificationAgent.InvokeAsync(
                    context,
                    new NotificationInput
                    {
                        ClaimId = claim.ClaimId,
                        ProviderId = claim.ProviderId,
                        EventType = envelope.EventType,
                        Message = envelope.Message,
                        EventTimestampUtc = output.CreatedAt,
                        RecipientUserId = claim.ClaimantId,
                        RecipientEmail = $"{claim.ClaimantId}@example.com",
                        ResponseDeadlineUtc = envelope.ResponseDeadlineUtc,
                        MissingItems = envelope.MissingItems
                    },
                    ct);
            }
            finally
            {
                DisposeContext(context);
            }

            if (notificationResult is null)
            {
                continue;
            }

            dbContext.AgentOutputs.Add(new AgentOutput
            {
                OutputId = Guid.NewGuid(),
                ClaimId = claim.ClaimId,
                AgentId = "NotificationAgent",
                OutputPayload = JsonSerializer.Serialize(new
                {
                    sourceOutputId = output.OutputId,
                    eventType = envelope.EventType,
                    result = notificationResult
                }),
                CreatedAt = DateTime.UtcNow,
                SchemaVersion = "1.0"
            });

            await dbContext.SaveChangesAsync(ct);
        }

        _lastSeenUtc = maxSeen;
    }

    private static bool TryParseEnvelope(string payload, out NotificationEnvelope envelope)
    {
        envelope = default;

        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("notificationEventType", out var eventTypeElement) ||
            eventTypeElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var eventType = eventTypeElement.GetString();
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return false;
        }

        var message = doc.RootElement.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
            ? messageElement.GetString() ?? $"Claim lifecycle event: {eventType}."
            : $"Claim lifecycle event: {eventType}.";

        DateTime? responseDeadlineUtc = null;
        if (doc.RootElement.TryGetProperty("responseDeadlineUtc", out var deadlineElement) && deadlineElement.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(deadlineElement.GetString(), out var parsedDeadline))
        {
            responseDeadlineUtc = DateTime.SpecifyKind(parsedDeadline, DateTimeKind.Utc);
        }

        var missingItems = new List<string>();
        if (doc.RootElement.TryGetProperty("missingItems", out var missingElement) && missingElement.ValueKind == JsonValueKind.Array)
        {
            missingItems = missingElement
                .EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToList();
        }

        envelope = new NotificationEnvelope(eventType, message, responseDeadlineUtc, missingItems);
        return true;
    }

    private static ClaimAgentContext BuildContext(Claim claim, ProviderConfiguration providerConfiguration)
    {
        var identity = new ClaimsIdentity("notification-dispatcher");
        identity.AddClaim(new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, "notification-dispatcher"));
        identity.AddClaim(new System.Security.Claims.Claim("provider_id", claim.ProviderId));

        return new ClaimAgentContext
        {
            ClaimId = claim.ClaimId,
            ClaimRecord = claim,
            UpstreamOutputs = new Dictionary<string, JsonDocument>(StringComparer.OrdinalIgnoreCase),
            ProviderConfig = providerConfiguration,
            UserIdentity = identity
        };
    }

    private static void DisposeContext(ClaimAgentContext context)
    {
        foreach (var output in context.UpstreamOutputs.Values)
        {
            output.Dispose();
        }
    }

    private readonly record struct NotificationEnvelope(
        string EventType,
        string Message,
        DateTime? ResponseDeadlineUtc,
        IReadOnlyList<string> MissingItems);
}
