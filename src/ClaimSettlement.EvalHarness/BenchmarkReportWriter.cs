using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaimSettlement.EvalHarness;

internal sealed class BenchmarkReportWriter
{
    private readonly BlobContainerClient _containerClient;

    public BenchmarkReportWriter(Uri containerUri, TokenCredential credential)
    {
        _containerClient = new BlobContainerClient(containerUri, credential);
    }

    public async Task<BenchmarkReportPublication> PublishAsync(BenchmarkReport report, CancellationToken ct)
    {
        await _containerClient.CreateIfNotExistsAsync(cancellationToken: ct);

        var prefix = $"evaluation-runs/{report.RunTimestampUtc:yyyy/MM/dd}/{report.RunId:N}";
        var jsonLines = BuildJsonLines(report);
        var markdown = BuildMarkdown(report);
        var jsonLinesPublication = await UploadAsync($"{prefix}/results.jsonl", jsonLines, "application/x-ndjson", ct);
        var markdownPublication = await UploadAsync($"{prefix}/summary.md", markdown, "text/markdown", ct);
        return new BenchmarkReportPublication(jsonLinesPublication, markdownPublication);
    }

    private async Task<PublishedReport> UploadAsync(string blobName, string content, string contentType, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        var blobClient = _containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(
            new BinaryData(bytes),
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sha256"] = sha256
                }
            },
            ct);
        return new PublishedReport(blobClient.Uri, sha256);
    }

    private static string BuildJsonLines(BenchmarkReport report)
    {
        var lines = new List<string>
        {
            JsonSerializer.Serialize(new
            {
                recordType = "summary",
                report.RunId,
                report.RunTimestampUtc,
                report.Environment,
                report.DatasetVersion,
                report.Accuracy,
                report.FraudMetrics,
                report.LatencyMetrics,
                report.HallucinationCount,
                report.ToolDiscrepancyCount,
                report.HumanReviewRates,
                report.CostMetrics
            })
        };
        lines.AddRange(report.Outcomes.Select(outcome => JsonSerializer.Serialize(new { recordType = "case", outcome })));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string BuildMarkdown(BenchmarkReport report)
    {
        var markdown = new StringBuilder();
        markdown.AppendLine("# Claim Settlement Evaluation Benchmark");
        markdown.AppendLine();
        markdown.AppendLine($"- Run: `{report.RunId}`");
        markdown.AppendLine($"- Timestamp (UTC): {report.RunTimestampUtc:O}");
        markdown.AppendLine($"- Environment: {report.Environment}");
        markdown.AppendLine($"- Dataset: {report.DatasetVersion}");
        markdown.AppendLine($"- Completed: {report.Outcomes.Count(outcome => outcome.Completed)}/{report.Outcomes.Count}");
        markdown.AppendLine($"- Decision accuracy: {report.Accuracy.OverallAccuracy:P2}");
        markdown.AppendLine($"- Total Azure OpenAI cost: ${report.CostMetrics.TotalCostUsd:F6}");
        markdown.AppendLine($"- Cost per completed claim: ${report.CostMetrics.CostPerCompletedClaimUsd?.ToString("F6") ?? "unavailable"}");
        markdown.AppendLine();
        markdown.AppendLine("## Cases");
        markdown.AppendLine();
        markdown.AppendLine("| Scenario | Expected | Actual | Completed | Details |");
        markdown.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var outcome in report.Outcomes)
        {
            markdown.AppendLine($"| {outcome.ScenarioId} | {outcome.ExpectedDecision} | {outcome.ActualDecision ?? "unavailable"} | {outcome.Completed} | {outcome.Message} |");
        }

        return markdown.ToString();
    }
}

internal sealed record BenchmarkReport(
    Guid RunId,
    DateTime RunTimestampUtc,
    string Environment,
    string DatasetVersion,
    IReadOnlyList<EvaluationSubmissionOutcome> Outcomes,
    DecisionAccuracyReport Accuracy,
    FraudMetricsReport FraudMetrics,
    IReadOnlyList<LatencyPercentiles> LatencyMetrics,
    int HallucinationCount,
    int ToolDiscrepancyCount,
    IReadOnlyList<HumanReviewRate> HumanReviewRates,
    CostMetricsResult CostMetrics);

internal sealed record PublishedReport(Uri Uri, string Sha256);

internal sealed record BenchmarkReportPublication(PublishedReport JsonLines, PublishedReport Markdown);