// components/PolicySelector.tsx
// Policy filter on the results page (Change #1). A scan always covers every
// policy set (GDPR, HIPAA, Secrets); here the reviewer narrows findings to one
// or more policies. Multiple selections are allowed.

import type { PolicyId } from '../api/client'

const ALL_POLICIES: PolicyId[] = ['GDPR', 'HIPAA', 'Secrets']

interface Props {
  available: PolicyId[]
  selected: PolicyId[]
  onChange: (next: PolicyId[]) => void
}

export default function PolicySelector({ available, selected, onChange }: Props) {
  const policies = ALL_POLICIES.filter((p) => available.includes(p))

  function toggle(p: PolicyId) {
    if (selected.includes(p)) onChange(selected.filter((x) => x !== p))
    else onChange([...selected, p])
  }

  const allActive = selected.length === 0

  return (
    <div className="pg-policy-filter" role="group" aria-label="Filter by policy">
      <span className="pg-policy-filter-label">Policies</span>
      <button
        type="button"
        className={`pg-policy-pill${allActive ? ' is-active' : ''}`}
        onClick={() => onChange([])}
      >
        All
      </button>
      {policies.map((p) => (
        <button
          key={p}
          type="button"
          className={`pg-policy-pill${selected.includes(p) ? ' is-active' : ''}`}
          onClick={() => toggle(p)}
        >
          {p}
        </button>
      ))}
    </div>
  )
}

