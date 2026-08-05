using ClaimSettlement.Agents;
using ClaimSettlement.Infrastructure;
using ClaimSettlement.Orchestrator;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddClaimSettlementInfrastructure(builder.Configuration);
builder.Services.AddClaimSettlementAgents();

builder.Services.Configure<OrchestratorOptions>(builder.Configuration.GetSection(OrchestratorOptions.SectionName));
builder.Services.AddHostedService<ClaimPipelineOrchestrator>();

var host = builder.Build();
host.Run();
