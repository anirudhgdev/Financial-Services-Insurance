using ClaimSettlement.Agents.Models;
using ClaimSettlement.Agents.Pipeline;
using ClaimSettlement.Domain.Entities;
using System.Security.Claims;
using System.Text.Json;
using Xunit;
using DomainClaim = ClaimSettlement.Domain.Entities.Claim;
using SecurityClaim = System.Security.Claims.Claim;

namespace ClaimSettlement.Agents.Tests;

public sealed class DocumentAnalysisAgentTests
{
    [Fact]
    public async Task ExtractsStructuredFieldsFromUploadedDocuments()
    {
        var agent = CreateAgent();
        var context = BuildContext(BuildUploadedDocumentsJson(new[]
        {
            new UploadedDocument("doc-1", "PolicyNumber:PN-1;DateOfLoss:2026-07-01;ClaimType:auto;LossAmount:1200;DescriptionOfLoss:Rear bumper damage", 0.92m)
        }));

        var result = await agent.InvokeAsync(context, new ClaimPipelineInput(BuildClaim()), CancellationToken.None);

        Assert.Single(result.Documents);
        var extracted = result.Documents[0];
        Assert.Equal("EXTRACTED", extracted.Status);
        Assert.Equal("PN-1", extracted.Fields["PolicyNumber"]);
        Assert.True(result.Confidence >= 0.80m);
    }

    [Fact]
    public async Task FlagsDuplicatesWhenTextOverlapExceedsNinetyPercent()
    {
        var text = "PolicyNumber:PN-1;DateOfLoss:2026-07-01;ClaimType:auto;LossAmount:1200;DescriptionOfLoss:Rear bumper damage at low speed";
        var agent = CreateAgent();
        var context = BuildContext(BuildUploadedDocumentsJson(new[]
        {
            new UploadedDocument("doc-1", text, 0.91m),
            new UploadedDocument("doc-2", text, 0.89m),
            new UploadedDocument("doc-3", "PolicyNumber:PN-2;DateOfLoss:2026-07-02;ClaimType:auto;LossAmount:800;DescriptionOfLoss:Windshield crack", 0.88m)
        }));

        var result = await agent.InvokeAsync(context, new ClaimPipelineInput(BuildClaim()), CancellationToken.None);

        Assert.Contains("doc-2", result.DuplicateDocumentIds);
        Assert.Contains(result.Documents, x => x.DocumentId == "doc-2" && x.Status == "DUPLICATE");
    }

    [Fact]
    public async Task ProducesSummaryWithinWordRange()
    {
        var agent = CreateAgent();
        var context = BuildContext(BuildUploadedDocumentsJson(new[]
        {
            new UploadedDocument("doc-1", "PolicyNumber:PN-1;DateOfLoss:2026-07-01;ClaimType:auto;LossAmount:1200;DescriptionOfLoss:Rear bumper damage", 0.95m)
        }));

        var result = await agent.InvokeAsync(context, new ClaimPipelineInput(BuildClaim()), CancellationToken.None);
        var wordCount = result.Summary.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        Assert.InRange(wordCount, 100, 300);
    }

    [Fact]
    public async Task GeneratesBlockingGapReportAndNotificationFlag()
    {
        var agent = CreateAgent();
        var context = BuildContext(BuildUploadedDocumentsJson(new[]
        {
            new UploadedDocument("doc-1", "ClaimantName:Alex;DescriptionOfLoss:Minor scratches", 0.76m)
        }));

        var result = await agent.InvokeAsync(context, new ClaimPipelineInput(BuildClaim()), CancellationToken.None);

        Assert.NotEmpty(result.BlockingMissingFields);
        Assert.Contains("PolicyNumber", result.BlockingMissingFields);
        Assert.True(result.NotificationRequired);
        Assert.Equal("MISSING_BLOCKING_INFORMATION", result.NotificationEventType);
    }

    private static DocumentAnalysisAgent CreateAgent()
        => new(
            new SimulatedDocumentExtractionClient(),
            new DocumentDeduplicationService(),
            new GapClassificationService(),
            new TemplateClaimSummaryGenerator());

    private static ClaimAgentContext BuildContext(string uploadedDocumentsJson)
    {
        var identity = new ClaimsIdentity("test");
        identity.AddClaim(new SecurityClaim(ClaimTypes.NameIdentifier, "user-1"));

        var upstream = new Dictionary<string, JsonDocument>
        {
            ["uploadedDocuments"] = JsonDocument.Parse(uploadedDocumentsJson)
        };

        return new ClaimAgentContext
        {
            ClaimId = Guid.NewGuid(),
            ClaimRecord = BuildClaim(),
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
                SupportedClaimTypes = "[\"auto\"]",
                SupportedNotificationChannels = "[\"email\"]",
                PipelineConcurrencyLimit = 100,
                IsActive = true,
                ClaimTypeMandatoryFields = "{\"auto\":[\"PolicyNumber\",\"DateOfLoss\",\"ClaimType\",\"LossAmount\",\"DescriptionOfLoss\"]}",
                CoverageMappingRules = "{}",
                ExclusionSets = "{}",
                AlwaysManualClaimTypes = "[]"
            },
            UserIdentity = identity
        };
    }

    private static DomainClaim BuildClaim()
        => new()
        {
            ClaimId = Guid.NewGuid(),
            ProviderId = "provider-1",
            ClaimantId = "user-1",
            PolicyNumber = "PN-1",
            DateOfLoss = new DateTime(2026, 7, 1),
            ClaimType = "auto",
            LossAmount = 1200m,
            Status = "INTAKE_COMPLETE",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static string BuildUploadedDocumentsJson(IEnumerable<UploadedDocument> docs)
    {
        var payload = new
        {
            documents = docs.Select(x => new { documentId = x.DocumentId, text = x.Text, confidence = x.Confidence })
        };

        return JsonSerializer.Serialize(payload);
    }

    private sealed record UploadedDocument(string DocumentId, string Text, decimal Confidence);
}
