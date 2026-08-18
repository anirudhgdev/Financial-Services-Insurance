using System.Text.Json;

namespace ClaimSettlement.Api.Evaluation;

public sealed class CreateEvaluationRunRequest
{
    public required string DatasetVersion { get; init; }
}

public sealed class EvaluationRunResponse
{
    public Guid RunId { get; init; }

    public string DatasetVersion { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }
}

public sealed class EvaluationClaimResultsResponse
{
    public Guid RunId { get; init; }

    public Guid ClaimId { get; init; }

    public string Status { get; init; } = string.Empty;

    public IReadOnlyList<EvaluationAgentOutputResponse> AgentOutputs { get; init; } = Array.Empty<EvaluationAgentOutputResponse>();

    public IReadOnlyList<EvaluationToolInvocationResponse> ToolInvocations { get; init; } = Array.Empty<EvaluationToolInvocationResponse>();
}

public sealed class EvaluationAgentOutputResponse
{
    public string AgentId { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }

    public JsonElement Output { get; init; }
}

public sealed class EvaluationToolInvocationResponse
{
    public string AgentId { get; init; } = string.Empty;

    public string ToolName { get; init; } = string.Empty;

    public DateTime InvokedAtUtc { get; init; }

    public string Outcome { get; init; } = string.Empty;
}