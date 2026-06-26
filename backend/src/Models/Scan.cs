using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PolicyGuard.Models;

/// <summary>
/// A Scan represents one user's request to scan files.
/// Status flow: PENDING → SCANNING → DONE (or ERROR)
/// </summary>
public class Scan
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Which policy is being enforced? "GDPR", "HIPAA", etc.
    /// </summary>
    [Required]
    public string PolicyId { get; set; } = default!;

    /// <summary>
    /// PENDING, SCANNING, DONE, ERROR
    /// </summary>
    [Required]
    public string Status { get; set; } = "PENDING";

    /// <summary>
    /// Names of uploaded files (comma-separated or JSON array)
    /// </summary>
    public string? InputFileNames { get; set; }

    /// <summary>
    /// If an error occurred, store the message here
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The actual findings from this scan
    /// </summary>
    public ICollection<Finding> Findings { get; set; } = new List<Finding>();

    /// <summary>
    /// Compliance score: 0-100 (higher = more compliant)
    /// </summary>
    public int? ComplianceScore { get; set; }

    /// <summary>
    /// Timestamps
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
