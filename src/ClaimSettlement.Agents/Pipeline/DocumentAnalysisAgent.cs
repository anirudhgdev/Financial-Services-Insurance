using ClaimSettlement.Agents.Abstractions;
using ClaimSettlement.Agents.Models;

namespace ClaimSettlement.Agents.Pipeline;

public sealed class DocumentAnalysisAgent : IClaimAgent<ClaimPipelineInput, DocumentAnalysisResult>
{
    private readonly IDocumentExtractionClient _extractionClient;
    private readonly IDocumentDeduplicationService _deduplicationService;
    private readonly IGapClassificationService _gapClassificationService;
    private readonly IClaimSummaryGenerator _summaryGenerator;

    public DocumentAnalysisAgent(
        IDocumentExtractionClient extractionClient,
        IDocumentDeduplicationService deduplicationService,
        IGapClassificationService gapClassificationService,
        IClaimSummaryGenerator summaryGenerator)
    {
        _extractionClient = extractionClient;
        _deduplicationService = deduplicationService;
        _gapClassificationService = gapClassificationService;
        _summaryGenerator = summaryGenerator;
    }

    public async Task<DocumentAnalysisResult> InvokeAsync(ClaimAgentContext context, ClaimPipelineInput input, CancellationToken ct)
    {
        var extracted = await _extractionClient.ExtractAsync(context, input, ct);

        var duplicateIds = _deduplicationService.DetectDuplicates(extracted);
        var duplicateSet = duplicateIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var materializedDocuments = extracted
            .Select(doc => duplicateSet.Contains(doc.DocumentId)
                ? new DocumentExtractionResult
                {
                    DocumentId = doc.DocumentId,
                    Status = "DUPLICATE",
                    RawExtractedText = doc.RawExtractedText,
                    Confidence = doc.Confidence,
                    Fields = doc.Fields
                }
                : doc)
            .ToList();

        var evaluatedDocuments = materializedDocuments
            .Where(x => x.Status != "DUPLICATE")
            .ToList();

        var (blocking, nonBlocking) = _gapClassificationService.Classify(context, input.ClaimRecord, evaluatedDocuments);
        var missingFields = blocking.Concat(nonBlocking).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var confidenceCandidates = evaluatedDocuments
            .Where(x => x.Status != "EXTRACTION_FAILED")
            .Select(x => x.Confidence)
            .ToList();

        var confidence = confidenceCandidates.Count == 0 ? 0m : confidenceCandidates.Average();
        var summary = _summaryGenerator.Summarize(input.ClaimRecord, materializedDocuments, missingFields);

        var result = new DocumentAnalysisResult
        {
            Summary = summary,
            Confidence = confidence,
            MissingFields = missingFields,
            BlockingMissingFields = blocking,
            NonBlockingMissingFields = nonBlocking,
            DuplicateDocumentIds = duplicateIds,
            Documents = materializedDocuments,
            NotificationRequired = blocking.Count > 0,
            NotificationEventType = blocking.Count > 0 ? "MISSING_BLOCKING_INFORMATION" : null
        };

        return result;
    }
}