namespace ClaimSettlement.Domain.Identity;

/// <summary>
/// Role values registered as appRoles in the Microsoft Entra ID backend API application manifest.
/// </summary>
public static class AppRoles
{
    public const string Customer = "Customer";

    public const string Adjuster = "Adjuster";

    public const string ProviderAdmin = "ProviderAdmin";

    public const string PlatformAdmin = "PlatformAdmin";

    public static IReadOnlyCollection<string> All { get; } =
    [
        Customer,
        Adjuster,
        ProviderAdmin,
        PlatformAdmin
    ];
}
