using Azure.Storage.Blobs;
using ClaimSettlement.Domain.Entities;
using ClaimSettlement.Infrastructure.Azure;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace ClaimSettlement.Api.Claims;

public sealed class ClaimIntakeService : IClaimIntakeService
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(24);

    private readonly IMemoryCache _memoryCache;
    private readonly ClaimSettlementDbContext _dbContext;
    private readonly IClaimIntakeValidationService _validationService;
    private readonly IClaimDuplicateGuard _duplicateGuard;
    private readonly IDocumentUploadPolicy _documentUploadPolicy;
    private readonly BlobServiceClient? _blobServiceClient;
    private readonly AzureStorageOptions _storageOptions;
    private readonly IProviderConfigurationService _providerConfigurationService;

    public ClaimIntakeService(
        IMemoryCache memoryCache,
        ClaimSettlementDbContext dbContext,
        IClaimIntakeValidationService validationService,
        IClaimDuplicateGuard duplicateGuard,
        IDocumentUploadPolicy documentUploadPolicy,
        IOptions<AzureStorageOptions> storageOptions,
        IProviderConfigurationService providerConfigurationService,
        BlobServiceClient? blobServiceClient = null)
    {
        _memoryCache = memoryCache;
        _dbContext = dbContext;
        _validationService = validationService;
        _duplicateGuard = duplicateGuard;
        _documentUploadPolicy = documentUploadPolicy;
        _blobServiceClient = blobServiceClient;
        _storageOptions = storageOptions.Value;
        _providerConfigurationService = providerConfigurationService;
    }

    public async Task<ClaimIntakeConversationResponse> ContinueConversationAsync(
        ClaimIntakeConversationRequest request,
        string providerId,
        string claimantId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var session = GetOrCreateSession(request.SessionId, providerId, claimantId, ct);
        MergeInputIntoSession(session, request);
        session.LastUpdatedUtc = DateTime.UtcNow;
        _memoryCache.Set(GetCacheKey(session.SessionId), session, SessionTtl);

        var providerConfig = await _providerConfigurationService.GetConfigurationAsync(providerId, ct);
        var claimType = session.CollectedFields.GetValueOrDefault("ClaimType", string.Empty);
        var gaps = _validationService.GetMandatoryFieldGaps(
            session.CollectedFields,
            claimType,
            providerId,
            providerConfig.ClaimTypeMandatoryFields,
            providerConfig.SupportedClaimTypes);

        var prompt = gaps.Count == 0
            ? "All mandatory fields are complete. Upload documents and finalize intake."
            : $"Please provide: {string.Join(", ", gaps.Select(x => x.FieldName))}.";

        return new ClaimIntakeConversationResponse
        {
            SessionId = session.SessionId,
            ClaimId = session.ClaimId,
            Prompt = prompt,
            MissingFields = gaps,
            CollectedFields = new Dictionary<string, string>(session.CollectedFields, StringComparer.OrdinalIgnoreCase),
            IsReadyForSubmission = gaps.Count == 0
        };
    }

    public async Task<CompleteClaimIntakeResponse> CompleteAsync(
        CompleteClaimIntakeRequest request,
        string providerId,
        string claimantId,
        CancellationToken ct)
    {
        var session = GetExistingSession(request.SessionId, providerId, claimantId);
        if (session is null)
        {
            return new CompleteClaimIntakeResponse
            {
                Created = false,
                Message = "Intake session not found or expired. Start intake again."
            };
        }

        var providerConfig = await _providerConfigurationService.GetConfigurationAsync(providerId, ct);
        var claimType = session.CollectedFields.GetValueOrDefault("ClaimType", string.Empty);
        var gaps = _validationService.GetMandatoryFieldGaps(
            session.CollectedFields,
            claimType,
            providerId,
            providerConfig.ClaimTypeMandatoryFields,
            providerConfig.SupportedClaimTypes);

        if (gaps.Count > 0)
        {
            return new CompleteClaimIntakeResponse
            {
                Created = false,
                Message = "Mandatory fields are still missing.",
                MissingFields = gaps
            };
        }

        var dateOfLoss = DateTime.Parse(session.CollectedFields["DateOfLoss"], CultureInfo.InvariantCulture);
        var policyNumber = session.CollectedFields["PolicyNumber"];

        var duplicateClaimId = await _duplicateGuard.FindDuplicateClaimAsync(
            providerId,
            claimantId,
            policyNumber,
            dateOfLoss,
            ct);

        if (duplicateClaimId.HasValue && !request.ConfirmDuplicate)
        {
            return new CompleteClaimIntakeResponse
            {
                Created = false,
                RequiresDuplicateConfirmation = true,
                ExistingClaimId = duplicateClaimId,
                Message = "Potential duplicate claim found for the same policy and date of loss in the last 24 hours. Confirm to continue."
            };
        }

        var documentsCount = await CountUploadedDocumentsAsync(providerId, session.ClaimId, ct);
        if (documentsCount == 0)
        {
            return new CompleteClaimIntakeResponse
            {
                Created = false,
                Message = "At least one supporting document must be uploaded before submission."
            };
        }

        var existingClaim = await _dbContext.Claims
            .FirstOrDefaultAsync(x => x.ProviderId == providerId && x.ClaimId == session.ClaimId, ct);

        if (existingClaim is null)
        {
            var now = DateTime.UtcNow;
            var claim = new Claim
            {
                ClaimId = session.ClaimId,
                ProviderId = providerId,
                PolicyNumber = policyNumber,
                ClaimantId = claimantId,
                DateOfLoss = dateOfLoss,
                ClaimType = claimType,
                LossAmount = decimal.Parse(session.CollectedFields["LossAmount"], CultureInfo.InvariantCulture),
                Status = "INTAKE_COMPLETE",
                CreatedAt = now,
                UpdatedAt = now
            };

            _dbContext.Claims.Add(claim);
            _dbContext.AgentOutputs.Add(new AgentOutput
            {
                OutputId = Guid.NewGuid(),
                ClaimId = claim.ClaimId,
                AgentId = "ClaimIntake",
                OutputPayload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    notificationEventType = "INTAKE_CONFIRMED",
                    message = "Your claim intake has been received and processing has started.",
                    eventTimestampUtc = now,
                    claimStatus = "INTAKE_COMPLETE"
                }),
                CreatedAt = now,
                SchemaVersion = "1.0"
            });

            await _dbContext.SaveChangesAsync(ct);
        }

        _memoryCache.Remove(GetCacheKey(session.SessionId));

        return new CompleteClaimIntakeResponse
        {
            Created = true,
            ClaimId = session.ClaimId,
            Message = "Claim intake completed successfully."
        };
    }

    public async Task<DocumentUploadResponse> UploadDocumentAsync(
        Guid claimId,
        IFormFile file,
        string providerId,
        CancellationToken ct)
    {
        var existingCount = await CountUploadedDocumentsAsync(providerId, claimId, ct);
        var validation = _documentUploadPolicy.Validate(file, existingCount);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.Error);
        }

        var containerClient = GetRequiredBlobServiceClient().GetBlobContainerClient(_storageOptions.DocumentsContainerName);
        await containerClient.CreateIfNotExistsAsync(cancellationToken: ct);

        var safeName = Path.GetFileName(file.FileName);
        var blobPath = $"{providerId}/{claimId}/{Guid.NewGuid():N}_{safeName}";
        var blob = containerClient.GetBlobClient(blobPath);

        await using var stream = file.OpenReadStream();
        await blob.UploadAsync(stream, overwrite: false, cancellationToken: ct);

        return new DocumentUploadResponse
        {
            ClaimId = claimId,
            BlobPath = blobPath,
            ContentType = file.ContentType,
            SizeBytes = file.Length
        };
    }

    private IntakeSessionState GetOrCreateSession(string? sessionId, string providerId, string claimantId, CancellationToken ct)
    {
        var existing = GetExistingSession(sessionId, providerId, claimantId);
        if (existing is not null)
        {
            return existing;
        }

        var createdSession = new IntakeSessionState
        {
            SessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId,
            ClaimId = Guid.NewGuid(),
            ProviderId = providerId,
            ClaimantId = claimantId,
            LastUpdatedUtc = DateTime.UtcNow
        };

        _memoryCache.Set(GetCacheKey(createdSession.SessionId), createdSession, SessionTtl);

        ct.ThrowIfCancellationRequested();
        return createdSession;
    }

    private IntakeSessionState? GetExistingSession(string? sessionId, string providerId, string claimantId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        if (!_memoryCache.TryGetValue(GetCacheKey(sessionId), out IntakeSessionState? session) || session is null)
        {
            return null;
        }

        if (!string.Equals(session.ProviderId, providerId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(session.ClaimantId, claimantId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return session;
    }

    private static string GetCacheKey(string sessionId) => $"claim-intake:{sessionId}";

    private static void MergeInputIntoSession(IntakeSessionState session, ClaimIntakeConversationRequest request)
    {
        if (request.Fields is not null)
        {
            foreach (var pair in request.Fields)
            {
                if (!string.IsNullOrWhiteSpace(pair.Value))
                {
                    session.CollectedFields[pair.Key.Trim()] = pair.Value.Trim();
                }
            }
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return;
        }

        // Basic key:value message parsing to support simple structured multi-turn capture.
        foreach (var segment in request.Message.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = segment.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                session.CollectedFields[parts[0]] = parts[1];
            }
        }
    }

    private async Task<int> CountUploadedDocumentsAsync(string providerId, Guid claimId, CancellationToken ct)
    {
        var containerClient = GetRequiredBlobServiceClient().GetBlobContainerClient(_storageOptions.DocumentsContainerName);
        var prefix = $"{providerId}/{claimId}/";

        var count = 0;
        await foreach (var _ in containerClient.GetBlobsAsync(prefix: prefix, cancellationToken: ct))
        {
            count++;
        }

        return count;
    }

    private BlobServiceClient GetRequiredBlobServiceClient()
    {
        return _blobServiceClient
            ?? throw new InvalidOperationException("Blob storage is not configured for this environment.");
    }

}