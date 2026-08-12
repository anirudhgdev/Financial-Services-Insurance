using System.Globalization;
using System.Text.RegularExpressions;

namespace ClaimSettlement.EvalHarness;

internal static partial class HallucinationDetection
{
    public static HallucinationCheckResult Check(EvaluationCase testCase, Guid claimId, IEnumerable<string> narratives, IEnumerable<decimal> structuredAmounts)
    {
        var policyNumber = GetInputString(testCase, "policyNumber");
        var lossDate = GetInputString(testCase, "lossDate");
        var expectedAmounts = structuredAmounts.Cast<decimal?>().Append(GetInputDecimal(testCase, "lossAmount"))
            .Where(amount => amount.HasValue)
            .Select(amount => amount!.Value)
            .ToHashSet();
        var inconsistencies = new List<string>();

        foreach (var narrative in narratives.Where(text => !string.IsNullOrWhiteSpace(text)))
        {
            foreach (Match match in GuidPattern().Matches(narrative))
            {
                if (!Guid.TryParse(match.Value, out var citedClaimId) || citedClaimId != claimId)
                {
                    inconsistencies.Add($"claim ID {match.Value}");
                }
            }

            foreach (Match match in PolicyNumberPattern().Matches(narrative))
            {
                if (!string.Equals(match.Value, policyNumber, StringComparison.OrdinalIgnoreCase))
                {
                    inconsistencies.Add($"policy number {match.Value}");
                }
            }

            foreach (Match match in IsoDatePattern().Matches(narrative))
            {
                if (!string.Equals(match.Value, lossDate, StringComparison.OrdinalIgnoreCase))
                {
                    inconsistencies.Add($"date {match.Value}");
                }
            }

            foreach (Match match in AmountPattern().Matches(narrative))
            {
                if (decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var citedAmount) &&
                    !expectedAmounts.Contains(citedAmount))
                {
                    inconsistencies.Add($"amount {match.Value}");
                }
            }
        }

        return new HallucinationCheckResult(inconsistencies.Count == 0, inconsistencies.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static string? GetInputString(EvaluationCase testCase, string propertyName) =>
        testCase.ClaimInput.TryGetProperty(propertyName, out var property) ? property.ToString() : null;

    private static decimal? GetInputDecimal(EvaluationCase testCase, string propertyName) =>
        testCase.ClaimInput.TryGetProperty(propertyName, out var property) && property.TryGetDecimal(out var value) ? value : null;

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\b")]
    private static partial Regex GuidPattern();

    [GeneratedRegex(@"\b[A-Z]{2,10}-[A-Z0-9-]+\b", RegexOptions.IgnoreCase)]
    private static partial Regex PolicyNumberPattern();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b")]
    private static partial Regex IsoDatePattern();

    [GeneratedRegex(@"(?:\$|amount\s+)(\d+(?:\.\d{1,2})?)", RegexOptions.IgnoreCase)]
    private static partial Regex AmountPattern();
}

internal sealed record HallucinationCheckResult(bool IsConsistent, IReadOnlyList<string> Inconsistencies);