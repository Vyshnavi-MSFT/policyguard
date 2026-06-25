// Agent/ScanOrchestrator.cs — Person F / Person E
// Reasoning stage of the pipeline: for each already-detected Finding, retrieve the most
// relevant policy clause (PolicyStore) and ask the LLM to reason about it (LlmReasoner),
// then attach the citation + proposed fix to the Finding. Detection happens upstream
// (Person C/D); persistence + status transitions happen in ScanBackgroundService (Person E).
//
// Guiding principle: "the LLM reasons; deterministic code acts." This class only enriches the
// Finding objects — it never applies a fix and never modifies raw data. Every proposed fix is
// left as PENDING_REVIEW for a human to approve.

using System.Text.Json;
using Microsoft.Extensions.Logging;
using PolicyGuard.Models;

namespace PolicyGuard.Agent;

/// <summary>
/// Coordinates policy retrieval + LLM reasoning over the findings of a scan, enriching each
/// Finding in place with severity, citation, explanation, and a proposed fix.
/// </summary>
public sealed class ScanOrchestrator
{
    private readonly PolicyStore _policyStore;
    private readonly LlmReasoner _reasoner;
    private readonly ILogger<ScanOrchestrator> _logger;

    public ScanOrchestrator(PolicyStore policyStore, LlmReasoner reasoner, ILogger<ScanOrchestrator> logger)
    {
        _policyStore = policyStore;
        _reasoner = reasoner;
        _logger = logger;
    }

    /// <summary>
    /// Enriches each finding of <paramref name="scan"/> with the retrieved policy citation and
    /// the LLM-proposed (human-approvable) fix. Modifies the Finding objects in place; the
    /// caller is responsible for persisting them.
    /// </summary>
    public async Task ReasonAsync(Scan scan, IReadOnlyList<Finding> findings, CancellationToken ct = default)
    {
        await _policyStore.InitializeAsync(ct);

        var policyName = ResolvePolicyName(scan.PolicyId);

        foreach (var finding in findings)
        {
            ct.ThrowIfCancellationRequested();

            var query = BuildQuery(finding);
            var clause = await _policyStore.RetrieveAsync(query, policyName, ct);
            var result = await _reasoner.ReasonAsync(finding, clause, ct);

            ApplyResult(finding, clause, result);
        }

        _logger.LogInformation(
            "ScanOrchestrator reasoned over {Count} findings for scan {ScanId} (policy store mock={PolicyMock}, reasoner mock={ReasonerMock}).",
            findings.Count, scan.Id, _policyStore.IsMockMode, _reasoner.IsMockMode);
    }

    private static string BuildQuery(Finding finding)
    {
        return $"{finding.DataType} personal data found at {finding.Location}. " +
               $"Detected by {finding.DetectedBy}. Snippet: {finding.Snippet}";
    }

    private static void ApplyResult(Finding finding, PolicyClauseMatch? clause, ReasoningResult result)
    {
        finding.Severity = result.Severity;
        finding.PolicyClauseId = result.PolicyClauseId;
        finding.PolicyClauseText = clause?.ClauseText ?? finding.PolicyClauseText;
        finding.Explanation = result.Explanation;
        finding.FixTool = result.FixTool;
        finding.FixArgs = result.FixArgs is not null
            ? JsonSerializer.Serialize(result.FixArgs)
            : finding.FixArgs;
        finding.Status = "PENDING_REVIEW";
    }

    /// <summary>
    /// Maps a scan's PolicyId to one of the loaded policy names (GDPR/HIPAA/SECRETS) when it
    /// can be inferred, so retrieval is restricted to the relevant policy. Returns null to
    /// search across all policies.
    /// </summary>
    private static string? ResolvePolicyName(string? policyId)
    {
        if (string.IsNullOrWhiteSpace(policyId))
        {
            return null;
        }

        var upper = policyId.ToUpperInvariant();
        if (upper.Contains("GDPR")) return "GDPR";
        if (upper.Contains("HIPAA")) return "HIPAA";
        if (upper.Contains("SECRET")) return "SECRETS";
        return null;
    }
}
