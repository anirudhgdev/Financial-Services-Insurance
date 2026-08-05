namespace ClaimSettlement.Api.Claims;

public interface IClaimIntakeService
{
    Task<ClaimIntakeConversationResponse> ContinueConversationAsync(
        ClaimIntakeConversationRequest request,
        string providerId,
        string claimantId,
        CancellationToken ct);

    Task<CompleteClaimIntakeResponse> CompleteAsync(
        CompleteClaimIntakeRequest request,
        string providerId,
        string claimantId,
        CancellationToken ct);

    Task<DocumentUploadResponse> UploadDocumentAsync(
        Guid claimId,
        IFormFile file,
        string providerId,
        CancellationToken ct);
}

public interface IClaimIntakeValidationService
{
    IReadOnlyList<ClaimFieldGap> GetMandatoryFieldGaps(
        IReadOnlyDictionary<string, string> fields,
        string claimType,
        string providerId,
        string mandatoryFieldConfigJson,
        string supportedClaimTypesJson);
}

public interface IClaimDuplicateGuard
{
    Task<Guid?> FindDuplicateClaimAsync(
        string providerId,
        string claimantId,
        string policyNumber,
        DateTime dateOfLoss,
        CancellationToken ct);
}

public interface IDocumentUploadPolicy
{
    DocumentValidationResult Validate(IFormFile file, int existingDocumentCount);
}