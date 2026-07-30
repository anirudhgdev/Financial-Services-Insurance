## Purpose

Verifies the validity of an insurance policy at the time of loss, checks coverage limits, exclusions, and deductibles against the claim details, and determines customer eligibility — producing a structured validation verdict consumed by downstream agents.

## ADDED Requirements

### Requirement: Policy existence and validity check
The Policy Validation Agent SHALL query the Policy Management API using the policy number extracted from the claim. It SHALL verify that: (a) the policy exists, (b) the policy was active on the date of loss, and (c) the policy has not been cancelled or lapsed. Results SHALL be stored as a structured verdict in the claim record.

#### Scenario: Active policy on date of loss
- **WHEN** the Policy Management API returns a policy that was active on the claimed date of loss
- **THEN** the agent SHALL record a `POLICY_VALID` verdict and proceed with coverage checks

#### Scenario: Expired policy
- **WHEN** the policy's expiry date precedes the claimed date of loss
- **THEN** the agent SHALL record an `POLICY_EXPIRED` verdict with the expiry date, and the Settlement Decision Agent SHALL receive this as a rejection signal

#### Scenario: Policy not found
- **WHEN** the Policy Management API returns a 404 for the provided policy number
- **THEN** the agent SHALL record a `POLICY_NOT_FOUND` verdict and the claim SHALL be flagged for manual review

### Requirement: Coverage, exclusion, and deductible verification
The agent SHALL retrieve the policy's coverage schedule and verify whether the claimed loss type and loss amount fall within covered categories. It SHALL identify applicable exclusions and calculate the deductible amount payable by the insured.

#### Scenario: Loss type covered within limits
- **WHEN** the claimed loss type is listed in the policy's covered categories and the loss amount is within the coverage limit
- **THEN** the agent SHALL record the applicable coverage limit, deductible, and net payable amount in the claim record

#### Scenario: Loss type excluded
- **WHEN** the claimed loss type matches an exclusion clause in the policy
- **THEN** the agent SHALL record a `COVERAGE_EXCLUDED` verdict with the exclusion clause reference and reason

#### Scenario: Loss amount exceeds coverage limit
- **WHEN** the claimed loss amount exceeds the policy's coverage limit for the loss type
- **THEN** the agent SHALL record the coverage limit, the excess amount, and a `PARTIAL_COVERAGE` verdict

### Requirement: Eligibility determination
The agent SHALL verify that the claimant is an authorized insured or named beneficiary on the policy. Eligibility checks SHALL include: identity match against policy holders, waiting-period compliance, and premium payment status.

#### Scenario: Claimant is authorized insured
- **WHEN** the claimant's identity matches a policy holder or named beneficiary
- **THEN** the agent SHALL record `ELIGIBLE` in the claim record

#### Scenario: Waiting period not met
- **WHEN** the policy was issued within a waiting period that applies to the claimed loss type
- **THEN** the agent SHALL record `INELIGIBLE_WAITING_PERIOD` with the waiting-period end date

#### Scenario: Premium in arrears
- **WHEN** the policy has outstanding premium payments on the date of loss
- **THEN** the agent SHALL record `INELIGIBLE_PREMIUM_ARREARS` and the claim SHALL be routed to manual review

### Requirement: Policy Management API resilience
The agent SHALL implement retry-with-backoff (up to 3 attempts, exponential backoff starting at 500 ms) when calling the Policy Management API. If all retries fail, the agent SHALL record a `POLICY_CHECK_UNAVAILABLE` verdict and route the claim to human review.

#### Scenario: API timeout
- **WHEN** the Policy Management API does not respond within 5 seconds
- **THEN** the agent SHALL retry up to 3 times and, if still unresponsive, record `POLICY_CHECK_UNAVAILABLE` and trigger human-review routing
