# Azure Infrastructure Bootstrap

This folder provisions the task 1.5 baseline infrastructure:

- Azure SQL Server + database
- Blob Storage with `claims-documents` and `eval-reports` containers
- Azure OpenAI account + GPT-4o deployment
- Azure AI Search service
- Log Analytics + Application Insights workspace

## Deploy

```powershell
az group create --name rg-claim-settlement-stg --location eastus
az deployment group create --resource-group rg-claim-settlement-stg --template-file infra/azure/main.bicep --parameters infra/azure/main.staging.bicepparam
```

## Notes

- Replace `sqlAdminPassword` in the parameter file with a secure value or pass it at deploy time.
- `storageAccountName` and other global names must be unique.
- Azure Search index creation is done by application startup/migration code in later tasks.

## Staging Database Schema + RLS

Apply schema migrations to staging:

```powershell
dotnet dotnet-ef database update \
	--project src/ClaimSettlement.Infrastructure/ClaimSettlement.Infrastructure.csproj \
	--startup-project src/ClaimSettlement.Api/ClaimSettlement.Api.csproj \
	--connection "<staging-sql-connection-string>"
```

Apply the provider row-level security policy script:

```powershell
sqlcmd -S <staging-sql-server>.database.windows.net -d <staging-db-name> -G -i infra/azure/sql/20260731-provider-rls.sql
```

The application must set `SESSION_CONTEXT('ProviderId')` for each authenticated request before querying provider-scoped tables.
