// Detection/IScanner.cs — Person C
// public interface IScanner { Task<List<Finding>> ScanAsync(SourceInput input); }
// Implemented below: IScanner + the SourceInput record the scanners consume.

using PolicyGuard.Api.Models;

namespace PolicyGuard.Api.Detection;

/// <summary>
/// A single unit of source to be scanned for personal data — either a code file
/// or a textual dataset blob. Detection scanners (regex, Roslyn, dataset) consume this.
/// </summary>
/// <param name="FileName">
/// The file name or relative path of the source, e.g. "src/Auth.cs" or "customers.csv".
/// Used to build the <c>Location</c> of any resulting <see cref="Finding"/>.
/// </param>
/// <param name="Content">The raw text content of the source.</param>
/// <param name="Language">
/// Optional language hint (e.g. "csharp", "python", "json"). When null, scanners
/// fall back to language-agnostic heuristics. Lets the orchestrator route C#/.NET
/// files to the Roslyn scanner and everything else to the regex scanner.
/// </param>
public record SourceInput(
    string FileName,
    string Content,
    string? Language = null
);

/// <summary>
/// Contract implemented by every detection scanner (e.g. RegexScanner, RoslynScanner,
/// DatasetScanner). A scanner reliably <i>finds</i> personal data and returns positioned
/// <see cref="Finding"/>s. Scanners only detect — severity, policy citation, and the
/// proposed fix are added later by the LLM reasoning step.
/// </summary>
public interface IScanner
{
    /// <summary>
    /// Scans a single source input and returns all personal-data findings it detects.
    /// </summary>
    /// <param name="input">The code or dataset content to scan.</param>
    /// <param name="cancellationToken">Token used to cancel a long-running scan.</param>
    /// <returns>
    /// The list of findings detected in <paramref name="input"/>. Returns an empty list
    /// (never null) when nothing is found.
    /// </returns>
    Task<List<Finding>> ScanAsync(SourceInput input, CancellationToken cancellationToken = default);
}
