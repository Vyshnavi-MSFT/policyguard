// pages/ResultsPage.tsx — Person B
// Results dashboard: compliance score, the submitted source with violations
// highlighted by severity (Change #2), a policy filter (Change #1), and the
// human-in-the-loop approval panel.

import { useMemo, useState } from 'react'
import type { Finding, PolicyId, ScanResult, Severity } from '../api/client'
import { approveFinding, policyOf, reportUrl } from '../api/client'
import ScoreGauge, { type SeverityFilter } from '../components/ScoreGauge'
import PolicySelector from '../components/PolicySelector'
import FindingsList from '../components/FindingsList'
import ApprovalQueue from '../components/ApprovalQueue'

interface Props {
  scan: ScanResult
  onNewScan: () => void
}

// Severity penalties tuned so the demo scan starts at the same score the
// backend reports; approving a fix removes that finding's penalty.
const SEVERITY_WEIGHT: Record<Severity, number> = {
  CRITICAL: 26,
  HIGH: 14,
  MEDIUM: 8,
  LOW: 3,
}

function computeScore(findings: Finding[]): number {
  const penalty = findings.reduce(
    (sum, f) => (f.status === 'approved' ? sum : sum + SEVERITY_WEIGHT[f.severity]),
    0,
  )
  return Math.max(0, Math.min(100, 100 - penalty))
}

export default function ResultsPage({ scan, onNewScan }: Props) {
  const [findings, setFindings] = useState<Finding[]>(scan.findings)
  const [severity, setSeverity] = useState<SeverityFilter>('ALL')
  const [policies, setPolicies] = useState<PolicyId[]>([])
  const [selectedId, setSelectedId] = useState<string | null>(scan.findings[0]?.id ?? null)
  // Snapshot taken before "Approve all High+" so the bulk action can be undone.
  const [bulkSnapshot, setBulkSnapshot] = useState<Finding[] | null>(null)

  const availablePolicies = useMemo(
    () => Array.from(new Set(findings.map(policyOf))),
    [findings],
  )

  const filtered = useMemo(
    () =>
      findings.filter((f) => {
        const sevOk = severity === 'ALL' || f.severity === severity
        const polOk = policies.length === 0 || policies.includes(policyOf(f))
        return sevOk && polOk
      }),
    [findings, severity, policies],
  )

  // Compliance score recomputes from the current statuses (Change #2).
  const score = useMemo(() => computeScore(findings), [findings])

  const selected = findings.find((f) => f.id === selectedId) ?? null

  const hasPendingHigh = findings.some(
    (f) => f.status === 'pending' && (f.severity === 'HIGH' || f.severity === 'CRITICAL'),
  )

  function setStatus(id: string, status: Finding['status']) {
    setFindings((prev) => prev.map((f) => (f.id === id ? { ...f, status } : f)))
  }

  async function approveAllHigh() {
    const targets = findings.filter(
      (f) => f.status === 'pending' && (f.severity === 'HIGH' || f.severity === 'CRITICAL'),
    )
    if (targets.length === 0) return
    setBulkSnapshot(findings) // remember the state so it can be reversed
    await Promise.all(targets.map((f) => approveFinding(f.id)))
    setFindings((prev) =>
      prev.map((f) =>
        targets.some((t) => t.id === f.id) ? { ...f, status: 'approved' as const } : f,
      ),
    )
  }

  function undoApproveAll() {
    if (bulkSnapshot) setFindings(bulkSnapshot)
    setBulkSnapshot(null)
  }

  return (
    <main className="pg-main pg-results-main">
      <div className="pg-results-head">
        <button type="button" className="pg-link-btn" onClick={onNewScan}>
          ← New scan
        </button>
        <div className="pg-head-actions">
          {bulkSnapshot ? (
            <div className="pg-approveall-done">
              <span className="pg-approveall-label">✓ Approved all High+</span>
              <button type="button" className="pg-link-btn" onClick={undoApproveAll}>
                Undo
              </button>
            </div>
          ) : (
            <button
              type="button"
              className="pg-btn"
              onClick={approveAllHigh}
              disabled={!hasPendingHigh}
            >
              Approve all High+
            </button>
          )}
          <a className="pg-btn pg-btn-primary" href={reportUrl(scan.scanId)} download>
            Download report
          </a>
        </div>
      </div>

      <ScoreGauge
        score={score}
        findings={findings}
        active={severity}
        onChange={setSeverity}
      />

      <PolicySelector available={availablePolicies} selected={policies} onChange={setPolicies} />

      <div className="pg-grid">
        <section>
          <p className="pg-section-title">Findings</p>
          <div className="pg-code-scroll">
            <FindingsList
              files={scan.files}
              findings={filtered}
              selectedId={selectedId}
              onSelect={setSelectedId}
            />
          </div>
        </section>

        <section className="pg-review-col">
          <ApprovalQueue finding={selected} onResolved={setStatus} />
        </section>
      </div>
    </main>
  )
}

// re-exported so other modules can reference the filter union if needed
export type { Severity }


