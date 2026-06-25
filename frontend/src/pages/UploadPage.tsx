// pages/UploadPage.tsx — Person A
// Entry point: drag-and-drop code/JSON/CSV files or paste a Git repo URL, then scan.
// A scan always covers every policy set (GDPR, HIPAA, Secrets) — there is no policy
// picker here (Change #1); policy filtering happens on the results page.

import { useRef, useState } from 'react'
import type { ScanResult } from '../api/client'
import { pollScan, startScan } from '../api/client'

interface Props {
  onScanComplete: (scan: ScanResult) => void
}

export default function UploadPage({ onScanComplete }: Props) {
  const [files, setFiles] = useState<File[]>([])
  const [repoUrl, setRepoUrl] = useState('')
  const [dragging, setDragging] = useState(false)
  const [scanning, setScanning] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  function addFiles(list: FileList | null) {
    if (!list) return
    setFiles((prev) => [...prev, ...Array.from(list)])
  }

  async function handleScan() {
    if (scanning) return
    setScanning(true)
    try {
      const { scanId } = await startScan({ files, repoUrl: repoUrl.trim() || undefined })
      const scan = await pollScan(scanId)
      onScanComplete(scan)
    } catch (err) {
      console.error(err)
      setScanning(false)
    }
  }

  const canScan = (files.length > 0 || repoUrl.trim().length > 0) && !scanning

  return (
    <main className="pg-main">
      <h1 className="pg-h1">Scan for personal data</h1>
      <p className="pg-sub">
        Upload a dataset or code, and PolicyGuard will find and fix personal data.
      </p>

      <div
        className={`pg-dropzone${dragging ? ' is-drag' : ''}`}
        onDragOver={(e) => {
          e.preventDefault()
          setDragging(true)
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={(e) => {
          e.preventDefault()
          setDragging(false)
          addFiles(e.dataTransfer.files)
        }}
      >
        <p className="pg-dropzone-hint">
          Drag &amp; drop <strong>code (.cs, .py, .js, .ts), .json, .csv</strong> files here
        </p>
        <p className="pg-dropzone-or">or</p>
        <button type="button" className="pg-btn" onClick={() => inputRef.current?.click()}>
          Browse files
        </button>
        <input
          ref={inputRef}
          type="file"
          multiple
          hidden
          accept=".cs,.py,.js,.ts,.json,.csv"
          onChange={(e) => addFiles(e.target.files)}
        />

        {files.length > 0 && (
          <ul className="pg-filelist">
            {files.map((f, i) => (
              <li key={i} className="pg-filechip">
                {f.name}
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="pg-field">
        <label className="pg-label" htmlFor="repo">
          …or paste a Git repo URL
        </label>
        <input
          id="repo"
          className="pg-input"
          placeholder="https://github.com/org/repo.git"
          value={repoUrl}
          onChange={(e) => setRepoUrl(e.target.value)}
        />
      </div>

      <button type="button" className="pg-btn pg-btn-primary pg-btn-block" disabled={!canScan} onClick={handleScan}>
        {scanning ? 'Scanning…' : 'Scan'}
      </button>
    </main>
  )
}

