using ClaimSettlement.Agents;
using ClaimSettlement.Agents.Models;
using ClaimSettlement.Api.Claims;
using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure.Azure;
using ClaimSettlement.Infrastructure.Observability;
using ClaimSettlement.Infrastructure.Persistence;
using ClaimSettlement.Orchestrator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace ClaimSettlement.Orchestrator.Tests;

public sealed class ClaimPipelineHappyPathIntegrationTests
{
    [Fact]
    public async Task CompletesIntakeThroughSettlementAndEmitsLifecycleNotifications()
    {
        var providerId = "provider-1";
        var claimantId = "user-1";
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        await using var serviceProvider = BuildServiceProvider();
        var dbContext = serviceProvider.GetRequiredService<ClaimSettlementDbContext>();
        var intakeService = serviceProvider.GetRequiredService<IClaimIntakeService>();

        var evaluationRun = new EvaluationRun
        {
            RunId = Guid.NewGuid(),
            ProviderId = providerId,
            CreatedByUserId = claimantId,
            DatasetVersion = "local-happy-path",
            CreatedAtUtc = DateTime.UtcNow
        };
        dbContext.EvaluationRuns.Add(evaluationRun);
        await dbContext.SaveChangesAsync();

        var conversation = await intakeService.ContinueConversationAsync(new ClaimIntakeConversationRequest
        {
            EvaluationRunId = evaluationRun.RunId,
            Fields = new Dictionary<string, string>
            {
                ["PolicyNumber"] = "POL-HAPPY-1",
                ["ClaimantName"] = "Local Test Customer",
                ["DateOfLoss"] = DateTime.UtcNow.AddDays(-45).ToString("O"),
                ["ClaimType"] = "auto",
                ["DescriptionOfLoss"] = "Rear bumper damage from a parking collision.",
                ["LossAmount"] = "1200",
                ["ContactInformation"] = "customer@example.test"
            }
        }, providerId, claimantId, CancellationToken.None);

        var intake = await intakeService.CompleteAsync(new CompleteClaimIntakeRequest { SessionId = conversation.SessionId }, providerId, claimantId, CancellationToken.None);
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
                            documentId = "local-document-1",
                            confidence = 0.95m,
                            text = "PolicyNumber:POL-HAPPY-1;DateOfLoss:2026-06-28;ClaimType:auto;LossAmount:1200;DescriptionOfLoss:Rear bumper damage;ClaimantName:Local Test Customer"
                        }
                    }
                })
            }),
            ProviderConfigSnapshot = "{}",
            Status = "INTAKE_COMPLETE",
            StartedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var logger = new CapturingLogger();
        var orchestrator = new ClaimPipelineOrchestrator(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OrchestratorOptions()),
            logger,
            serviceProvider.GetRequiredService<IClaimMetrics>());

        await orchestrator.ProcessClaimAsync(providerId, claimId, CancellationToken.None);

        Assert.Null(logger.LastError);
    Assert.Null(logger.LastWarning);
    dbContext.ChangeTracker.Clear();
        var claim = await dbContext.Claims
            .Include(item => item.PipelineState)
            .Include(item => item.AgentOutputs)
            .Include(item => item.ToolInvocations)
            .SingleAsync(item => item.ClaimId == claimId);
        var documentOutput = Assert.Single(claim.AgentOutputs, output => output.AgentId == "DocumentAnalysisAgent");
        var policyOutput = Assert.Single(claim.AgentOutputs, output => output.AgentId == "PolicyValidationAgent");
        var fraudOutput = Assert.Single(claim.AgentOutputs, output => output.AgentId == "FraudDetectionAgent");
        var document = JsonSerializer.Deserialize<DocumentAnalysisResult>(documentOutput.OutputPayload, jsonOptions)!;
        var policy = JsonSerializer.Deserialize<PolicyValidationResult>(policyOutput.OutputPayload, jsonOptions)!;
        var fraud = JsonSerializer.Deserialize<FraudDetectionResult>(fraudOutput.OutputPayload, jsonOptions)!;
        Assert.Empty(document.BlockingMissingFields);
        Assert.Equal("POLICY_VALID", policy.PolicyVerdict);
        Assert.Equal("FRAUD_LOW", fraud.Verdict);
        var settlementOutput = Assert.Single(claim.AgentOutputs, output => output.AgentId == "SettlementDecisionAgent");
        var settlement = JsonSerializer.Deserialize<SettlementDecisionResult>(settlementOutput.OutputPayload, jsonOptions)!;
        Assert.True(
            settlement.ConfidenceScore >= 0.70m,
            $"Confidence={settlement.ConfidenceScore:0.00}; fraudRisk={fraud.RiskScore:0.00}; documentConfidence={document.Confidence:0.00}; policy={policy.PolicyVerdict}.");
        Assert.Equal("APPROVE", settlement.Recommendation);
        Assert.Equal("PIPELINE_COMPLETE", claim.Status);
        Assert.NotNull(claim.PipelineState?.CompletedAt);
        Assert.Equal(
            ["DocumentAnalysisAgent", "PolicyValidationAgent", "FraudDetectionAgent", "SettlementDecisionAgent"],
            JsonSerializer.Deserialize<List<string>>(claim.PipelineState!.CompletedSteps));
        Assert.Contains(claim.AgentOutputs, output => output.AgentId == "ClaimIntake");
        Assert.Contains(claim.AgentOutputs, output => output.AgentId == "DocumentAnalysisAgent");
        Assert.Contains(claim.AgentOutputs, output => output.AgentId == "PolicyValidationAgent");
        Assert.Contains(claim.AgentOutputs, output => output.AgentId == "FraudDetectionAgent");
        Assert.Contains(claim.AgentOutputs, output => output.AgentId == "SettlementDecisionAgent");
        Assert.Equal(3, claim.ToolInvocations.Count);
        Assert.Contains(claim.AgentOutputs, output => output.AgentId == "NotificationLifecycle" && output.OutputPayload.Contains("DECISION_READY"));
        Assert.Contains(claim.AgentOutputs, output => output.AgentId == "NotificationLifecycle" && output.OutputPayload.Contains("PIPELINE_COMPLETED"));
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
        public IReadOnlyCollection<string> Roles => [AppRoles.Customer, AppRoles.EvaluationRunner];
        public string UserId => "user-1";
        public string? Email => "customer@example.test";
        public ClaimsIdentity Identity => new("test");
        public bool IsAuthenticated => true;
    }

    private sealed class CapturingLogger : ILogger<ClaimPipelineOrchestrator>
    {
        public Exception? LastError { get; private set; }

        public string? LastWarning { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error && exception is not null)
            {
                LastError = exception;
            }
            else if (logLevel == LogLevel.Warning)
            {
                LastWarning = formatter(state, exception);
            }
        }
    }
}