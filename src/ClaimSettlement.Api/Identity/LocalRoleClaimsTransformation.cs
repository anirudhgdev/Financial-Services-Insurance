using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using System.Security.Claims;

namespace ClaimSettlement.Api.Identity;

public sealed class LocalRoleClaimsTransformation : IClaimsTransformation
{
    private readonly ClaimSettlementDbContext _dbContext;

    public LocalRoleClaimsTransformation(ClaimSettlementDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = principal.Identity as ClaimsIdentity;
        var userId = principal.FindFirstValue(ClaimConstants.Oid) ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var providerId = principal.FindFirstValue("provider_id") ?? principal.FindFirstValue(ClaimConstants.TenantId);
        if (identity is null || !identity.IsAuthenticated || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(providerId)) return principal;

        var localRoles = await _dbContext.ProviderUserRoles
            .Where(role => role.ProviderId == providerId && role.UserId == userId)
            .Select(role => role.Role)
            .ToListAsync();
        foreach (var role in localRoles.Where(role => !principal.IsInRole(role)))
        {
            identity.AddClaim(new System.Security.Claims.Claim(ClaimConstants.Roles, role));
        }

        return principal;
    }
}