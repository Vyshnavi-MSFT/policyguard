// components/CodeViewer.tsx — Person B
// Snippet viewer used in the review panel: shows the offending location and the
// exact value(s) that triggered the finding.

import type { Finding } from '../api/client'

export default function CodeViewer({ finding }: { finding: Finding }) {
  return (
    <div className="pg-snippet">
      <div className="pg-snippet-head">{finding.location}</div>
      <div className="pg-snippet-body">{finding.snippet}</div>
    </div>
  )
}

