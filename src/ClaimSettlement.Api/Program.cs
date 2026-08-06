using ClaimSettlement.Api.Authorization;
using ClaimSettlement.Api.Claims;
using ClaimSettlement.Api.Identity;
using ClaimSettlement.Api.Observability;
using Azure.Monitor.OpenTelemetry.Exporter;
using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure.Azure;
using ClaimSettlement.Infrastructure.Observability;
using ClaimSettlement.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Microsoft.Identity.Web;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();

// Configure Microsoft Entra ID bearer token authentication.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

// Register RBAC authorization policies.
builder.Services.AddClaimSettlementAuthorization();

// Make the current HTTP context available to the provider context accessor.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IProviderContextAccessor, ProviderContextAccessor>();

// Register persistence and data-access services.
builder.Services.AddClaimSettlementInfrastructure(builder.Configuration);
builder.Services.AddClaimIntakeServices();

builder.Services.Configure<AzureOpenAIOptions>(builder.Configuration.GetSection(AzureOpenAIOptions.SectionName));
builder.Services.Configure<NotificationServiceOptions>(builder.Configuration.GetSection(NotificationServiceOptions.SectionName));

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddSource(ClaimTelemetry.ActivitySourceName)
            .AddSource("ClaimSettlement.Api")
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddEntityFrameworkCoreInstrumentation();

        var connectionString = builder.Configuration["AzureMonitor:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            tracing.AddAzureMonitorTraceExporter(options => options.ConnectionString = connectionString);
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(ClaimTelemetry.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        var connectionString = builder.Configuration["AzureMonitor:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            metrics.AddAzureMonitorMetricExporter(options => options.ConnectionString = connectionString);
        }
    });

builder.Services.AddHealthChecks()
    .AddCheck<SqlReadinessHealthCheck>("azure-sql", tags: ["ready"])
    .AddCheck<BlobStorageReadinessHealthCheck>("blob-storage", tags: ["ready"])
    .AddCheck<OpenAiReadinessHealthCheck>("azure-openai", tags: ["ready"])
    .AddCheck<NotificationServiceReadinessHealthCheck>("notification-service", tags: ["ready"]);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AuthenticationAuditMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = "Healthy" }));
    }
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(x => x.Key, x => x.Value.Status.ToString())
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}).AllowAnonymous();

app.Run();

public partial class Program;
