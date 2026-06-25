// components/ScoreGauge.tsx — Person B
// Compliance score gauge + severity counts that double as filter chips.

import type { Finding, Severity } from '../api/client'

type SeverityFilter = 'ALL' | Severity

const ORDER: Severity[] = ['CRITICAL', 'HIGH', 'MEDIUM', 'LOW']

function riskLabel(score: number): string {
  if (score >= 80) return 'Low risk'
  if (score >= 50) return 'Medium risk'
  return 'High risk'
}

function gaugeColor(score: number): string {
  if (score >= 80) return '#16a34a'
  if (score >= 50) return '#d4a017'
  return '#dc2626'
}

interface Props {
  score: number
  findings: Finding[]
  active: SeverityFilter
  onChange: (s: SeverityFilter) => void
}

export default function ScoreGauge({ score, findings, active, onChange }: Props) {
  const counts: Record<Severity, number> = { CRITICAL: 0, HIGH: 0, MEDIUM: 0, LOW: 0 }
  findings.forEach((f) => (counts[f.severity] += 1))

  const radius = 46
  const circ = 2 * Math.PI * radius
  const pct = Math.max(0, Math.min(100, score)) / 100

  return (
    <div className="pg-card pg-score-card">
      <div className="pg-gauge" aria-label={`Compliance score ${score} of 100`}>
        <svg width="104" height="104" viewBox="0 0 104 104">
          <circle cx="52" cy="52" r={radius} fill="none" stroke="#eef2f6" strokeWidth="9" />
          <circle
            cx="52"
            cy="52"
            r={radius}
            fill="none"
            stroke={gaugeColor(score)}
            strokeWidth="9"
            strokeLinecap="round"
            strokeDasharray={circ}
            strokeDashoffset={circ * (1 - pct)}
            transform="rotate(-90 52 52)"
          />
        </svg>
        <div className="pg-gauge-num">
          <div>
            <strong>{score}</strong>
            <span> / 100</span>
          </div>
        </div>
      </div>

      <div className="pg-score-meta">
        <div className="pg-score-label">Compliance score</div>
        <div className="pg-score-risk">{riskLabel(score)}</div>
      </div>

      <div className="pg-chips" role="group" aria-label="Filter by severity">
        <button
          type="button"
          className={`pg-chip${active === 'ALL' ? ' is-active' : ''}`}
          onClick={() => onChange('ALL')}
        >
          All ({findings.length})
        </button>
        {ORDER.map((sev) => (
          <button
            key={sev}
            type="button"
            className={`pg-chip pg-chip-${sev.toLowerCase()}${active === sev ? ' is-active' : ''}`}
            onClick={() => onChange(sev)}
          >
            {sev} ({counts[sev]})
          </button>
        ))}
      </div>
    </div>
  )
}

export type { SeverityFilter }

