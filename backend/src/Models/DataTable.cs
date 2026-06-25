// Models/DataTable.cs — Person D
// Lightweight in-memory table that the deterministic fix functions (Actions/*) operate on,
// and that DatasetScanner loads CSV/JSON into. Column-aligned: Rows[i][j] -> Columns[j].
using System;
using System.Collections.Generic;
using System.Linq;

namespace PolicyGuard.Models;

public sealed class DataTable
{
    public List<string> Columns { get; }
    public List<List<string>> Rows { get; }

    public DataTable()
        : this(new List<string>(), new List<List<string>>())
    {
    }

    public DataTable(IEnumerable<string> columns, IEnumerable<IEnumerable<string>> rows)
    {
        Columns = columns?.ToList() ?? throw new ArgumentNullException(nameof(columns));
        Rows = (rows ?? throw new ArgumentNullException(nameof(rows)))
            .Select(r => r.ToList())
            .ToList();
    }

    /// <summary>Case-insensitive column lookup. Returns -1 if not present.</summary>
    public int IndexOf(string columnName) =>
        Columns.FindIndex(c => string.Equals(c, columnName, StringComparison.OrdinalIgnoreCase));

    public bool HasColumn(string columnName) => IndexOf(columnName) >= 0;
}
