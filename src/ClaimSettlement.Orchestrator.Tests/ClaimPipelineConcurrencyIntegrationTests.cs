using ClaimSettlement.Agents;
using ClaimSettlement.Api.Claims;
using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure.Azure;
using ClaimSettlement.Infrastructure.Observability;
using ClaimSettlement.Infrastructure.Persistence;
using ClaimSettlement.Orchestrator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace ClaimSettlement.Orchestrator.Tests;

public sealed class ClaimPipelineConcurrencyIntegrationTests
{
    [Fact]
    public async Task ProcessesOneHundredClaimsAtProviderConcurrencyLimitWithoutLossOrDeadlock()
    {
        const string providerId = "provider-load";
        const string claimantId = "user-1";
        const int claimCount = 100;
        await using var serviceProvider = BuildServiceProvider();
        var dbContext = serviceProvider.GetRequiredService<ClaimSettlementDbContext>();
        var intakeService = serviceProvider.GetRequiredService<IClaimIntakeService>();

        dbContext.ProviderConfigurations.Add(new ProviderConfiguration
        {
            ProviderId = providerId,
            ProviderName = "Local load provider",
            ManualReviewFraudThreshold = 0.70m,
            ManualReviewClaimAmountThreshold = 5000m,
            DeduplicationWindowDays = 90,
            InformationRequestDeadlineDays = 7,
            AdjusterSlaPeriodHours = 48,
            SupportedClaimTypes = "[]",
            SupportedNotificationChannels = "[]",
            PipelineConcurrencyLimit = claimCount,
            IsActive = true
        });

        var evaluationRun = new EvaluationRun
        {
            RunId = Guid.NewGuid(),
            ProviderId = providerId,
            CreatedByUserId = claimantId,
            DatasetVersion = "local-concurrency",
            CreatedAtUtc = DateTime.UtcNow
        };
        dbContext.EvaluationRuns.Add(evaluationRun);
        await dbContext.SaveChangesAsync();

        for (var index = 0; index < claimCount; index++)
        {
            var policyNumber = $"POL-LOAD-{index:D3}";
            var conversation = await intakeService.ContinueConversationAsync(new ClaimIntakeConversationRequest
            {
                EvaluationRunId = evaluationRun.RunId,
                Fields = new Dictionary<string, string>
                {
                    ["PolicyNumber"] = policyNumber,
                    ["ClaimantName"] = $"Load Customer {index}",
                    ["DateOfLoss"] = DateTime.UtcNow.AddDays(-45).ToString("O"),
                    ["ClaimType"] = "auto",
                    ["DescriptionOfLoss"] = "Low-risk local load-test collision.",
                    ["LossAmount"] = "1201",
                    ["ContactInformation"] = $"customer-{index}@example.test"
                }
            }, providerId, claimantId, CancellationToken.None);

            var intake = await intakeService.CompleteAsync(
                new CompleteClaimIntakeRequest { SessionId = conversation.SessionId },
                providerId,
                claimantId,
                CancellationToken.None);
            var claimId = Assert.IsType<Guid>(intake.ClaimId);
            var claim = await dbContext.Claims.SingleAsync(item => item.ClaimId == claimId);
            claim.CreatedAt = DateTime.UtcNow.AddDays(-90);
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
                                documentId = $"load-document-{index}",
                                confidence = 0.95m,
                                text = $"PolicyNumber:{policyNumber};DateOfLoss:2026-06-28;ClaimType:auto;LossAmount:1201;DescriptionOfLoss:Low-risk collision;ClaimantName:Load Customer {index}"
                            }
                        }
                    })
                }),
                ProviderConfigSnapshot = "{}",
                Status = "INTAKE_COMPLETE",
                StartedAt = DateTime.UtcNow
            });
        }
        await dbContext.SaveChangesAsync();

        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var logger = new CapturingLogger();
        var orchestrator = new ClaimPipelineOrchestrator(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OrchestratorOptions
            {
                PollIntervalSeconds = 1,
                PendingBatchSize = claimCount,
                DefaultProviderConcurrencyLimit = claimCount,
                AgentTimeoutSeconds = 5
            }),
            logger,
            serviceProvider.GetRequiredService<IClaimMetrics>());

        await orchestrator.StartAsync(cancellationSource.Token);
        try
        {
            var completed = await WaitForCompletionAsync(serviceProvider, providerId, claimCount, cancellationSource.Token);
            Assert.True(completed, logger.LastError?.ToString() ?? "The provider queue did not complete all 100 claims before the timeout.");
        }
        finally
        {
            await orchestrator.StopAsync(CancellationToken.None);
        }

        dbContext.ChangeTracker.Clear();
        var outcomes = await dbContext.Claims
            .Where(item => item.ProviderId == providerId)
            .GroupBy(item => item.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync();
        Assert.Equal(claimCount, outcomes.Sum(item => item.Count));
        Assert.Equal(claimCount, outcomes.Single(item => item.Status == "PIPELINE_COMPLETE").Count);
    }

    private static async Task<bool> WaitForCompletionAsync(IServiceProvider serviceProvider, string providerId, int expectedCount, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ClaimSettlementDbContext>();
            var completedCount = await dbContext.Claims.CountAsync(
                item => item.ProviderId == providerId && item.Status == "PIPELINE_COMPLETE",
                ct);
            if (completedCount == expectedCount)
            {
                return true;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return false;
            }
        }

        return false;
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");
        var databaseRoot = new InMemoryDatabaseRoot();
        services.AddDbContext<ClaimSettlementDbContext>(options => options.UseInMemoryDatabase(databaseName, databaseRoot));
        services.AddMemoryCache();
        services.AddMetrics();
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
        public string ProviderId => "provider-load";
        public IReadOnlyCollection<string> Roles => [AppRoles.Customer, AppRoles.EvaluationRunner];
        public string UserId => "user-1";
        public string? Email => "customer@example.test";
        public ClaimsIdentity Identity => new("test");
        public bool IsAuthenticated => true;
    }

    private sealed class CapturingLogger : ILogger<ClaimPipelineOrchestrator>
    {
        public Exception? LastError { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error && exception is not null)
            {
                LastError = exception;
            }
        }
    }
}