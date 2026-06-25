// Actions/AnonymizeColumn.cs — Person D (deterministic fix, NO LLM)
// Anonymize a dataset column by replacing each value with a one-way pseudonym.
// Deterministic: the same input (with the same salt) always yields the same token,
// so referential integrity is preserved across rows (same person -> same token),
// while the original value cannot be recovered. Must be unit-tested.
using System;
using System.Security.Cryptography;
using System.Text;
using PolicyGuard.Api.Models;

namespace PolicyGuard.Api.Actions;

public static class AnonymizeColumn
{
    /// <summary>
    /// Replaces every non-empty value in <paramref name="columnName"/> with a stable pseudonym.
    /// Mutates and returns the same <see cref="DataTable"/> for convenient chaining.
    /// </summary>
    /// <param name="table">The table to transform.</param>
    /// <param name="columnName">The column to anonymize (case-insensitive).</param>
    /// <param name="salt">Optional salt; the same salt must be used to get matching tokens.</param>
    public static DataTable Apply(DataTable table, string columnName, string? salt = null)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (string.IsNullOrWhiteSpace(columnName))
            throw new ArgumentException("Column name is required.", nameof(columnName));

        int col = table.IndexOf(columnName);
        if (col < 0)
            throw new ArgumentException($"Column '{columnName}' not found.", nameof(columnName));

        foreach (var row in table.Rows)
        {
            if (col >= row.Count) continue;            // ragged row: nothing to anonymize
            var value = row[col];
            if (string.IsNullOrEmpty(value)) continue; // preserve blanks
            row[col] = Anonymize(value, salt);
        }

        return table;
    }

    /// <summary>
    /// One-way pseudonym for a single value: "anon_" + first 12 hex chars of SHA-256(salt + value).
    /// Deterministic and irreversible. Exposed separately so it is trivial to unit-test.
    /// </summary>
    public static string Anonymize(string value, string? salt = null)
    {
        var input = (salt ?? string.Empty) + (value ?? string.Empty);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var hex = Convert.ToHexString(hash, 0, 6).ToLowerInvariant(); // 6 bytes -> 12 hex chars
        return $"anon_{hex}";
    }
}
