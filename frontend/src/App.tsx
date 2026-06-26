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
        <svg
          className="pg-brand"
          viewBox="0 0 660 78"
          role="img"
          aria-label="PolicyGuard"
          xmlns="http://www.w3.org/2000/svg"
        >
          <text
            x="0"
            y="58"
            fontFamily="Arial, Helvetica, sans-serif"
            fontSize="62"
            fontWeight="600"
            letterSpacing="1"
            fill="currentColor"
          >
            POLICYGUARD
          </text>
          <path
            d="M560 42 L584 64 L648 12"
            stroke="currentColor"
            strokeWidth="20"
            strokeLinecap="square"
            strokeLinejoin="miter"
            fill="none"
          />
        </svg>
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

