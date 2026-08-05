using ClaimSettlement.Agents.Abstractions;
using ClaimSettlement.Agents.Models;

namespace ClaimSettlement.Agents.Pipeline;

public sealed class ClaimIntakeAgent : IClaimAgent<ClaimIntakeInput, ClaimIntakeResult>
{
    private static readonly string[] MandatoryFields =
    [
        "PolicyNumber",
        "ClaimantName",
        "DateOfLoss",
        "ClaimType",
        "DescriptionOfLoss",
        "LossAmount",
        "ContactInformation"
    ];

    public Task<ClaimIntakeResult> InvokeAsync(ClaimAgentContext context, ClaimIntakeInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Placeholder conversational logic until Copilot SDK runtime wiring is available in this environment.
        var missing = MandatoryFields
            .Where(field => !input.CollectedFields.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
            .ToList();

        var prompt = missing.Count == 0
            ? "All required intake fields are complete. Please proceed with supporting document upload."
            : $"To continue your claim intake, provide: {string.Join(", ", missing)}.";

        return Task.FromResult(new ClaimIntakeResult
        {
            Prompt = prompt,
            MissingFields = missing,
            ReadyForSubmission = missing.Count == 0
        });
    }
}
