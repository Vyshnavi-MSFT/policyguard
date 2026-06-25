// Actions/MaskColumn.cs — Person D (deterministic fix, NO LLM)
// Mask values in a dataset column while keeping them human-recognizable.
// "partial" (default) keeps a hint: john@gmail.com -> j***@gmail.com, John -> J***.
// "full" replaces the whole value with ***. Must be unit-tested.
using System;
using PolicyGuard.Models;

namespace PolicyGuard.Actions;

public static class MaskColumn
{
    private const string Stars = "***";

    /// <summary>
    /// Masks every non-empty value in <paramref name="columnName"/>.
    /// Mutates and returns the same <see cref="DataTable"/> for convenient chaining.
    /// </summary>
    /// <param name="table">The table to transform.</param>
    /// <param name="columnName">The column to mask (case-insensitive).</param>
    /// <param name="style">"partial" (default) keeps a hint; "full" hides everything.</param>
    public static DataTable Apply(DataTable table, string columnName, string style = "partial")
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (string.IsNullOrWhiteSpace(columnName))
            throw new ArgumentException("Column name is required.", nameof(columnName));

        int col = table.IndexOf(columnName);
        if (col < 0)
            throw new ArgumentException($"Column '{columnName}' not found.", nameof(columnName));

        foreach (var row in table.Rows)
        {
            if (col >= row.Count) continue;            // ragged row guard
            var value = row[col];
            if (string.IsNullOrEmpty(value)) continue; // preserve blanks
            row[col] = Mask(value, style);
        }

        return table;
    }

    /// <summary>
    /// Masks a single value. Exposed separately so it is trivial to unit-test.
    /// Email-aware in "partial" mode so the domain stays readable.
    /// </summary>
    public static string Mask(string value, string style = "partial")
    {
        if (string.IsNullOrEmpty(value)) return value;

        if (string.Equals(style, "full", StringComparison.OrdinalIgnoreCase))
            return Stars;

        // "partial" (default)
        int at = value.IndexOf('@');
        if (at > 0)
        {
            // Email: keep first char of the local part, keep the whole domain.
            // john@gmail.com -> j***@gmail.com
            return value[0] + Stars + value[at..];
        }

        // Generic value: keep the first character, mask the rest.
        // John -> J***
        return value[0] + Stars;
    }
}
