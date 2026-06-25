using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PolicyGuard.Models;

/// <summary>
/// A Finding represents one detected data privacy issue.
/// This is the universal contract all parts of the system speak.
/// </summary>
public class Finding
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string ScanId { get; set; } = default!;

    [ForeignKey(nameof(ScanId))]
    public Scan? Scan { get; set; }

    /// <summary>
    /// What kind of personal data? EMAIL, SSN, CREDIT_CARD, etc.
    /// </summary>
    [Required]
    public string DataType { get; set; } = default!;

    /// <summary>
    /// How bad is it? CRITICAL, HIGH, MEDIUM, LOW
    /// </summary>
    [Required]
    public string Severity { get; set; } = "MEDIUM";

    /// <summary>
    /// Where was it found? "customers.csv:column=email" or "src/Program.cs:42"
    /// </summary>
    [Required]
    public string Location { get; set; } = default!;

    /// <summary>
    /// The actual dangerous snippet (up to 200 chars)
    /// </summary>
    public string? Snippet { get; set; }

    /// <summary>
    /// Which legal clause applies? "GDPR-Art5-1c" etc.
    /// </summary>
    public string? PolicyClauseId { get; set; }

    /// <summary>
    /// The full text of the clause for the UI
    /// </summary>
    public string? PolicyClauseText { get; set; }

    /// <summary>
    /// Why the AI thinks this is a violation
    /// </summary>
    public string? Explanation { get; set; }

    /// <summary>
    /// Which scanner detected it? REGEX, ROSLYN, AZURE, etc.
    /// </summary>
    public string? DetectedBy { get; set; }

    /// <summary>
    /// Status flow: PENDING_REVIEW → APPROVED → REJECTED
    /// </summary>
    [Required]
    public string Status { get; set; } = "PENDING_REVIEW";

    /// <summary>
    /// Which fix tool to apply? MASK_COLUMN, DROP_COLUMN, etc.
    /// </summary>
    public string? FixTool { get; set; }

    /// <summary>
    /// The arguments for the fix tool (JSON serialized)
    /// </summary>
    public string? FixArgs { get; set; }

    /// <summary>
    /// Audit: who approved this and when?
    /// </summary>
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    /// <summary>
    /// Timestamps
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
