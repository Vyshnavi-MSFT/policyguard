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
  repoUrl?: string
}

// Toggle to false once the live backend is wired up.
const USE_MOCK = true
const API_BASE = '/api'

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms))

// POST /api/scan — always scans every policy set (GDPR, HIPAA, Secrets).
// Policy filtering happens client-side on the results page (Change #1).
export async function startScan(input: StartScanInput): Promise<{ scanId: string }> {
  if (USE_MOCK) {
    await sleep(400)
    return { scanId: mockScan.scanId }
  }
  const form = new FormData()
  input.files?.forEach((f) => form.append('files', f))
  if (input.repoUrl) form.append('repoUrl', input.repoUrl)
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
  return res.json()
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
  if (prefix === 'SECRETS' || prefix === 'SECRET') return 'Secrets'
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

