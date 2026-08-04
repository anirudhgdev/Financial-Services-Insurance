using ClaimSettlement.Domain.Identity;

namespace ClaimSettlement.Api.Authorization;

public static class AuthorizationPolicies
{
    public const string Customer = AppRoles.Customer;

    public const string Adjuster = AppRoles.Adjuster;

    public const string ProviderAdmin = AppRoles.ProviderAdmin;

    public const string PlatformAdmin = AppRoles.PlatformAdmin;

    public const string AnyAuthenticated = "AnyAuthenticated";

    public const string ProviderOrPlatformAdmin = "ProviderOrPlatformAdmin";

    /// <summary>
    /// Adds RBAC authorization policies used across API controllers.
    /// </summary>
    public static void AddClaimSettlementAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Customer, policy => policy.RequireRole(AppRoles.Customer));
            options.AddPolicy(Adjuster, policy => policy.RequireRole(AppRoles.Adjuster));
            options.AddPolicy(ProviderAdmin, policy => policy.RequireRole(AppRoles.ProviderAdmin));
            options.AddPolicy(PlatformAdmin, policy => policy.RequireRole(AppRoles.PlatformAdmin));

            options.AddPolicy(AnyAuthenticated, policy => policy.RequireAuthenticatedUser());

            options.AddPolicy(ProviderOrPlatformAdmin, policy =>
                policy.RequireRole(AppRoles.ProviderAdmin, AppRoles.PlatformAdmin));
        });
    }
}
