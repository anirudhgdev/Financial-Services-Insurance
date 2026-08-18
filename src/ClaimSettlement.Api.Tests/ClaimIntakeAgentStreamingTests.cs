using ClaimSettlement.Agents.Models;
using ClaimSettlement.Agents.Pipeline;
using Xunit;

namespace ClaimSettlement.Api.Tests;

public sealed class ClaimIntakeAgentStreamingTests
{
    [Fact]
    public async Task StreamsConfiguredConversationChunksInOrder()
    {
        var agent = new ClaimIntakeAgent(new TestConversationService(true, ["Please provide ", "your policy number."]));

        var chunks = await CollectChunksAsync(agent.StreamPromptAsync(CreateInput(), CancellationToken.None));

        Assert.Equal(["Please provide ", "your policy number."], chunks);
    }

    [Fact]
    public async Task UsesMandatoryFieldPromptWhenFoundryIsNotConfigured()
    {
        var agent = new ClaimIntakeAgent(new TestConversationService(false, []));

        var chunks = await CollectChunksAsync(agent.StreamPromptAsync(CreateInput(), CancellationToken.None));

        Assert.Single(chunks);
        Assert.Contains("PolicyNumber", chunks[0]);
    }

    private static ClaimIntakeInput CreateInput() => new()
    {
        SessionId = "session-1",
        Message = "I need to file a claim.",
        CollectedFields = new Dictionary<string, string>()
    };

    private static async Task<List<string>> CollectChunksAsync(IAsyncEnumerable<string> stream)
    {
        var chunks = new List<string>();
        await foreach (var chunk in stream)
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private sealed class TestConversationService(bool isConfigured, IReadOnlyList<string> chunks)
        : IClaimIntakeConversationService
    {
        public bool IsConfigured => isConfigured;

        public ClaimIntakeTokenUsage? LastTokenUsage => null;

        public async IAsyncEnumerable<string> StreamResponseAsync(
            ClaimIntakeInput input,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();
                yield return chunk;
            }
        }
    }
}