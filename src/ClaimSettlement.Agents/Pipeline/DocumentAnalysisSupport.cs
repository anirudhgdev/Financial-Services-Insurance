using ClaimSettlement.Agents.Models;
using ClaimSettlement.Domain.Entities;
using System.Text.Json;

namespace ClaimSettlement.Agents.Pipeline;

public interface IDocumentExtractionClient
{
    Task<IReadOnlyList<DocumentExtractionResult>> ExtractAsync(ClaimAgentContext context, ClaimPipelineInput input, CancellationToken ct);
}

public sealed class SimulatedDocumentExtractionClient : IDocumentExtractionClient
{
    public Task<IReadOnlyList<DocumentExtractionResult>> ExtractAsync(ClaimAgentContext context, ClaimPipelineInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!context.UpstreamOutputs.TryGetValue("uploadedDocuments", out var uploadedDocuments))
        {
            return Task.FromResult<IReadOnlyList<DocumentExtractionResult>>(Array.Empty<DocumentExtractionResult>());
        }

        var results = new List<DocumentExtractionResult>();
        if (!uploadedDocuments.RootElement.TryGetProperty("documents", out var docsElement) || docsElement.ValueKind != JsonValueKind.Array)
        {
            return Task.FromResult<IReadOnlyList<DocumentExtractionResult>>(results);
        }

        foreach (var docElement in docsElement.EnumerateArray())
        {
            var documentId = docElement.TryGetProperty("documentId", out var idElement)
                ? idElement.GetString() ?? Guid.NewGuid().ToString("N")
                : Guid.NewGuid().ToString("N");

            var text = docElement.TryGetProperty("text", out var textElement)
                ? textElement.GetString() ?? string.Empty
                : string.Empty;

            var confidence = docElement.TryGetProperty("confidence", out var confidenceElement) && confidenceElement.TryGetDecimal(out var parsedConfidence)
                ? parsedConfidence
                : 0.90m;

            if (string.IsNullOrWhiteSpace(text))
            {
                results.Add(new DocumentExtractionResult
                {
                    DocumentId = documentId,
                    Status = "EXTRACTION_FAILED",
                    RawExtractedText = string.Empty,
                    Confidence = 0m,
                    Fields = new Dictionary<string, string>()
                });

                continue;
            }

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var segment in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var pair = segment.Split(':', 2, StringSplitOptions.TrimEntries);
                if (pair.Length == 2 && !string.IsNullOrWhiteSpace(pair[1]))
                {
                    fields[pair[0]] = pair[1];
                }
            }

            var status = confidence < 0.80m ? "NEEDS_REVIEW" : "EXTRACTED";
            results.Add(new DocumentExtractionResult
            {
                DocumentId = documentId,
                Status = status,
                RawExtractedText = text,
                Confidence = confidence,
                Fields = fields
            });
        }

        return Task.FromResult<IReadOnlyList<DocumentExtractionResult>>(results);
    }
}

public interface IClaimSummaryGenerator
{
    string Summarize(Claim claim, IReadOnlyList<DocumentExtractionResult> documents, IReadOnlyList<string> missingFields);
}

public sealed class TemplateClaimSummaryGenerator : IClaimSummaryGenerator
{
    public string Summarize(Claim claim, IReadOnlyList<DocumentExtractionResult> documents, IReadOnlyList<string> missingFields)
    {
        var extracted = documents.Count(x => x.Status != "EXTRACTION_FAILED");
        var duplicates = documents.Count(x => x.Status == "DUPLICATE");
        var needsReview = documents.Count(x => x.Status == "NEEDS_REVIEW");
        var missing = missingFields.Count == 0
            ? "No mandatory gaps were detected."
            : $"Missing mandatory fields: {string.Join(", ", missingFields)}.";

        var summary =
            $"Claim {claim.ClaimId} for provider {claim.ProviderId} was analyzed across {documents.Count} uploaded documents, " +
            $"with {extracted} successfully extracted records, {needsReview} low-confidence items requiring manual review, " +
            $"and {duplicates} duplicate documents filtered from downstream processing. " +
            "The analysis focused on claimant identity, policy references, incident timing, financial values, and evidence text quality. " +
            "Extracted fields were normalized into structured key-value output to support policy validation and risk scoring, while preserving raw text for auditability and later human review if necessary. " +
            $"{missing} " +
            "When blocking gaps are present, the result explicitly requests a customer information follow-up so the workflow can pause safely before automatic settlement decisions proceed. " +
            "This summary is generated to remain concise while still capturing confidence posture, extraction coverage, and operational handoff indicators for the next pipeline stage.";

        var words = summary.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 100)
        {
            summary += " Additional context: document-level confidence and field coverage are preserved per file to support transparent troubleshooting and deterministic replay across environments.";
        }

        return summary;
    }
}

public interface IGapClassificationService
{
    (IReadOnlyList<string> blocking, IReadOnlyList<string> nonBlocking) Classify(
        ClaimAgentContext context,
        Claim claim,
        IReadOnlyList<DocumentExtractionResult> documents);
}

public sealed class GapClassificationService : IGapClassificationService
{
    private static readonly HashSet<string> BaselineMandatoryFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "PolicyNumber",
        "DateOfLoss",
        "ClaimType",
        "LossAmount",
        "DescriptionOfLoss"
    };

    public (IReadOnlyList<string> blocking, IReadOnlyList<string> nonBlocking) Classify(
        ClaimAgentContext context,
        Claim claim,
        IReadOnlyList<DocumentExtractionResult> documents)
    {
        var required = ResolveRequiredFields(claim.ClaimType, context.ProviderConfig.ClaimTypeMandatoryFields);
        var captured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in documents)
        {
            foreach (var key in document.Fields.Keys)
            {
                captured.Add(key);
            }
        }

        var blocking = required
            .Where(x => !captured.Contains(x))
            .OrderBy(x => x)
            .ToList();

        var nonBlocking = new List<string>();
        if (!captured.Contains("ClaimantName"))
        {
            nonBlocking.Add("ClaimantName");
        }

        if (documents.Any(x => x.Status == "NEEDS_REVIEW"))
        {
            nonBlocking.Add("LowConfidenceFields");
        }

        if (documents.Any(x => x.Status == "EXTRACTION_FAILED"))
        {
            nonBlocking.Add("UnreadableDocument");
        }

        return (blocking, nonBlocking.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static IReadOnlyList<string> ResolveRequiredFields(string claimType, string mandatoryFieldConfigJson)
    {
        var required = new HashSet<string>(BaselineMandatoryFields, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(mandatoryFieldConfigJson) || mandatoryFieldConfigJson == "{}")
        {
            return required.ToList();
        }

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(mandatoryFieldConfigJson);
            if (map is null)
            {
                return required.ToList();
            }

            foreach (var pair in map)
            {
                if (!string.Equals(pair.Key, claimType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var field in pair.Value)
                {
                    if (!string.IsNullOrWhiteSpace(field))
                    {
                        required.Add(field.Trim());
                    }
                }
            }
        }
        catch
        {
            return required.ToList();
        }

        return required.ToList();
    }
}

public interface IDocumentDeduplicationService
{
    IReadOnlyList<string> DetectDuplicates(IReadOnlyList<DocumentExtractionResult> documents);
}

public sealed class DocumentDeduplicationService : IDocumentDeduplicationService
{
    public IReadOnlyList<string> DetectDuplicates(IReadOnlyList<DocumentExtractionResult> documents)
    {
        var duplicates = new List<string>();

        for (var i = 0; i < documents.Count; i++)
        {
            if (documents[i].Status == "EXTRACTION_FAILED")
            {
                continue;
            }

            for (var j = i + 1; j < documents.Count; j++)
            {
                if (documents[j].Status == "EXTRACTION_FAILED")
                {
                    continue;
                }

                if (TextOverlap(documents[i].RawExtractedText, documents[j].RawExtractedText) > 0.90m)
                {
                    duplicates.Add(documents[j].DocumentId);
                }
            }
        }

        return duplicates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static decimal TextOverlap(string left, string right)
    {
        var leftTokens = Tokenize(left);
        var rightTokens = Tokenize(right);

        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0m;
        }

        var intersection = leftTokens.Intersect(rightTokens).Count();
        var union = leftTokens.Union(rightTokens).Count();
        return union == 0 ? 0m : (decimal)intersection / union;
    }

    private static HashSet<string> Tokenize(string input)
    {
        var chars = input.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        var normalized = new string(chars);
        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
