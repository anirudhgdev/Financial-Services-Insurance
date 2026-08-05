using ClaimSettlement.Domain.Entities;
using System.Security.Claims;
using System.Text.Json;

namespace ClaimSettlement.Agents.Models;

public sealed class ClaimAgentContext
{
    public required Guid ClaimId { get; init; }

    public required Claim ClaimRecord { get; init; }

    public required IReadOnlyDictionary<string, JsonDocument> UpstreamOutputs { get; init; }

    public required ProviderConfiguration ProviderConfig { get; init; }

    public required ClaimsIdentity UserIdentity { get; init; }
}