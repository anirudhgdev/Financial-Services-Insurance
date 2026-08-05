namespace ClaimSettlement.Api.Claims;

public sealed class DocumentUploadPolicy : IDocumentUploadPolicy
{
    public const int MaxDocumentsPerClaim = 10;
    public const long MaxDocumentBytes = 50L * 1024L * 1024L;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpeg", ".jpg", ".png", ".tiff", ".tif"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/tiff"
    };

    public DocumentValidationResult Validate(IFormFile file, int existingDocumentCount)
    {
        if (existingDocumentCount >= MaxDocumentsPerClaim)
        {
            return new DocumentValidationResult { IsValid = false, Error = "Maximum of 10 documents per claim has been reached." };
        }

        if (file.Length <= 0)
        {
            return new DocumentValidationResult { IsValid = false, Error = "File is empty." };
        }

        if (file.Length > MaxDocumentBytes)
        {
            return new DocumentValidationResult { IsValid = false, Error = "File exceeds 50 MB limit." };
        }

        var extension = Path.GetExtension(file.FileName ?? string.Empty);
        if (!AllowedExtensions.Contains(extension))
        {
            return new DocumentValidationResult { IsValid = false, Error = "Unsupported file extension. Allowed: PDF, JPEG, PNG, TIFF." };
        }

        if (!AllowedContentTypes.Contains(file.ContentType ?? string.Empty))
        {
            return new DocumentValidationResult { IsValid = false, Error = "Unsupported file content type. Allowed: application/pdf, image/jpeg, image/png, image/tiff." };
        }

        return new DocumentValidationResult { IsValid = true };
    }
}