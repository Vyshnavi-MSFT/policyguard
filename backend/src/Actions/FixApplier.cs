// Actions/FixApplier.cs — Person E (orchestrates Person D's deterministic fixes)
// Applies an APPROVED Finding's proposed fix to the stored uploaded file. This is the
// "deterministic code acts" half of the agent: it runs only after a human approves, never
// calls the LLM, and only mutates the local uploaded copy under uploads/{scanId}/.
//
// Routing by Finding.FixTool:
//   REDACT_CODE_LINE  -> RedactCodeLine.ApplyToFile (source code)
//   MASK_COLUMN       -> MaskColumn.Apply           (CSV/JSON dataset)
//   DROP_COLUMN       -> DropColumn.Apply            (CSV/JSON dataset)
//   ANONYMIZE_COLUMN  -> AnonymizeColumn.Apply       (CSV/JSON dataset)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using CsvHelper;
using PolicyGuard.Models;

namespace PolicyGuard.Actions;

/// <summary>The outcome of attempting to apply a fix: whether it ran, and a human-readable note.</summary>
public sealed record FixOutcome(bool Applied, string Detail);

/// <summary>
/// Applies a Finding's deterministic fix to its stored file. Pure file I/O + Person D's
/// Actions, so it is straightforward to unit-test against a temp folder.
/// </summary>
public static class FixApplier
{
    public static FixOutcome Apply(Finding finding, string scanFolder)
    {
        if (finding is null) throw new ArgumentNullException(nameof(finding));
        if (string.IsNullOrWhiteSpace(scanFolder))
            throw new ArgumentException("Scan folder is required.", nameof(scanFolder));

        if (string.IsNullOrWhiteSpace(finding.FixTool))
            return new FixOutcome(false, "No fix tool was proposed for this finding.");

        var fileName = ExtractFileName(finding.Location);
        if (fileName is null)
            return new FixOutcome(false, $"Could not parse a file name from location '{finding.Location}'.");

        // Path.GetFileName strips any directory components to keep us inside the scan folder.
        var path = Path.Combine(scanFolder, Path.GetFileName(fileName));
        if (!File.Exists(path))
            return new FixOutcome(false, $"File '{fileName}' was not found for this scan.");

        var args = ParseArgs(finding.FixArgs);

        return finding.FixTool.ToUpperInvariant() switch
        {
            "REDACT_CODE_LINE" => ApplyRedact(finding, path, args),
            "MASK_COLUMN" or "DROP_COLUMN" or "ANONYMIZE_COLUMN" => ApplyColumn(finding, path, args),
            _ => new FixOutcome(false, $"Unknown fix tool '{finding.FixTool}'."),
        };
    }

    private static FixOutcome ApplyRedact(Finding finding, string path, IReadOnlyDictionary<string, string> args)
    {
        int? line = GetInt(args, "line") ?? ExtractLineNumber(finding.Location);
        if (line is null)
            return new FixOutcome(false, "No line number was available to redact.");

        args.TryGetValue("reason", out var reason);
        RedactCodeLine.ApplyToFile(path, line.Value, string.IsNullOrWhiteSpace(reason)
            ? $"Contains {finding.DataType}"
            : reason);

        return new FixOutcome(true, $"Redacted line {line} of {Path.GetFileName(path)}.");
    }

    private static FixOutcome ApplyColumn(Finding finding, string path, IReadOnlyDictionary<string, string> args)
    {
        if (!args.TryGetValue("column", out var column) || string.IsNullOrWhiteSpace(column))
            column = ExtractColumn(finding.Location);

        if (string.IsNullOrWhiteSpace(column))
            return new FixOutcome(false, "No column was specified for the dataset fix.");

        bool isJson = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        DataTable table = isJson ? LoadJson(path) : LoadCsv(path);

        if (!table.HasColumn(column))
            return new FixOutcome(false, $"Column '{column}' was not found in {Path.GetFileName(path)}.");

        switch (finding.FixTool!.ToUpperInvariant())
        {
            case "MASK_COLUMN":
                args.TryGetValue("style", out var style);
                MaskColumn.Apply(table, column, string.IsNullOrWhiteSpace(style) ? "partial" : style);
                break;
            case "DROP_COLUMN":
                DropColumn.Apply(table, column);
                break;
            case "ANONYMIZE_COLUMN":
                args.TryGetValue("salt", out var salt);
                AnonymizeColumn.Apply(table, column, string.IsNullOrWhiteSpace(salt) ? null : salt);
                break;
        }

        if (isJson) SaveJson(path, table); else SaveCsv(path, table);
        return new FixOutcome(true, $"Applied {finding.FixTool} to column '{column}' in {Path.GetFileName(path)}.");
    }

    // ----- Location / args parsing ----------------------------------------------------------

    /// <summary>Returns the file name portion of a location like "Program.cs:42" or "data.csv:column=email".</summary>
    private static string? ExtractFileName(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        int idx = location.IndexOf(':');
        var name = idx > 0 ? location[..idx] : location;
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    /// <summary>Parses a column hint from a location like "customers.csv:column=email".</summary>
    private static string? ExtractColumn(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        const string marker = "column=";
        int i = location.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return i >= 0 ? location[(i + marker.Length)..].Trim() : null;
    }

    /// <summary>Parses a 1-based line number from a location like "src/Program.cs:42".</summary>
    private static int? ExtractLineNumber(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return null;
        var parts = location.Split(':');
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            if (int.TryParse(parts[i], out var n) && n > 0) return n;
        }
        return null;
    }

    private static Dictionary<string, string> ParseArgs(string? fixArgs)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(fixArgs)) return result;

        try
        {
            using var doc = JsonDocument.Parse(fixArgs);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    result[p.Name] = p.Value.ValueKind == JsonValueKind.String
                        ? p.Value.GetString() ?? string.Empty
                        : p.Value.GetRawText();
                }
            }
        }
        catch (JsonException)
        {
            // Malformed fix args — treat as empty; the caller reports the soft failure.
        }

        return result;
    }

    private static int? GetInt(IReadOnlyDictionary<string, string> args, string key)
        => args.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;

    // ----- CSV / JSON round-trip ------------------------------------------------------------

    private static DataTable LoadCsv(string path)
    {
        using var reader = new StreamReader(path);
        using var parser = new CsvParser(reader, CultureInfo.InvariantCulture);

        var rows = new List<string[]>();
        while (parser.Read())
        {
            if (parser.Record is { } record) rows.Add(record);
        }

        if (rows.Count == 0) return new DataTable();
        return new DataTable(rows[0], rows.Skip(1));
    }

    private static void SaveCsv(string path, DataTable table)
    {
        using var writer = new StreamWriter(path, append: false);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);

        foreach (var column in table.Columns) csv.WriteField(column);
        csv.NextRecord();

        foreach (var row in table.Rows)
        {
            foreach (var cell in row) csv.WriteField(cell);
            csv.NextRecord();
        }
    }

    private static DataTable LoadJson(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array) return new DataTable();

        var columns = new List<string>();
        var records = new List<JsonElement>();
        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            records.Add(element);
            foreach (var prop in element.EnumerateObject())
            {
                if (!columns.Contains(prop.Name)) columns.Add(prop.Name);
            }
        }

        var rows = new List<List<string>>();
        foreach (var record in records)
        {
            var row = new List<string>(columns.Count);
            foreach (var column in columns)
            {
                row.Add(record.TryGetProperty(column, out var value)
                    ? JsonValueToString(value)
                    : string.Empty);
            }
            rows.Add(row);
        }

        return new DataTable(columns, rows);
    }

    private static void SaveJson(string path, DataTable table)
    {
        var records = new List<Dictionary<string, string>>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            var obj = new Dictionary<string, string>(table.Columns.Count);
            for (int i = 0; i < table.Columns.Count; i++)
            {
                obj[table.Columns[i]] = i < row.Count ? row[i] : string.Empty;
            }
            records.Add(obj);
        }

        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static string JsonValueToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null => string.Empty,
        _ => value.GetRawText(),
    };
}
