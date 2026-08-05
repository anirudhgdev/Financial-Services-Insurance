using ClaimSettlement.Api.Authorization;
using ClaimSettlement.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimSettlement.Api.Claims;

public sealed class ClaimsController : Controllers.BaseApiController
{
    private readonly IClaimIntakeService _claimIntakeService;
    private readonly IProviderContextAccessor _providerContextAccessor;

    public ClaimsController(
        IClaimIntakeService claimIntakeService,
        IProviderContextAccessor providerContextAccessor)
    {
        _claimIntakeService = claimIntakeService;
        _providerContextAccessor = providerContextAccessor;
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

            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}