namespace ClaimSettlement.Api.Providers;

public sealed class UpdateProviderConfigurationRequest
{
    public string ProviderName { get; init; } = string.Empty;

    public decimal ManualReviewFraudThreshold { get; init; }

    public decimal ManualReviewClaimAmountThreshold { get; init; }

    public int DeduplicationWindowDays { get; init; }

    public int InformationRequestDeadlineDays { get; init; }

    public int AdjusterSlaPeriodHours { get; init; }

    public string SupportedClaimTypes { get; init; } = "[]";

    public string SupportedNotificationChannels { get; init; } = "[]";

    public int PipelineConcurrencyLimit { get; init; }

    public string ClaimTypeMandatoryFields { get; init; } = "{}";

    public string CoverageMappingRules { get; init; } = "{}";

    public string ExclusionSets { get; init; } = "{}";

    public string AlwaysManualClaimTypes { get; init; } = "[]";

    public bool IsActive { get; init; } = true;
}

public sealed class ProviderConfigurationResponse
{
    public string ProviderId { get; init; } = string.Empty;

    public string ProviderName { get; init; } = string.Empty;

    public decimal ManualReviewFraudThreshold { get; init; }

    public decimal ManualReviewClaimAmountThreshold { get; init; }

    public int DeduplicationWindowDays { get; init; }

    public int InformationRequestDeadlineDays { get; init; }

    public int AdjusterSlaPeriodHours { get; init; }

    public string SupportedClaimTypes { get; init; } = "[]";

    public string SupportedNotificationChannels { get; init; } = "[]";

    public int PipelineConcurrencyLimit { get; init; }

    public string ClaimTypeMandatoryFields { get; init; } = "{}";

    public string CoverageMappingRules { get; init; } = "{}";

    public string ExclusionSets { get; init; } = "{}";

    public string AlwaysManualClaimTypes { get; init; } = "[]";

    public bool IsActive { get; init; }
}
