# Claim Settlement AI Platform

## Offer summary

Claim Settlement AI Platform is a configurable, multi-tenant insurance claims workflow that combines conversational intake, document analysis, policy validation, fraud detection, settlement recommendations, adjuster review, and lifecycle notifications.

## Customer value

- Accelerate low-risk claim handling with structured AI-assisted automation.
- Route fraud-risk and ambiguous claims to adjusters with an auditable review package.
- Isolate provider data through Microsoft Entra ID, provider-scoped queries, and Azure SQL row-level security.
- Monitor claim outcomes, latency, agent reliability, fraud distributions, notifications, and evaluation cost through Application Insights.

## Included components

- ASP.NET Core Claim Settlement API and durable pipeline orchestrator.
- Claim Intake, Document Analysis, Policy Validation, Fraud Detection, Settlement Decision, Human Review, and Notification agents.
- MCP adapters for policy, fraud, document intelligence, and notification services.
- Angular customer, adjuster, and provider administration portals.
- Azure infrastructure template for Azure SQL, Blob Storage, Azure OpenAI, Azure AI Search, Log Analytics, and Application Insights.
- Evaluation harness with accuracy, latency, fraud, quality, human-review, and cost reporting.

## Deployment prerequisites

- Azure subscription with permissions to deploy the template.
- Microsoft Entra ID tenant and application-registration administration access.
- Azure OpenAI capacity for the included GPT-4o deployment.
- Notification, policy-management, and fraud-scoring service endpoints.
- Azure Key Vault and managed identities for production secrets and service access.

## Support and onboarding

Deploy `marketplace/deployment-template.bicep`, then follow `docs/deployment-runbook.md` for identity configuration, database migration and row-level-security activation, first-provider onboarding, and release validation.

## Data handling

The platform stores provider-scoped claim, audit, and workflow data in Azure SQL, documents in private Azure Blob Storage, and observability data in Application Insights. Customers configure retention, service endpoints, model capacity, and provider-specific workflow thresholds for their environment.
