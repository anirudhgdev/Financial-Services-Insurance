## Purpose

Provides tamper-evident, structured audit logging of all agent decisions, tool calls, and user actions across the claim lifecycle, and exposes OpenTelemetry/Application Insights observability instrumentation for production operations and compliance reporting.

## ADDED Requirements

### Requirement: Structured audit log entries
The system SHALL write an immutable audit log entry for every: claim state transition, agent invocation and output, adjuster action, configuration change, and authentication event. Each entry SHALL contain: event type, claim ID (if applicable), actor identity (user ID or agent ID), timestamp (UTC, millisecond precision), event payload (JSON), and provider ID.

#### Scenario: Agent output audit logged
- **WHEN** any agent completes an invocation
- **THEN** the system SHALL write an audit log entry within 1 second containing the agent ID, claim ID, input hash, output summary, and outcome

#### Scenario: Adjuster decision audit logged
- **WHEN** an adjuster submits a claim decision
- **THEN** the system SHALL write an audit log entry containing the adjuster ID, claim ID, decision, rationale, settlement amount override (if any), and timestamp

#### Scenario: Configuration change audit logged
- **WHEN** a provider administrator modifies any configuration setting
- **THEN** the system SHALL write an audit log entry with the previous value, new value, administrator identity, and timestamp

### Requirement: Audit log immutability
Audit log entries SHALL be append-only. The system SHALL prevent deletion or modification of any existing audit entry through any API or direct database operation. Immutability SHALL be enforced at the data access layer.

#### Scenario: Attempted audit log deletion blocked
- **WHEN** any system component or user attempts to delete or update an existing audit log entry
- **THEN** the operation SHALL be rejected with an error and the attempt itself SHALL be logged

### Requirement: OpenTelemetry distributed tracing
Every HTTP request, agent invocation, external service call, and database query SHALL emit an OpenTelemetry trace span. Spans SHALL include: operation name, claim ID (when applicable), duration, success/failure, and relevant attributes. Traces SHALL be exported to Azure Application Insights.

#### Scenario: End-to-end claim trace
- **WHEN** a claim is processed through the full pipeline
- **THEN** a complete distributed trace SHALL be available in Application Insights linking all agent spans under a single root trace ID

#### Scenario: External service call traced
- **WHEN** any agent calls an external service (Policy Management API, Fraud Detection Service, Notification Service)
- **THEN** a child span SHALL be emitted with the service name, endpoint, HTTP status, and latency

### Requirement: Application Insights metrics
The system SHALL publish the following custom metrics to Azure Application Insights: claims submitted per hour, claims auto-approved per hour, claims auto-rejected per hour, claims routed to human review per hour, average end-to-end pipeline duration, fraud score distribution (histogram), agent error rate per agent, notification delivery success rate, and cost-per-claim estimate.

#### Scenario: Pipeline metrics published
- **WHEN** a claim completes the pipeline (any outcome)
- **THEN** the system SHALL emit metrics for pipeline duration, outcome, and fraud score to Application Insights within 10 seconds

### Requirement: Audit log export
The system SHALL provide an API endpoint allowing authorized administrators to export audit logs for a given provider and time range as a JSON Lines file. The export SHALL be paginated and signed with a tamper-evident hash.

#### Scenario: Audit log export requested
- **WHEN** an authorized administrator requests an audit log export for a date range
- **THEN** the system SHALL return a downloadable JSON Lines file containing all matching entries with a SHA-256 hash of the file contents

#### Scenario: Unauthorized export attempt
- **WHEN** a non-administrator user requests an audit log export
- **THEN** the system SHALL return 403 Forbidden and log the unauthorized access attempt

### Requirement: Health and readiness endpoints
The system SHALL expose `/health/live` (liveness) and `/health/ready` (readiness) HTTP endpoints returning structured JSON status. Readiness SHALL verify connectivity to Azure SQL, Azure Blob Storage, Azure OpenAI, and the Notification Service.

#### Scenario: All dependencies healthy
- **WHEN** all downstream dependencies respond within their timeout thresholds
- **THEN** `/health/ready` SHALL return HTTP 200 with `status: "healthy"` and per-dependency status

#### Scenario: Dependency unhealthy
- **WHEN** any downstream dependency fails its health check
- **THEN** `/health/ready` SHALL return HTTP 503 with the specific failing dependency identified
