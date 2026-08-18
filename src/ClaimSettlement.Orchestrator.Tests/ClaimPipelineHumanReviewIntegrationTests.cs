using ClaimSettlement.Agents;
using ClaimSettlement.Agents.Models;
using ClaimSettlement.Agents.Pipeline;
using ClaimSettlement.Api.Claims;
using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure.Azure;
using ClaimSettlement.Infrastructure.Observability;
using ClaimSettlement.Infrastructure.Persistence;
using ClaimSettlement.Orchestrator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace ClaimSettlement.Orchestrator.Tests;

public sealed class ClaimPipelineHumanReviewIntegrationTests
{
    [Fact]
    public async Task RoutesHighFraudClaimToAdjusterThenRecordsDecisionAndNotification()
    {
        const string providerId = "provider-1";
        const string claimantId = "user-1";
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await using var serviceProvider = BuildServiceProvider();
        var dbContext = serviceProvider.GetRequiredService<ClaimSettlementDbContext>();
        var intakeService = serviceProvider.GetRequiredService<IClaimIntakeService>();

        var evaluationRun = new EvaluationRun
        {
            RunId = Guid.NewGuid(),
            ProviderId = providerId,
            CreatedByUserId = claimantId,
            DatasetVersion = "local-human-review",
            CreatedAtUtc = DateTime.UtcNow
        };
        dbContext.EvaluationRuns.Add(evaluationRun);
        dbContext.ProviderConfigurations.Add(new ProviderConfiguration
        {
            ProviderId = providerId,
            ProviderName = "Local review provider",
            ManualReviewFraudThreshold = 0.70m,
            ManualReviewClaimAmountThreshold = 5000m,
            DeduplicationWindowDays = 90,
            InformationRequestDeadlineDays = 7,
            AdjusterSlaPeriodHours = 48,
            PipelineConcurrencyLimit = 100,
            IsActive = true
        });
        await dbContext.SaveChangesAsync();

        var conversation = await intakeService.ContinueConversationAsync(new ClaimIntakeConversationRequest
        {
            EvaluationRunId = evaluationRun.RunId,
            Fields = new Dictionary<string, string>
            {
                ["PolicyNumber"] = "POL-HIGH-FRAUD-1",
                ["ClaimantName"] = "Local Review Customer",
                ["DateOfLoss"] = DateTime.UtcNow.AddDays(-45).ToString("O"),
                ["ClaimType"] = "auto",
                ["DescriptionOfLoss"] = "Vehicle damage requiring specialist review.",
                ["LossAmount"] = "10000",
                ["ContactInformation"] = "customer@example.test"
            }
        }, providerId, claimantId, CancellationToken.None);

        var intake = await intakeService.CompleteAsync(
            new CompleteClaimIntakeRequest { SessionId = conversation.SessionId },
            providerId,
            claimantId,
            CancellationToken.None);
        var claimId = Assert.IsType<Guid>(intake.ClaimId);
        var claimRecord = await dbContext.Claims.SingleAsync(item => item.ClaimId == claimId);
        claimRecord.CreatedAt = DateTime.UtcNow.AddDays(-90);

        dbContext.ClaimPipelineStates.Add(new ClaimPipelineState
        {
            ClaimId = claimId,
            ProviderId = providerId,
            CurrentStep = "NONE",
            CompletedSteps = "[]",
            AgentOutputs = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["uploadedDocuments"] = JsonSerializer.Serialize(new
                {
                    documents = new[]
                    {
                        new
                        {
                            documentId = "local-high-fraud-document",
                            confidence = 0.95m,
                            text = "PolicyNumber:POL-HIGH-FRAUD-1;DateOfLoss:2026-06-28;ClaimType:auto;LossAmount:10000;DescriptionOfLoss:Vehicle damage;ClaimantName:Local Review Customer"
                        }
                    }
                })
            }),
            ProviderConfigSnapshot = "{}",
            Status = "INTAKE_COMPLETE",
            StartedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var orchestrator = new ClaimPipelineOrchestrator(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OrchestratorOptions()),
            NullLogger<ClaimPipelineOrchestrator>.Instance,
            serviceProvider.GetRequiredService<IClaimMetrics>());

        await orchestrator.ProcessClaimAsync(providerId, claimId, CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var claim = await dbContext.Claims
            .Include(item => item.AgentOutputs)
            .Include(item => item.AdjusterAssignments)
            .SingleAsync(item => item.ClaimId == claimId);
        var fraudOutput = Assert.Single(claim.AgentOutputs, output => output.AgentId == "FraudDetectionAgent");
        var settlementOutput = Assert.Single(claim.AgentOutputs, output => output.AgentId == "SettlementDecisionAgent");
        var fraud = JsonSerializer.Deserialize<FraudDetectionResult>(fraudOutput.OutputPayload, jsonOptions)!;
        var settlement = JsonSerializer.Deserialize<SettlementDecisionResult>(settlementOutput.OutputPayload, jsonOptions)!;
        var assignment = await dbContext.AdjusterAssignments.SingleAsync(item => item.ClaimId == claimId);
        Assert.Equal("FRAUD_HIGH", fraud.Verdict);
        Assert.Equal("MANUAL_REVIEW", settlement.Recommendation);
        Assert.Equal("MANUAL_REVIEW", claim.Status);
        Assert.Equal("adjuster-auto-1", assignment.AdjusterId);
        Assert.Null(assignment.DecidedAt);
        Assert.Contains(claim.AgentOutputs, output => output.AgentId == "HumanReviewQueue" && output.OutputPayload.Contains("HUMAN_REVIEW_ASSIGNED"));

        var controller = new ClaimsController(
            intakeService,
            serviceProvider.GetRequiredService<IProviderContextAccessor>(),
            dbContext,
            serviceProvider.GetRequiredService<IAuditLogger>(),
            serviceProvider.GetRequiredService<IClaimMetrics>());
        var response = await controller.SubmitAdjusterDecision(
            claimId,
            new AdjusterDecisionRequest
            {
                Decision = "APPROVE",
                Rationale = "The adjuster verified the evidence and approved the settlement.",
                SettlementOverride = 9500m
            },
            CancellationToken.None);

        var decision = Assert.IsType<OkObjectResult>(response.Result).Value;
        Assert.IsType<AdjusterDecisionResponse>(decision);

        dbContext.ChangeTracker.Clear();
        claim = await dbContext.Claims
            .Include(item => item.AgentOutputs)
            .Include(item => item.AdjusterAssignments)
            .SingleAsync(item => item.ClaimId == claimId);
        assignment = await dbContext.AdjusterAssignments.SingleAsync(item => item.ClaimId == claimId);
        Assert.Equal("SETTLEMENT_APPROVED", claim.Status);
        Assert.Equal("APPROVE", assignment.Decision);
        Assert.Equal(9500m, assignment.SettlementOverride);
        Assert.NotNull(assignment.DecidedAt);
        Assert.Contains(claim.AgentOutputs, output => output.AgentId == "AdjusterDecision" && output.OutputPayload.Contains("ADJUSTER_DECISION_ISSUED"));
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        var databaseRoot = new InMemoryDatabaseRoot();
        services.AddDbContext<ClaimSettlementDbContext>(options => options.UseInMemoryDatabase(databaseName, databaseRoot));
        services.AddMemoryCache();
        services.AddMetrics();
        services.AddSingleton<IProviderSqlSessionContext, ProviderSqlSessionContext>();
        services.AddScoped<IProviderConfigurationService, ProviderConfigurationService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddSingleton<IClaimMetrics, ClaimMetrics>();
        services.AddClaimSettlementAgents(new ConfigurationBuilder().Build());
        services.RemoveAll<IHumanReviewQueueStore>();
        services.AddScoped<IHumanReviewQueueStore, SqlHumanReviewQueueStore>();
        services.AddSingleton<IProviderContextAccessor>(new TestProviderContextAccessor());
        services.AddScoped<IClaimIntakeValidationService, ClaimIntakeValidationService>();
        services.AddScoped<IClaimDuplicateGuard, ClaimDuplicateGuard>();
        services.AddScoped<IDocumentUploadPolicy, DocumentUploadPolicy>();
        services.AddScoped<IClaimIntakeService, ClaimIntakeService>();
        services.Configure<AzureStorageOptions>(_ => { });
        return services.BuildServiceProvider();
    }

    private sealed class TestProviderContextAccessor : IProviderContextAccessor
    {
        public string ProviderId => "provider-1";
        public IReadOnlyCollection<string> Roles => [AppRoles.Adjuster, AppRoles.EvaluationRunner];
        public string UserId => "adjuster-auto-1";
        public string? Email => "adjuster@example.test";
        public ClaimsIdentity Identity => new("test");
        public bool IsAuthenticated => true;
    }
}