using ClaimSettlement.Api.Claims;
using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Xunit;
using DomainClaim = ClaimSettlement.Domain.Entities.Claim;

namespace ClaimSettlement.Api.Tests;

public sealed class AdjusterDecisionTests
{
    [Fact]
    public async Task RejectsShortRationale()
    {
        await using var dbContext = BuildDbContext();
        var controller = CreateController(dbContext);

        var claimId = Guid.NewGuid();
        dbContext.Claims.Add(BuildClaim(claimId));
        await dbContext.SaveChangesAsync();

        var response = await controller.SubmitAdjusterDecision(
            claimId,
            new AdjusterDecisionRequest { Decision = "APPROVE", Rationale = "too short" },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response.Result);
    }

    [Fact]
    public async Task PersistsDecisionAndAssignment()
    {
        await using var dbContext = BuildDbContext();
        var controller = CreateController(dbContext);

        var claimId = Guid.NewGuid();
        dbContext.Claims.Add(BuildClaim(claimId));
        dbContext.AdjusterAssignments.Add(new AdjusterAssignment
        {
            AssignmentId = Guid.NewGuid(),
            ClaimId = claimId,
            ProviderId = "provider-1",
            AdjusterId = "adjuster-1",
            AssignedAt = DateTime.UtcNow.AddHours(-2)
        });
        await dbContext.SaveChangesAsync();

        var response = await controller.SubmitAdjusterDecision(
            claimId,
            new AdjusterDecisionRequest
            {
                Decision = "APPROVE",
                Rationale = "Approved after verifying policy and supporting evidence.",
                SettlementOverride = 1250m
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var payload = Assert.IsType<AdjusterDecisionResponse>(ok.Value);

        Assert.Equal("APPROVE", payload.Decision);
        Assert.Equal(1250m, payload.SettlementOverride);

        var updatedClaim = await dbContext.Claims.FirstAsync(x => x.ClaimId == claimId);
        Assert.Equal("SETTLEMENT_APPROVED", updatedClaim.Status);

        var assignment = await dbContext.AdjusterAssignments
            .OrderByDescending(x => x.AssignedAt)
            .FirstAsync(x => x.ClaimId == claimId);
        Assert.Equal("APPROVE", assignment.Decision);
        Assert.NotNull(assignment.DecidedAt);

        var output = await dbContext.AgentOutputs.FirstOrDefaultAsync(x => x.ClaimId == claimId && x.AgentId == "AdjusterDecision");
        Assert.NotNull(output);
    }

    private static ClaimsController CreateController(ClaimSettlementDbContext dbContext)
    {
        return new ClaimsController(
            new NoOpClaimIntakeService(),
            new TestProviderContextAccessor(),
            dbContext);
    }

    private static ClaimSettlementDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<ClaimSettlementDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ClaimSettlementDbContext(options);
    }

    private static DomainClaim BuildClaim(Guid claimId)
    {
        return new DomainClaim
        {
            ClaimId = claimId,
            ProviderId = "provider-1",
            PolicyNumber = "POL-1",
            ClaimantId = "user-1",
            DateOfLoss = DateTime.UtcNow.Date.AddDays(-2),
            ClaimType = "auto",
            LossAmount = 1200m,
            Status = "MANUAL_REVIEW_ASSIGNED",
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            UpdatedAt = DateTime.UtcNow.AddDays(-2)
        };
    }

    private sealed class TestProviderContextAccessor : IProviderContextAccessor
    {
        public string ProviderId => "provider-1";

        public IReadOnlyCollection<string> Roles => [AppRoles.Adjuster];

        public string UserId => "adjuster-1";

        public string? Email => "adjuster@contoso.com";

        public ClaimsIdentity Identity => new("test");

        public bool IsAuthenticated => true;
    }

    private sealed class NoOpClaimIntakeService : IClaimIntakeService
    {
        public Task<ClaimIntakeConversationResponse> ContinueConversationAsync(ClaimIntakeConversationRequest request, string providerId, string claimantId, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<CompleteClaimIntakeResponse> CompleteAsync(CompleteClaimIntakeRequest request, string providerId, string claimantId, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<DocumentUploadResponse> UploadDocumentAsync(Guid claimId, IFormFile file, string providerId, CancellationToken ct)
            => throw new NotImplementedException();
    }
}
