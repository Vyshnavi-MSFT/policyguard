// Actions/DropColumn.cs — Person D (deterministic fix, NO LLM)
// Remove a dataset column entirely (header + every row's cell for that column).
// Used for data that cannot be salvaged, e.g. an SSN column under GDPR Art. 9.
// Must be unit-tested.
using System;
using PolicyGuard.Api.Models;

namespace PolicyGuard.Api.Actions;

public static class DropColumn
{
    /// <summary>
    /// Removes <paramref name="columnName"/> and its cells from every row.
    /// Mutates and returns the same <see cref="DataTable"/> for convenient chaining.
    /// </summary>
    /// <param name="table">The table to transform.</param>
    /// <param name="columnName">The column to drop (case-insensitive).</param>
    public static DataTable Apply(DataTable table, string columnName)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (string.IsNullOrWhiteSpace(columnName))
            throw new ArgumentException("Column name is required.", nameof(columnName));

        int col = table.IndexOf(columnName);
        if (col < 0)
            throw new ArgumentException($"Column '{columnName}' not found.", nameof(columnName));

        table.Columns.RemoveAt(col);
        foreach (var row in table.Rows)
        {
            if (col < row.Count) row.RemoveAt(col); // ragged row guard
        }

        return table;
    }
}
