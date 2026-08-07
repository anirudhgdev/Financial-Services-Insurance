namespace ClaimSettlement.McpAdapters.Abstractions;

/// Generic interface for MCP tools. Agents use this to invoke external services uniformly.
public interface IMcpTool<TRequest, TResponse>
{
    string ToolName { get; }
    string Description { get; }
    Task<McpToolResult<TResponse>> InvokeAsync(TRequest request, CancellationToken cancellationToken = default);
}

public sealed record McpToolResult<T>(
    bool Success,
    T? Result,
    string? ErrorMessage,
    McpErrorCode? ErrorCode
);

public enum McpErrorCode
{
    InvalidInput,
    ServiceUnavailable,
    CircuitOpen,
    Timeout,
    Unknown
}
