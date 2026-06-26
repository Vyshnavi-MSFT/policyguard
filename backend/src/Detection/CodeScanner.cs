// Detection/CodeScanner.cs — Person C
// Composite IScanner that routes a source to the right detector(s):
//   - RegexScanner runs on EVERY file (literal emails, keys, SSNs, etc.).
//   - RoslynScanner runs ADDITIONALLY on C# files for structural detection
//     (PII in logging, sensitive vars, PII into HTTP calls).
// Non-C# files fall back to regex-only. detected_by stays "regex" or "roslyn"
// on each individual finding.

using PolicyGuard.Models;

namespace PolicyGuard.Detection;

/// <summary>
/// Entry-point scanner for source code. Combines the language-agnostic
/// <see cref="RegexScanner"/> with the C#-aware <see cref="RoslynScanner"/>, choosing the
/// right detectors based on the source language. The orchestrator can call this single
/// scanner for any code file and get back the union of all findings.
/// </summary>
public sealed class CodeScanner : IScanner
{
    private readonly RegexScanner _regexScanner;
    private readonly RoslynScanner _roslynScanner;

    public CodeScanner(RegexScanner? regexScanner = null, RoslynScanner? roslynScanner = null)
    {
        _regexScanner = regexScanner ?? new RegexScanner();
        _roslynScanner = roslynScanner ?? new RoslynScanner();
    }

    /// <inheritdoc />
    public async Task<List<Finding>> ScanAsync(SourceInput input, CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();

        // Regex always runs — it catches literal values in any language.
        findings.AddRange(await _regexScanner.ScanAsync(input, cancellationToken));

        // Roslyn adds structural detection, but only for C# sources.
        if (IsCSharp(input))
        {
            findings.AddRange(await _roslynScanner.ScanAsync(input, cancellationToken));
        }

        return findings;
    }

    /// <summary>
    /// Determines whether a source should be treated as C#. Prefers the explicit
    /// <see cref="SourceInput.Language"/> hint and falls back to the ".cs" file extension.
    /// </summary>
    private static bool IsCSharp(SourceInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.Language))
        {
            return input.Language.Equals("csharp", StringComparison.OrdinalIgnoreCase)
                || input.Language.Equals("c#", StringComparison.OrdinalIgnoreCase)
                || input.Language.Equals("cs", StringComparison.OrdinalIgnoreCase);
        }

        return input.FileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    }
}
