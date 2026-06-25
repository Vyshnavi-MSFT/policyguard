// Detection/DatasetScanner.cs — Person D
// Loads a CSV (CsvHelper) or JSON (System.Text.Json) dataset into an in-memory DataTable,
// then flags each column two ways:
//   1) column-NAME check against a keyword list (deterministic),
//   2) column-VALUE check via Azure AI Language PII detection (AzurePiiClient).
// Emits at most one detection-stage Finding per offending column. Severity, the policy
// citation, and the proposed fix are added downstream by the LLM reasoning step (Person F).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using PolicyGuard.Models;

namespace PolicyGuard.Detection;

public sealed class DatasetScanner : IScanner
{
    private const int MaxSampleValues = 5;
    private const int MaxSnippetLength = 200;
    private const string DetectedByName = "KEYWORD";

    private readonly AzurePiiClient _pii;

    public DatasetScanner(AzurePiiClient pii)
    {
        _pii = pii ?? throw new ArgumentNullException(nameof(pii));
    }

    // Column-name keyword -> DataType. First match wins. Names are normalized to letters/digits.
    private static readonly (string[] Keywords, string DataType)[] NameRules =
    {
        (new[] { "email", "mail" }, "EMAIL"),
        (new[] { "ssn", "socialsecurity" }, "SSN"),
        (new[] { "phone", "mobile", "telephone", "tel" }, "PHONE"),
        (new[] { "dob", "dateofbirth", "birth" }, "DOB"),
        (new[] { "creditcard", "cardnumber", "ccn" }, "CREDIT_CARD"),
        (new[] { "iban", "bankaccount", "account" }, "BANK_ACCOUNT"),
        (new[] { "passport", "nationalid", "license" }, "NATIONAL_ID"),
        (new[] { "diagnosis", "icd", "condition" }, "DIAGNOSIS"),
        (new[] { "mrn", "medicalrecord", "patientid" }, "MEDICAL_RECORD"),
        (new[] { "address", "street", "postal", "zipcode" }, "ADDRESS"),
        (new[] { "firstname", "lastname", "fullname", "name" }, "NAME"),
    };

    // Azure AI Language PII category -> our DataType vocabulary.
    private static string MapAzureCategory(string category) => category switch
    {
        "Email" => "EMAIL",
        "USSocialSecurityNumber" => "SSN",
        "PhoneNumber" => "PHONE",
        "CreditCardNumber" => "CREDIT_CARD",
        "Person" => "NAME",
        "Address" => "ADDRESS",
        "IPAddress" => "IP",
        "InternationalBankingAccountNumber" => "BANK_ACCOUNT",
        _ => category.ToUpperInvariant(),
    };

    /// <inheritdoc />
    public async Task<List<Finding>> ScanAsync(SourceInput input, CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();
        if (string.IsNullOrWhiteSpace(input.Content))
        {
            return findings;
        }

        DataTable table;
        try
        {
            table = IsJson(input) ? LoadJson(input.Content) : LoadCsv(input.Content);
        }
        catch
        {
            // Not a parseable dataset (e.g. a code file routed here by mistake) — nothing to do.
            return findings;
        }

        for (int col = 0; col < table.Columns.Count; col++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var columnName = table.Columns[col];
            if (string.IsNullOrWhiteSpace(columnName))
            {
                continue;
            }

            var samples = SampleColumn(table, col);
            if (samples.Count == 0)
            {
                continue;
            }

            // 1) column-NAME check (deterministic keyword match)
            string? nameType = MatchByName(columnName);

            // 2) column-VALUE check (Azure AI Language PII; falls back to fake mode offline)
            string? valueType = null;
            try
            {
                var blob = string.Join("\n", samples);
                var entities = await _pii.DetectAsync(blob, cancellationToken);
                valueType = entities
                    .GroupBy(e => e.Category)
                    .OrderByDescending(g => g.Count())
                    .Select(g => MapAzureCategory(g.Key))
                    .FirstOrDefault();
            }
            catch
            {
                // Azure unavailable — fall back to name-based detection only.
            }

            if (nameType is null && valueType is null)
            {
                continue; // column is not sensitive
            }

            // Prefer the value-confirmed type; note who detected it.
            var dataType = valueType ?? nameType!;
            var detectedBy = valueType is not null ? AzurePiiClient.DetectedBy : DetectedByName;

            findings.Add(new Finding
            {
                DataType = dataType,
                Location = $"{input.FileName}:column={columnName}",
                Snippet = Truncate(samples[0]),
                DetectedBy = detectedBy,
                Status = "PENDING_REVIEW",
                // Id defaults to a new GUID; ScanId is stamped by the orchestrator before saving.
                // Severity, PolicyClauseId/Text, Explanation, and FixTool/FixArgs are filled in
                // downstream by the LLM reasoning step (Person F).
            });
        }

        return findings;
    }

    private static string? MatchByName(string columnName)
    {
        var normalized = new string(columnName.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        foreach (var (keywords, dataType) in NameRules)
        {
            if (keywords.Any(k => normalized.Contains(k)))
            {
                return dataType;
            }
        }
        return null;
    }

    private static List<string> SampleColumn(DataTable table, int col)
    {
        var values = new List<string>();
        foreach (var row in table.Rows)
        {
            if (col >= row.Count)
            {
                continue;
            }
            var value = row[col];
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }
            values.Add(value);
            if (values.Count >= MaxSampleValues)
            {
                break;
            }
        }
        return values;
    }

    private static bool IsJson(SourceInput input) =>
        input.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
        || string.Equals(input.Language, "json", StringComparison.OrdinalIgnoreCase);

    private static DataTable LoadCsv(string content)
    {
        using var reader = new StringReader(content);
        using var parser = new CsvParser(reader, CultureInfo.InvariantCulture);

        var rows = new List<string[]>();
        while (parser.Read())
        {
            if (parser.Record is { } record)
            {
                rows.Add(record);
            }
        }
        if (rows.Count == 0)
        {
            return new DataTable();
        }

        var columns = rows[0];
        return new DataTable(columns, rows.Skip(1));
    }

    private static DataTable LoadJson(string content)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
        {
            return new DataTable();
        }

        // Column order = union of object keys in first-seen order.
        var columns = new List<string>();
        var records = new List<JsonElement>();
        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            records.Add(element);
            foreach (var prop in element.EnumerateObject())
            {
                if (!columns.Contains(prop.Name))
                {
                    columns.Add(prop.Name);
                }
            }
        }

        var rows = new List<List<string>>();
        foreach (var record in records)
        {
            var row = new List<string>(columns.Count);
            foreach (var columnName in columns)
            {
                row.Add(record.TryGetProperty(columnName, out var value)
                    ? JsonValueToString(value)
                    : string.Empty);
            }
            rows.Add(row);
        }
        return new DataTable(columns, rows);
    }

    private static string JsonValueToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null => string.Empty,
        _ => value.GetRawText(),
    };

    private static string Truncate(string value) =>
        value.Length <= MaxSnippetLength ? value : value[..MaxSnippetLength];
}
