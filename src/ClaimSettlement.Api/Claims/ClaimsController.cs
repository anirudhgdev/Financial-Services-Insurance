using ClaimSettlement.Api.Authorization;
using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure.Observability;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace ClaimSettlement.Api.Claims;

public sealed class ClaimsController : Controllers.BaseApiController
{
    private readonly IClaimIntakeService _claimIntakeService;
    private readonly IProviderContextAccessor _providerContextAccessor;
    private readonly ClaimSettlementDbContext _dbContext;
    private readonly IAuditLogger _auditLogger;
    private readonly IClaimMetrics _claimMetrics;

    public ClaimsController(
        IClaimIntakeService claimIntakeService,
        IProviderContextAccessor providerContextAccessor,
        ClaimSettlementDbContext dbContext,
        IAuditLogger auditLogger,
        IClaimMetrics claimMetrics)
    {
        _claimIntakeService = claimIntakeService;
        _providerContextAccessor = providerContextAccessor;
        _dbContext = dbContext;
        _auditLogger = auditLogger;
        _claimMetrics = claimMetrics;
    }

    [HttpPost("intake/conversation")]
    [Authorize(Policy = AuthorizationPolicies.Customer)]
    [ProducesResponseType(typeof(ClaimIntakeConversationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ClaimIntakeConversationResponse>> IntakeConversation(
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
                missingFields = response.MissingFields.Select(x => x.FieldName)
            }
        }, ct);

        return Ok(response);
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