# PolicyGuard Backend - Person E Implementation

## Overview
This is the **orchestration layer** for PolicyGuard. It ties together all the other people's work into a unified system.

## What's Implemented

### 1. Database Models (Contract)
All teams read/write these models through the database:

#### **Scan**
```csharp
public class Scan
{
    public string Id { get; set; }                    // UUID
    public string PolicyId { get; set; }              // "GDPR", "HIPAA", etc.
    public string Status { get; set; }                // PENDING → SCANNING → DONE → ERROR
    public string? InputFileNames { get; set; }       // User's uploaded files
    public string? ErrorMessage { get; set; }         // If error occurs
    public ICollection<Finding> Findings { get; set; }// All findings from this scan
    public int? ComplianceScore { get; set; }         // 0-100 (higher = more compliant)
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

#### **Finding**
```csharp
public class Finding
{
    public string Id { get; set; }
    public string ScanId { get; set; }                // FK to Scan
    public string DataType { get; set; }              // EMAIL, SSN, CREDIT_CARD, etc.
    public string Severity { get; set; }              // CRITICAL, HIGH, MEDIUM, LOW
    public string Location { get; set; }              // "file.csv:column=2" or "src/Program.cs:42"
    public string? Snippet { get; set; }              // The actual dangerous data (truncated)
    public string? PolicyClauseId { get; set; }       // "GDPR-Art5-1c" (Person F fills this)
    public string? PolicyClauseText { get; set; }
    public string? Explanation { get; set; }          // Why it's a violation (Person F fills this)
    public string? DetectedBy { get; set; }           // REGEX, ROSLYN, AZURE, etc.
    public string Status { get; set; }                // PENDING_REVIEW → APPROVED → REJECTED
    public string? FixTool { get; set; }              // MASK_COLUMN, DROP_COLUMN, etc.
    public string? FixArgs { get; set; }              // JSON blob of arguments
    public string? ApprovedBy { get; set; }           // Audit trail
    public DateTime? ApprovedAt { get; set; }         // When approved
    public DateTime CreatedAt { get; set; }
}
```

#### **Policy & PolicyClause**
```csharp
public class Policy
{
    public string Id { get; set; }
    public string Name { get; set; }                  // "GDPR", "HIPAA"
    public string? Description { get; set; }
    public ICollection<PolicyClause> Clauses { get; set; }
}

public class PolicyClause
{
    public string Id { get; set; }
    public string PolicyId { get; set; }
    public string ClauseId { get; set; }              // "GDPR-Art5-1c"
    public string Title { get; set; }
    public string? FullText { get; set; }
    public string? EmbeddingVector { get; set; }      // For semantic search (Person F)
}
```

### 2. API Endpoints

#### **POST /api/scan** — Start a Scan
**Request:**
```http
POST /api/scan
Content-Type: multipart/form-data

policyId=GDPR
files=@customers.csv
files=@code.cs
```

**Response:**
```json
{ "scanId": "abc123-def456" }
```

**What it does:**
- Creates a `Scan` entry with status=PENDING
- Returns immediately (doesn't wait for actual scanning)
- Background worker picks it up and processes it

---

#### **GET /api/scan/{id}** — Poll for Results
**Request:**
```http
GET /api/scan/abc123-def456
```

**Response:**
```json
{
  "id": "abc123-def456",
  "policyId": "GDPR",
  "status": "DONE",
  "createdAt": "2026-06-24T10:00:00Z",
  "completedAt": "2026-06-24T10:15:00Z",
  "complianceScore": 40,
  "findings": [
    {
      "id": "finding-1",
      "dataType": "EMAIL",
      "severity": "HIGH",
      "location": "customers.csv:column=2",
      "snippet": "john@gmail.com",
      "policyClauseId": "GDPR-Art5-1c",
      "explanation": "Email addresses are personal data...",
      "status": "PENDING_REVIEW",
      "fixTool": "MASK_COLUMN",
      "fixArgs": "{\"column\":\"email\",\"style\":\"partial\"}"
    },
    {
      "id": "finding-2",
      "dataType": "SSN",
      "severity": "CRITICAL",
      "location": "customers.csv:column=4",
      "status": "PENDING_REVIEW",
      "fixTool": "DROP_COLUMN",
      "fixArgs": "{\"column\":\"ssn\"}"
    }
  ]
}
```

**Status values:**
- `PENDING` — waiting to be processed
- `SCANNING` — currently running detectors & AI
- `DONE` — finished successfully
- `ERROR` — something broke

---

#### **PATCH /api/scan/findings/{id}** — Approve/Reject a Finding
**Request:**
```json
PATCH /api/scan/findings/finding-1
{
  "action": "APPROVE",
  "approvedBy": "sam@company.com"
}
```

**Response:**
```json
{ "status": "APPROVED" }
```

**What it does:**
- User clicks "Approve" or "Reject"
- Marks the Finding as approved/rejected
- **TODO:** Queues the fix to be executed (Person D's functions)

---

### 3. Background Worker (`ScanBackgroundService`)
Runs every 5 seconds and:
1. Finds all Scans with status=PENDING
2. Calls Person C's detectors to find raw findings
3. **TODO:** Calls Person F's AI to enrich findings with severity, clause, explanation
4. Saves findings to database
5. Sets scan status = DONE

**Currently stubbed** with mock data so the pipeline works end-to-end.

### 4. Database Setup
- **SQLite** via EF Core (just one file: `policyguard.db`)
- **Migrations** auto-run on startup
- All cascading deletes configured

---

## How to Run

### Prerequisites
- .NET 8.0 SDK
- A `c:\Users\t-sthurtado\hackathon-project\policyguard` folder with the backend

### Build
```bash
cd backend
dotnet build
```

### Run
```bash
dotnet run
```

API starts at `https://localhost:5001` (or `http://localhost:5000` if not HTTPS).

Check **Swagger** at `http://localhost:5000/swagger/ui` to test endpoints interactively.

### Database
Automatically created/migrated as `policyguard.db` in the working directory.

---

## How Other Teams Integrate

### Person C & D (Detectors)
**When:** Background worker calls `ProcessScan()`
- Read files from temp storage
- Create `Finding` objects and insert into `_db.Findings`
- Fill in: `DataType`, `Location`, `Snippet`, `DetectedBy`

**Contract:** The `Finding` model above.

### Person F (AI Reasoner)
**When:** After findings are created
- Query `_db.Findings` where `Status == "PENDING_REVIEW"`
- For each Finding:
  - Embed its snippet + data type
  - Find the most similar PolicyClause via cosine similarity
  - Call gpt-5 to confirm it's a violation
  - Update Finding with `Severity`, `PolicyClauseId`, `PolicyClauseText`, `Explanation`, `FixTool`, `FixArgs`

**Contract:** Fields on `Finding` model.

### Person B (Results Dashboard / Frontend)
**Polling loop:**
```typescript
while (scan.status !== "done") {
  scan = await fetch(`/api/scan/${scanId}`).then(r => r.json());
  await sleep(1500);
}
```

**On Approve:**
```typescript
await fetch(`/api/scan/findings/${findingId}`, {
  method: "PATCH",
  body: JSON.stringify({ action: "APPROVE", approvedBy: username })
});
```

---

## Known TODOs

1. **File Storage** — Currently findings are stubbed. Need to store uploaded files (temp folder or blob storage).
2. **Fix Execution** — When Finding approved, need to call Person D's C# functions and apply the fix.
3. **Policy Loading** — Policies (GDPR.json, etc.) need to be loaded from `Policies/` folder into database on startup.
4. **Embedding Storage** — Person F will populate `PolicyClause.EmbeddingVector` for semantic search.
5. **Error Handling** — Add validation, better error messages, retry logic.
6. **Authentication** — No auth yet; add if needed.
7. **Logging** — Currently logs to console; consider structured logging.

---

## Testing

Sample test flow (using curl or Swagger):

1. **Create a scan:**
   ```bash
   curl -X POST http://localhost:5000/api/scan \
     -F "policyId=GDPR" \
     -F "files=@test.csv"
   ```
   → Returns `{ "scanId": "xyz123" }`

2. **Poll until done:**
   ```bash
   curl http://localhost:5000/api/scan/xyz123
   ```
   → Repeat until `status == "DONE"`

3. **Approve a finding:**
   ```bash
   curl -X PATCH http://localhost:5000/api/scan/findings/finding-id \
     -H "Content-Type: application/json" \
     -d '{"action":"APPROVE","approvedBy":"you"}'
   ```

---

## Branch Info
- **Branch:** `feature/person-e-orchestration`
- **Based on:** `main`
- **Ready for:** Merge once other teams integrate their pieces

---

## Questions?
Refer back to Part 2–7 of the project brief for the big picture.
