namespace ClaimSettlement.Api.Claims;

public sealed class ClaimIntakeConversationRequest
{
    public string? SessionId { get; init; }

    public string? Message { get; init; }

    public Dictionary<string, string>? Fields { get; init; }
}

public sealed class ClaimIntakeConversationResponse
{
    public required string SessionId { get; init; }

    public required Guid ClaimId { get; init; }

    public string Prompt { get; init; } = string.Empty;

    public IReadOnlyList<ClaimFieldGap> MissingFields { get; init; } = Array.Empty<ClaimFieldGap>();

    public IReadOnlyDictionary<string, string> CollectedFields { get; init; } = new Dictionary<string, string>();

    public bool IsReadyForSubmission { get; init; }
}

public sealed class CompleteClaimIntakeRequest
{
    public required string SessionId { get; init; }

    public bool ConfirmDuplicate { get; init; }

    public string? TranscriptReference { get; init; }
}

public sealed class CompleteClaimIntakeResponse
{
    public bool Created { get; init; }

    public bool RequiresDuplicateConfirmation { get; init; }

    public Guid? ExistingClaimId { get; init; }

    public Guid? ClaimId { get; init; }

    public string Message { get; init; } = string.Empty;

    public IReadOnlyList<ClaimFieldGap> MissingFields { get; init; } = Array.Empty<ClaimFieldGap>();
}

public sealed class ClaimFieldGap
{
    public required string FieldName { get; init; }

    public required bool IsBlocking { get; init; }

    public required string Message { get; init; }
}

public sealed class IntakeSessionState
{
    public required string SessionId { get; init; }

    public required Guid ClaimId { get; init; }

    public required string ProviderId { get; init; }

    public required string ClaimantId { get; init; }

    public DateTime LastUpdatedUtc { get; set; }

    public Dictionary<string, string> CollectedFields { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DocumentValidationResult
{
    public bool IsValid { get; init; }

    public string? Error { get; init; }
}

public sealed class DocumentUploadResponse
{
    public required Guid ClaimId { get; init; }

    public required string BlobPath { get; init; }

    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }
}