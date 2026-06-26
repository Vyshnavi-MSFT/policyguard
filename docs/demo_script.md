# Demo Script — PolicyGuard

A walkthrough for the live demo.

## One-line pitch
PolicyGuard is an AI agent that finds personal data in your code and datasets,
explains which rule it breaks, and — with your approval — fixes it so the data is
safe to use for AI.

## Steps
1. Pre-build a leaky CSV and a leaky code file (see `backend/tests/PolicyGuard.Tests/SampleDatasets` and `SampleCode`).
2. Live: upload → findings appear → click one → show the cited policy clause + proposed fix → APPROVE → download the now-clean dataset.
3. Show the compliance score jump after fixes are applied.

> TODO: flesh out the exact click-by-click script before demo rehearsal (Hour 22–24).
