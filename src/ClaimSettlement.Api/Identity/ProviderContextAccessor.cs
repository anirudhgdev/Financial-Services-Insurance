using ClaimSettlement.Domain.Identity;
using Microsoft.Identity.Web;
using System.Security.Claims;

namespace ClaimSettlement.Api.Identity;

/// <summary>
/// Extracts provider-scoped identity information from the current HTTP user's Entra ID bearer token.
/// </summary>
public sealed class ProviderContextAccessor : IProviderContextAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ProviderContextAccessor> _logger;

    public ProviderContextAccessor(IHttpContextAccessor httpContextAccessor, ILogger<ProviderContextAccessor> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public ClaimsIdentity Identity => Principal.Identity as ClaimsIdentity
        ?? new ClaimsIdentity();

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated ?? false;

    public string ProviderId => GetProviderId();

    public IReadOnlyCollection<string> Roles => GetRoles();

    public string UserId => GetClaimValue(ClaimConstants.Oid)
        ?? GetClaimValue(ClaimConstants.Sub)
        ?? GetClaimValue(ClaimTypes.NameIdentifier)
        ?? string.Empty;

    public string? Email => GetClaimValue(ClaimConstants.PreferredUserName)
        ?? GetClaimValue(ClaimTypes.Email);

    private ClaimsPrincipal Principal => _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();

    private string? GetClaimValue(string claimType) =>
        Principal.FindFirst(claimType)?.Value;

    private string GetProviderId()
    {
        // Preferred: a custom provider_id claim emitted by Entra ID for this application.
        string? providerId = GetClaimValue("provider_id");

        // Fallbacks: tenant ID or domain hint claims commonly present in Entra tokens.
        providerId ??= GetClaimValue(ClaimConstants.TenantId);
        providerId ??= GetClaimValue("tenantid");
        providerId ??= GetClaimValue("iss")?.TrimEnd('/').Split('/').LastOrDefault();

        if (string.IsNullOrEmpty(providerId))
        {
            _logger.LogWarning("No provider identifier could be resolved from the authenticated token.");
            return string.Empty;
        }

        return providerId;
    }

    private IReadOnlyCollection<string> GetRoles()
    {
        return Principal
            .FindAll(ClaimConstants.Roles)
            .Select(c => c.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
