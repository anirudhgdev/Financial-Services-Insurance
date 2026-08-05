## 1. Solution Structure & Project Scaffolding

- [x] 1.1 Create the .NET 9 solution `ClaimSettlement.sln` with the following projects: `ClaimSettlement.Api`, `ClaimSettlement.Orchestrator`, `ClaimSettlement.Agents`, `ClaimSettlement.Domain`, `ClaimSettlement.Infrastructure`, `ClaimSettlement.McpAdapters`, `ClaimSettlement.EvalHarness`
- [x] 1.2 Add NuGet references: `Microsoft.AgentFramework`, `Microsoft.CopilotSDK`, `Azure.AI.OpenAI`, `Azure.Search.Documents`, `Azure.Storage.Blobs`, `Azure.AI.FormRecognizer`, `Microsoft.EntityFrameworkCore.SqlServer`, `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.AzureMonitor`, `Polly`, `Microsoft.Identity.Web`
- [x] 1.3 Create the Angular workspace `claim-settlement-ui` with modules: `claim-portal`, `adjuster-portal`, `admin-portal`, `copilot-chat`
- [x] 1.4 Configure Microsoft Entra ID app registrations for frontend SPA, backend API, and service-to-service (MCP adapters)
- [x] 1.5 Set up Azure infrastructure: Azure SQL database, Blob Storage containers, Azure OpenAI deployment (GPT-4o), Azure AI Search index, Application Insights workspace

## 2. Domain Model & Database Schema

- [x] 2.1 Define `Claim` entity with fields: `ClaimId`, `ProviderId`, `PolicyNumber`, `ClaimantId`, `DateOfLoss`, `ClaimType`, `LossAmount`, `Status`, `CreatedAt`, `UpdatedAt`
- [x] 2.2 Define `ClaimPipelineState` entity with fields: `ClaimId`, `ProviderId`, `CurrentStep`, `CompletedSteps` (JSON), `AgentOutputs` (JSON), `Status`, `StartedAt`, `CompletedAt`
- [x] 2.3 Define `AgentOutput` entity (per-agent, per-claim): `OutputId`, `ClaimId`, `AgentId`, `OutputPayload` (JSON), `CreatedAt`, `SchemaVersion`
- [x] 2.4 Define `AuditLog` entity: `EntryId`, `ClaimId`, `ProviderId`, `EventType`, `ActorId`, `ActorType`, `Payload` (JSON), `Timestamp` — with append-only enforcement
- [x] 2.5 Define `ProviderConfiguration` entity with all configurable fields from the `configurable-workflows` spec
- [x] 2.6 Define `AdjusterAssignment` entity: `AssignmentId`, `ClaimId`, `AdjusterId`, `AssignedAt`, `DecidedAt`, `Decision`, `Rationale`, `SettlementOverride`
- [x] 2.7 Write EF Core migrations (additive-only) for all entities; apply to Azure SQL (staging environment)
- [x] 2.8 Implement Row-Level Security policy in Azure SQL filtering all claim/audit tables by `ProviderId`

## 3. Authentication & Authorization

- [x] 3.1 Configure `Microsoft.Identity.Web` middleware in `ClaimSettlement.Api` to validate Entra ID bearer tokens
- [x] 3.2 Define RBAC roles (`Customer`, `Adjuster`, `ProviderAdmin`, `PlatformAdmin`) and register them in the Entra ID app manifest
- [x] 3.3 Implement `IProviderContextAccessor` that extracts `ProviderId` and roles from the authenticated token claims
- [x] 3.4 Add authorization policies to all API controllers; enforce provider-scoped data access in all repository queries
- [x] 3.5 Configure managed identity for service-to-service calls (API → Azure OpenAI, Azure SQL, Azure Blob Storage, Azure AI Search)

## 4. MAF Orchestrator

- [x] 4.1 Define `IClaimAgent<TInput, TOutput>` interface with `InvokeAsync(ClaimAgentContext context, TInput input, CancellationToken ct)` signature
- [x] 4.2 Implement `ClaimAgentContext` DTO with fields: `ClaimId`, `ClaimRecord`, `UpstreamOutputs`, `ProviderConfig`, `UserIdentity`
- [x] 4.3 Implement `ClaimPipelineOrchestrator` as an ASP.NET Core `BackgroundService` that reads pending claims from Azure SQL and executes the agent pipeline
- [x] 4.4 Implement durable pipeline state machine: persist step completion to `ClaimPipelineState` after each agent; support resume-from-last-step on restart
- [x] 4.5 Implement agent invocation with retry (2 retries, 2-second delay) and output schema validation; route to human review on exhausted retries
- [x] 4.6 Implement concurrency limiter (configurable max per provider, default 100) using `SemaphoreSlim`; queue excess claims in FIFO order
- [x] 4.7 Implement human-review branch: detect `MANUAL_REVIEW` recommendation and invoke `HumanReviewAgent`
- [x] 4.8 Emit OpenTelemetry `ActivitySource` spans for each agent invocation (agent name, claim ID, duration, outcome)

## 5. Claim Intake Agent

- [x] 5.1 Implement `ClaimIntakeAgent` using Microsoft Copilot SDK to drive a multi-turn conversational claim collection flow
- [x] 5.2 Implement mandatory field validation for all claim types; return structured gap list for missing fields
- [x] 5.3 Implement document upload endpoint `POST /api/v1/claims/{claimId}/documents` — validate file type (PDF, JPEG, PNG, TIFF), size (≤ 50 MB), and count (≤ 10); store in Azure Blob Storage
- [x] 5.4 Implement duplicate submission guard: query `Claim` table for matching policy number + date of loss within 24 hours; require explicit customer confirmation
- [x] 5.5 Create `Claim` record in Azure SQL with status `INTAKE_COMPLETE` and return claim ID to customer
- [x] 5.6 Write unit tests for mandatory field validation, duplicate guard, and document upload constraints

## 6. Document Analysis Agent

- [x] 6.1 Implement `DocumentAnalysisAgent` using Azure Document Intelligence to extract structured fields from each uploaded document
- [x] 6.2 Implement confidence threshold check (< 0.80 → `NEEDS_REVIEW`); persist raw extracted text and confidence scores
- [x] 6.3 Implement multi-document deduplication (> 90% textual overlap → `DUPLICATE` flag)
- [x] 6.4 Implement claim summarization using Azure OpenAI GPT-4o (100–300 word natural-language summary)
- [x] 6.5 Implement gap report generation: compare extracted fields to mandatory field set for the claim type; classify gaps as blocking or non-blocking
- [x] 6.6 Persist extraction results and gap report to `AgentOutput` table; trigger Notification Agent for blocking gaps
- [x] 6.7 Write unit tests for extraction, deduplication, summarization, and gap detection

## 7. Policy Validation Agent

- [x] 7.1 Implement `PolicyValidationAgent` that calls the Policy Management MCP tool to retrieve policy details by policy number
- [x] 7.2 Implement policy validity check (active on date of loss, not cancelled/lapsed); record verdict
- [x] 7.3 Implement coverage, exclusion, and deductible verification; compute net payable amount
- [x] 7.4 Implement eligibility determination (identity match, waiting period, premium payment status)
- [x] 7.5 Implement retry-with-backoff (3 attempts, 500 ms exponential backoff) for Policy Management API; fall back to `POLICY_CHECK_UNAVAILABLE` and human-review routing on failure
- [x] 7.6 Persist policy validation verdict to `AgentOutput` table
- [x] 7.7 Write unit tests for each verdict scenario (valid, expired, not found, excluded, partial coverage, eligibility checks)

## 8. Fraud Detection Agent

- [x] 8.1 Implement `FraudDetectionAgent` that calls the Fraud Detection Service MCP tool and combines results with rule-based pattern detection
- [x] 8.2 Implement composite risk score computation (weighted aggregation of service score, history score, pattern signals)
- [x] 8.3 Implement duplicate claim detection query (same policy, date of loss, claim type within configurable deduplication window)
- [x] 8.4 Implement pattern rules: high-frequency claimant, new-policy claim, round-amount detection, geographic anomaly
- [x] 8.5 Implement Polly circuit breaker for Fraud Detection Service (30-second open window); fall back to rule-only scoring with `FRAUD_SERVICE_UNAVAILABLE` flag
- [x] 8.6 Implement fraud signal explainability: generate structured explanation with signal weights and evidence using Azure OpenAI
- [x] 8.7 Persist fraud verdict and explanation to `AgentOutput` table
- [x] 8.8 Write unit tests for each fraud scenario (low, medium, high, duplicate, pattern rules, service unavailability)

## 9. Settlement Decision Agent

- [x] 9.1 Implement `SettlementDecisionAgent` that reads all upstream `AgentOutput` records and validates their presence
- [x] 9.2 Implement the weighted rule engine for `APPROVE`, `REJECT`, `MANUAL_REVIEW` recommendation logic (configurable rules per provider)
- [x] 9.3 Implement recommended settlement amount computation: `min(claimed, coverage_limit) - deductible`
- [x] 9.4 Implement confidence score computation; auto-escalate to `MANUAL_REVIEW` if confidence < 0.70
- [x] 9.5 Implement reasoning narrative generation using Azure OpenAI GPT-4o (150–500 words, referencing upstream agent evidence)
- [x] 9.6 Implement decision record immutability: persist to `AgentOutput` table as immutable; create new versioned record for overrides
- [x] 9.7 Write unit tests for all decision scenarios (auto-approve, reject-expired, reject-excluded, manual-high-fraud, manual-ambiguous, low-confidence escalation)

## 10. Human Review Agent & Adjuster Portal

- [x] 10.1 Implement `HumanReviewAgent` that places claims in the adjuster assignment queue in Azure SQL, applying workload and specialization routing
- [x] 10.2 Implement SLA tracker: background job checks queue every 5 minutes; escalate to supervisor and set `SLA_BREACHED` status for overdue claims
- [x] 10.3 Implement AI-assisted review package assembly: aggregate all agent outputs into a structured summary DTO for the adjuster portal
- [x] 10.4 Implement `POST /api/v1/claims/{claimId}/adjuster-decision` endpoint: validate rationale length (≥ 20 chars), persist decision, trigger notification
- [x] 10.5 Build Angular adjuster portal: claim queue view, claim detail with AI review package, decision submission form with rationale and settlement override
- [x] 10.6 Write unit tests for assignment routing, SLA breach detection, and decision capture validation

## 11. Notification Agent

- [ ] 11.1 Implement `NotificationAgent` as an MAF agent that accepts typed notification events and calls the Notification Service MCP tool
- [ ] 11.2 Implement at-least-once delivery with idempotent message ID generation (claim ID + event type + timestamp hash)
- [ ] 11.3 Implement retry-with-exponential-backoff (3 attempts) and dead-letter queue logging for failed deliveries
- [ ] 11.4 Implement customer communication preference lookup and channel filtering (email-only, SMS-only, or both)
- [ ] 11.5 Implement all lifecycle event notification triggers: intake confirmation, processing milestones, decision, human-review assignment, SLA delay, information requests with deadline
- [ ] 11.6 Implement information-request reminder job: 24 hours before deadline, send reminder; after deadline, escalate to `INFO_TIMEOUT`
- [ ] 11.7 Write unit tests for duplicate prevention, delivery retry, preference filtering, and deadline tracking

## 12. Configurable Workflows

- [ ] 12.1 Implement `ProviderConfigurationService` with a 5-minute cache (using `IMemoryCache`) on top of Azure SQL reads
- [ ] 12.2 Implement `GET /api/v1/providers/{providerId}/config` and `PUT /api/v1/providers/{providerId}/config` REST endpoints with `ProviderAdmin` role enforcement
- [ ] 12.3 Implement threshold validation on PUT: reject fraud threshold outside [0.30, 0.90] with 400 Bad Request
- [ ] 12.4 Implement supported claim-type and mandatory-field enforcement in the Claim Intake Agent
- [ ] 12.5 Implement `always_manual` routing override in the Settlement Decision Agent
- [ ] 12.6 Inject provider configuration snapshot into `ClaimAgentContext` at pipeline start; persist snapshot in `ClaimPipelineState`
- [ ] 12.7 Write unit tests for threshold validation, claim-type enforcement, and routing overrides

## 13. Audit & Observability

- [ ] 13.1 Implement `AuditLogger` service: append-only writes to `AuditLog` table; throw on any update/delete attempt
- [ ] 13.2 Integrate `AuditLogger` into all agents, orchestrator state transitions, API controllers (adjuster decisions, config changes), and auth middleware
- [ ] 13.3 Implement `GET /api/v1/providers/{providerId}/audit-log` export endpoint: paginated JSON Lines download with SHA-256 file hash; `PlatformAdmin` role only
- [ ] 13.4 Configure OpenTelemetry SDK: auto-instrument ASP.NET Core, HttpClient, EF Core; register custom `ActivitySource` for agents; export to Azure Monitor
- [ ] 13.5 Implement all custom Application Insights metrics: claims per hour (by outcome), pipeline duration histogram, fraud score distribution, agent error rate, notification delivery rate
- [ ] 13.6 Implement `/health/live` and `/health/ready` endpoints; readiness probe checks Azure SQL, Blob Storage, Azure OpenAI, Notification Service connectivity
- [ ] 13.7 Write integration tests for audit log append-only enforcement and health endpoint responses

## 14. MCP Server Adapters

- [ ] 14.1 Implement `PolicyManagementMcpAdapter` (ASP.NET Core minimal API): `GET /policy/{policyNumber}` → wraps Policy Management API with retry and circuit-breaker
- [ ] 14.2 Implement `FraudDetectionMcpAdapter`: `POST /fraud/score` → wraps Fraud Detection Service with Polly circuit-breaker
- [ ] 14.3 Implement `DocumentIntelligenceMcpAdapter`: `POST /extract` → wraps Azure Document Intelligence SDK
- [ ] 14.4 Implement `NotificationMcpAdapter`: `POST /notify` → wraps Notification Service with idempotent message ID header
- [ ] 14.5 Register all adapters as MAF tools in the agent DI container using `IMcpTool<TRequest, TResponse>` interface
- [ ] 14.6 Write integration tests for each adapter (mock external services; verify retry, circuit-breaker, and error-mapping behavior)

## 15. Angular Frontend

- [ ] 15.1 Implement MSAL Angular integration with Entra ID; guard all routes by role
- [ ] 15.2 Implement Copilot Chat component in `claim-portal` module: conversational intake using Copilot SDK streaming; document upload with drag-and-drop
- [ ] 15.3 Implement claim status tracker component: real-time status polling (or SignalR push) showing pipeline stage, estimated completion, and current status message
- [ ] 15.4 Implement adjuster portal: paginated claim queue table, claim detail view with full AI review package, decision form with settlement override
- [ ] 15.5 Implement admin portal: provider configuration editor with validation; user management; audit log viewer with date-range export
- [ ] 15.6 Add Angular unit tests for Copilot chat flow, claim status tracker, and adjuster decision form validation

## 16. AI Evaluation Harness

- [ ] 16.1 Create `ClaimSettlement.EvalHarness` .NET 9 console project with CLI entry point (`dotnet run -- run --env <env> --dataset <version>`)
- [ ] 16.2 Define versioned test dataset (JSON fixtures) for all 7 required scenarios: valid claim, expired policy, duplicate claim, missing documents, high fraud score, large claim amount, multiple damaged assets
- [ ] 16.3 Implement test case submission: authenticate against the target environment; submit each claim via the intake API; poll for pipeline completion
- [ ] 16.4 Implement decision accuracy metrics: compare actual vs. expected decisions; compute overall accuracy, per-class precision/recall/F1
- [ ] 16.5 Implement fraud metrics: detection rate, false positive rate, AUC-ROC for fraud scores, mean score per scenario type
- [ ] 16.6 Implement latency measurement: record wall-clock time per pipeline stage and total; compute P50/P95/P99 percentiles
- [ ] 16.7 Implement hallucination detection: parse reasoning narrative and review summary; verify all cited identifiers, dates, and amounts against claim record
- [ ] 16.8 Implement tool invocation validation: read agent output metadata to verify expected vs. actual tool calls per agent
- [ ] 16.9 Implement human-review rate computation per scenario type; flag anomalies > 10 percentage points from expected rate
- [ ] 16.10 Implement cost-per-claim estimation: sum token usage across all Azure OpenAI calls; apply current pricing; report per-agent and total USD
- [ ] 16.11 Implement benchmark report writer: output JSON Lines + Markdown reports to Azure Blob Storage with SHA-256 hash; print summary to stdout
- [ ] 16.12 Add harness invocation to CI/CD pipeline (GitHub Actions / Azure DevOps); fail build if accuracy < 95% or P95 latency > 30 seconds

## 17. Production Readiness

- [ ] 17.1 Write end-to-end integration tests covering the full happy path (intake → document analysis → policy validation → fraud detection → settlement decision → notification)
- [ ] 17.2 Write end-to-end integration tests for the human-review branch (high-fraud claim → adjuster assignment → decision → notification)
- [ ] 17.3 Perform load test at configured concurrency limit (100 claims/provider); verify no claims lost or deadlocked
- [ ] 17.4 Conduct security review: verify all endpoints require valid Entra ID token, provider isolation enforced, no PII in logs, secrets in Key Vault only
- [ ] 17.5 Write deployment runbook: infrastructure provisioning steps, environment variable checklist, first-provider onboarding steps
- [ ] 17.6 Create Application Insights dashboard: claims pipeline metrics, agent error rates, fraud score distribution, cost-per-claim trend, SLA breach rate
- [ ] 17.7 Package for Microsoft Marketplace: create offer listing artifacts, deployment template, and marketplace configuration manifest
