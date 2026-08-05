using System.Text.Json;

namespace ClaimSettlement.Api.Claims;

public sealed class ClaimIntakeValidationService : IClaimIntakeValidationService
{
    private static readonly string[] DefaultMandatoryFields =
    [
        "PolicyNumber",
        "ClaimantName",
        "DateOfLoss",
        "ClaimType",
        "DescriptionOfLoss",
        "LossAmount",
        "ContactInformation"
    ];

    public IReadOnlyList<ClaimFieldGap> GetMandatoryFieldGaps(
        IReadOnlyDictionary<string, string> fields,
        string claimType,
        string providerId,
        string mandatoryFieldConfigJson,
        string supportedClaimTypesJson)
    {
        var gaps = new List<ClaimFieldGap>();

        var normalizedClaimType = claimType.Trim();
        if (!IsSupportedClaimType(normalizedClaimType, supportedClaimTypesJson))
        {
            gaps.Add(new ClaimFieldGap
            {
                FieldName = "ClaimType",
                IsBlocking = true,
                Message = "Unsupported claim type for this provider."
            });

            return gaps;
        }

        var requiredFields = ResolveMandatoryFields(normalizedClaimType, mandatoryFieldConfigJson);

        foreach (var required in requiredFields)
        {
            if (!fields.TryGetValue(required, out var value) || string.IsNullOrWhiteSpace(value))
            {
                gaps.Add(new ClaimFieldGap
                {
                    FieldName = required,
                    IsBlocking = true,
                    Message = $"{required} is required before submission."
                });
            }
        }

        return gaps;
    }

    private static bool IsSupportedClaimType(string claimType, string supportedClaimTypesJson)
    {
        if (string.IsNullOrWhiteSpace(claimType))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(supportedClaimTypesJson) || supportedClaimTypesJson == "[]")
        {
            return true;
        }

        try
        {
            var supported = JsonSerializer.Deserialize<List<string>>(supportedClaimTypesJson) ?? [];
            return supported.Any(x => string.Equals(x, claimType, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return true;
        }
    }

    private static IReadOnlyList<string> ResolveMandatoryFields(string claimType, string mandatoryFieldConfigJson)
    {
        var required = new HashSet<string>(DefaultMandatoryFields, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(mandatoryFieldConfigJson) || mandatoryFieldConfigJson == "{}")
        {
            return required.ToList();
        }

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(mandatoryFieldConfigJson);
            if (map is null)
            {
                return required.ToList();
            }

            foreach (var pair in map)
            {
                if (!string.Equals(pair.Key, claimType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var field in pair.Value)
                {
                    if (!string.IsNullOrWhiteSpace(field))
                    {
                        required.Add(field.Trim());
                    }
                }
            }
        }
        catch
        {
            // Keep defaults if provider config JSON is malformed.
        }

        return required.ToList();
    }
}