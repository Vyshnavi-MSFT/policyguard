using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PolicyGuard.Models;

/// <summary>
/// A Policy represents a compliance framework like GDPR or HIPAA.
/// Contains clauses that get cited when violations are found.
/// </summary>
public class Policy
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Short name: "GDPR", "HIPAA", "SECRETS"
    /// </summary>
    [Required]
    public string Name { get; set; } = default!;

    /// <summary>
    /// Long description of the policy
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The clauses within this policy
    /// </summary>
    public ICollection<PolicyClause> Clauses { get; set; } = new List<PolicyClause>();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A single clause or rule within a policy.
/// Example: "GDPR Article 5(1)(c) — lawful processing"
/// </summary>
public class PolicyClause
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public string PolicyId { get; set; } = default!;

    /// <summary>
    /// Human-readable ID: "GDPR-Art5-1c"
    /// </summary>
    [Required]
    public string ClauseId { get; set; } = default!;

    /// <summary>
    /// "Article 5(1)(c): Integrity and confidentiality"
    /// </summary>
    [Required]
    public string Title { get; set; } = default!;

    /// <summary>
    /// Full text of the clause for citation
    /// </summary>
    public string? FullText { get; set; }

    /// <summary>
    /// Embedding vector (for semantic search) — stored as comma-separated float string
    /// </summary>
    public string? EmbeddingVector { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
