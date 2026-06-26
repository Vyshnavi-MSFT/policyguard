# PolicyGuard

PolicyGuard is an AI-assisted privacy and compliance scanner for datasets and source code. It detects personally identifiable information, protected health information, and secrets, explains which policy rule is being violated, and proposes a human-reviewed fix such as masking, redacting, anonymizing, or dropping a column.

The workflow is straightforward: upload a file, scan it against policy, review the findings, approve or reject the recommended fix, and download the cleaned output.

Built for the Microsoft Global Intern Hackathon 2026.

## Team

Anna Gornyitzki · Claire Li · Khang Nguyen · Katherine Charry · Victoria Salomon · Stephanie Hurtado

## What PolicyGuard does

- Scans CSV, JSON, and code files for privacy and security issues.
- Maps findings to policy clauses from GDPR, HIPAA, and Secrets/Credentials rules.
- Produces a compliance score and a review queue.
- Applies approved deterministic fixes to the stored upload.
- Keeps the human in the loop before any change is executed.

## Who it is for

- Security and compliance teams that need a fast review path for sensitive files.
- Data teams preparing datasets for analytics or model training.
- Engineering teams that want to catch hardcoded secrets and logging mistakes before release.

## Business value

- Reduces manual review time by turning raw scans into actionable findings and fixes.
- Lowers the risk of shipping regulated or sensitive data into production systems.
- Creates an auditable workflow with policy citations and approval history.
- Helps teams decide whether a dataset is safe to use and what must be changed before use.

## Core features

- Dataset scanning for identifiers, PHI, and sensitive values.
- Code scanning for hardcoded secrets and logging of personal data.
- Policy-aware explanations with citations.
- Approval workflow for fixes.
- Downloadable cleaned files after approval.

## Repository layout

- `backend/` - ASP.NET Core API, scanners, policy store, background worker, and tests.
- `frontend/` - Vite + React dashboard for upload, findings, and approvals.
- `docs/` - Demo script and implementation notes.
- `backend/src/Policies/` - GDPR, HIPAA, and Secrets policy definitions.
- `backend/tests/PolicyGuard.Tests/` - Sample files for demo and scanner testing.

## Prerequisites

- .NET 8 SDK
- Node.js 20+ and npm
- Git

## Local setup

### 1. Clone the repository

```bash
git clone <repo-url>
cd policyguard
```

### 2. Start the backend

Open a terminal in `backend/` and run:

```bash
cd backend
dotnet run
```

The API listens on `http://localhost:5099` in development.

### 3. Start the frontend

Open a second terminal in `frontend/` and run:

```bash
cd frontend
npm install
npm run dev
```

The UI runs on Vite, usually at `http://localhost:5173`.

### 4. Open the app

Visit the frontend in your browser, upload a file such as `backend/tests/PolicyGuard.Tests/SampleDatasets/leaky_patient_dataset.csv`, then press Scan.

## Suggested walkthrough

1. Upload a leaky CSV or source file.
2. Wait for the scan to finish.
3. Review the findings and policy citations.
4. Approve a fix.
5. Download the cleaned output.

## Sample files

The repository includes test fixtures under `backend/tests/PolicyGuard.Tests/`.

- `SampleDatasets/` contains leaky CSV examples.
- `SampleCode/` contains leaky source-code examples.

Use these to validate the product quickly and consistently.

## Deployment

PolicyGuard is currently set up for local development. For production or evaluation, the most reliable deployment shape is:

- Frontend: deploy the Vite build as a static site.
- Backend: deploy the ASP.NET Core API as a web service.
- Networking: keep both behind the same origin or reverse proxy so `/api` requests reach the backend.

### Recommended deployment steps

1. Build the frontend.

```bash
cd frontend
npm install
npm run build
```

2. Build and verify the backend.

```bash
cd backend
dotnet build
```

3. Publish the backend to your hosting target.

```bash
cd backend
dotnet publish -c Release -o publish
```

4. Deploy the frontend `dist/` folder to your static host.

5. Configure the frontend to reach the backend through `/api` on the same domain, or place a reverse proxy in front of both services.

6. Verify the deployed app by uploading one of the sample files and confirming that scans, findings, and approvals work end-to-end.

### Deployment checklist

- Confirm the frontend can reach the backend through `/api`.
- Confirm the backend can read the policy files in `backend/src/Policies/`.
- Confirm uploads are being stored and re-read from the scan folder.
- Confirm the approval action applies fixes and updates the file content.
- Confirm the deployed environment uses the expected runtime variables.

### Environment variables to set in deployment

- `AZURE_OPENAI_ENDPOINT` - optional, enables policy reasoning embeddings.
- `AZURE_OPENAI_API_KEY` - optional, enables Azure OpenAI calls.
- `AZURE_OPENAI_EMBEDDING_DEPLOYMENT` - optional, embedding deployment name.
- `AZURE_LANGUAGE_ENDPOINT` - optional, enables Azure PII detection.
- `AZURE_LANGUAGE_KEY` - optional, Azure Language key.

If these are not provided, the app falls back to deterministic offline behavior where possible.

## API overview

- `POST /api/scan` - start a scan.
- `GET /api/scan/{id}` - poll for scan status and findings.
- `POST /api/findings/{id}/approve` - approve a finding and apply its fix.
- `POST /api/findings/{id}/reject` - reject a finding.
- `GET /api/report/{scanId}` - download the audit report.

## Notes for judges

PolicyGuard addresses a common operational need: teams regularly receive datasets or code that may contain regulated data, but they need a practical way to detect the risk, understand the policy basis, and produce a clean version that is safe to use.

The project combines detection, policy reasoning, approval, and file repair in one workflow so a customer can move from uncertainty to an actionable outcome instead of a static warning.

## Next steps

- Add support for more file types and richer dataset parsing.
- Expand policy coverage with custom organization-specific rules.
- Add authentication and role-based approval controls.
- Improve deployment automation so the frontend and backend can be published together.
- Add persistent audit export and reporting.

## Development notes

- The backend uses SQLite through Entity Framework Core.
- The frontend uses Vite and React.
- The scan worker runs in the background and enriches findings after detection.
- Sample leaky files are available in the test project for repeatable demos.
