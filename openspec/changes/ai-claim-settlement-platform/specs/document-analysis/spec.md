## Purpose

Automatically extracts structured claim information from uploaded documents and images using Azure Document Intelligence and Azure OpenAI, summarizes findings for downstream agents, and identifies any information gaps requiring follow-up.

## ADDED Requirements

### Requirement: Structured data extraction from documents
The Document Analysis Agent SHALL process each uploaded claim document (PDF, JPEG, PNG, TIFF) using Azure Document Intelligence to extract key fields including: claimant name, date of loss, asset description, damage description, monetary amounts, witness statements, and policy references. Extracted fields SHALL be stored as structured JSON linked to the claim record.

#### Scenario: Full extraction from a well-formed PDF
- **WHEN** a claim contains a PDF with clearly readable text fields
- **THEN** the agent SHALL extract all identifiable claim fields with confidence scores ≥ 0.80 and persist them as structured JSON in the claim record

#### Scenario: Low-confidence extraction
- **WHEN** one or more extracted fields have a confidence score below 0.80
- **THEN** the agent SHALL flag those fields as `NEEDS_REVIEW`, record the raw extracted text, and include them in the missing-information report

#### Scenario: Unreadable document
- **WHEN** a document is corrupted, password-protected, or produces no extractable text
- **THEN** the agent SHALL mark the document as `EXTRACTION_FAILED`, log the reason, and add it to the information-gap report without blocking the pipeline

### Requirement: Claim summarization
The agent SHALL generate a concise natural-language summary (100–300 words) of each claim using Azure OpenAI, based on the extracted structured fields. The summary SHALL be stored in the claim record and surfaced to human reviewers and downstream agents.

#### Scenario: Summary generated for complete claim
- **WHEN** all mandatory fields are successfully extracted
- **THEN** the agent SHALL produce a 100–300 word summary capturing the key claim facts and store it in the claim record

#### Scenario: Summary generated for partial claim
- **WHEN** some fields are missing or flagged as low-confidence
- **THEN** the agent SHALL produce a summary noting the missing information and explicitly listing the gaps

### Requirement: Missing information detection
The agent SHALL compare extracted fields against the mandatory field set defined for the claim type and produce a structured gap report. The gap report SHALL list each missing or low-confidence field by name and indicate whether it is blocking (prevents downstream processing) or non-blocking.

#### Scenario: All mandatory fields present
- **WHEN** extraction yields all mandatory fields with confidence ≥ 0.80
- **THEN** the gap report SHALL be empty and the claim SHALL proceed to the Policy Validation Agent

#### Scenario: Blocking fields missing
- **WHEN** one or more blocking mandatory fields (e.g., policy number, date of loss) are absent
- **THEN** the gap report SHALL list the blocking fields, the Notification Agent SHALL be triggered to request the missing information from the customer, and downstream processing SHALL be paused

### Requirement: Multi-document deduplication
The agent SHALL detect when two uploaded documents contain substantially identical content (>90% textual overlap) and flag them as duplicates. Only the first copy SHALL be processed; the duplicate SHALL be noted in the extraction metadata.

#### Scenario: Duplicate document detected
- **WHEN** two uploaded documents share more than 90% textual content
- **THEN** the agent SHALL mark the second document as `DUPLICATE`, skip its extraction, and log the detection event
