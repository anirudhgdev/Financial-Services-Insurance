using ClaimSettlement.Api.Evaluation;
using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ClaimSettlement.Api.Tests;

public sealed class EvaluationRunsControllerTests
{
    [Fact]
    public async Task ReturnsResultsForClaimInCallersRunAndProvider()
    {
        await using var dbContext = BuildDbContext();
        var run = new EvaluationRun
        {
            RunId = Guid.NewGuid(),
            ProviderId = "provider-1",
            CreatedByUserId = "runner-1",
            DatasetVersion = "v1",
            CreatedAtUtc = DateTime.UtcNow
        };
        var claim = new Claim
        {
            ClaimId = Guid.NewGuid(),
            ProviderId = "provider-1",
            ClaimantId = "runner-1",
            PolicyNumber = "POL-1",
            DateOfLoss = DateTime.UtcNow.AddDays(-1),
            ClaimType = "auto",
            LossAmount = 1200m,
            Status = "SETTLEMENT_APPROVED",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            EvaluationRunId = run.RunId,
            AgentOutputs =
            [
                new AgentOutput
                {
                    OutputId = Guid.NewGuid(),
                    AgentId = "FraudDetectionAgent",
                    OutputPayload = "{\"Verdict\":\"FRAUD_LOW\",\"RiskScore\":0.1}",
                    CreatedAt = DateTime.UtcNow,
                    SchemaVersion = "1.0"
                }
            ]
        };
        dbContext.Add(run);
        dbContext.Add(claim);
        await dbContext.SaveChangesAsync();
        var controller = new EvaluationRunsController(dbContext, new TestProviderContextAccessor("provider-1", "runner-1"));

        var response = await controller.GetClaimResults(run.RunId, claim.ClaimId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var results = Assert.IsType<EvaluationClaimResultsResponse>(ok.Value);
        Assert.Equal(run.RunId, results.RunId);
        Assert.Equal(claim.ClaimId, results.ClaimId);
        Assert.Single(results.AgentOutputs);
    }

    [Fact]
    public async Task HidesRunCreatedByAnotherRunner()
    {
        await using var dbContext = BuildDbContext();
        var run = new EvaluationRun
        {
            RunId = Guid.NewGuid(),
            ProviderId = "provider-1",
            CreatedByUserId = "runner-1",
            DatasetVersion = "v1",
            CreatedAtUtc = DateTime.UtcNow
        };
        dbContext.Add(run);
        await dbContext.SaveChangesAsync();
        var controller = new EvaluationRunsController(dbContext, new TestProviderContextAccessor("provider-1", "runner-2"));

        var response = await controller.GetClaimResults(run.RunId, Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(response.Result);
    }

    private static ClaimSettlementDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<ClaimSettlementDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ClaimSettlementDbContext(options);
    }

    private sealed class TestProviderContextAccessor : IProviderContextAccessor
    {
        private readonly string _providerId;
        private readonly string _userId;

        public TestProviderContextAccessor(string providerId, string userId)
        {
            _providerId = providerId;
            _userId = userId;
        }

        public string ProviderId => _providerId;
        public IReadOnlyCollection<string> Roles => [AppRoles.EvaluationRunner];
        public string UserId => _userId;
        public string? Email => null;
        public System.Security.Claims.ClaimsIdentity Identity => new("test");
        public bool IsAuthenticated => true;
    }
}