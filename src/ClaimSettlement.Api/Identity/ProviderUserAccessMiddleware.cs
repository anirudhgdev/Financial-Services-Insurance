using ClaimSettlement.Infrastructure.Persistence;
using Microsoft.Identity.Web;
using System.Security.Claims;

namespace ClaimSettlement.Api.Identity;

public sealed class ProviderUserAccessMiddleware
{
    private readonly RequestDelegate _next;

    public ProviderUserAccessMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ClaimSettlementDbContext dbContext,
        IProviderSqlSessionContext providerSqlSessionContext)
    {
        var user = context.User;
        var userId = user.FindFirstValue(ClaimConstants.Oid) ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        var providerId = user.FindFirstValue("provider_id") ?? user.FindFirstValue(ClaimConstants.TenantId);
        using var providerSession = providerSqlSessionContext.Begin(providerId);
        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(providerId))
        {
            var membership = await dbContext.ProviderUserMemberships.FindAsync([providerId, userId], context.RequestAborted);
            if (membership is null)
            {
                membership = new ClaimSettlement.Domain.Entities.ProviderUserMembership
                {
                    ProviderId = providerId,
                    UserId = userId,
                    Email = user.FindFirstValue(ClaimConstants.PreferredUserName) ?? user.FindFirstValue(ClaimTypes.Email),
                    FirstAccessedAt = DateTime.UtcNow,
                    LastAccessedAt = DateTime.UtcNow
                };
                dbContext.ProviderUserMemberships.Add(membership);
            }
            else
            {
                membership.Email = user.FindFirstValue(ClaimConstants.PreferredUserName) ?? user.FindFirstValue(ClaimTypes.Email);
                membership.LastAccessedAt = DateTime.UtcNow;
            }

            await dbContext.SaveChangesAsync(context.RequestAborted);
        }

        await _next(context);
    }
}