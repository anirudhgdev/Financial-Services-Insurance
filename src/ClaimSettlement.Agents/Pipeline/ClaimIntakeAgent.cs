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

    private readonly IClaimIntakeConversationService _conversationService;

    public ClaimIntakeAgent(IClaimIntakeConversationService conversationService)
    {
        _conversationService = conversationService;
    }

    public ClaimIntakeTokenUsage? LastTokenUsage => _conversationService.LastTokenUsage;

    public async Task<ClaimIntakeResult> InvokeAsync(ClaimAgentContext context, ClaimIntakeInput input, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var missing = MandatoryFields
            .Where(field => !input.CollectedFields.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
            .ToList();

        var fallbackPrompt = missing.Count == 0
            ? "All required intake fields are complete. Please proceed with supporting document upload."
            : $"To continue your claim intake, provide: {string.Join(", ", missing)}.";

        var generatedPrompt = await CollectResponseAsync(input, ct);

        return new ClaimIntakeResult
        {
            Prompt = string.IsNullOrWhiteSpace(generatedPrompt) ? fallbackPrompt : generatedPrompt,
            MissingFields = missing,
            ReadyForSubmission = missing.Count == 0
        };
    }

    private async Task<string> CollectResponseAsync(ClaimIntakeInput input, CancellationToken ct)
    {
        var response = new System.Text.StringBuilder();
        await foreach (var chunk in StreamPromptAsync(input, ct))
        {
            response.Append(chunk);
        }

        return response.ToString();
    }

    public async IAsyncEnumerable<string> StreamPromptAsync(
        ClaimIntakeInput input,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (_conversationService.IsConfigured)
        {
            await foreach (var chunk in _conversationService.StreamResponseAsync(input, ct))
            {
                yield return chunk;
            }

            yield break;
        }

        var missing = MandatoryFields
            .Where(field => !input.CollectedFields.TryGetValue(field, out var value) || string.IsNullOrWhiteSpace(value))
            .ToList();

        yield return missing.Count == 0
            ? "All required intake fields are complete. Please proceed with supporting document upload."
            : $"To continue your claim intake, provide: {string.Join(", ", missing)}.";
    }
}
