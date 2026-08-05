using ClaimSettlement.Domain.Entities;
using System.Security.Claims;
using System.Text.Json;
using DomainClaim = ClaimSettlement.Domain.Entities.Claim;

namespace ClaimSettlement.Agents.Models;

public sealed class ClaimAgentContext
{
    public required Guid ClaimId { get; init; }

    public required DomainClaim ClaimRecord { get; init; }

    public required IReadOnlyDictionary<string, JsonDocument> UpstreamOutputs { get; init; }

    public required ProviderConfiguration ProviderConfig { get; init; }

    public required ClaimsIdentity UserIdentity { get; init; }
}