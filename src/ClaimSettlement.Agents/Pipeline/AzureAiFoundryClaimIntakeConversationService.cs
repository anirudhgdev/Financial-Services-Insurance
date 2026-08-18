using Azure.AI.OpenAI;
using ClaimSettlement.Agents.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ClaimSettlement.Agents.Pipeline;

public sealed class AzureAiFoundryOptions
{
    public const string SectionName = "AzureAiFoundry";

    public string Endpoint { get; set; } = string.Empty;

    public string DeploymentName { get; set; } = string.Empty;

    public string Authentication { get; set; } = "ManagedIdentity";

    public bool IsConfigured =>
        Uri.TryCreate(Endpoint, UriKind.Absolute, out _) &&
        !Endpoint.Contains('<') &&
        !string.IsNullOrWhiteSpace(DeploymentName) &&
        string.Equals(Authentication, "ManagedIdentity", StringComparison.OrdinalIgnoreCase);
}

public interface IClaimIntakeConversationService
{
    bool IsConfigured { get; }

    ClaimIntakeTokenUsage? LastTokenUsage { get; }

    IAsyncEnumerable<string> StreamResponseAsync(ClaimIntakeInput input, CancellationToken ct);
}

public sealed record ClaimIntakeTokenUsage(long? InputTokenCount, long? OutputTokenCount);

public sealed class AzureAiFoundryClaimIntakeConversationService : IClaimIntakeConversationService
{
    private const string Instructions = """
        You are a claim-intake assistant for an insurance platform. Help the customer provide missing claim details.
        Do not make coverage or settlement decisions. Ask for only the next missing field and keep the response concise.
        """;

    private readonly AzureOpenAIClient? _client;
    private readonly AzureAiFoundryOptions _options;

    public AzureAiFoundryClaimIntakeConversationService(
        IOptions<AzureAiFoundryOptions> options,
        AzureOpenAIClient? client = null)
    {
        _options = options.Value;
        _client = client;
    }

    public bool IsConfigured => _client is not null && _options.IsConfigured;

    public ClaimIntakeTokenUsage? LastTokenUsage { get; private set; }

    public async IAsyncEnumerable<string> StreamResponseAsync(
        ClaimIntakeInput input,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (!IsConfigured)
        {
            yield break;
        }

        LastTokenUsage = null;
        var agent = _client!
            .GetChatClient(_options.DeploymentName)
            .AsIChatClient()
            .AsAIAgent(name: "ClaimIntake", instructions: Instructions);

        await foreach (var update in agent.RunStreamingAsync(input.Message, cancellationToken: ct))
        {
            var usage = update.Contents.OfType<UsageContent>().LastOrDefault()?.Details;
            if (usage is not null)
            {
                LastTokenUsage = new ClaimIntakeTokenUsage(usage.InputTokenCount, usage.OutputTokenCount);
            }

            if (!string.IsNullOrWhiteSpace(update.Text))
            {
                yield return update.Text;
            }
        }
    }
}