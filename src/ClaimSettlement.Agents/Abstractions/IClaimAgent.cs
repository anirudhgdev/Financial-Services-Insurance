using ClaimSettlement.Agents.Models;

namespace ClaimSettlement.Agents.Abstractions;

public interface IClaimAgent<TInput, TOutput>
{
    Task<TOutput> InvokeAsync(ClaimAgentContext context, TInput input, CancellationToken ct);
}