# PolicyGuard

> Find, explain, and fix personal data before it ships.

## Team

Anna Gornyitzki · Claire Li · Khang Nguyen · Katherine Charry · Victoria Salomon · Stephanie Hurtado

PolicyGuard is an AI agent you point at a dataset and/or the code that uses it. It
detects personal data (PII and PHI), checks it against real data-protection policy
(GDPR, HIPAA, and custom company rules), and proposes concrete fixes — masking,
dropping, anonymizing, or redacting — with every recommendation citing the specific
policy clause and gated behind human approval.

It answers one question: **"Is this dataset and codebase compliant and
training-ready, and if not, fix it for me."**

---

## What makes it stand out

- **It acts, not just advises** — detect → propose fix → apply on approval → clean
  dataset/code.
- **Policy-grounded reasoning** — every fix cites a *specific* GDPR / HIPAA / company
  policy clause, so it's auditable.
- **Covers both datasets and code** across the whole data lifecycle.
- **Safe-by-design** — the LLM reasons over abstractions; deterministic code touches
  the raw data; a human approves every change.
- **Training-readiness outcome** — answers "can I use this data for AI?" by *making*
  it usable.

---

## What it covers

| Category | Examples |
| --- | --- |
| Direct identifiers | Name, email, phone, address, passport/national ID, SSN |
| Financial | Credit card numbers, IBAN, bank account numbers |
| Online identifiers | IP / MAC address, device IDs, cookies, geolocation, usernames |
| Credentials & secrets | API keys, passwords, tokens, private keys |
| Sensitive categories | Health data, race, religion, political views, sexual orientation, biometrics |
| PHI | The 18 HIPAA identifiers — medical record numbers, diagnoses, health-plan IDs, etc. |
| Personal data in **code** | Hardcoded PII, logging of PII, plaintext storage, sending PII to third parties |

---

## How it works

1. **Upload / connect** — drag in a dataset (CSV/JSON/Excel), paste code files, or a
   Git repo URL.
2. **Pick a policy** — GDPR, HIPAA, Secrets/Credentials, or a custom upload.
3. **Scan** — deterministic detection finds the data; the LLM cites the policy clause,
   judges the violation, and proposes a fix.
4. **Review** — a dashboard with a compliance score and findings grouped by
   file/column.
5. **Approve / reject / edit** — every fix is human-approved before anything changes.
6. **Download clean output** — cleaned dataset, code diff, and an audit report.

---

> *"PolicyGuard is an AI agent that finds personal data in your code and datasets,
> explains which rule it breaks, and, with your approval, fixes it so the data is
> safe to use for AI."*