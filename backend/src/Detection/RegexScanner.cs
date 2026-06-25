// Detection/RegexScanner.cs — Person C
// Compiled regex for emails, phones, SSNs, credit cards (+ Luhn), IP/MAC, API keys, tokens, private keys.
// Each match -> Finding with file + line + column. detected_by = "regex".

using System.Text.RegularExpressions;
using PolicyGuard.Models;

namespace PolicyGuard.Detection;

/// <summary>
/// Language-agnostic scanner that finds personal data and secrets in raw text using
/// source-generated (compiled) regular expressions. Pure detection only — it positions
/// each match (file + line + column) and guesses a <c>DataType</c>; severity, the policy
/// citation, and the proposed fix are added later by the LLM reasoning step.
/// </summary>
public sealed partial class RegexScanner : IScanner
{
    private const string DetectedByTag = "REGEX";

    // --- Source-generated, compiled patterns (built at compile time for speed) ---

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?:\+?\d{1,3}[\s.-]?)?(?:\(?\d{3}\)?[\s.-]?)\d{3}[\s.-]?\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)]
    private static partial Regex SsnRegex();

    // Loose candidate for card numbers (13–19 digits, optional spaces/dashes); confirmed by Luhn.
    [GeneratedRegex(@"\b\d(?:[ -]?\d){12,18}\b", RegexOptions.Compiled)]
    private static partial Regex CreditCardRegex();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b", RegexOptions.Compiled)]
    private static partial Regex IpRegex();

    [GeneratedRegex(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b", RegexOptions.Compiled)]
    private static partial Regex MacRegex();

    // Common API-key shapes: AWS access key, OpenAI, GitHub PAT, generic "sk-/pk-" prefixes.
    [GeneratedRegex(@"\bAKIA[0-9A-Z]{16}\b|\b(?:sk|pk)-[A-Za-z0-9]{20,}\b|\bghp_[A-Za-z0-9]{36}\b", RegexOptions.Compiled)]
    private static partial Regex ApiKeyRegex();

    // JSON Web Tokens (three base64url segments separated by dots).
    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b", RegexOptions.Compiled)]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"-----BEGIN (?:RSA |EC |OPENSSH |DSA |PGP )?PRIVATE KEY-----", RegexOptions.Compiled)]
    private static partial Regex PrivateKeyRegex();

    /// <summary>A pattern paired with the data type it detects and an optional validator.</summary>
    private sealed record Rule(string DataType, Regex Regex, Func<string, bool>? Validate = null);

    private static readonly Rule[] Rules =
    [
        new("EMAIL", EmailRegex()),
        new("PHONE", PhoneRegex()),
        new("SSN", SsnRegex()),
        new("CREDIT_CARD", CreditCardRegex(), IsValidLuhn),
        new("IP", IpRegex()),
        new("MAC", MacRegex()),
        new("API_KEY", ApiKeyRegex()),
        new("TOKEN", JwtRegex()),
        new("PRIVATE_KEY", PrivateKeyRegex()),
    ];

    /// <inheritdoc />
    public Task<List<Finding>> ScanAsync(SourceInput input, CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();
        if (string.IsNullOrEmpty(input.Content))
        {
            return Task.FromResult(findings);
        }

        // Split on newlines so we can report 1-based line numbers (\r is trimmed per line).
        string[] lines = input.Content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            int lineNumber = i + 1;

            foreach (Rule rule in Rules)
            {
                foreach (Match match in rule.Regex.Matches(line))
                {
                    if (rule.Validate is not null && !rule.Validate(match.Value))
                    {
                        continue; // e.g. a digit run that fails the Luhn check
                    }

                    findings.Add(BuildFinding(input.FileName, lineNumber, line.Trim(), rule.DataType));
                }
            }
        }

        return Task.FromResult(findings);
    }

    /// <summary>
    /// Builds a detection-stage <see cref="Finding"/>. Severity, the policy citation, the
    /// explanation, and the proposed fix are intentionally left blank — they are populated
    /// downstream by the LLM reasoning step (Person F).
    /// </summary>
    private static Finding BuildFinding(string fileName, int line, string snippet, string dataType) =>
        new()
        {
            DataType = dataType,
            Location = $"{fileName}:{line}",
            Snippet = snippet,
            DetectedBy = DetectedByTag,
            Status = "PENDING_REVIEW",
            // Id defaults to a new GUID. ScanId is assigned by the orchestrator before saving.
            // Severity (defaults to MEDIUM), PolicyClauseId/Text, Explanation, and FixTool/FixArgs
            // are filled in downstream by the LLM reasoning step (Person F).
        };

    /// <summary>
    /// Validates a candidate card number with the Luhn checksum to cut false positives.
    /// Non-digit separators (spaces/dashes) are ignored; the cleaned length must be 13–19.
    /// </summary>
    private static bool IsValidLuhn(string candidate)
    {
        int sum = 0;
        int digitCount = 0;
        bool doubleDigit = false;

        for (int i = candidate.Length - 1; i >= 0; i--)
        {
            char c = candidate[i];
            if (c is < '0' or > '9')
            {
                continue;
            }

            int digit = c - '0';
            digitCount++;

            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            doubleDigit = !doubleDigit;
        }

        return digitCount is >= 13 and <= 19 && sum % 10 == 0;
    }
}
