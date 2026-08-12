# Claim Settlement Application Insights Dashboard

## Scope

Create a shared Application Insights workbook named **Claim Settlement Operations** and bind it to the environment's Application Insights resource. Use a default time range of 24 hours, with a dashboard parameter named `TimeRange`.

The application emits metrics through the `ClaimSettlement.Metrics` meter. In Application Insights, query them from `customMetrics`; span and dependency diagnostics are available through `traces`, `requests`, and `dependencies`.

## Dashboard Panels

### Claims Pipeline Metrics

Display claim outcomes per hour, split by outcome.

```kusto
customMetrics
| where name == "claims_per_hour"
| extend outcome = tostring(customDimensions.outcome)
| summarize Claims = sum(value) by bin(timestamp, 1h), outcome
| render timechart
```

### Pipeline Duration

Display P50, P95, and P99 end-to-end duration in seconds, split by outcome.

```kusto
customMetrics
| where name == "pipeline_duration_seconds"
| extend outcome = tostring(customDimensions.outcome)
| summarize P50 = percentile(value, 50), P95 = percentile(value, 95), P99 = percentile(value, 99) by bin(timestamp, 15m), outcome
| render timechart
```

Set an alert when P95 exceeds 30 seconds for two consecutive evaluation windows.

### Agent Error Rate

Display agent executions, failures, and error percentage by agent.

```kusto
let executions = customMetrics
| where name == "agent_executions_total"
| extend agent = tostring(customDimensions.agent)
| summarize Executions = sum(value) by agent;
let errors = customMetrics
| where name == "agent_errors_total"
| extend agent = tostring(customDimensions.agent)
| summarize Errors = sum(value) by agent;
executions
| join kind=leftouter errors on agent
| extend Errors = coalesce(Errors, 0.0), ErrorRate = 100.0 * Errors / Executions
| project agent, Executions, Errors, ErrorRate
| order by ErrorRate desc
```

Set an alert when an agent has more than five failures or an error rate above 5% in 15 minutes.

### Fraud Score Distribution

Display a histogram of fraud scores across the 0.0 to 1.0 range.

```kusto
customMetrics
| where name == "fraud_score"
| summarize Claims = count() by ScoreBand = bin(value, 0.1)
| order by ScoreBand asc
| render columnchart
```

### Notification Delivery Rate

Display successful and failed notification counts and the delivery percentage by lifecycle event type.

```kusto
let delivered = customMetrics
| where name == "notification_delivered_total"
| extend eventType = tostring(customDimensions.eventType)
| summarize Delivered = sum(value) by eventType;
let failed = customMetrics
| where name == "notification_failed_total"
| extend eventType = tostring(customDimensions.eventType)
| summarize Failed = sum(value) by eventType;
delivered
| join kind=fullouter failed on eventType
| extend eventType = coalesce(eventType, eventType1), Delivered = coalesce(Delivered, 0.0), Failed = coalesce(Failed, 0.0)
| extend DeliveryRate = 100.0 * Delivered / (Delivered + Failed)
| project eventType, Delivered, Failed, DeliveryRate
| order by DeliveryRate asc
```

Set an alert when delivery rate falls below 99% in 15 minutes.

### Cost Per Claim Trend

The evaluation harness stores benchmark reports in the `eval-reports` Blob container. Add a workbook parameter for the exported report data source and use the report's `costPerCompletedClaimUsd`, `totalCostUsd`, and per-agent cost records to show:

- Mean and P95 cost per completed claim by evaluation run.
- Total cost by agent.
- Cost trend by evaluation run timestamp.

Alert when cost per completed claim exceeds the provider's approved budget.

### SLA Breach Rate

Display claims routed to `SLA_BREACHED` compared with all human-review outcomes.

```kusto
customMetrics
| where name == "claims_per_hour"
| extend outcome = tostring(customDimensions.outcome)
| where outcome in ("SLA_BREACHED", "MANUAL_REVIEW", "MANUAL_REVIEW_PENDING")
| summarize Claims = sum(value) by bin(timestamp, 1h), outcome
| render timechart
```

Set an alert for any SLA breach in production.

## Operational Drilldown

Add a linked log view for a selected claim ID:

```kusto
union traces, dependencies, requests
| where customDimensions.claim_id == "{ClaimId}" or customDimensions.claimId == "{ClaimId}"
| project timestamp, itemType, name, message, success, duration, customDimensions
| order by timestamp asc
```

Add a second linked view for failed pipeline executions:

```kusto
traces
| where severityLevel >= 3
| where message has "claim" or customDimensions.claim_id != ""
| project timestamp, message, customDimensions
| order by timestamp desc
```

## Deployment Checklist

1. Open the environment's Application Insights resource in Azure portal.
2. Create the shared workbook and add the panels above.
3. Save it to the `Claim Settlement` resource group and grant operations staff read access.
4. Configure the listed alerts through Azure Monitor action groups.
5. Validate data after an evaluation run and after a human-review claim completes.
