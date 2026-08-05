using ClaimSettlement.Agents.Abstractions;
using ClaimSettlement.Agents.Models;
using Microsoft.Extensions.Logging;

namespace ClaimSettlement.Agents.Pipeline;

public sealed class NotificationAgent : IClaimAgent<NotificationInput, NotificationResult>
{

    private readonly INotificationServiceClient _notificationClient;
    private readonly INotificationIdGenerator _idGenerator;
    private readonly INotificationDedupStore _dedupStore;
    private readonly INotificationPreferenceService _preferenceService;
    private readonly IDeadLetterNotificationSink _deadLetterSink;
    private readonly ILogger<NotificationAgent> _logger;

    public NotificationAgent(
        INotificationServiceClient notificationClient,
        INotificationIdGenerator idGenerator,
        INotificationDedupStore dedupStore,
        INotificationPreferenceService preferenceService,
        IDeadLetterNotificationSink deadLetterSink,
        ILogger<NotificationAgent> logger)
    {
        _notificationClient = notificationClient;
        _idGenerator = idGenerator;
        _dedupStore = dedupStore;
        _preferenceService = preferenceService;
        _deadLetterSink = deadLetterSink;
        _logger = logger;
    }

    public async Task<NotificationResult> InvokeAsync(ClaimAgentContext context, NotificationInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var channels = _preferenceService.ResolveChannels(context, input);
        var messageId = _idGenerator.CreateMessageId(input);

        var reserved = await _dedupStore.TryReserveAsync(messageId, ct);
        if (!reserved)
        {
            return new NotificationResult
            {
                MessageId = messageId,
                EventType = input.EventType,
                Channels = channels,
                Delivered = true,
                DuplicateSuppressed = true,
                DeliveredAtUtc = DateTime.UtcNow
            };
        }

        var request = new NotificationDeliveryRequest
        {
            MessageId = messageId,
            EventType = input.EventType,
            Message = input.Message,
            Channels = channels,
            RecipientEmail = input.RecipientEmail,
            RecipientPhone = input.RecipientPhone
        };

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                var delivered = await _notificationClient.SendAsync(request, ct);
                if (delivered)
                {
                    await _dedupStore.MarkDeliveredAsync(messageId, ct);
                    return new NotificationResult
                    {
                        MessageId = messageId,
                        EventType = input.EventType,
                        Channels = channels,
                        Delivered = true,
                        DeliveredAtUtc = DateTime.UtcNow
                    };
                }
            }
            catch (Exception ex) when (attempt < 3)
            {
                _logger.LogWarning(ex, "Notification delivery failed for message {MessageId} on attempt {Attempt}.", messageId, attempt);
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification delivery exhausted retries for message {MessageId}.", messageId);
                await _deadLetterSink.WriteAsync(input, messageId, ex.Message, ct);
                return new NotificationResult
                {
                    MessageId = messageId,
                    EventType = input.EventType,
                    Channels = channels,
                    Delivered = false,
                    ServiceUnavailable = true,
                    FailureReason = ex.Message,
                    DeliveredAtUtc = DateTime.UtcNow
                };
            }
        }

        await _deadLetterSink.WriteAsync(input, messageId, "Unknown delivery failure.", ct);
        return new NotificationResult
        {
            MessageId = messageId,
            EventType = input.EventType,
            Channels = channels,
            Delivered = false,
            ServiceUnavailable = true,
            FailureReason = "Unknown delivery failure.",
            DeliveredAtUtc = DateTime.UtcNow
        };
    }
}
