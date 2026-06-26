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

function riskClass(score: number): string {
  if (score >= 80) return 'risk-low'
  if (score >= 50) return 'risk-medium'
  return 'risk-high'
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
          <defs>
            <linearGradient id="pgGaugeGradient" x1="0" y1="0" x2="1" y2="1">
              <stop offset="0%" stopColor="#7C4DFF" />
              <stop offset="100%" stopColor="#0BB5A5" />
            </linearGradient>
          </defs>
          <circle cx="52" cy="52" r={radius} fill="none" stroke="rgba(17,24,39,0.10)" strokeWidth="9" />
          <circle
            cx="52"
            cy="52"
            r={radius}
            fill="none"
            stroke="url(#pgGaugeGradient)"
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
        <div className={`pg-score-risk ${riskClass(score)}`}>{riskLabel(score)}</div>
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

