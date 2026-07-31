# Microsoft Entra ID App Registration Setup

This folder contains baseline manifests and setup guidance for the three required Entra applications:

1. `claim-settlement-ui-spa` (frontend SPA)
2. `claim-settlement-api` (backend API)
3. `claim-settlement-mcp-adapters` (service-to-service adapters)

## Prerequisites

- Azure CLI installed and authenticated (`az login`)
- Rights to create app registrations in the target tenant
- Tenant ID available in environment variable `AZURE_TENANT_ID`

## 1) Create backend API app registration

```powershell
az ad app create --display-name "claim-settlement-api" --sign-in-audience AzureADMyOrg --required-resource-accesses @backend-api.required-resource-access.json --optional-claims @backend-api.optional-claims.json
```

## 2) Create frontend SPA app registration

```powershell
az ad app create --display-name "claim-settlement-ui-spa" --sign-in-audience AzureADMyOrg --web-redirect-uris "http://localhost:4200" "https://localhost:4200" --enable-access-token-issuance true --enable-id-token-issuance true
```

After API app creation, add delegated API permission from SPA to API scope:

- Scope name: `api://<API_APP_ID>/ClaimSettlement.Access`

## 3) Create MCP adapters service app registration

```powershell
az ad app create --display-name "claim-settlement-mcp-adapters" --sign-in-audience AzureADMyOrg
```

Create service principal and grant app role on API:

```powershell
az ad sp create --id <MCP_APP_ID>
az ad app permission add --id <MCP_APP_ID> --api <API_APP_ID> --api-permissions <APP_ROLE_ID>=Role
az ad app permission grant --id <MCP_APP_ID> --api <API_APP_ID>
```

## Role Design

Define app roles on backend API app:

- `Customer`
- `Adjuster`
- `ProviderAdmin`
- `PlatformAdmin`

Use the manifests in this folder as starting templates for role and scope definitions.
