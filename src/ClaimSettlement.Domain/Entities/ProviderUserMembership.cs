using ClaimSettlement.Domain.Identity;

namespace ClaimSettlement.Domain.Entities;

public sealed class ProviderUserMembership : IProviderScoped
{
    public string ProviderId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string? Email { get; set; }

    public DateTime FirstAccessedAt { get; set; }

    public DateTime LastAccessedAt { get; set; }

    public ICollection<ProviderUserRole> Roles { get; set; } = new List<ProviderUserRole>();
}

public sealed class ProviderUserRole : IProviderScoped
{
    public string ProviderId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public DateTime AssignedAt { get; set; }

    public string AssignedByUserId { get; set; } = string.Empty;

    public ProviderUserMembership? Membership { get; set; }
}