using System.Security.Claims;

namespace ClaimSettlement.Domain.Identity;

/// <summary>
/// Provides access to the provider-scoped identity of the authenticated caller.
/// </summary>
public interface IProviderContextAccessor
{
    /// <summary>
    /// The provider identifier used for multi-tenant data isolation.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Entra ID app roles assigned to the caller.
    /// </summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>
    /// The object identifier (oid) of the signed-in user or service principal.
    /// </summary>
    string UserId { get; }

    /// <summary>
    /// The email or user principal name of the caller, when available.
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// The full claims identity of the authenticated caller.
    /// </summary>
    ClaimsIdentity Identity { get; }

    /// <summary>
    /// Indicates whether a caller is authenticated and provider context is available.
    /// </summary>
    bool IsAuthenticated { get; }
}
