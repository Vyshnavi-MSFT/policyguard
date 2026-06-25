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

// The deterministic fixes (redact / mask / drop) are stopgaps that hide the
// exposed value. This maps a finding to a one-line recommendation for the
// durable remediation the team should still implement at the source.
function permanentFix(finding: Finding): string {
  const type = (finding.dataType || '').toUpperCase()

  if (type.includes('PRIVATE') || type.includes('CERT'))
    return 'Revoke and reissue this key/certificate, then store it in a key vault instead of source.'
  if (
    type.includes('KEY') ||
    type.includes('TOKEN') ||
    type.includes('SECRET') ||
    type.includes('PASSWORD') ||
    type.includes('CREDENTIAL')
  )
    return 'Rotate the exposed secret and load it from an environment variable or secrets manager, never from source.'
  if (type.includes('CREDIT') || type.includes('CARD') || type.includes('PAN'))
    return 'Stop storing raw card numbers; tokenize them through a PCI-compliant processor and keep only the token.'
  if (type.includes('SSN') || type.includes('NATIONAL') || type.includes('MRN') || type.includes('PASSPORT'))
    return 'Avoid keeping this identifier; if essential, encrypt it at rest and restrict access to the minimum necessary.'
  if (type.includes('DIAGNOSIS') || type.includes('PHI') || type.includes('HEALTH') || type.includes('MEDICATION'))
    return 'Treat as PHI: keep it in a HIPAA-compliant store, encrypt at rest, and limit access to authorized roles.'
  if (
    type.includes('EMAIL') ||
    type.includes('PHONE') ||
    type.includes('NAME') ||
    type.includes('ADDRESS') ||
    type.includes('DOB') ||
    type.includes('BIRTH')
  )
    return 'Collect this personal data only with a lawful basis, minimize what you retain, and encrypt it at rest with role-based access.'
  if (type.includes('IP') || type.includes('MAC'))
    return 'Avoid logging network identifiers, or truncate/anonymize them and apply a short retention window.'

  switch (finding.proposedFix.tool) {
    case 'DropColumn':
      return 'Remove this field from the source system so it is never collected in the first place.'
    case 'MaskColumn':
    case 'AnonymizeColumn':
      return 'Mask or pseudonymize this field at ingestion and encrypt it at rest with restricted access.'
    case 'RedactCodeLine':
    default:
      return 'Remove the sensitive value from source and load it from secure configuration at runtime.'
  }
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
        <p className="pg-block-title">Temporary fix (applied on approval)</p>
        <div className="pg-fix-code">
          <b>{finding.proposedFix.tool}</b>({fixArgs})
        </div>
      </div>

      <div className="pg-permfix">
        <p className="pg-block-title">Recommended permanent fix</p>
        <p className="pg-permfix-text">{permanentFix(finding)}</p>
      </div>

      {finding.status === 'pending' ? (
        <div className="pg-actions">
          <button type="button" className="pg-btn pg-btn-approve" disabled={busy} onClick={() => act('approve')}>
            {busy ? 'Working…' : 'Approve temporary fix'}
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
            {finding.status === 'approved' ? '✓ Temporary fix approved' : '✕ Finding rejected'}
          </div>
          <button type="button" className="pg-btn pg-btn-undo" onClick={undo}>
            Undo
          </button>
        </div>
      )}
    </div>
  )
}

