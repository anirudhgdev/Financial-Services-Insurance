# NuGet Feed Prerequisites

Two package references in this solution are expected to come from a Microsoft/private feed and are not available on public NuGet by default:

- Microsoft.AgentFramework
- Microsoft.CopilotSDK

If your organization provides these packages, configure the feed before restoring:

```powershell
nuget sources Add -Name <source-name> -Source <feed-url>
```

Or via .NET CLI:

```powershell
dotnet nuget add source <feed-url> --name <source-name>
```

After configuring the feed, run:

```powershell
dotnet restore ClaimSettlement.sln
```
