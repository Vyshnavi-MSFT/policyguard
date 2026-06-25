// PolicyGuard — app shell + page navigation.

import { useState } from 'react'
import type { ScanResult } from './api/client'
import UploadPage from './pages/UploadPage'
import ResultsPage from './pages/ResultsPage'
import './App.css'

function App() {
  const [scan, setScan] = useState<ScanResult | null>(null)

  return (
    <div className="pg-app">
      <header className="pg-topbar">
        <span className="pg-logo">PG</span>
        <span className="pg-brand">PolicyGuard</span>
        <span className="pg-tagline">Find · explain · fix personal data</span>
      </header>

      {scan ? (
        <ResultsPage scan={scan} onNewScan={() => setScan(null)} />
      ) : (
        <UploadPage onScanComplete={setScan} />
      )}
    </div>
  )
}

export default App

