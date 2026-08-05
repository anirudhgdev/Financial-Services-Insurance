using ClaimSettlement.Agents.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaimSettlement.Agents.Pipeline;

public sealed class NotificationDeliveryRequest
{
    public required string MessageId { get; init; }

    public required string EventType { get; init; }

    public required string Message { get; init; }

    public required IReadOnlyList<string> Channels { get; init; }

    public string? RecipientEmail { get; init; }

    public string? RecipientPhone { get; init; }
}

public interface INotificationServiceClient
{
    Task<bool> SendAsync(NotificationDeliveryRequest request, CancellationToken ct);
}

public sealed class SimulatedNotificationServiceClient : INotificationServiceClient
{
    public Task<bool> SendAsync(NotificationDeliveryRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (request.EventType.Contains("SIMULATE_FAILURE", StringComparison.OrdinalIgnoreCase))
        {
            throw new TimeoutException("Simulated notification service failure.");
        }

        return Task.FromResult(true);
    }
}

public interface INotificationIdGenerator
{
    string CreateMessageId(NotificationInput input);
}

public sealed class HashNotificationIdGenerator : INotificationIdGenerator
{
    public string CreateMessageId(NotificationInput input)
    {
        var payload = $"{input.ClaimId:N}|{input.EventType}|{input.EventTimestampUtc:O}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes)[..24];
    }
}

public interface INotificationDedupStore
{
    Task<bool> TryReserveAsync(string messageId, CancellationToken ct);

    Task MarkDeliveredAsync(string messageId, CancellationToken ct);
}

public sealed class InMemoryNotificationDedupStore : INotificationDedupStore
{
    private static readonly HashSet<string> Delivered = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Sync = new();

    public Task<bool> TryReserveAsync(string messageId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (Sync)
        {
            if (Delivered.Contains(messageId))
            {
                return Task.FromResult(false);
            }

            Delivered.Add(messageId);
            return Task.FromResult(true);
        }
    }

    public Task MarkDeliveredAsync(string messageId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

public interface INotificationPreferenceService
{
    IReadOnlyList<string> ResolveChannels(ClaimAgentContext context, NotificationInput input);
}

public sealed class NotificationPreferenceService : INotificationPreferenceService
{
    public IReadOnlyList<string> ResolveChannels(ClaimAgentContext context, NotificationInput input)
    {
        var supported = ParseChannels(context.ProviderConfig.SupportedNotificationChannels);
        var preferred = ExtractPreferredChannels(context.UpstreamOutputs);

        var channels = preferred.Count == 0 ? supported : supported.Intersect(preferred, StringComparer.OrdinalIgnoreCase).ToList();
        if (channels.Count == 0)
        {
            channels = ["email"];
        }

        if (!string.IsNullOrWhiteSpace(input.RecipientPhone) && channels.Contains("sms", StringComparer.OrdinalIgnoreCase))
        {
            return channels;
        }

        if (channels.Count == 1 && string.Equals(channels[0], "sms", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(input.RecipientPhone))
        {
            return ["email"];
        }

        return channels;
    }

    private static List<string> ParseChannels(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "[]")
        {
            return ["email"];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            return parsed
                .Where(x => string.Equals(x, "email", StringComparison.OrdinalIgnoreCase) || string.Equals(x, "sms", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return ["email"];
        }
    }

    private static List<string> ExtractPreferredChannels(IReadOnlyDictionary<string, JsonDocument> upstreamOutputs)
    {
        if (!upstreamOutputs.TryGetValue("customerPreferences", out var doc) ||
            !doc.RootElement.TryGetProperty("channels", out var channelsElement) ||
            channelsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return channelsElement
            .EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public interface IDeadLetterNotificationSink
{
    Task WriteAsync(NotificationInput input, string messageId, string reason, CancellationToken ct);
}

public sealed class InMemoryDeadLetterNotificationSink : IDeadLetterNotificationSink
{
    private static readonly List<string> DeadLetters = [];
    private static readonly object Sync = new();

    public Task WriteAsync(NotificationInput input, string messageId, string reason, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (Sync)
        {
            DeadLetters.Add($"{DateTime.UtcNow:O}|{messageId}|{input.EventType}|{reason}");
        }

        return Task.CompletedTask;
    }
}

public interface INotificationEventFactory
{
    NotificationInput Build(ClaimSettlement.Domain.Entities.Claim claim, string eventType, string message, DateTime? deadlineUtc = null, IReadOnlyList<string>? missingItems = null);
}

public sealed class NotificationEventFactory : INotificationEventFactory
{
    public NotificationInput Build(ClaimSettlement.Domain.Entities.Claim claim, string eventType, string message, DateTime? deadlineUtc = null, IReadOnlyList<string>? missingItems = null)
    {
        return new NotificationInput
        {
            ClaimId = claim.ClaimId,
            ProviderId = claim.ProviderId,
            EventType = eventType,
            Message = message,
            EventTimestampUtc = DateTime.UtcNow,
            RecipientUserId = claim.ClaimantId,
            RecipientEmail = $"{claim.ClaimantId}@example.com",
            ResponseDeadlineUtc = deadlineUtc,
            MissingItems = missingItems ?? Array.Empty<string>()
        };
    }
}
