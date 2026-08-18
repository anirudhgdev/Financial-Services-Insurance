using ClaimSettlement.Api.Authorization;
using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure.Observability;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaimSettlement.Api.Providers;

public sealed class ProvidersController : Controllers.BaseApiController
{
    private readonly IProviderConfigurationService _providerConfigurationService;
    private readonly IProviderContextAccessor _providerContextAccessor;
    private readonly ClaimSettlementDbContext _dbContext;
    private readonly IAuditLogger _auditLogger;

    public ProvidersController(
        IProviderConfigurationService providerConfigurationService,
        IProviderContextAccessor providerContextAccessor,
        ClaimSettlementDbContext dbContext,
        IAuditLogger auditLogger)
    {
        _providerConfigurationService = providerConfigurationService;
        _providerContextAccessor = providerContextAccessor;
        _dbContext = dbContext;
        _auditLogger = auditLogger;
    }

    [HttpGet("{providerId}/config")]
    [Authorize(Policy = AuthorizationPolicies.ProviderOrPlatformAdmin)]
    [ProducesResponseType(typeof(ProviderConfigurationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProviderConfigurationResponse>> GetConfiguration(
        [FromRoute] string providerId,
        CancellationToken ct)
    {
        if (!CanManageProvider(providerId))
        {
            return Forbid();
        }

        var config = await _providerConfigurationService.GetConfigurationAsync(providerId, ct);
        return Ok(ToResponse(config));
    }

    [HttpPut("{providerId}/config")]
    [Authorize(Policy = AuthorizationPolicies.ProviderOrPlatformAdmin)]
    [ProducesResponseType(typeof(ProviderConfigurationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProviderConfigurationResponse>> UpdateConfiguration(
        [FromRoute] string providerId,
        [FromBody] UpdateProviderConfigurationRequest request,
        CancellationToken ct)
    {
        if (!CanManageProvider(providerId))
        {
            return Forbid();
        }

        if (request.ManualReviewFraudThreshold is < 0.30m or > 0.90m)
        {
            return BadRequest(new { error = "ManualReviewFraudThreshold must be between 0.30 and 0.90." });
        }

        var saved = await _providerConfigurationService.UpsertConfigurationAsync(
            new ClaimSettlement.Domain.Entities.ProviderConfiguration
            {
                ProviderId = providerId,
                ProviderName = string.IsNullOrWhiteSpace(request.ProviderName) ? "Default" : request.ProviderName.Trim(),
                ManualReviewFraudThreshold = request.ManualReviewFraudThreshold,
                ManualReviewClaimAmountThreshold = request.ManualReviewClaimAmountThreshold,
                DeduplicationWindowDays = request.DeduplicationWindowDays,
                InformationRequestDeadlineDays = request.InformationRequestDeadlineDays,
                AdjusterSlaPeriodHours = request.AdjusterSlaPeriodHours,
                SupportedClaimTypes = string.IsNullOrWhiteSpace(request.SupportedClaimTypes) ? "[]" : request.SupportedClaimTypes,
                SupportedNotificationChannels = string.IsNullOrWhiteSpace(request.SupportedNotificationChannels) ? "[]" : request.SupportedNotificationChannels,
                PipelineConcurrencyLimit = request.PipelineConcurrencyLimit,
                IsActive = request.IsActive,
                ClaimTypeMandatoryFields = string.IsNullOrWhiteSpace(request.ClaimTypeMandatoryFields) ? "{}" : request.ClaimTypeMandatoryFields,
                CoverageMappingRules = string.IsNullOrWhiteSpace(request.CoverageMappingRules) ? "{}" : request.CoverageMappingRules,
                ExclusionSets = string.IsNullOrWhiteSpace(request.ExclusionSets) ? "{}" : request.ExclusionSets,
                AlwaysManualClaimTypes = string.IsNullOrWhiteSpace(request.AlwaysManualClaimTypes) ? "[]" : request.AlwaysManualClaimTypes
            },
            ct);

        await _auditLogger.AppendAsync(new AuditLogEntry
        {
            ProviderId = providerId,
            EventType = "PROVIDER_CONFIGURATION_UPDATED",
            ActorId = _providerContextAccessor.UserId,
            ActorType = "ProviderAdmin",
            Payload = new
            {
                request.ManualReviewFraudThreshold,
                request.ManualReviewClaimAmountThreshold,
                request.PipelineConcurrencyLimit,
                request.IsActive
            }
        }, ct);

        return Ok(ToResponse(saved));
    }

    [HttpGet("{providerId}/audit-log")]
    [Authorize(Policy = AuthorizationPolicies.ProviderOrPlatformAdmin)]
    [Produces("application/x-ndjson")]
    public async Task<IActionResult> ExportAuditLog(
        [FromRoute] string providerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 200,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken ct = default)
    {
        if (!CanManageProvider(providerId))
        {
            return Forbid();
        }

        if (page < 1)
        {
            return BadRequest(new { error = "Page must be greater than or equal to 1." });
        }

        if (pageSize is < 1 or > 1000)
        {
            return BadRequest(new { error = "PageSize must be between 1 and 1000." });
        }

        var query = _dbContext.AuditLogs
            .AsNoTracking()
            .Where(x => x.ProviderId == providerId);
        if (fromUtc.HasValue)
        {
            query = query.Where(x => x.Timestamp >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(x => x.Timestamp <= toUtc.Value);
        }

        var entries = await query
            .OrderBy(x => x.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var lines = entries.Select(entry => JsonSerializer.Serialize(new
        {
            entry.EntryId,
            entry.ClaimId,
            entry.ProviderId,
            entry.EventType,
            entry.ActorId,
            entry.ActorType,
            entry.Payload,
            entry.Timestamp
        }));

        var content = string.Join('\n', lines);
        if (content.Length > 0)
        {
            content += "\n";
        }

        var bytes = Encoding.UTF8.GetBytes(content);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        Response.Headers["X-Content-SHA256"] = sha256;
        Response.Headers["X-Audit-Page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Response.Headers["X-Audit-Page-Size"] = pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return File(bytes, "application/x-ndjson", $"audit-{providerId}-p{page}.jsonl");
    }

    [HttpGet("{providerId}/users")]
    [Authorize(Policy = AuthorizationPolicies.ProviderAdmin)]
    [ProducesResponseType(typeof(IReadOnlyList<ProviderUserAccessResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProviderUserAccessResponse>>> GetProviderUsers(string providerId, CancellationToken ct)
    {
        if (!CanManageProvider(providerId)) return Forbid();

        var users = await _dbContext.ProviderUserMemberships
            .Where(user => user.ProviderId == providerId)
            .Include(user => user.Roles)
            .OrderByDescending(user => user.LastAccessedAt)
            .AsNoTracking()
            .ToListAsync(ct);
        return Ok(users.Select(user => new ProviderUserAccessResponse
        {
            UserId = user.UserId,
            Email = user.Email,
            FirstAccessedAtUtc = user.FirstAccessedAt,
            LastAccessedAtUtc = user.LastAccessedAt,
            Roles = user.Roles.Select(role => role.Role).Order().ToList()
        }).ToList());
    }

    [HttpPut("{providerId}/users/{userId}/roles")]
    [Authorize(Policy = AuthorizationPolicies.ProviderAdmin)]
    [ProducesResponseType(typeof(ProviderUserAccessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProviderUserAccessResponse>> UpdateProviderUserRoles(
        string providerId,
        string userId,
        UpdateProviderUserRolesRequest request,
        CancellationToken ct)
    {
        if (!CanManageProvider(providerId)) return Forbid();
        var allowedRoles = new[] { AppRoles.Customer, AppRoles.Adjuster, AppRoles.ProviderAdmin };
        var roles = request.Roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (roles.Any(role => !allowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase)))
        {
            return BadRequest(new { error = "Roles must be Customer, Adjuster, or ProviderAdmin." });
        }

        var membership = await _dbContext.ProviderUserMemberships
            .Include(user => user.Roles)
            .FirstOrDefaultAsync(user => user.ProviderId == providerId && user.UserId == userId, ct);
        if (membership is null) return NotFound();

        _dbContext.ProviderUserRoles.RemoveRange(membership.Roles);
        var now = DateTime.UtcNow;
        membership.Roles = roles.Select(role => new ClaimSettlement.Domain.Entities.ProviderUserRole
        {
            ProviderId = providerId,
            UserId = userId,
            Role = role,
            AssignedAt = now,
            AssignedByUserId = _providerContextAccessor.UserId
        }).ToList();
        await _dbContext.SaveChangesAsync(ct);
        await _auditLogger.AppendAsync(new AuditLogEntry
        {
            ProviderId = providerId,
            EventType = "PROVIDER_USER_ROLES_UPDATED",
            ActorId = _providerContextAccessor.UserId,
            ActorType = "ProviderAdmin",
            Payload = new { targetUserId = userId, roles }
        }, ct);

        return Ok(new ProviderUserAccessResponse
        {
            UserId = membership.UserId,
            Email = membership.Email,
            FirstAccessedAtUtc = membership.FirstAccessedAt,
            LastAccessedAtUtc = membership.LastAccessedAt,
            Roles = roles
        });
    }

    private bool CanManageProvider(string providerId)
    {
        if (_providerContextAccessor.Roles.Contains(AppRoles.PlatformAdmin, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(_providerContextAccessor.ProviderId, providerId, StringComparison.OrdinalIgnoreCase);
    }

    private static ProviderConfigurationResponse ToResponse(ClaimSettlement.Domain.Entities.ProviderConfiguration config)
    {
        return new ProviderConfigurationResponse
        {
            ProviderId = config.ProviderId,
            ProviderName = config.ProviderName,
            ManualReviewFraudThreshold = config.ManualReviewFraudThreshold,
            ManualReviewClaimAmountThreshold = config.ManualReviewClaimAmountThreshold,
            DeduplicationWindowDays = config.DeduplicationWindowDays,
            InformationRequestDeadlineDays = config.InformationRequestDeadlineDays,
            AdjusterSlaPeriodHours = config.AdjusterSlaPeriodHours,
            SupportedClaimTypes = config.SupportedClaimTypes,
            SupportedNotificationChannels = config.SupportedNotificationChannels,
            PipelineConcurrencyLimit = config.PipelineConcurrencyLimit,
            ClaimTypeMandatoryFields = config.ClaimTypeMandatoryFields,
            CoverageMappingRules = config.CoverageMappingRules,
            ExclusionSets = config.ExclusionSets,
            AlwaysManualClaimTypes = config.AlwaysManualClaimTypes,
            IsActive = config.IsActive
        };
    }
}
