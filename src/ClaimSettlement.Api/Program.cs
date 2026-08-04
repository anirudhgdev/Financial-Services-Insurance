using ClaimSettlement.Api.Authorization;
using ClaimSettlement.Api.Identity;
using ClaimSettlement.Domain.Identity;
using ClaimSettlement.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
