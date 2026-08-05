using ClaimSettlement.Agents.Models;
using ClaimSettlement.Agents.Pipeline;
using ClaimSettlement.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using System.Text.Json;
using Xunit;
using DomainClaim = ClaimSettlement.Domain.Entities.Claim;
using SecurityClaim = System.Security.Claims.Claim;

namespace ClaimSettlement.Agents.Tests;

public sealed class NotificationAgentTests
{
    [Fact]
    public async Task SuppressesDuplicateNotification_WhenMessageIdAlreadyProcessed()
    {
        var client = new RecordingNotificationClient();
        var dedup = new TestDedupStore();
        var deadLetter = new TestDeadLetterSink();
        var agent = BuildAgent(client, dedup, deadLetter);

        var claimId = Guid.NewGuid();
        var eventTimestamp = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc);
        var input = new NotificationInput
        {
            ClaimId = claimId,
            ProviderId = "provider-1",
            EventType = "INTAKE_CONFIRMED",
            Message = "Claim intake received.",
            EventTimestampUtc = eventTimestamp,
            RecipientEmail = "user@example.com"
        };

        var context = BuildContext();
        var first = await agent.InvokeAsync(context, input, CancellationToken.None);
        var second = await agent.InvokeAsync(context, input, CancellationToken.None);

        Assert.True(first.Delivered);
        Assert.False(first.DuplicateSuppressed);
        Assert.True(second.Delivered);
        Assert.True(second.DuplicateSuppressed);
        Assert.Equal(1, client.AttemptCount);
    }

    [Fact]
    public async Task RetriesWithBackoff_AndEventuallyDelivers()
    {
        var client = new FlakyNotificationClient(failuresBeforeSuccess: 2);
        var dedup = new TestDedupStore();
        var deadLetter = new TestDeadLetterSink();
        var agent = BuildAgent(client, dedup, deadLetter);

        var result = await agent.InvokeAsync(
            BuildContext(),
            new NotificationInput
            {
                ClaimId = Guid.NewGuid(),
                ProviderId = "provider-1",
                EventType = "PROCESSING_MILESTONE",
                Message = "Step completed.",
                EventTimestampUtc = DateTime.UtcNow,
                RecipientEmail = "user@example.com"
            },
            CancellationToken.None);

        Assert.True(result.Delivered);
        Assert.False(result.ServiceUnavailable);
        Assert.Equal(3, client.AttemptCount);
        Assert.Empty(deadLetter.Entries);
    }

    [Fact]
    public async Task WritesDeadLetter_WhenAllDeliveryAttemptsFail()
    {
        var client = new AlwaysFailNotificationClient();
        var dedup = new TestDedupStore();
        var deadLetter = new TestDeadLetterSink();
        var agent = BuildAgent(client, dedup, deadLetter);

        var result = await agent.InvokeAsync(
            BuildContext(),
            new NotificationInput
            {
                ClaimId = Guid.NewGuid(),
                ProviderId = "provider-1",
                EventType = "SLA_DELAY",
                Message = "Claim is delayed.",
                EventTimestampUtc = DateTime.UtcNow,
                RecipientEmail = "user@example.com"
            },
            CancellationToken.None);

        Assert.False(result.Delivered);
        Assert.True(result.ServiceUnavailable);
        Assert.Single(deadLetter.Entries);
    }

    [Fact]
    public async Task AppliesPreferenceFiltering_ForSmsWhenPhoneIsPresent()
    {
        var client = new RecordingNotificationClient();
        var dedup = new TestDedupStore();
        var deadLetter = new TestDeadLetterSink();
        var agent = BuildAgent(client, dedup, deadLetter);
        var context = BuildContext(preferredChannels: ["sms"]);

        var result = await agent.InvokeAsync(
            context,
            new NotificationInput
            {
                ClaimId = Guid.NewGuid(),
                ProviderId = "provider-1",
                EventType = "DECISION_READY",
                Message = "Decision available.",
                EventTimestampUtc = DateTime.UtcNow,
                RecipientPhone = "+14155552671",
                RecipientEmail = "user@example.com"
            },
            CancellationToken.None);

        Assert.True(result.Delivered);
        Assert.Contains("sms", result.Channels, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", result.Channels, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FallsBackToEmail_WhenOnlySmsPreferredButPhoneMissing()
    {
        var client = new RecordingNotificationClient();
        var dedup = new TestDedupStore();
        var deadLetter = new TestDeadLetterSink();
        var agent = BuildAgent(client, dedup, deadLetter);
        var context = BuildContext(preferredChannels: ["sms"]);

        var result = await agent.InvokeAsync(
            context,
            new NotificationInput
            {
                ClaimId = Guid.NewGuid(),
                ProviderId = "provider-1",
                EventType = "INFO_REQUESTED",
                Message = "Need additional information.",
                EventTimestampUtc = DateTime.UtcNow,
                RecipientEmail = "user@example.com"
            },
            CancellationToken.None);

        Assert.True(result.Delivered);
        Assert.Single(result.Channels);
        Assert.Equal("email", result.Channels[0]);
    }

    [Fact]
    public void GeneratesDeterministicMessageId_FromClaimEventAndTimestamp()
    {
        var generator = new HashNotificationIdGenerator();
        var timestamp = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var claimId = Guid.NewGuid();

        var id1 = generator.CreateMessageId(new NotificationInput
        {
            ClaimId = claimId,
            EventType = "INTAKE_CONFIRMED",
            EventTimestampUtc = timestamp
        });

        var id2 = generator.CreateMessageId(new NotificationInput
        {
            ClaimId = claimId,
            EventType = "INTAKE_CONFIRMED",
            EventTimestampUtc = timestamp
        });

        var id3 = generator.CreateMessageId(new NotificationInput
        {
            ClaimId = claimId,
            EventType = "INTAKE_CONFIRMED",
            EventTimestampUtc = timestamp.AddMinutes(1)
        });

        Assert.Equal(id1, id2);
        Assert.NotEqual(id1, id3);
    }

    private static NotificationAgent BuildAgent(
        INotificationServiceClient client,
        INotificationDedupStore dedupStore,
        IDeadLetterNotificationSink deadLetterSink)
    {
        return new NotificationAgent(
            client,
            new HashNotificationIdGenerator(),
            dedupStore,
            new NotificationPreferenceService(),
            deadLetterSink,
            NullLogger<NotificationAgent>.Instance);
    }

    private static ClaimAgentContext BuildContext(IReadOnlyList<string>? preferredChannels = null)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new SecurityClaim(ClaimTypes.NameIdentifier, "user-1"));

        var upstream = new Dictionary<string, JsonDocument>(StringComparer.OrdinalIgnoreCase);
        if (preferredChannels is not null)
        {
            upstream["customerPreferences"] = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                channels = preferredChannels
            }));
        }

        return new ClaimAgentContext
        {
            ClaimId = Guid.NewGuid(),
            ClaimRecord = new DomainClaim
            {
                ClaimId = Guid.NewGuid(),
                ProviderId = "provider-1",
                ClaimantId = "user-1",
                PolicyNumber = "POL-100",
                DateOfLoss = DateTime.UtcNow.AddDays(-2),
                ClaimType = "auto",
                LossAmount = 1200m,
                Status = "PIPELINE_IN_PROGRESS",
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                UpdatedAt = DateTime.UtcNow
            },
            UpstreamOutputs = upstream,
            ProviderConfig = new ProviderConfiguration
            {
                ProviderId = "provider-1",
                ProviderName = "Provider",
                ManualReviewFraudThreshold = 0.70m,
                ManualReviewClaimAmountThreshold = 5000m,
                DeduplicationWindowDays = 90,
                InformationRequestDeadlineDays = 7,
                AdjusterSlaPeriodHours = 48,
                SupportedClaimTypes = "[\"auto\",\"property\"]",
                SupportedNotificationChannels = "[\"email\",\"sms\"]",
                PipelineConcurrencyLimit = 100,
                IsActive = true,
                ClaimTypeMandatoryFields = "{}",
                CoverageMappingRules = "{}",
                ExclusionSets = "{}",
                AlwaysManualClaimTypes = "[]"
            },
            UserIdentity = identity
        };
    }

    private sealed class RecordingNotificationClient : INotificationServiceClient
    {
        public int AttemptCount { get; private set; }

        public Task<bool> SendAsync(NotificationDeliveryRequest request, CancellationToken ct)
        {
            AttemptCount++;
            return Task.FromResult(true);
        }
    }

    private sealed class FlakyNotificationClient : INotificationServiceClient
    {
        private readonly int _failuresBeforeSuccess;

        public FlakyNotificationClient(int failuresBeforeSuccess)
        {
            _failuresBeforeSuccess = failuresBeforeSuccess;
        }

        public int AttemptCount { get; private set; }

        public Task<bool> SendAsync(NotificationDeliveryRequest request, CancellationToken ct)
        {
            AttemptCount++;
            if (AttemptCount <= _failuresBeforeSuccess)
            {
                throw new TimeoutException("Transient failure.");
            }

            return Task.FromResult(true);
        }
    }

    private sealed class AlwaysFailNotificationClient : INotificationServiceClient
    {
        public Task<bool> SendAsync(NotificationDeliveryRequest request, CancellationToken ct)
            => throw new InvalidOperationException("Permanent notification service failure.");
    }

    private sealed class TestDedupStore : INotificationDedupStore
    {
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

        public Task<bool> TryReserveAsync(string messageId, CancellationToken ct)
        {
            if (_seen.Contains(messageId))
            {
                return Task.FromResult(false);
            }

            _seen.Add(messageId);
            return Task.FromResult(true);
        }

        public Task MarkDeliveredAsync(string messageId, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class TestDeadLetterSink : IDeadLetterNotificationSink
    {
        public List<string> Entries { get; } = [];

        public Task WriteAsync(NotificationInput input, string messageId, string reason, CancellationToken ct)
        {
            Entries.Add($"{messageId}|{reason}");
            return Task.CompletedTask;
        }
    }
}
