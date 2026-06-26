// api/client.ts — Person A
// Typed API client for the PolicyGuard backend.
// Types mirror the shared backend contract: Models/Finding.cs + Models/ProposedFix.cs.
// A USE_MOCK switch ("fake AI mode") lets the whole UI run end-to-end before the
// backend exists, and is a safe fallback for the live demo.

import { mockScan } from '../mocks/findings'

// ---- Shared contract types (match the C# records) ----
export type Severity = 'CRITICAL' | 'HIGH' | 'MEDIUM' | 'LOW'
export type PolicyId = 'GDPR' | 'HIPAA' | 'Secrets'
export type SourceType = 'code' | 'dataset'
export type FixTool = 'MaskColumn' | 'DropColumn' | 'AnonymizeColumn' | 'RedactCodeLine'
export type FindingStatus = 'pending' | 'approved' | 'rejected'
export type ScanStatus = 'pending' | 'running' | 'done'

export interface ProposedFix {
  tool: FixTool
  args: Record<string, string>
}

export interface Finding {
  id: string
  sourceType: SourceType
  location: string // "customers.csv:column=email" | "src/auth.cs:42"
  snippet: string
  dataType: string // EMAIL | SSN | API_KEY | PHI_DIAGNOSIS ...
  severity: Severity
  policyClauseId: string // "GDPR-ART5-1C"
  policyClauseText: string
  explanation: string
  proposedFix: ProposedFix
  detectedBy: string // azure-pii | regex | roslyn
  status: FindingStatus
}

// The full text of each submitted file, so the results page can render the
// source/script with violations highlighted (Change #2). Backend exposes this
// via the scan inputs; the mock provides it directly.
export interface ScanFile {
  path: string
  sourceType: SourceType
  content: string
}

export interface ScanResult {
  scanId: string
  status: ScanStatus
  complianceScore: number
  files: ScanFile[]
  findings: Finding[]
}

export interface StartScanInput {
  files?: File[]
}

// Toggle to true to run the whole UI on mock data ("fake AI mode") with no backend.
const USE_MOCK = false
const API_BASE = '/api'

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms))

// ---- Backend <-> frontend shape mapping ------------------------------------------
// The backend speaks the C# contract (UPPERCASE statuses, fixTool/fixArgs strings);
// these helpers translate a raw API response into the typed shapes above.

const FIX_TOOL_MAP: Record<string, FixTool> = {
  MASK_COLUMN: 'MaskColumn',
  DROP_COLUMN: 'DropColumn',
  ANONYMIZE_COLUMN: 'AnonymizeColumn',
  REDACT_CODE_LINE: 'RedactCodeLine',
}

function sourceTypeOf(fileOrLocation: string): SourceType {
  const file = (fileOrLocation || '').toLowerCase().split(':')[0]
  return file.endsWith('.csv') || file.endsWith('.json') ? 'dataset' : 'code'
}

function scanStatusOf(s: string): ScanStatus {
  switch ((s || '').toUpperCase()) {
    case 'DONE':
    case 'ERROR':
      return 'done'
    case 'SCANNING':
      return 'running'
    default:
      return 'pending'
  }
}

function findingStatusOf(s: string): FindingStatus {
  switch ((s || '').toUpperCase()) {
    case 'APPROVED':
      return 'approved'
    case 'REJECTED':
      return 'rejected'
    default:
      return 'pending'
  }
}

function parseFixArgs(raw: unknown): Record<string, string> {
  if (!raw || typeof raw !== 'string') return {}
  try {
    const obj = JSON.parse(raw)
    const out: Record<string, string> = {}
    for (const [k, v] of Object.entries(obj ?? {})) out[k] = String(v)
    return out
  } catch {
    return {}
  }
}

/* eslint-disable @typescript-eslint/no-explicit-any */
function mapFinding(f: any): Finding {
  const location: string = f.location ?? ''
  return {
    id: f.id,
    sourceType: sourceTypeOf(location),
    location,
    snippet: f.snippet ?? '',
    dataType: f.dataType ?? '',
    severity: (f.severity ?? 'MEDIUM') as Severity,
    policyClauseId: f.policyClauseId ?? '',
    policyClauseText: f.policyClauseText ?? '',
    explanation: f.explanation ?? '',
    proposedFix: {
      tool: FIX_TOOL_MAP[(f.fixTool ?? '').toUpperCase()] ?? 'RedactCodeLine',
      args: parseFixArgs(f.fixArgs),
    },
    detectedBy: f.detectedBy ?? '',
    status: findingStatusOf(f.status),
  }
}

function mapScan(data: any): ScanResult {
  return {
    scanId: data.id,
    status: scanStatusOf(data.status),
    complianceScore: data.complianceScore ?? 0,
    files: (data.files ?? []).map((file: any) => ({
      path: file.path,
      sourceType: sourceTypeOf(file.path),
      content: file.content ?? '',
    })),
    findings: (data.findings ?? []).map(mapFinding),
  }
}
/* eslint-enable @typescript-eslint/no-explicit-any */

// POST /api/scan — scans every policy set (GDPR, HIPAA, Secrets); the backend reasons
// across all of them when policyId is not one specific framework.
// Policy filtering happens client-side on the results page (Change #1).
export async function startScan(input: StartScanInput): Promise<{ scanId: string }> {
  if (USE_MOCK) {
    await sleep(400)
    return { scanId: mockScan.scanId }
  }
  const form = new FormData()
  form.append('policyId', 'ALL')
  input.files?.forEach((f) => form.append('files', f))
  const res = await fetch(`${API_BASE}/scan`, { method: 'POST', body: form })
  if (!res.ok) throw new Error(`startScan failed: ${res.status}`)
  return res.json()
}

// GET /api/scan/{id} — status + findings.
export async function getScan(scanId: string): Promise<ScanResult> {
  if (USE_MOCK) {
    await sleep(300)
    return { ...mockScan, status: 'done' }
  }
  const res = await fetch(`${API_BASE}/scan/${scanId}`)
  if (!res.ok) throw new Error(`getScan failed: ${res.status}`)
  return mapScan(await res.json())
}

// Poll until the scan is done (the backend runs detection + AI in the background).
export async function pollScan(
  scanId: string,
  onTick?: (s: ScanResult) => void,
  intervalMs = 1500,
): Promise<ScanResult> {
  let scan = await getScan(scanId)
  onTick?.(scan)
  while (scan.status !== 'done') {
    await sleep(intervalMs)
    scan = await getScan(scanId)
    onTick?.(scan)
  }
  return scan
}

// POST /api/findings/{id}/approve — applies the deterministic C# fix.
export async function approveFinding(findingId: string): Promise<void> {
  if (USE_MOCK) {
    await sleep(250)
    return
  }
  const res = await fetch(`${API_BASE}/findings/${findingId}/approve`, { method: 'POST' })
  if (!res.ok) throw new Error(`approveFinding failed: ${res.status}`)
}

// POST /api/findings/{id}/reject
export async function rejectFinding(findingId: string): Promise<void> {
  if (USE_MOCK) {
    await sleep(250)
    return
  }
  const res = await fetch(`${API_BASE}/findings/${findingId}/reject`, { method: 'POST' })
  if (!res.ok) throw new Error(`rejectFinding failed: ${res.status}`)
}

// GET /api/report/{scanId} — audit report download.
export function reportUrl(scanId: string): string {
  return `${API_BASE}/report/${scanId}`
}

// ---- Helpers shared across components ----

export function policyOf(f: Finding): PolicyId {
  const prefix = f.policyClauseId.split('-')[0]?.toUpperCase()
  if (prefix === 'HIPAA') return 'HIPAA'
  // Secrets clauses are prefixed "SEC-..." (e.g. SEC-001-HardcodedApiKey).
  if (prefix === 'SEC' || prefix === 'SECRETS' || prefix === 'SECRET') return 'Secrets'
  return 'GDPR'
}

export function parseLocation(location: string): {
  file: string
  line?: number
  column?: string
} {
  const [file, rest] = location.split(/:(.+)/)
  if (!rest) return { file }
  if (rest.startsWith('column=')) return { file, column: rest.slice('column='.length) }
  const line = Number(rest)
  return Number.isNaN(line) ? { file } : { file, line }
}

