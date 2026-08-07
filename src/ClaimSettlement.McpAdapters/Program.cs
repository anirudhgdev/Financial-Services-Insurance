using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Identity;
using ClaimSettlement.McpAdapters.Abstractions;
using ClaimSettlement.McpAdapters.Adapters;
using ClaimSettlement.McpAdapters.Configuration;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

// --- Authentication (Entra ID) ---
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration, "AzureAd");

// --- OpenAPI ---
builder.Services.AddOpenApi();

// --- Configuration ---
builder.Services.Configure<ExternalServiceSettings>(
    builder.Configuration.GetSection("ExternalServices"));

var externalServices = builder.Configuration
    .GetSection("ExternalServices")
    .Get<ExternalServiceSettings>() ?? new ExternalServiceSettings();

// --- Named HttpClients for external services ---
builder.Services.AddHttpClient("PolicyManagement", client =>
{
    client.BaseAddress = new Uri(externalServices.PolicyManagementApi.BaseUrl);
});

builder.Services.AddHttpClient("FraudDetection", client =>
{
    client.BaseAddress = new Uri(externalServices.FraudDetectionService.BaseUrl);
});

builder.Services.AddHttpClient("Notification", client =>
{
    client.BaseAddress = new Uri(externalServices.NotificationService.BaseUrl);
});

// --- Azure Document Intelligence ---
builder.Services.AddSingleton(_ =>
    new DocumentAnalysisClient(
        new Uri(externalServices.DocumentIntelligence.Endpoint),
        new DefaultAzureCredential()));

// --- Register MCP adapters ---
builder.Services.AddScoped<PolicyManagementMcpAdapter>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<PolicyManagementMcpAdapter>>();
    return new PolicyManagementMcpAdapter(factory.CreateClient("PolicyManagement"), logger);
});
builder.Services.AddScoped<IMcpTool<PolicyLookupRequest, PolicyLookupResponse>>(
    sp => sp.GetRequiredService<PolicyManagementMcpAdapter>());

builder.Services.AddScoped<FraudDetectionMcpAdapter>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<FraudDetectionMcpAdapter>>();
    return new FraudDetectionMcpAdapter(factory.CreateClient("FraudDetection"), logger);
});
builder.Services.AddScoped<IMcpTool<FraudScoreRequest, FraudScoreResponse>>(
    sp => sp.GetRequiredService<FraudDetectionMcpAdapter>());

builder.Services.AddScoped<DocumentIntelligenceMcpAdapter>();
builder.Services.AddScoped<IMcpTool<DocumentExtractRequest, DocumentExtractResponse>>(
    sp => sp.GetRequiredService<DocumentIntelligenceMcpAdapter>());

builder.Services.AddScoped<NotificationMcpAdapter>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<NotificationMcpAdapter>>();
    return new NotificationMcpAdapter(factory.CreateClient("Notification"), logger);
});
builder.Services.AddScoped<IMcpTool<NotifyRequest, NotifyResponse>>(
    sp => sp.GetRequiredService<NotificationMcpAdapter>());

var app = builder.Build();

// --- Middleware ---
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// --- MCP Adapter Endpoints ---

app.MapGet("/policy/{policyNumber}", async (
    string policyNumber,
    PolicyManagementMcpAdapter adapter,
    CancellationToken ct) =>
{
    var result = await adapter.InvokeAsync(new PolicyLookupRequest(policyNumber), ct);
    return result.Success ? Results.Ok(result.Result) : Results.StatusCode(502);
})
.WithName("GetPolicy")
.WithOpenApi();

app.MapPost("/fraud/score", async (
    FraudScoreRequest request,
    FraudDetectionMcpAdapter adapter,
    CancellationToken ct) =>
{
    var result = await adapter.InvokeAsync(request, ct);
    return result.Success ? Results.Ok(result.Result) : Results.StatusCode(502);
})
.WithName("ScoreFraud")
.WithOpenApi();

app.MapPost("/extract", async (
    DocumentExtractRequest request,
    DocumentIntelligenceMcpAdapter adapter,
    CancellationToken ct) =>
{
    var result = await adapter.InvokeAsync(request, ct);
    return result.Success ? Results.Ok(result.Result) : Results.StatusCode(502);
})
.WithName("ExtractDocument")
.WithOpenApi();

app.MapPost("/notify", async (
    NotifyRequest request,
    NotificationMcpAdapter adapter,
    CancellationToken ct) =>
{
    var result = await adapter.InvokeAsync(request, ct);
    return result.Success ? Results.Ok(result.Result) : Results.StatusCode(502);
})
.WithName("SendNotification")
.WithOpenApi();

// --- Health Endpoints ---

app.MapGet("/health/live", () => Results.Ok(new { Status = "Healthy" }))
    .WithName("HealthLive")
    .ExcludeFromDescription();

app.MapGet("/health/ready", () => Results.Ok(new { Status = "Ready" }))
    .WithName("HealthReady")
    .ExcludeFromDescription();

app.Run();
