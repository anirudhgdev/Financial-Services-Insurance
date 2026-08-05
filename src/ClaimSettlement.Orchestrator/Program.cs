using ClaimSettlement.Agents;
using ClaimSettlement.Infrastructure;
using ClaimSettlement.Orchestrator;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddClaimSettlementInfrastructure(builder.Configuration);
builder.Services.AddClaimSettlementAgents();
builder.Services.AddScoped<ClaimSettlement.Agents.Pipeline.IHumanReviewQueueStore, SqlHumanReviewQueueStore>();

builder.Services.Configure<OrchestratorOptions>(builder.Configuration.GetSection(OrchestratorOptions.SectionName));
builder.Services.AddHostedService<ClaimPipelineOrchestrator>();
builder.Services.AddHostedService<HumanReviewSlaTrackerService>();
builder.Services.AddHostedService<NotificationOutboxDispatcherService>();
builder.Services.AddHostedService<InformationRequestReminderService>();

var host = builder.Build();
host.Run();
