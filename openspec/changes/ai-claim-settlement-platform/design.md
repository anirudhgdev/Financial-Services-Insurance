## Context

See [proposal.md](proposal.md) for motivation. The platform is greenfield — no existing codebase to migrate. Key constraints: all compute runs on Azure; authentication is exclusively via Microsoft Entra ID; the solution must be deployable to the Microsoft Marketplace as a configurable multi-tenant offering; the multi-agent pipeline must be observable end-to-end and produce explainable, auditable decisions.

The specs define the observable behavior contracts. This document explains the architectural decisions that implement them.

## Goals / Non-Goals

**Goals:**

- Define the solution architecture for a multi-agent claim settlement system on .NET 9 / ASP.NET Core + Angular.
- Establish the MAF Orchestrator design: agent contract, context model, retry strategy, and state persistence.
- Specify data models, API surface, and integration contracts for all external services.
- Define the security architecture (AuthN, AuthZ, data isolation, secret management).
- Establish the observability stack (OpenTelemetry → Application Insights).
- Define the AI Evaluation Harness architecture.

**Non-Goals:**

- Detailed UI wireframes (handled in frontend implementation tasks).
- Infrastructure-as-Code (Bicep/Terraform) — separate infra deliverable.
- Billing and payment processing for settlement disbursement.
- Mobile app (web-only Angular frontend for this phase).

---

## Decisions

### D1: MAF Orchestrator as a hosted .NET service with a durable state machine

**Decision**: The MAF Orchestrator is implemented as a dedicated ASP.NET Core hosted service that manages claim pipelines as durable state machines persisted in Azure SQL. Each claim's pipeline state (current step, completed agent outputs, errors) is a first-class entity in the database, not held in memory.

**Rationale**: Durable state enables resumption after restarts, supports the concurrency requirement (up to 100 simultaneous claims per provider), and provides an authoritative audit trail without a separate workflow engine dependency.

**Alternatives considered**:
- Azure Durable Functions: Rejected — adds a separate runtime dependency and complicates the MAF agent integration model.
- In-memory state only: Rejected — violates the pipeline-recovery requirement (spec: `multi-agent-orchestration` → pipeline state persistence).

---

### D2: Agents as strongly-typed .NET classes implementing `IClaimAgent<TInput, TOutput>`

**Decision**: Each of the seven agents is a .NET class implementing a typed interface `IClaimAgent<TInput, TOutput>` registered in the DI container. The orchestrator resolves agents by type, passes the context DTO, and deserializes the typed output. MAF's tool-calling capabilities are used for external service calls within each agent.

**Rationale**: Strong typing catches contract mismatches at compile time, simplifies testing (mock the interface), and enables the orchestrator to validate agent outputs against schemas before downstream propagation.

**Context object model**:
```
ClaimAgentContext
├── ClaimId (Guid)
├── ClaimRecord (snapshot)
├── UpstreamOutputs (Dictionary<string, JsonDocument>)
├── ProviderConfig (ProviderConfiguration)
└── UserIdentity (ClaimsIdentity)
```

---

### D3: Azure AI Foundry for LLM model access via Microsoft Copilot SDK

**Decision**: All natural-language generation (summaries, reasoning narratives, fraud explanations, conversational intake) and structured extraction tasks use models deployed in Azure AI Foundry, accessed through the Microsoft Copilot SDK. Model selection (GPT-4o, GPT-4o-mini, or future models) is configurable per agent via deployment name in Foundry, not hardcoded. A single `AzureOpenAIClient` is registered as a scoped service and shared across agents, connecting to the Foundry inference endpoint.

**Rationale**: Azure AI Foundry provides enterprise-grade model serving with:
- **Model flexibility** — Deploy any LLM (GPT-4o, GPT-4o-mini, future models) without code changes; switch via configuration
- **Unified model lifecycle management** — Versioning, rollback, A/B testing, quota enforcement per provider
- **Cost optimization** — Route reasoning tasks to GPT-4o, summarization to GPT-4o-mini, keeping per-claim costs down
- **Seamless Copilot SDK integration** — Native support via Foundry inference endpoints
- **Built-in scaling + quotas** — Rate limiting and token management per provider

Copilot SDK provides the conversational interface for the Claim Intake Agent and standardizes prompt management. Function-calling capability (supported by most modern LLMs) enables reliable structured output from document analysis and settlement reasoning. A shared client reduces cold-start latency.

**Alternatives considered**:
- Direct Azure OpenAI SDK: Rejected — loses model versioning, quota management, and model-agnostic flexibility that Foundry provides.
- Hardcoding to GPT-4o only: Rejected — limits future model upgrades, cost optimization, and A/B testing.
- Separate LLM clients per agent: Rejected — increases connection overhead and complicates token-usage tracking for cost-per-claim metrics.

---

### D4: External service calls via MCP Server adapters

**Decision**: The Policy Management API, Fraud Detection Service, and Notification Service are each wrapped in a thin MCP Server adapter (.NET minimal API) that exposes a consistent tool interface to MAF agents. Agents call these services as MAF tools, not directly as HTTP clients.

**Rationale**: MCP abstraction decouples agents from service-specific SDKs, enables mock injection for testing, and lets providers swap or configure service endpoints without changing agent code.

**Interface pattern**:
```
IMcpTool<TRequest, TResponse>
├── PolicyManagementTool
├── FraudDetectionTool
├── DocumentIntelligenceTool
└── NotificationTool
```

---

### D5: Azure SQL as the system of record; Azure Blob Storage for documents; Azure AI Search for knowledge retrieval

**Decision**:
- **Azure SQL**: Claims, pipeline state, agent outputs, audit logs, provider configuration, adjuster assignments.
- **Azure Blob Storage**: Raw uploaded documents (isolated per claim/provider by container path). Soft-delete enabled; 90-day retention policy.
- **Azure AI Search**: Indexed policy documents and claim history to support RAG-based policy validation and fraud pattern context for the Azure OpenAI calls.

**Rationale**: Azure SQL provides ACID guarantees for state persistence and audit immutability. Blob Storage separates binary document payloads from structured data. AI Search enables semantic lookup of policy terms for coverage reasoning without embedding policy logic in the LLM prompt.

---

### D6: Microsoft Entra ID with RBAC for authentication and multi-tenant isolation

**Decision**: All API endpoints require a valid Entra ID bearer token. RBAC roles defined: `Customer`, `Adjuster`, `ProviderAdmin`, `PlatformAdmin`. Every data-access query is filtered by `ProviderId` extracted from the token claims. Row-level security in Azure SQL enforces provider isolation as a defense-in-depth measure.

**Rationale**: Entra ID is the stated auth requirement. Provider isolation via claim-based filtering satisfies the multi-tenancy isolation spec without a complex sharding architecture.

---

### D7: OpenTelemetry SDK with Azure Monitor exporter

**Decision**: All projects reference the `OpenTelemetry.Extensions.Hosting` package. Instrumentation is auto-registered for ASP.NET Core, HttpClient, and Entity Framework. Custom `ActivitySource` spans are emitted for each agent invocation. The Azure Monitor exporter sends traces and metrics to Application Insights. No additional observability vendor is introduced.

**Rationale**: OpenTelemetry is the CNCF standard; the Azure Monitor exporter is a first-party package with zero extra infrastructure cost. Application Insights provides the alerting and dashboard surface required for production operations.

---

### D8: AI Evaluation Harness as a standalone .NET console tool

**Decision**: The evaluation harness is a standalone .NET 9 console application (`ClaimSettlement.EvalHarness`) that submits test cases against the claim API, collects agent outputs, computes metrics, and writes JSON + Markdown reports to Azure Blob Storage. It is invoked from CI/CD via `dotnet run --project EvalHarness -- run --env <env> --dataset <version>`.

**Rationale**: A standalone tool is independently runnable, testable, and deployable to CI/CD without coupling to the main application's deployment lifecycle. It reuses the same HTTP client and auth infrastructure as the main app.

---

## Risks / Trade-offs

| Risk | Mitigation |
|---|---|
| Azure OpenAI rate limits causing pipeline throttling under load | Implement token-bucket rate limiting in the shared `AzureOpenAIClient` wrapper; configure retry-with-jitter (max 3 attempts). Monitor TPM/RPM metrics in Application Insights and alert at 80% utilization. Foundry quotas are enforced per provider — throttle gracefully when limits approached. |
| MCP Server adapter adds latency to external service calls | Measure adapter overhead during load testing. If overhead exceeds 50 ms P95, consider direct HTTP calls with an adapter interface for testability only. |
| Azure Document Intelligence confidence degradation on poor-quality scans | Flag low-confidence fields (< 0.80) for human review rather than blocking the pipeline. Track extraction confidence distribution in Application Insights. |
| Fraud Detection Service unavailability forcing fallback to rule-only scoring | Rule-based fallback is defined in the fraud-detection spec. Add a circuit breaker (Polly) with a 30-second open window. Alert on circuit-open events. |
| Azure SQL becomes a throughput bottleneck at high concurrency | Partition the `ClaimPipelineState` table by `ProviderId`. Add read replicas for reporting queries. Index on `ClaimId`, `ProviderId`, `Status`, `CreatedAt`. |
| Hallucination in settlement reasoning narrative misleads adjusters | Reasoning narrative is advisory only — the structured decision fields (verdict, amount) are computed deterministically by rules. Harness validates narrative facts against claim record on every evaluation run. |
| Token cost per claim exceeds budget at scale | Cost-per-claim metric tracked in evaluation harness and Application Insights. Prompt length optimized; GPT-4o-mini used for summarization tasks where GPT-4o is not required. |
| Provider configuration changes applied to in-flight claims | Configuration is snapshot-loaded at claim start and stored in pipeline state. In-flight claims always use the configuration snapshot from intake time. |

## Migration Plan

This is a greenfield deployment. No data migration is required.

**Deployment sequence**:
1. Provision Azure infrastructure (Azure SQL, Blob Storage, Azure AI Foundry with GPT-4o deployment, AI Search, Application Insights, Entra ID app registrations).
2. Configure Foundry model deployments (GPT-4o for reasoning, GPT-4o-mini for summarization) and obtain inference endpoints.
3. Deploy MCP Server adapters (Policy, Fraud, Notification, DocumentIntelligence).
4. Deploy the backend API and MAF Orchestrator service (with Foundry endpoint credentials in Key Vault).
5. Deploy the Angular frontend.
6. Seed provider configuration for the first insurance provider.
7. Run the evaluation harness against the staging environment with the full test dataset.
8. Promote to production after harness pass rate ≥ 95% and P95 latency < 30 seconds.

**Rollback**: Each service is independently versioned and deployed via container images. Rollback is a redeployment of the prior container tag. Azure SQL schema changes use additive-only migrations (no destructive DDL in the first release).

## Open Questions

- **Q1**: What is the expected peak claim submission rate (claims/hour) for the initial production provider? This determines whether the default concurrency of 100 simultaneous claims is sufficient or needs to be raised at infrastructure provisioning time.
- **Q2**: Does the Notification Service support idempotent message IDs natively, or must the harness implement deduplication at the adapter level? This affects the MCP adapter design for the Notification tool.
- **Q3**: Is there a requirement to support on-premises Policy Management APIs (as opposed to cloud-hosted REST APIs)? This could require an API Gateway or VPN integration that is not in scope for the current design.
