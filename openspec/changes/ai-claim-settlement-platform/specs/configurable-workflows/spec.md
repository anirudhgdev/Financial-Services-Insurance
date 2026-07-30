## Purpose

Enables insurance providers to configure the platform's claim routing thresholds, agent parameters, coverage rules, and business-logic overrides without code changes — supporting multi-tenancy and marketplace deployment across diverse insurance organizations.

## ADDED Requirements

### Requirement: Provider configuration schema
The system SHALL maintain a provider configuration record per insurance provider containing: provider ID, provider name, manual-review fraud threshold, manual-review claim amount threshold, deduplication window (days), information request deadline (days), adjuster SLA period (hours), supported claim types, supported notification channels, pipeline concurrency limit, and active/inactive status. Configuration SHALL be stored in Azure SQL and cached for runtime access.

#### Scenario: Provider configuration loaded at claim start
- **WHEN** a new claim enters the pipeline
- **THEN** the orchestrator SHALL load the provider configuration by provider ID and inject it into the agent context

#### Scenario: Default configuration applied
- **WHEN** a provider record exists but a specific configuration key is absent
- **THEN** the system SHALL apply the platform default value for that key and log the fallback

### Requirement: Fraud threshold configuration
The manual-review fraud score threshold SHALL be configurable per provider within the range 0.30–0.90. Changes to the threshold SHALL take effect for new claims within 5 minutes of being saved without requiring a service restart.

#### Scenario: Threshold updated by provider admin
- **WHEN** a provider administrator updates the fraud threshold via the configuration API
- **THEN** the new threshold SHALL apply to all claims submitted after the change takes effect (within 5 minutes)

#### Scenario: Threshold outside allowed range
- **WHEN** a provider attempts to set a fraud threshold below 0.30 or above 0.90
- **THEN** the system SHALL reject the update with a 400 Bad Request and an error message specifying the allowed range

### Requirement: Claim type and coverage rules
Providers SHALL be able to define supported claim types (e.g., auto, property, health, life) and their associated mandatory intake fields, coverage mapping rules, and exclusion sets. These rules SHALL govern intake validation and coverage checks for claims submitted under the provider.

#### Scenario: Unsupported claim type submitted
- **WHEN** a customer submits a claim of a type not in the provider's supported claim types list
- **THEN** the system SHALL reject the claim at intake with a message indicating the supported types

#### Scenario: Custom mandatory fields enforced
- **WHEN** a provider defines additional mandatory intake fields for a claim type
- **THEN** the Claim Intake Agent SHALL enforce those fields as mandatory for claims of that type under that provider

### Requirement: Workflow routing overrides
Providers SHALL be able to configure routing overrides that force specific claim types or amount ranges to always route to human review, regardless of fraud score or automated decision.

#### Scenario: High-value claim forced to manual review
- **WHEN** a claim amount exceeds the provider-configured manual-review amount threshold
- **THEN** the Settlement Decision Agent SHALL recommend `MANUAL_REVIEW` regardless of other signals

#### Scenario: Claim type always routed to manual review
- **WHEN** a provider configures a claim type as `always_manual`
- **THEN** the orchestrator SHALL route all claims of that type to human review immediately after intake

### Requirement: Multi-tenancy isolation
All configuration, claim data, audit logs, and agent outputs SHALL be logically isolated per provider. Cross-provider data access SHALL be prevented at the API and data access layers.

#### Scenario: Provider data isolation enforced
- **WHEN** a request is made to any claim or configuration endpoint
- **THEN** the system SHALL validate that the authenticated identity has access only to data belonging to their provider and return 403 for any cross-provider access attempt
