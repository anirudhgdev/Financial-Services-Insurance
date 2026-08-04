using ClaimSettlement.Domain.Identity;

namespace ClaimSettlement.Domain.Entities;

public sealed class ProviderConfiguration : IProviderScoped
{
    public string ProviderId { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public decimal ManualReviewFraudThreshold { get; set; }

    public decimal ManualReviewClaimAmountThreshold { get; set; }

    public int DeduplicationWindowDays { get; set; }

    public int InformationRequestDeadlineDays { get; set; }

    public int AdjusterSlaPeriodHours { get; set; }

    public string SupportedClaimTypes { get; set; } = "[]";

    public string SupportedNotificationChannels { get; set; } = "[]";

    public int PipelineConcurrencyLimit { get; set; }

    public bool IsActive { get; set; }

    public string ClaimTypeMandatoryFields { get; set; } = "{}";

    public string CoverageMappingRules { get; set; } = "{}";

    public string ExclusionSets { get; set; } = "{}";

    public string AlwaysManualClaimTypes { get; set; } = "[]";
}
