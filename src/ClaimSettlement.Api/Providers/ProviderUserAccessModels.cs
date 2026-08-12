namespace ClaimSettlement.Api.Providers;

public sealed class ProviderUserAccessResponse
{
    public string UserId { get; init; } = string.Empty;
    public string? Email { get; init; }
    public DateTime FirstAccessedAtUtc { get; init; }
    public DateTime LastAccessedAtUtc { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}

public sealed class UpdateProviderUserRolesRequest
{
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}