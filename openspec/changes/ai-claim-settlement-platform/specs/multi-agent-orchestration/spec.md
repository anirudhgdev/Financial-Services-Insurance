## Purpose

Provides the MAF Orchestrator layer that coordinates the full multi-agent claim processing pipeline — managing agent invocation order, passing structured context between agents, handling agent errors and retries, and maintaining the authoritative workflow state for each claim.

## ADDED Requirements

### Requirement: Agent pipeline invocation
The MAF Orchestrator SHALL invoke agents in the following canonical order for each claim: (1) Document Analysis Agent, (2) Policy Validation Agent, (3) Fraud Detection Agent, (4) Settlement Decision Agent. The Notification Agent MAY be invoked at any pipeline stage by other agents. The Human Review Agent SHALL be invoked only when the Settlement Decision Agent produces a `MANUAL_REVIEW` recommendation.

#### Scenario: Standard pipeline execution
- **WHEN** a claim with status `INTAKE_COMPLETE` enters the orchestration pipeline
- **THEN** the orchestrator SHALL invoke agents in canonical order, wait for each agent's structured output before invoking the next, and update the claim's pipeline state after each step

#### Scenario: Human review branch
- **WHEN** the Settlement Decision Agent outputs `MANUAL_REVIEW`
- **THEN** the orchestrator SHALL invoke the Human Review Agent and suspend automated processing until an adjuster decision is recorded

### Requirement: Structured context passing
The orchestrator SHALL pass a structured context object to each agent containing: claim ID, claim record snapshot, all upstream agent outputs produced so far, provider configuration, and the authenticated user identity. Each agent SHALL return a typed output object. The orchestrator SHALL validate each output against its schema before passing it downstream.

#### Scenario: Context object passed to each agent
- **WHEN** the orchestrator invokes an agent
- **THEN** the agent SHALL receive a context object containing all upstream outputs and provider configuration

#### Scenario: Invalid agent output schema
- **WHEN** an agent returns an output that fails schema validation
- **THEN** the orchestrator SHALL log the validation error, mark the agent step as `SCHEMA_ERROR`, and route the claim to human review

### Requirement: Agent error handling and retry
If an agent invocation fails (exception, timeout, or invalid output), the orchestrator SHALL retry the agent up to 2 times with a 2-second delay. If the agent continues to fail, the orchestrator SHALL mark the pipeline step as `AGENT_FAILED`, log the error with full context, and route the claim to human review.

#### Scenario: Agent transient failure with successful retry
- **WHEN** an agent invocation fails on the first attempt but succeeds on the second retry
- **THEN** the orchestrator SHALL record the retry count in the pipeline state and continue processing

#### Scenario: Agent exhausts retries
- **WHEN** an agent fails on all retry attempts
- **THEN** the orchestrator SHALL mark the step `AGENT_FAILED` and route the claim to human review without blocking other claims in the pipeline

### Requirement: Pipeline state persistence
The orchestrator SHALL persist the pipeline state (current step, completed steps with outputs, errors) in Azure SQL after each agent completes. State SHALL be recoverable on orchestrator restart so that in-progress claims resume from the last completed step.

#### Scenario: Orchestrator restart mid-pipeline
- **WHEN** the orchestrator process restarts while a claim is mid-pipeline
- **THEN** the claim SHALL resume from the last persisted completed step without re-processing earlier steps

#### Scenario: All steps completed
- **WHEN** all pipeline steps complete without error
- **THEN** the orchestrator SHALL set the claim's pipeline status to `PIPELINE_COMPLETE` and record the total end-to-end processing duration

### Requirement: Concurrent claim processing
The orchestrator SHALL support processing multiple claims concurrently, with a configurable maximum concurrency level per provider (default: 100 simultaneous claims). Claims exceeding the concurrency limit SHALL be queued and processed in FIFO order.

#### Scenario: Concurrency limit reached
- **WHEN** the number of in-flight claims reaches the configured concurrency limit
- **THEN** new claims SHALL be enqueued and processed as in-flight claims complete, without returning errors to the customer

### Requirement: Orchestration observability
Every agent invocation, retry, branch decision, and error SHALL emit an OpenTelemetry trace span. The orchestrator SHALL record latency per agent step, overall pipeline duration, and error rate as Azure Application Insights metrics.

#### Scenario: Trace emitted for each agent step
- **WHEN** the orchestrator invokes or completes an agent step
- **THEN** an OpenTelemetry span SHALL be emitted with agent name, claim ID, duration, and outcome
