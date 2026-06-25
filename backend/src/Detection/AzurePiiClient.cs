// Detection/AzurePiiClient.cs — Person D
// HTTP client wrapper for the Azure AI Language "PII detection" endpoint.
// Has a fake-AI-mode switch (Part 6 step 7): when no endpoint/key is configured,
// or POLICYGUARD_FAKE_AI=true, it falls back to local regex detection so the app
// runs and demos offline. detected_by = "azure-pii".
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PolicyGuard.Api.Detection;

/// <summary>A single piece of PII detected in some text.</summary>
public sealed record PiiEntity(string Text, string Category, double ConfidenceScore);

/// <summary>Configuration for <see cref="AzurePiiClient"/>.</summary>
public sealed class AzurePiiOptions
{
    public string? Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public bool FakeMode { get; set; }
    public string ApiVersion { get; set; } = "2023-04-01";
    public double MinConfidence { get; set; } = 0.5;

    /// <summary>Reads config from environment variables (used by Program.cs / DI).</summary>
    public static AzurePiiOptions FromEnvironment() => new()
    {
        Endpoint = Environment.GetEnvironmentVariable("AZURE_LANGUAGE_ENDPOINT"),
        ApiKey = Environment.GetEnvironmentVariable("AZURE_LANGUAGE_API_KEY"),
        FakeMode = string.Equals(
            Environment.GetEnvironmentVariable("POLICYGUARD_FAKE_AI"),
            "true", StringComparison.OrdinalIgnoreCase),
    };
}

public sealed class AzurePiiClient
{
    public const string DetectedBy = "azure-pii";

    private readonly HttpClient _http;
    private readonly AzurePiiOptions _options;
    private readonly bool _useFake;

    public AzurePiiClient(HttpClient http, AzurePiiOptions options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        // Fall back to fake mode whenever real credentials are missing — keeps dev/demo working.
        _useFake = options.FakeMode
            || string.IsNullOrWhiteSpace(options.Endpoint)
            || string.IsNullOrWhiteSpace(options.ApiKey);
    }

    /// <summary>True when running on local regex detection instead of the real Azure service.</summary>
    public bool IsFake => _useFake;

    /// <summary>
    /// Detects PII entities in <paramref name="text"/>. Returns an empty list for blank input.
    /// </summary>
    public async Task<IReadOnlyList<PiiEntity>> DetectAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<PiiEntity>();
        return _useFake ? FakeDetect(text) : await RealDetectAsync(text, ct);
    }

    // ---- Real Azure AI Language call -----------------------------------------------------

    private async Task<IReadOnlyList<PiiEntity>> RealDetectAsync(string text, CancellationToken ct)
    {
        var url = $"{_options.Endpoint!.TrimEnd('/')}/language/:analyze-text?api-version={_options.ApiVersion}";
        var payload = new
        {
            kind = "PiiEntityRecognition",
            analysisInput = new
            {
                documents = new[] { new { id = "1", language = "en", text } }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Add("Ocp-Apim-Subscription-Key", _options.ApiKey);

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<AnalyzeTextResponse>(cancellationToken: ct);
        var entities = body?.Results?.Documents?.FirstOrDefault()?.Entities ?? new List<AnalyzeEntity>();

        return entities
            .Where(e => e.ConfidenceScore >= _options.MinConfidence)
            .Select(e => new PiiEntity(e.Text ?? string.Empty, e.Category ?? "Unknown", e.ConfidenceScore))
            .ToList();
    }

    // ---- Fake mode (offline regex detection) ---------------------------------------------

    private static readonly (string Category, Regex Rx)[] FakePatterns =
    {
        ("Email", new Regex(@"[\w.+-]+@[\w-]+\.[\w.-]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        ("USSocialSecurityNumber", new Regex(@"\b\d{3}-\d{2}-\d{4}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        ("PhoneNumber", new Regex(@"\b(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant)),
        ("CreditCardNumber", new Regex(@"\b\d{13,16}\b",
            RegexOptions.Compiled | RegexOptions.CultureInvariant)),
    };

    private static IReadOnlyList<PiiEntity> FakeDetect(string text)
    {
        var found = new List<PiiEntity>();
        foreach (var (category, rx) in FakePatterns)
            foreach (Match m in rx.Matches(text))
                found.Add(new PiiEntity(m.Value, category, 0.95));
        return found;
    }

    // ---- Response DTOs -------------------------------------------------------------------

    private sealed class AnalyzeTextResponse
    {
        [JsonPropertyName("results")] public AnalyzeResults? Results { get; set; }
    }

    private sealed class AnalyzeResults
    {
        [JsonPropertyName("documents")] public List<AnalyzeDocument>? Documents { get; set; }
    }

    private sealed class AnalyzeDocument
    {
        [JsonPropertyName("entities")] public List<AnalyzeEntity>? Entities { get; set; }
    }

    private sealed class AnalyzeEntity
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
        [JsonPropertyName("category")] public string? Category { get; set; }
        [JsonPropertyName("confidenceScore")] public double ConfidenceScore { get; set; }
    }
}
