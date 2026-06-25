// components/ApprovalQueue.tsx — Person B
// Human-in-the-loop review panel for the selected finding: shows the snippet,
// AI explanation, cited policy clause and proposed deterministic fix, then lets
// the reviewer Approve or Reject. Wires to POST /api/findings/{id}/approve|reject.

import { useState } from 'react'
import type { Finding } from '../api/client'
import { approveFinding, rejectFinding } from '../api/client'
import CodeViewer from './CodeViewer'

interface Props {
  finding: Finding | null
  onResolved: (id: string, status: Finding['status']) => void
}

export default function ApprovalQueue({ finding, onResolved }: Props) {
  const [busy, setBusy] = useState(false)

  if (!finding) {
    return (
      <div className="pg-card">
        <p className="pg-section-title">Review &amp; approve</p>
        <p className="pg-empty">Select a highlighted violation to review it.</p>
      </div>
    )
  }

  async function act(kind: 'approve' | 'reject') {
    if (!finding || busy) return
    setBusy(true)
    try {
      if (kind === 'approve') await approveFinding(finding.id)
      else await rejectFinding(finding.id)
      onResolved(finding.id, kind === 'approve' ? 'approved' : 'rejected')
    } finally {
      setBusy(false)
    }
  }

  function undo() {
    if (!finding) return
    // Reverse the decision and re-open the finding for review (Change #3).
    onResolved(finding.id, 'pending')
  }


  const fixArgs = Object.entries(finding.proposedFix.args)
    .map(([k, v]) => `${k}: "${v}"`)
    .join(', ')

  return (
    <div className="pg-card">
      <p className="pg-section-title">Review &amp; approve</p>

      <div className="pg-review-head">
        <span className="pg-review-type">{finding.dataType}</span>
        <span className={`pg-badge sev-${finding.severity}`}>{finding.severity}</span>
        <span className="pg-detected-by">detected by {finding.detectedBy}</span>
      </div>
      <div className="pg-review-loc">{finding.location}</div>

      <CodeViewer finding={finding} />

      <p className="pg-block-title">Explanation</p>
      <p className="pg-explanation">{finding.explanation}</p>

      <div className="pg-policy-cite">
        <div className="pg-policy-cite-id">Cited policy · {finding.policyClauseId}</div>
        <div className="pg-policy-cite-text">“{finding.policyClauseText}”</div>
      </div>

      <div className="pg-fix-box">
        <p className="pg-block-title">Proposed fix</p>
        <div className="pg-fix-code">
          <b>{finding.proposedFix.tool}</b>({fixArgs})
        </div>
      </div>

      {finding.status === 'pending' ? (
        <div className="pg-actions">
          <button type="button" className="pg-btn pg-btn-approve" disabled={busy} onClick={() => act('approve')}>
            {busy ? 'Working…' : 'Approve fix'}
          </button>
          <button type="button" className="pg-btn pg-btn-reject" disabled={busy} onClick={() => act('reject')}>
            Reject
          </button>
        </div>
      ) : (
        <div className="pg-resolved">
          <div
            className={`pg-status-line ${
              finding.status === 'approved' ? 'pg-status-approved' : 'pg-status-rejected'
            }`}
          >
            {finding.status === 'approved' ? '✓ Fix approved' : '✕ Finding rejected'}
          </div>
          <button type="button" className="pg-btn pg-btn-undo" onClick={undo}>
            Undo
          </button>
        </div>
      )}
    </div>
  )
}

