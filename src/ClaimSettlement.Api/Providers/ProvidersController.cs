using ClaimSettlement.Api.Authorization;
using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimSettlement.Api.Providers;

public sealed class ProvidersController : Controllers.BaseApiController
{
    private readonly IProviderConfigurationService _providerConfigurationService;
    private readonly IProviderContextAccessor _providerContextAccessor;

    public ProvidersController(
        IProviderConfigurationService providerConfigurationService,
        IProviderContextAccessor providerContextAccessor)
    {
        _providerConfigurationService = providerConfigurationService;
        _providerContextAccessor = providerContextAccessor;
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

        return Ok(ToResponse(saved));
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
