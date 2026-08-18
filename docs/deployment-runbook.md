# Claim Settlement Deployment Runbook

## Purpose

Use this runbook to provision and deploy a Claim Settlement environment, configure runtime settings through managed identity and Azure Key Vault, onboard the first provider, and validate the release.

## Prerequisites

- Azure subscription access to create or update the target resource group.
- Azure CLI and .NET 9 SDK installed.
- A Microsoft Entra ID application registration for the API and SPA, using the manifests in `entra/`.
- A managed identity for each deployed application and the Azure DevOps self-hosted agent pool `ClaimSettlement-ManagedIdentity`.
- A Key Vault holding all non-placeholder secrets. Do not commit values to `.bicepparam`, `appsettings*.json`, or pipeline YAML.

## 1. Provision infrastructure

1. Copy `infra/azure/main.staging.bicepparam` for the target environment and replace global resource names with unique values.
2. Leave `sqlAdminPassword` unset in source control. Pass it through a secure pipeline variable or retrieve it from Key Vault at deployment time.
3. Create the resource group and deploy the Bicep template:

```powershell
az group create --name rg-claim-settlement-stg --location eastus
az deployment group create `
  --resource-group rg-claim-settlement-stg `
  --template-file infra/azure/main.bicep `
  --parameters infra/azure/main.staging.bicepparam `
  --parameters sqlAdminPassword=$env:CLAIM_SETTLEMENT_SQL_ADMIN_PASSWORD
```

4. Record the deployment outputs: SQL server FQDN, Azure OpenAI endpoint, Application Insights connection string, storage account ID, and Azure AI Search name.

## 2. Configure identity and access

Assign least-privilege roles to each application managed identity:

| Identity | Required access |
| --- | --- |
| API and Orchestrator | SQL database access, `Storage Blob Data Contributor` for claim documents, Azure OpenAI inference access, Azure AI Search query access, and Key Vault secret read access |
| Evaluation pipeline identity | API application permission, `Storage Blob Data Contributor` for evaluation reports, Azure OpenAI inference access, and Key Vault secret read access |
| Azure DevOps agent pool identity | Key Vault secret read access and the deployment permissions required by the target resource group |

Configure the API and SPA app registrations from `infra/entra/backend-api.manifest.json` and `infra/entra/frontend-spa.manifest.json`. Grant the application roles required by the deployed provider users: `Customer`, `Adjuster`, `ProviderAdmin`, `PlatformAdmin`, and `EvaluationRunner`.

## 3. Configure application settings

Set runtime configuration through managed identity and Key Vault references. The following values are required:

| Setting | Source |
| --- | --- |
| `ConnectionStrings__ClaimSettlementDb` | Azure SQL connection string or managed-identity SQL configuration |
| `AzureAd__TenantId`, `AzureAd__ClientId`, `AzureAd__Audience` | Entra ID app registration |
| `Azure__OpenAI__Endpoint`, `Azure__OpenAI__DeploymentName` | Azure OpenAI deployment output |
| `Azure__Search__Endpoint`, `Azure__Search__IndexName` | Azure AI Search deployment |
| `Azure__Storage__BlobServiceUri`, `Azure__Storage__DocumentsContainerName` | Storage account and `claims-documents` container |
| `AzureMonitor__ConnectionString` | Application Insights deployment output |
| `AzureAiFoundry__Endpoint`, `AzureAiFoundry__DeploymentName`, `AzureAiFoundry__Authentication` | Azure AI Foundry configuration; use `ManagedIdentity` |
| `ExternalServices__NotificationService__BaseUrl` | Notification service endpoint |

Configure the Azure DevOps variable group `claim-settlement-evaluation` with the non-secret evaluation settings used by `azure-pipelines.yml`:

- `CLAIM_SETTLEMENT_EVALUATION_ENVIRONMENT`
- `CLAIM_SETTLEMENT_API_BASE_URL`
- `CLAIM_SETTLEMENT_API_AUDIENCE`
- `CLAIM_SETTLEMENT_EVALUATION_REPORTS_CONTAINER_URI`
- `CLAIM_SETTLEMENT_OPENAI_INPUT_USD_PER_MILLION`
- `CLAIM_SETTLEMENT_OPENAI_OUTPUT_USD_PER_MILLION`
- `CLAIM_SETTLEMENT_MIN_ACCURACY` (default `0.95`)
- `CLAIM_SETTLEMENT_MAX_P95_LATENCY_SECONDS` (default `30`)

Store secret values in Key Vault and expose them to the pipeline only through secret variable mappings.

## 4. Apply database schema and RLS

Apply additive EF Core migrations:

```powershell
dotnet ef database update `
  --project src/ClaimSettlement.Infrastructure/ClaimSettlement.Infrastructure.csproj `
  --startup-project src/ClaimSettlement.Api/ClaimSettlement.Api.csproj `
  --connection "<staging-sql-connection-string>"
```

Apply the provider RLS policy:

```powershell
sqlcmd -S <staging-sql-server>.database.windows.net `
  -d <staging-db-name> `
  -G `
  -i infra/azure/sql/20260731-provider-rls.sql
```

Verify that the application is configured with `ProviderSqlSessionConnectionInterceptor`. It sets `SESSION_CONTEXT('ProviderId')` for authenticated API requests and background claim execution, and clears it when no provider scope is active.

## 5. Deploy applications

1. Build and test the solution:

```powershell
dotnet restore ClaimSettlement.sln
dotnet build ClaimSettlement.sln --configuration Release --no-restore
dotnet test ClaimSettlement.sln --configuration Release --no-build --no-restore
```

2. Deploy the API, Orchestrator, MCP adapters, and Angular applications through the approved release pipeline.
3. Confirm all services use managed identity and Key Vault references before enabling production traffic.

## 6. Onboard the first provider

1. Create the provider configuration through the provider configuration API using a `ProviderAdmin` identity.
2. Set the provider ID, supported claim types, mandatory fields, fraud threshold, manual-review amount threshold, SLA period, pipeline concurrency limit, and notification channels.
3. Grant provider user memberships and roles for at least one customer, adjuster, provider administrator, and evaluation runner.
4. Submit a low-risk claim and a high-risk claim. Confirm each request has a provider claim and cannot access another provider's records.

## 7. Validate release

1. Verify liveness and readiness:

```powershell
Invoke-RestMethod https://<api-host>/health/live
Invoke-RestMethod https://<api-host>/health/ready
```

2. Run the evaluation harness using the managed-identity Azure DevOps pool:

```powershell
dotnet run --project src/ClaimSettlement.EvalHarness/ClaimSettlement.EvalHarness.csproj --configuration Release -- run --env staging --dataset v1
```

3. Promote only when evaluation accuracy is at least 95% and end-to-end P95 latency is at most 30 seconds.
4. Confirm JSON Lines and Markdown evaluation reports exist in the configured `eval-reports` container.
5. Review Application Insights for pipeline failures, notification delivery failures, fraud-score anomalies, and provider-isolation violations.

## Rollback

1. Stop release traffic and redeploy the previous application artifacts.
2. Keep database migrations additive; do not roll back data schema destructively.
3. Restore the prior provider configuration only through the audited configuration API.
4. Re-run readiness checks and the evaluation harness before reopening traffic.
