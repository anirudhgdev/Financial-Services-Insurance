## Purpose

Provides a Copilot-powered conversational interface for customers to initiate insurance claims, capture all mandatory claim details, and upload supporting documents — forming the validated entry point for the entire claim settlement pipeline.

## ADDED Requirements

### Requirement: Conversational claim submission
The system SHALL allow authenticated customers to initiate a new insurance claim through a Microsoft Copilot SDK conversational interface. The Claim Intake Agent SHALL guide the customer through a structured conversation collecting: policy number, claimant name, date of loss, type of loss (e.g., auto, property, health), description of loss, loss amount estimate, and contact information.

#### Scenario: Successful claim initiation
- **WHEN** an authenticated customer sends a message stating they want to file a claim
- **THEN** the Claim Intake Agent SHALL respond with a guided prompt requesting the policy number

#### Scenario: Incomplete mandatory fields
- **WHEN** the customer provides partial information and attempts to proceed
- **THEN** the system SHALL identify the missing mandatory fields, list them clearly, and prompt the customer to supply them before advancing to document upload

#### Scenario: Session continuity
- **WHEN** a customer's session is interrupted mid-intake
- **THEN** the system SHALL persist the partially collected claim data and allow the customer to resume from the last completed step within 24 hours

### Requirement: Supporting document upload
The system SHALL accept uploads of supporting documents (PDF, JPEG, PNG, TIFF) up to 50 MB per file and up to 10 files per claim submission. Uploaded files SHALL be stored in Azure Blob Storage with a claim-scoped access policy.

#### Scenario: Valid document upload
- **WHEN** a customer uploads a PDF or image file within the size and count limits
- **THEN** the system SHALL acknowledge receipt, store the file in Azure Blob Storage, and associate it with the active claim record

#### Scenario: File type or size violation
- **WHEN** a customer uploads a file that exceeds 50 MB or is not in a supported format
- **THEN** the system SHALL reject the upload, display the specific reason (size or type), and prompt the customer to provide a valid file

#### Scenario: Document count limit
- **WHEN** a customer attempts to upload more than 10 documents for a single claim
- **THEN** the system SHALL reject the additional file and inform the customer of the 10-document limit

### Requirement: Claim record creation
Upon successful completion of intake, the system SHALL create a new claim record in Azure SQL with a unique claim ID, capture timestamp, agent conversation transcript reference, policy number, and status of `INTAKE_COMPLETE`. The claim ID SHALL be returned to the customer.

#### Scenario: Claim record persisted
- **WHEN** all mandatory fields are captured and at least one document is uploaded
- **THEN** the system SHALL create a claim record, assign a unique claim ID, and respond to the customer with the claim ID and next-steps summary

#### Scenario: Duplicate submission guard
- **WHEN** the same policy number and date of loss are submitted within a 24-hour window by the same user
- **THEN** the system SHALL warn the customer of a potential duplicate and require explicit confirmation before creating a new record

### Requirement: Authentication enforcement
The system SHALL reject claim intake requests from unauthenticated users. All claim operations SHALL require a valid Microsoft Entra ID token.

#### Scenario: Unauthenticated access attempt
- **WHEN** a user attempts to initiate a claim without a valid Entra ID session
- **THEN** the system SHALL respond with a 401 Unauthorized response and redirect the user to the Entra ID login flow
