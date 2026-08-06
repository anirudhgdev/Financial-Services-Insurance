using ClaimSettlement.Agents;
using Azure.Monitor.OpenTelemetry.Exporter;
using ClaimSettlement.Infrastructure;
using ClaimSettlement.Infrastructure.Observability;
using ClaimSettlement.Orchestrator;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddClaimSettlementInfrastructure(builder.Configuration);
builder.Services.AddClaimSettlementAgents();
builder.Services.AddScoped<ClaimSettlement.Agents.Pipeline.IHumanReviewQueueStore, SqlHumanReviewQueueStore>();

builder.Services.Configure<OrchestratorOptions>(builder.Configuration.GetSection(OrchestratorOptions.SectionName));

builder.Services.AddOpenTelemetry()
	.WithTracing(tracing =>
	{
		tracing
			.AddSource(ClaimTelemetry.ActivitySourceName)
			.AddSource("ClaimSettlement.Orchestrator")
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
			.AddHttpClientInstrumentation();

		var connectionString = builder.Configuration["AzureMonitor:ConnectionString"];
		if (!string.IsNullOrWhiteSpace(connectionString))
		{
			metrics.AddAzureMonitorMetricExporter(options => options.ConnectionString = connectionString);
		}
	});

builder.Services.AddHostedService<ClaimPipelineOrchestrator>();
builder.Services.AddHostedService<HumanReviewSlaTrackerService>();
builder.Services.AddHostedService<NotificationOutboxDispatcherService>();
builder.Services.AddHostedService<InformationRequestReminderService>();

var host = builder.Build();
host.Run();
