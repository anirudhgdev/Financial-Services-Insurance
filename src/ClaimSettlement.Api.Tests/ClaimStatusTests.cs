using ClaimSettlement.Api.Claims;
using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure.Observability;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;
using DomainClaim = ClaimSettlement.Domain.Entities.Claim;

namespace ClaimSettlement.Api.Tests;

public sealed class ClaimStatusTests
{
    [Fact]
    public async Task ReturnsPipelineProgressForProviderClaim()
    {
        await using var dbContext = BuildDbContext();
        var claimId = Guid.NewGuid();
        dbContext.Claims.Add(new DomainClaim
        {
            ClaimId = claimId,
            ProviderId = "provider-1",
            PolicyNumber = "POL-1",
            ClaimantId = "customer-1",
            DateOfLoss = DateTime.UtcNow.Date,
            ClaimType = "auto",
            LossAmount = 1200m,
            Status = "PROCESSING",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        dbContext.ClaimPipelineStates.Add(new ClaimPipelineState
        {
            ClaimId = claimId,
            ProviderId = "provider-1",
            CurrentStep = "Fraud assessment",
            CompletedSteps = "[\"Document analysis\",\"Policy validation\"]",
            AgentOutputs = "{}",
            ProviderConfigSnapshot = "{}",
            Status = "PROCESSING",
            StartedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var response = await CreateController(dbContext).GetClaimStatus(claimId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var status = Assert.IsType<ClaimStatusResponse>(ok.Value);
        Assert.Equal("Fraud assessment", status.CurrentStage);
        Assert.Equal(["Document analysis", "Policy validation"], status.CompletedStages);
        Assert.True(status.EstimatedMinutesRemaining > 0);
    }

    [Fact]
    public async Task DoesNotExposeAnotherProvidersClaim()
    {
        await using var dbContext = BuildDbContext();
        var claimId = Guid.NewGuid();
        dbContext.Claims.Add(new DomainClaim
        {
            ClaimId = claimId,
            ProviderId = "provider-2",
            PolicyNumber = "POL-2",
            ClaimantId = "customer-2",
            DateOfLoss = DateTime.UtcNow.Date,
            ClaimType = "auto",
            LossAmount = 500m,
            Status = "INTAKE_COMPLETE",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var response = await CreateController(dbContext).GetClaimStatus(claimId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(response.Result);
    }

    private static ClaimsController CreateController(ClaimSettlementDbContext dbContext) => new(
        new NoOpClaimIntakeService(),
        new TestProviderContextAccessor(),
        dbContext,
        new NoOpAuditLogger(),
        new NoOpClaimMetrics());

    private static ClaimSettlementDbContext BuildDbContext() => new(
        new DbContextOptionsBuilder<ClaimSettlementDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class TestProviderContextAccessor : IProviderContextAccessor
    {
        public string ProviderId => "provider-1";
        public IReadOnlyCollection<string> Roles => [AppRoles.Customer];
        public string UserId => "customer-1";
        public string? Email => "customer@contoso.com";
        public ClaimsIdentity Identity => new("test");
        public bool IsAuthenticated => true;
    }

    private sealed class NoOpClaimIntakeService : IClaimIntakeService
    {
        public Task<ClaimIntakeConversationResponse> ContinueConversationAsync(ClaimIntakeConversationRequest request, string providerId, string claimantId, CancellationToken ct) => throw new NotImplementedException();
        public Task<CompleteClaimIntakeResponse> CompleteAsync(CompleteClaimIntakeRequest request, string providerId, string claimantId, CancellationToken ct) => throw new NotImplementedException();
        public void RecordTokenUsage(string sessionId, string providerId, string claimantId, long? inputTokenCount, long? outputTokenCount) { }
        public Task<DocumentUploadResponse> UploadDocumentAsync(Guid claimId, IFormFile file, string providerId, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class NoOpAuditLogger : IAuditLogger
    {
        public Task AppendAsync(AuditLogEntry entry, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateAsync(Guid entryId, object payload, CancellationToken ct) => throw new InvalidOperationException();
        public Task DeleteAsync(Guid entryId, CancellationToken ct) => throw new InvalidOperationException();
    }

    private sealed class NoOpClaimMetrics : IClaimMetrics
    {
        public void RecordClaimOutcome(string outcome) { }
        public void RecordPipelineDuration(TimeSpan duration, string outcome) { }
        public void RecordFraudScore(decimal score) { }
        public void RecordAgentExecution(string agentName, bool success) { }
        public void RecordNotificationDelivery(string eventType, bool delivered) { }
    }
}