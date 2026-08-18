using ClaimSettlement.Api.Authorization;
using ClaimSettlement.Agents.Models;
using ClaimSettlement.Agents.Pipeline;
using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure.Observability;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace ClaimSettlement.Api.Claims;

public sealed class ClaimsController : Controllers.BaseApiController
{
    private readonly IClaimIntakeService _claimIntakeService;
    private readonly IProviderContextAccessor _providerContextAccessor;
    private readonly ClaimSettlementDbContext _dbContext;
    private readonly IAuditLogger _auditLogger;
    private readonly IClaimMetrics _claimMetrics;
    private readonly ClaimIntakeAgent? _claimIntakeAgent;

    public ClaimsController(
        IClaimIntakeService claimIntakeService,
        IProviderContextAccessor providerContextAccessor,
        ClaimSettlementDbContext dbContext,
        IAuditLogger auditLogger,
        IClaimMetrics claimMetrics,
        ClaimIntakeAgent? claimIntakeAgent = null)
    {
        _claimIntakeService = claimIntakeService;
        _providerContextAccessor = providerContextAccessor;
        _dbContext = dbContext;
        _auditLogger = auditLogger;
        _claimMetrics = claimMetrics;
        _claimIntakeAgent = claimIntakeAgent;
    }

    [HttpPost("intake/conversation")]
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [ProducesResponseType(typeof(ClaimIntakeConversationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClaimIntakeConversationResponse>> IntakeConversation(
        [FromBody] ClaimIntakeConversationRequest request,
        CancellationToken ct)
    {
        if (request.EvaluationRunId.HasValue && !await IsOwnedEvaluationRunAsync(request.EvaluationRunId.Value, ct))
        {
            return NotFound();
        }

        var response = await _claimIntakeService.ContinueConversationAsync(
            request,
            _providerContextAccessor.ProviderId,
            _providerContextAccessor.UserId,
            ct);

        await _auditLogger.AppendAsync(new AuditLogEntry
        {
            ProviderId = _providerContextAccessor.ProviderId,
            EventType = "CLAIM_INTAKE_CONVERSATION",
            ActorId = _providerContextAccessor.UserId,
            ActorType = "Customer",
            ClaimId = response.ClaimId,
            Payload = new
            {
                response.SessionId,
                response.IsReadyForSubmission,
                missingFields = response.MissingFields.Select(x => x.FieldName)
            }
        }, ct);

        return Ok(response);
    }

    [HttpPost("intake/conversation/stream")]
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [Produces("text/event-stream")]
    public async Task StreamIntakeConversation(
        [FromBody] ClaimIntakeConversationRequest request,
        CancellationToken ct)
    {
        var response = await _claimIntakeService.ContinueConversationAsync(
            request,
            _providerContextAccessor.ProviderId,
            _providerContextAccessor.UserId,
            ct);

        await _auditLogger.AppendAsync(new AuditLogEntry
        {
            ProviderId = _providerContextAccessor.ProviderId,
            EventType = "CLAIM_INTAKE_CONVERSATION",
            ActorId = _providerContextAccessor.UserId,
            ActorType = "Customer",
            ClaimId = response.ClaimId,
            Payload = new
            {
                response.SessionId,
                response.IsReadyForSubmission,
                missingFields = response.MissingFields.Select(x => x.FieldName),
                streamed = true
            }
        }, ct);

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Append("X-Accel-Buffering", "no");

        if (_claimIntakeAgent is null)
        {
            foreach (var chunk in SplitIntoChunks(response.Prompt))
            {
                await WriteStreamEventAsync("delta", new { text = chunk }, ct);
            }
        }
        else
        {
            await foreach (var chunk in _claimIntakeAgent.StreamPromptAsync(new ClaimIntakeInput
            {
                SessionId = response.SessionId,
                Message = request.Message ?? string.Empty,
                CollectedFields = response.CollectedFields
            }, ct))
            {
                await WriteStreamEventAsync("delta", new { text = chunk }, ct);
            }

            var tokenUsage = _claimIntakeAgent.LastTokenUsage;
            if (tokenUsage is not null)
            {
                _claimIntakeService.RecordTokenUsage(
                    response.SessionId,
                    _providerContextAccessor.ProviderId,
                    _providerContextAccessor.UserId,
                    tokenUsage.InputTokenCount,
                    tokenUsage.OutputTokenCount);
            }
        }

        await WriteStreamEventAsync("complete", new
        {
            response.SessionId,
            response.ClaimId,
            response.MissingFields,
            response.CollectedFields,
            response.IsReadyForSubmission
        }, ct);
    }

    [HttpPost("intake/complete")]
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [ProducesResponseType(typeof(CompleteClaimIntakeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CompleteClaimIntakeResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CompleteClaimIntakeResponse>> CompleteIntake(
        [FromBody] CompleteClaimIntakeRequest request,
        CancellationToken ct)
    {
        var response = await _claimIntakeService.CompleteAsync(
            request,
            _providerContextAccessor.ProviderId,
            _providerContextAccessor.UserId,
            ct);

        await _auditLogger.AppendAsync(new AuditLogEntry
        {
            ProviderId = _providerContextAccessor.ProviderId,
            EventType = "CLAIM_INTAKE_COMPLETED",
            ActorId = _providerContextAccessor.UserId,
            ActorType = "Customer",
            ClaimId = response.ClaimId,
            Payload = new
            {
                response.Created,
                response.Message,
                response.RequiresDuplicateConfirmation,
                response.ExistingClaimId
            }
        }, ct);

        if (response.Created)
        {
            _claimMetrics.RecordClaimOutcome("INTAKE_COMPLETE");
        }

        if (!response.Created)
        {
            return BadRequest(response);
        }

        return Ok(response);
    }

    [HttpGet("{claimId:guid}/status")]
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [ProducesResponseType(typeof(ClaimStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ClaimStatusResponse>> GetClaimStatus(
        [FromRoute] Guid claimId,
        CancellationToken ct)
    {
        var claim = await _dbContext.Claims
            .Include(x => x.PipelineState)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.ClaimId == claimId && x.ProviderId == _providerContextAccessor.ProviderId,
                ct);

        if (claim is null)
        {
            return NotFound();
        }

        var completedStages = DeserializeCompletedStages(claim.PipelineState?.CompletedSteps);
        var currentStage = claim.PipelineState?.CurrentStep;
        if (string.IsNullOrWhiteSpace(currentStage))
        {
            currentStage = GetCurrentStageFromStatus(claim.Status);
        }

        return Ok(new ClaimStatusResponse
        {
            ClaimId = claim.ClaimId,
            Status = claim.Status,
            CurrentStage = currentStage,
            CompletedStages = completedStages,
            EstimatedMinutesRemaining = EstimateRemainingMinutes(claim.Status, completedStages.Count),
            StatusMessage = GetStatusMessage(claim.Status, currentStage),
            UpdatedAtUtc = claim.UpdatedAt
        });
    }

    [HttpGet("adjuster-queue")]
    [Authorize(Policy = AuthorizationPolicies.Adjuster)]
    [ProducesResponseType(typeof(AdjusterQueueResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdjusterQueueResponse>> GetAdjusterQueue(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = _dbContext.Claims.Where(claim =>
            claim.ProviderId == _providerContextAccessor.ProviderId &&
            (claim.Status == "MANUAL_REVIEW_ASSIGNED" || claim.Status == "SLA_BREACHED" || claim.Status == "PENDING_ASSIGNMENT"));
        var totalCount = await query.CountAsync(ct);
        var claims = await query.Include(claim => claim.AdjusterAssignments)
            .OrderByDescending(claim => claim.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

        return Ok(new AdjusterQueueResponse
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Claims = claims.Select(claim =>
            {
                var assignment = claim.AdjusterAssignments.OrderByDescending(item => item.AssignedAt).FirstOrDefault();
                return new AdjusterQueueClaim
                {
                    ClaimId = claim.ClaimId,
                    ClaimantId = claim.ClaimantId,
                    ClaimType = claim.ClaimType,
                    Status = claim.Status,
                    Priority = claim.Status == "SLA_BREACHED" ? "High" : "Standard",
                    AssignedAdjusterId = assignment?.AdjusterId,
                    AssignedAtUtc = assignment?.AssignedAt ?? claim.UpdatedAt
                };
            }).ToList()
        });
    }

    [HttpGet("{claimId:guid}/review-package")]
    [Authorize(Policy = AuthorizationPolicies.Adjuster)]
    [ProducesResponseType(typeof(AdjusterReviewPackageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdjusterReviewPackageResponse>> GetReviewPackage(Guid claimId, CancellationToken ct)
    {
        var claim = await _dbContext.Claims.Include(item => item.AgentOutputs).AsNoTracking()
            .FirstOrDefaultAsync(item => item.ClaimId == claimId && item.ProviderId == _providerContextAccessor.ProviderId, ct);
        var output = claim?.AgentOutputs.Where(item => item.AgentId == "HumanReviewAgent").OrderByDescending(item => item.CreatedAt).FirstOrDefault();
        if (claim is null || output is null) return NotFound();

        using var document = JsonDocument.Parse(output.OutputPayload);
        var package = document.RootElement.TryGetProperty("ReviewPackage", out var reviewPackage)
            ? reviewPackage
            : document.RootElement.TryGetProperty("reviewPackage", out reviewPackage) ? reviewPackage : default;
        return Ok(new AdjusterReviewPackageResponse
        {
            ClaimId = claim.ClaimId,
            Status = claim.Status,
            ClaimSummary = GetJsonString(package, "ClaimSummary", "claimSummary"),
            PolicyValidationSummary = GetJsonString(package, "PolicyValidationSummary", "policyValidationSummary"),
            FraudSummary = GetJsonString(package, "FraudSummary", "fraudSummary"),
            DocumentHighlights = GetJsonString(package, "DocumentHighlights", "documentHighlights"),
            SettlementReasoning = GetJsonString(package, "SettlementReasoning", "settlementReasoning"),
            RecommendedSettlementAmount = GetJsonDecimal(package, "RecommendedSettlementAmount", "recommendedSettlementAmount"),
            MissingSections = GetJsonStrings(package, "MissingSections", "missingSections")
        });
    }

    [HttpPost("{claimId:guid}/documents")]
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [ProducesResponseType(typeof(DocumentUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [RequestSizeLimit(DocumentUploadPolicy.MaxDocumentBytes)]
    public async Task<ActionResult<DocumentUploadResponse>> UploadDocument(
        [FromRoute] Guid claimId,
        [FromForm] IFormFile file,
        CancellationToken ct)
    {
        try
        {
            var response = await _claimIntakeService.UploadDocumentAsync(
                claimId,
                file,
                _providerContextAccessor.ProviderId,
                ct);

            await _auditLogger.AppendAsync(new AuditLogEntry
            {
                ProviderId = _providerContextAccessor.ProviderId,
                EventType = "DOCUMENT_UPLOADED",
                ActorId = _providerContextAccessor.UserId,
                ActorType = "Customer",
                ClaimId = claimId,
                Payload = new
                {
                    response.BlobPath,
                    response.ContentType,
                    response.SizeBytes
                }
            }, ct);

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private async Task WriteStreamEventAsync(string eventName, object payload, CancellationToken ct)
    {
        await Response.WriteAsync($"event: {eventName}\n", ct);
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(payload)}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }

    private static IReadOnlyList<string> DeserializeCompletedStages(string? completedSteps)
    {
        if (string.IsNullOrWhiteSpace(completedSteps))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(completedSteps) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private Task<bool> IsOwnedEvaluationRunAsync(Guid runId, CancellationToken ct) => _dbContext.EvaluationRuns.AnyAsync(
        run => run.RunId == runId &&
            run.ProviderId == _providerContextAccessor.ProviderId &&
            run.CreatedByUserId == _providerContextAccessor.UserId,
        ct);

    private static string GetCurrentStageFromStatus(string status) => status switch
    {
        "INTAKE_COMPLETE" => "Document analysis",
        "MANUAL_REVIEW_ASSIGNED" or "SLA_BREACHED" => "Human review",
        "SETTLEMENT_APPROVED" or "SETTLEMENT_REJECTED" => "Decision complete",
        _ => "Claim processing"
    };

    private static int EstimateRemainingMinutes(string status, int completedStageCount) => status switch
    {
        "SETTLEMENT_APPROVED" or "SETTLEMENT_REJECTED" or "PIPELINE_COMPLETE" => 0,
        "MANUAL_REVIEW_ASSIGNED" or "SLA_BREACHED" => 2880,
        _ => Math.Max(5, (4 - Math.Min(completedStageCount, 4)) * 5)
    };

    private static string GetStatusMessage(string status, string currentStage) => status switch
    {
        "SETTLEMENT_APPROVED" => "Your claim has been approved.",
        "SETTLEMENT_REJECTED" => "Your claim decision is available.",
        "MANUAL_REVIEW_ASSIGNED" => "A specialist is reviewing your claim.",
        "SLA_BREACHED" => "Your claim review is taking longer than expected.",
        _ => $"Your claim is currently in {currentStage.ToLowerInvariant()}."
    };

    private static string GetJsonString(JsonElement element, string propertyName, string camelCaseName)
        => TryGetJsonProperty(element, propertyName, camelCaseName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static decimal GetJsonDecimal(JsonElement element, string propertyName, string camelCaseName)
        => TryGetJsonProperty(element, propertyName, camelCaseName, out var value) && value.TryGetDecimal(out var amount) ? amount : 0m;

    private static IReadOnlyList<string> GetJsonStrings(JsonElement element, string propertyName, string camelCaseName)
        => TryGetJsonProperty(element, propertyName, camelCaseName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString() ?? string.Empty).ToList()
            : Array.Empty<string>();

    private static bool TryGetJsonProperty(JsonElement element, string propertyName, string camelCaseName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value)) return true;
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(camelCaseName, out value)) return true;
        value = default;
        return false;
    }


    private static IEnumerable<string> SplitIntoChunks(string text)
    {
        const int maximumChunkLength = 40;
        for (var index = 0; index < text.Length; index += maximumChunkLength)
        {
            yield return text.Substring(index, Math.Min(maximumChunkLength, text.Length - index));
        }
    }

    [HttpPost("{claimId:guid}/adjuster-decision")]
    [Authorize(Policy = AuthorizationPolicies.Adjuster)]
    [ProducesResponseType(typeof(AdjusterDecisionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdjusterDecisionResponse>> SubmitAdjusterDecision(
        [FromRoute] Guid claimId,
        [FromBody] AdjusterDecisionRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Rationale) || request.Rationale.Trim().Length < 20)
        {
            return BadRequest(new { error = "Rationale must contain at least 20 characters." });
        }

        var decision = request.Decision.Trim().ToUpperInvariant();
        if (decision is not ("APPROVE" or "REJECT" or "ESCALATE"))
        {
            return BadRequest(new { error = "Decision must be APPROVE, REJECT, or ESCALATE." });
        }

        var claim = await _dbContext.Claims
            .Include(x => x.AdjusterAssignments)
            .FirstOrDefaultAsync(x => x.ClaimId == claimId && x.ProviderId == _providerContextAccessor.ProviderId, ct);

        if (claim is null)
        {
            return NotFound();
        }

        var assignment = claim.AdjusterAssignments
            .OrderByDescending(x => x.AssignedAt)
            .FirstOrDefault(x => !x.DecidedAt.HasValue);

        if (assignment is null)
        {
            assignment = new AdjusterAssignment
            {
                AssignmentId = Guid.NewGuid(),
                ClaimId = claim.ClaimId,
                ProviderId = claim.ProviderId,
                AdjusterId = _providerContextAccessor.UserId,
                AssignedAt = DateTime.UtcNow
            };

            _dbContext.AdjusterAssignments.Add(assignment);
        }

        var now = DateTime.UtcNow;
        assignment.Decision = decision;
        assignment.Rationale = request.Rationale.Trim();
        assignment.SettlementOverride = request.SettlementOverride;
        assignment.DecidedAt = now;

        claim.Status = decision switch
        {
            "APPROVE" => "SETTLEMENT_APPROVED",
            "REJECT" => "SETTLEMENT_REJECTED",
            _ => "ESCALATED"
        };
        claim.UpdatedAt = now;

        _dbContext.AgentOutputs.Add(new AgentOutput
        {
            OutputId = Guid.NewGuid(),
            ClaimId = claim.ClaimId,
            AgentId = "AdjusterDecision",
            OutputPayload = System.Text.Json.JsonSerializer.Serialize(new
            {
                decision,
                rationale = assignment.Rationale,
                settlementOverride = assignment.SettlementOverride,
                adjusterId = assignment.AdjusterId,
                decidedAtUtc = now,
                notificationEventType = "ADJUSTER_DECISION_ISSUED"
            }),
            CreatedAt = now,
            SchemaVersion = "1.0"
        });

        await _dbContext.SaveChangesAsync(ct);

        await _auditLogger.AppendAsync(new AuditLogEntry
        {
            ProviderId = claim.ProviderId,
            EventType = "ADJUSTER_DECISION_SUBMITTED",
            ActorId = _providerContextAccessor.UserId,
            ActorType = "Adjuster",
            ClaimId = claim.ClaimId,
            Payload = new
            {
                decision,
                rationale = assignment.Rationale,
                assignment.SettlementOverride,
                claim.Status,
                decidedAtUtc = now
            }
        }, ct);

        _claimMetrics.RecordClaimOutcome(claim.Status);

        return Ok(new AdjusterDecisionResponse
        {
            ClaimId = claim.ClaimId,
            Decision = decision,
            Rationale = assignment.Rationale,
            SettlementOverride = assignment.SettlementOverride,
            DecidedAtUtc = now
        });
    }
}