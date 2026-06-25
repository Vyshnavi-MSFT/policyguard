// components/FindingsList.tsx — Person B
// Source viewer (Change #2): renders each submitted file/script and highlights the
// detected violations, colour-coded by severity. Clicking a highlighted line (code)
// or column (dataset) selects that finding so its details show in the review panel.

import type { Finding, ScanFile } from '../api/client'
import { parseLocation } from '../api/client'

interface Props {
  files: ScanFile[]
  findings: Finding[]
  selectedId: string | null
  onSelect: (id: string) => void
}

export default function FindingsList({ files, findings, selectedId, onSelect }: Props) {
  // Only show files that have at least one finding in the current filter.
  const filesWithFindings = files.filter((file) =>
    findings.some((f) => parseLocation(f.location).file === file.path),
  )

  if (filesWithFindings.length === 0) {
    return <p className="pg-empty">No violations match the current filters.</p>
  }

  return (
    <div>
      {filesWithFindings.map((file) => {
        const fileFindings = findings.filter(
          (f) => parseLocation(f.location).file === file.path,
        )
        return (
          <div className="pg-sourcefile" key={file.path}>
            <div className="pg-sourcefile-name">{file.path}</div>
            {file.sourceType === 'dataset' ? (
              <DatasetView
                file={file}
                findings={fileFindings}
                selectedId={selectedId}
                onSelect={onSelect}
              />
            ) : (
              <CodeView
                file={file}
                findings={fileFindings}
                selectedId={selectedId}
                onSelect={onSelect}
              />
            )}
          </div>
        )
      })}
    </div>
  )
}

function CodeView({ file, findings, selectedId, onSelect }: Omit<Props, 'files'> & { file: ScanFile }) {
  const lines = file.content.split('\n')
  // Map 1-based line number -> finding.
  const byLine = new Map<number, Finding>()
  findings.forEach((f) => {
    const { line } = parseLocation(f.location)
    if (line) byLine.set(line, f)
  })

  return (
    <div className="pg-code">
      {lines.map((text, idx) => {
        const lineNo = idx + 1
        const finding = byLine.get(lineNo)
        const sevClass = finding ? ` sev-${finding.severity}` : ''
        const selectedClass = finding && finding.id === selectedId ? ' is-selected' : ''
        return (
          <button
            type="button"
            key={lineNo}
            className={`pg-code-line${finding ? ' has-finding' : ''}${sevClass}${selectedClass}`}
            onClick={finding ? () => onSelect(finding.id) : undefined}
            disabled={!finding}
            title={finding ? `${finding.dataType} · ${finding.severity}` : undefined}
          >
            <span className="pg-code-gutter">{lineNo}</span>
            <span className="pg-code-text">{text || ' '}</span>
          </button>
        )
      })}
    </div>
  )
}

function DatasetView({ file, findings, selectedId, onSelect }: Omit<Props, 'files'> & { file: ScanFile }) {
  const rows = file.content.split('\n').map((r) => r.split(','))
  const header = rows[0] ?? []
  const body = rows.slice(1)

  // Map column index -> finding (by column name from the location).
  const byCol = new Map<number, Finding>()
  findings.forEach((f) => {
    const { column } = parseLocation(f.location)
    if (!column) return
    const idx = header.findIndex((h) => h.trim() === column)
    if (idx >= 0) byCol.set(idx, f)
  })

  const cellClass = (colIdx: number) => {
    const finding = byCol.get(colIdx)
    if (!finding) return ''
    const selected = finding.id === selectedId ? ' is-selected' : ''
    return ` pg-col-finding sev-${finding.severity}${selected}`
  }

  const handleClick = (colIdx: number) => {
    const finding = byCol.get(colIdx)
    if (finding) onSelect(finding.id)
  }

  return (
    <div className="pg-table-wrap">
      <table className="pg-data-table">
        <thead>
          <tr>
            {header.map((h, i) => (
              <th
                key={i}
                className={cellClass(i).trim()}
                onClick={() => handleClick(i)}
                title={byCol.get(i) ? `${byCol.get(i)!.dataType} · ${byCol.get(i)!.severity}` : undefined}
              >
                {h}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {body.map((row, ri) => (
            <tr key={ri}>
              {row.map((cell, ci) => (
                <td key={ci} className={cellClass(ci).trim()} onClick={() => handleClick(ci)}>
                  {cell}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

