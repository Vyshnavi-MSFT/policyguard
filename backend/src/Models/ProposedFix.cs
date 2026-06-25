using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PolicyGuard.Models;

/// <summary>
/// A proposed fix is what the AI recommends to remedy a Finding.
/// The AI only outputs the tool name and its arguments — it never executes.
/// A C# function in Person D's Actions/ folder does the actual work.
/// </summary>
public class ProposedFix
{
    /// <summary>
    /// Which fix tool to run: MASK_COLUMN, DROP_COLUMN, ANONYMIZE_COLUMN, REDACT_CODE_LINE
    /// </summary>
    [JsonPropertyName("fix_tool")]
    public string Tool { get; set; } = default!;

    /// <summary>
    /// Arguments for the tool, e.g. { "column": "email", "style": "partial" }
    /// This is intentionally schemaless so the AI can propose novel argument combinations.
    /// </summary>
    [JsonPropertyName("fix_args")]
    public Dictionary<string, object>? Args { get; set; }
}
