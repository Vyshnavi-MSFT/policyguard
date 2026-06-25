// Agent/LlmReasoner.cs — Person F
// For each Finding: send the (already-detected) snippet + the retrieved policy clause to
// Azure OpenAI and get back STRICT JSON:
//   { is_violation, severity, fix_tool, fix_args, policy_clause_id, explanation }
// The JSON is validated against the ReasoningResult record; on a parse/validation failure we
// retry once, then fall back to deterministic reasoning. The LLM only ever sees the short
// snippet the scanners already flagged plus public policy text — it never touches raw data
// and it never performs the fix itself (it only NAMES a fix tool + args for a human to approve).

using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PolicyGuard.Models;

namespace PolicyGuard.Agent;

/// <summary>The validated result of reasoning about a single Finding.</summary>
public sealed record ReasoningResult(
    bool IsViolation,
    string Severity,
    string FixTool,
    Dictionary<string, object>? FixArgs,
    string PolicyClauseId,
    string Explanation);

/// <summary>
/// Wraps the Azure OpenAI chat model to turn a detected Finding + policy clause into a
/// structured, validated reasoning result. Offline-safe: with no credentials (or on any API
/// error) it produces a deterministic result so the pipeline never blocks.
/// </summary>
public sealed class LlmReasoner
{
    private static readonly HashSet<string> ValidSeverities =
        new(StringComparer.OrdinalIgnoreCase) { "CRITICAL", "HIGH", "MEDIUM", "LOW" };

    private static readonly HashSet<string> ValidFixTools =
        new(StringComparer.OrdinalIgnoreCase)
        { "MASK_COLUMN", "DROP_COLUMN", "ANONYMIZE_COLUMN", "REDACT_CODE_LINE" };

    private readonly ILogger<LlmReasoner> _logger;
    private readonly string? _endpoint;
    private readonly string? _apiKey;
    private readonly string? _chatDeployment;
    private readonly bool _useMock;
    private readonly OpenAIClient? _client;

    public LlmReasoner(IConfiguration configuration, ILogger<LlmReasoner> logger)
    {
        _logger = logger;
        _endpoint = configuration["AZURE_OPENAI_ENDPOINT"];
        _apiKey = configuration["AZURE_OPENAI_API_KEY"];
        _chatDeployment = configuration["AZURE_OPENAI_CHAT_DEPLOYMENT"];

        _useMock = string.IsNullOrWhiteSpace(_endpoint)
                   || string.IsNullOrWhiteSpace(_apiKey)
                   || string.IsNullOrWhiteSpace(_chatDeployment);

        if (!_useMock)
        {
            try
            {
                _client = new OpenAIClient(new Uri(_endpoint!), new AzureKeyCredential(_apiKey!));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LlmReasoner failed to create OpenAI client; using mock reasoning.");
                _useMock = true;
            }
        }
    }

    /// <summary>True when reasoning runs deterministically (no Azure OpenAI chat config).</summary>
    public bool IsMockMode => _useMock;

    /// <summary>
    /// Reasons about a single Finding against the retrieved policy clause. Always returns a
    /// valid result (never throws for an individual finding).
    /// </summary>
    public async Task<ReasoningResult> ReasonAsync(
        Finding finding,
        PolicyClauseMatch? clause,
        CancellationToken ct = default)
    {
        if (!_useMock && _client is not null)
        {
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    var raw = await CallModelAsync(finding, clause, ct);
                    var parsed = ParseAndValidate(raw, finding, clause);
                    if (parsed is not null)
                    {
                        return parsed;
                    }
                    _logger.LogWarning("LlmReasoner got invalid JSON (attempt {Attempt}); retrying.", attempt);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "LlmReasoner call failed (attempt {Attempt}).", attempt);
                }
            }

            _logger.LogWarning("LlmReasoner exhausted retries; falling back to deterministic reasoning.");
        }

        return MockReason(finding, clause);
    }

    private async Task<string> CallModelAsync(Finding finding, PolicyClauseMatch? clause, CancellationToken ct)
    {
        var options = new ChatCompletionsOptions
        {
            DeploymentName = _chatDeployment,
            Messages =
            {
                new ChatRequestSystemMessage(SystemPrompt),
                new ChatRequestUserMessage(BuildUserPrompt(finding, clause)),
            },
        };

        Response<ChatCompletions> response = await _client!.GetChatCompletionsAsync(options, ct);
        return response.Value.Choices.Count > 0 ? response.Value.Choices[0].Message.Content ?? "" : "";
    }

    private const string SystemPrompt =
        "You are a data-protection compliance reasoner for the PolicyGuard agent. " +
        "You are given a single finding that a deterministic scanner already flagged, plus the " +
        "most relevant policy clause. Decide whether it is a genuine violation and NAME a remediation " +
        "tool for a human to approve — you never modify data yourself. " +
        "Respond with ONLY a single minified JSON object and no markdown, using exactly these keys: " +
        "is_violation (boolean), severity (one of CRITICAL, HIGH, MEDIUM, LOW), " +
        "fix_tool (one of MASK_COLUMN, DROP_COLUMN, ANONYMIZE_COLUMN, REDACT_CODE_LINE), " +
        "fix_args (object of string keys to string/number values), " +
        "policy_clause_id (string), explanation (one short sentence). " +
        "Use REDACT_CODE_LINE for findings located in source code; use the column-based tools for dataset findings.";

    private static string BuildUserPrompt(Finding finding, PolicyClauseMatch? clause)
    {
        var clauseId = clause?.ClauseId ?? finding.PolicyClauseId ?? "UNKNOWN";
        var clauseTitle = clause?.Title ?? "";
        var clauseText = clause?.ClauseText ?? "";

        return
            $"Finding:\n" +
            $"- data_type: {finding.DataType}\n" +
            $"- detected_by: {finding.DetectedBy}\n" +
            $"- location: {finding.Location}\n" +
            $"- snippet: {finding.Snippet}\n\n" +
            $"Most relevant policy clause:\n" +
            $"- policy_clause_id: {clauseId}\n" +
            $"- title: {clauseTitle}\n" +
            $"- text: {clauseText}\n\n" +
            "Return the JSON object now.";
    }

    private ReasoningResult? ParseAndValidate(string raw, Finding finding, PolicyClauseMatch? clause)
    {
        var json = ExtractJsonObject(raw);
        if (json is null)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool isViolation = root.TryGetProperty("is_violation", out var iv)
                ? (iv.ValueKind == JsonValueKind.True
                   || (iv.ValueKind == JsonValueKind.String && bool.TryParse(iv.GetString(), out var b) && b))
                : true;

            var severity = NormalizeSeverity(GetString(root, "severity"), finding);
            var fixTool = NormalizeFixTool(GetString(root, "fix_tool"), finding);
            var clauseId = GetString(root, "policy_clause_id");
            if (string.IsNullOrWhiteSpace(clauseId))
            {
                clauseId = clause?.ClauseId ?? finding.PolicyClauseId ?? "UNKNOWN";
            }

            var explanation = GetString(root, "explanation");
            if (string.IsNullOrWhiteSpace(explanation))
            {
                explanation = $"{finding.DataType} detected; review against {clauseId}.";
            }

            Dictionary<string, object>? fixArgs = null;
            if (root.TryGetProperty("fix_args", out var fa) && fa.ValueKind == JsonValueKind.Object)
            {
                fixArgs = ReadFixArgs(fa);
            }
            fixArgs ??= DefaultFixArgs(finding, fixTool);

            return new ReasoningResult(isViolation, severity, fixTool, fixArgs, clauseId!, explanation!);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, object> ReadFixArgs(JsonElement obj)
    {
        var result = new Dictionary<string, object>();
        foreach (var prop in obj.EnumerateObject())
        {
            object value = prop.Value.ValueKind switch
            {
                JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => prop.Value.ToString(),
            };
            result[prop.Name] = value;
        }
        return result;
    }

    /// <summary>
    /// Extracts the first complete JSON object from a model response (tolerant of stray prose
    /// or markdown fences the model may add despite instructions).
    /// </summary>
    private static string? ExtractJsonObject(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        int start = raw.IndexOf('{');
        int end = raw.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }
        return raw.Substring(start, end - start + 1);
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static string NormalizeSeverity(string? value, Finding finding)
    {
        if (value is not null && ValidSeverities.Contains(value))
        {
            return value.ToUpperInvariant();
        }
        return DefaultSeverity(finding.DataType);
    }

    private static string NormalizeFixTool(string? value, Finding finding)
    {
        if (value is not null && ValidFixTools.Contains(value))
        {
            return value.ToUpperInvariant();
        }
        return DefaultFixTool(finding);
    }

    // ----- Deterministic fallback reasoning -------------------------------------------------

    private static ReasoningResult MockReason(Finding finding, PolicyClauseMatch? clause)
    {
        var clauseId = clause?.ClauseId ?? finding.PolicyClauseId ?? "UNKNOWN";
        var severity = DefaultSeverity(finding.DataType);
        var fixTool = DefaultFixTool(finding);
        var fixArgs = DefaultFixArgs(finding, fixTool);
        var clauseTitle = clause?.Title is { Length: > 0 } t ? $" ({t})" : "";
        var explanation =
            $"{finding.DataType} detected at {finding.Location}; this implicates {clauseId}{clauseTitle}.";

        return new ReasoningResult(true, severity, fixTool, fixArgs, clauseId, explanation);
    }

    private static string DefaultSeverity(string? dataType) => (dataType ?? "").ToUpperInvariant() switch
    {
        "SSN" or "CREDIT_CARD" or "PRIVATE_KEY" or "PASSWORD" or "SECRET" or "API_KEY" or "TOKEN" => "CRITICAL",
        "EMAIL" or "PHONE" or "ADDRESS" => "HIGH",
        "IP" or "MAC" => "MEDIUM",
        _ => "MEDIUM",
    };

    private static bool IsCodeFinding(Finding finding)
    {
        if (string.Equals(finding.DetectedBy, "ROSLYN", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var loc = finding.Location ?? "";
        return loc.Contains(".cs", StringComparison.OrdinalIgnoreCase)
               || loc.Contains(".py", StringComparison.OrdinalIgnoreCase)
               || loc.Contains(".js", StringComparison.OrdinalIgnoreCase)
               || loc.Contains(".ts", StringComparison.OrdinalIgnoreCase)
               || loc.Contains(".java", StringComparison.OrdinalIgnoreCase);
    }

    private static string DefaultFixTool(Finding finding)
    {
        if (IsCodeFinding(finding))
        {
            return "REDACT_CODE_LINE";
        }

        return (finding.DataType ?? "").ToUpperInvariant() switch
        {
            "SSN" or "CREDIT_CARD" or "PRIVATE_KEY" or "PASSWORD" or "SECRET" or "API_KEY" or "TOKEN" => "DROP_COLUMN",
            "EMAIL" or "PHONE" or "ADDRESS" => "MASK_COLUMN",
            _ => "ANONYMIZE_COLUMN",
        };
    }

    private static Dictionary<string, object> DefaultFixArgs(Finding finding, string fixTool)
    {
        if (fixTool == "REDACT_CODE_LINE")
        {
            var args = new Dictionary<string, object> { ["reason"] = $"Contains {finding.DataType}" };
            var line = ExtractLineNumber(finding.Location);
            if (line is not null)
            {
                args["line"] = line.Value;
            }
            return args;
        }

        var column = ExtractColumn(finding.Location) ?? (finding.DataType ?? "value").ToLowerInvariant();
        var columnArgs = new Dictionary<string, object> { ["column"] = column };
        if (fixTool == "MASK_COLUMN")
        {
            columnArgs["style"] = "partial";
        }
        return columnArgs;
    }

    /// <summary>Parses a column hint from a location like "customers.csv:column=email".</summary>
    private static string? ExtractColumn(string? location)
    {
        if (string.IsNullOrEmpty(location))
        {
            return null;
        }

        const string marker = "column=";
        int idx = location.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        var rest = location[(idx + marker.Length)..];
        int stop = rest.IndexOfAny(new[] { ':', ',', ' ' });
        return stop >= 0 ? rest[..stop] : rest;
    }

    /// <summary>Parses a 1-based line number from a location like "src/Program.cs:42".</summary>
    private static int? ExtractLineNumber(string? location)
    {
        if (string.IsNullOrEmpty(location))
        {
            return null;
        }

        var parts = location.Split(':');
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            if (int.TryParse(parts[i], out var n) && n > 0)
            {
                return n;
            }
        }
        return null;
    }
}
