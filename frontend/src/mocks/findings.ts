// mocks/findings.ts — Person B
// Fake ScanResult so the dashboard/approval UI is never blocked by the backend.
// Shapes match the shared Finding contract in api/client.ts.

import type { ScanResult } from '../api/client'

const customersCsv = `name,email,age,ssn,favorite_color
John Smith,jane.doe@example.com,34,123-45-6789,blue
Jane Doe,john.smith@acme.io,28,987-65-4321,red
Bob Lee,bob@acme.io,41,555-12-9999,green`

const patientsCsv = `patient_id,name,dob,diagnosis,room
P001,Alice Wong,1985-04-12,Diabetes Type 2,204
P002,Carlos Diaz,1978-11-30,Hypertension,118`

const authCs = `using System;

namespace Acme.Auth;

public class AuthService
{
    private readonly ILogger _logger;

    public AuthService(ILogger logger)
    {
        _logger = logger;
    }

    public void Notify(User user)
    {
        _logger.LogInformation("Sending reset to " + user.Email);
    }
}`

const configCs = `namespace Acme.Config;

public static class AppSecrets
{
    public const string ApiKey = "EXAMPLE_FAKE_API_KEY_DO_NOT_USE";
    public const string Region = "eu-west-1";
}`

export const mockScan: ScanResult = {
  scanId: 'scan_demo_001',
  status: 'done',
  complianceScore: 12,
  files: [
    { path: 'customers.csv', sourceType: 'dataset', content: customersCsv },
    { path: 'patients.csv', sourceType: 'dataset', content: patientsCsv },
    { path: 'src/auth.cs', sourceType: 'code', content: authCs },
    { path: 'src/config.cs', sourceType: 'code', content: configCs },
  ],
  findings: [
    {
      id: '1',
      sourceType: 'dataset',
      location: 'customers.csv:column=email',
      snippet: 'jane.doe@example.com, john.smith@acme.io, bob@acme.io',
      dataType: 'EMAIL',
      severity: 'HIGH',
      policyClauseId: 'GDPR-ART5-1C',
      policyClauseText:
        'Personal data shall be adequate, relevant and limited to what is necessary (data minimisation).',
      explanation:
        'Column "email" contains direct personal identifiers stored in plaintext, exceeding what is necessary for model training.',
      proposedFix: { tool: 'MaskColumn', args: { column: 'email', style: 'partial' } },
      detectedBy: 'azure-pii',
      status: 'pending',
    },
    {
      id: '2',
      sourceType: 'dataset',
      location: 'customers.csv:column=ssn',
      snippet: '123-45-6789, 987-65-4321, 555-12-9999',
      dataType: 'SSN',
      severity: 'CRITICAL',
      policyClauseId: 'GDPR-ART9',
      policyClauseText:
        'Processing of special categories of personal data shall be prohibited unless an explicit condition applies.',
      explanation:
        'Column "ssn" holds Social Security Numbers — a special category of personal data that should not be retained.',
      proposedFix: { tool: 'DropColumn', args: { column: 'ssn' } },
      detectedBy: 'azure-pii',
      status: 'pending',
    },
    {
      id: '3',
      sourceType: 'dataset',
      location: 'patients.csv:column=diagnosis',
      snippet: 'Diabetes Type 2, Hypertension',
      dataType: 'PHI_DIAGNOSIS',
      severity: 'HIGH',
      policyClauseId: 'HIPAA-164-514',
      policyClauseText:
        'Health information that identifies an individual must be de-identified before secondary use.',
      explanation:
        'Column "diagnosis" links a medical condition to an identifiable patient, constituting protected health information (PHI).',
      proposedFix: { tool: 'AnonymizeColumn', args: { column: 'diagnosis' } },
      detectedBy: 'azure-pii',
      status: 'pending',
    },
    {
      id: '4',
      sourceType: 'code',
      location: 'src/auth.cs:16',
      snippet: '_logger.LogInformation("Sending reset to " + user.Email);',
      dataType: 'EMAIL',
      severity: 'MEDIUM',
      policyClauseId: 'GDPR-ART5-1F',
      policyClauseText:
        'Personal data shall be processed in a manner that ensures appropriate security, including protection against unauthorised processing (integrity and confidentiality).',
      explanation:
        'A user email address is written to application logs, leaking personal data into log storage.',
      proposedFix: { tool: 'RedactCodeLine', args: { file: 'src/auth.cs', line: '16' } },
      detectedBy: 'roslyn',
      status: 'pending',
    },
    {
      id: '5',
      sourceType: 'code',
      location: 'src/config.cs:5',
      snippet: 'public const string ApiKey = "EXAMPLE_FAKE_API_KEY_DO_NOT_USE";',
      dataType: 'API_KEY',
      severity: 'CRITICAL',
      policyClauseId: 'SECRETS-PLAINTEXT-KEY',
      policyClauseText:
        'Secrets and API keys must never be hard-coded or stored in plaintext in source control.',
      explanation:
        'A live API key is hard-coded in source, exposing it to anyone with repository access.',
      proposedFix: { tool: 'RedactCodeLine', args: { file: 'src/config.cs', line: '5' } },
      detectedBy: 'regex',
      status: 'pending',
    },
  ],
}

