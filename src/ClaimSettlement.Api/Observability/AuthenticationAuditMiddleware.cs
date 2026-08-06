using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure.Observability;

namespace ClaimSettlement.Api.Observability;

public sealed class AuthenticationAuditMiddleware
{
    private readonly RequestDelegate _next;

    public AuthenticationAuditMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IAuditLogger auditLogger)
    {
        await _next(context);

        var identity = context.User.Identity;
        if (identity?.IsAuthenticated != true)
        {
            return;
        }

        var providerId = context.User.FindFirst("provider_id")?.Value
            ?? context.User.FindFirst("tid")?.Value
            ?? "unknown";

        var actorId = context.User.FindFirst("oid")?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? "unknown";

        var roleValues = context.User
            .FindAll("roles")
            .Select(x => x.Value)
            .ToArray();

        try
        {
            await auditLogger.AppendAsync(new AuditLogEntry
            {
                ProviderId = providerId,
                EventType = "AUTHENTICATED_REQUEST",
                ActorId = actorId,
                ActorType = "User",
                Payload = new
                {
                    path = context.Request.Path.Value,
                    method = context.Request.Method,
                    statusCode = context.Response.StatusCode,
                    roles = roleValues,
                    timestampUtc = DateTime.UtcNow
                }
            }, context.RequestAborted);
        }
        catch
        {
            // Do not fail the request pipeline if audit logging fails.
        }
    }
}
