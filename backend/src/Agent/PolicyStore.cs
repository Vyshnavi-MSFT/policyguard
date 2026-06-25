// Agent/PolicyStore.cs — Person F
// Loads policy clauses from src/Policies/*.json, embeds each clause once (Azure OpenAI),
// caches the vectors, and Retrieve(query) returns the most relevant clause via cosine
// similarity (this produces the legal citation attached to a Finding).
//
// Design: the store is offline-safe. When Azure OpenAI credentials are not configured it
// transparently falls back to a deterministic keyword-overlap scorer, so the demo always
// works without network access. The LLM/embedding model never sees raw scanned data here —
// only the policy text and the short query the orchestrator builds from a Finding.

using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PolicyGuard.Agent;

/// <summary>
/// A single policy clause loaded from the JSON policy files, plus its cached embedding.
/// </summary>
public sealed record PolicyClauseDoc(
    string PolicyName,
    string ClauseId,
    string Title,
    string ClauseText,
    IReadOnlyList<string> ExampleViolations)
{
    /// <summary>Cached embedding vector for this clause (null in mock mode).</summary>
    public float[]? Embedding { get; set; }

    /// <summary>The text that is embedded / keyword-matched for this clause.</summary>
    public string SearchText =>
        $"{Title}. {ClauseText} {string.Join(' ', ExampleViolations)}";
}

/// <summary>The result of a retrieval: the best-matching clause and its similarity score.</summary>
public sealed record PolicyClauseMatch(
    string ClauseId,
    string Title,
    string ClauseText,
    double Score);

/// <summary>
/// Loads, embeds, and retrieves policy clauses. Registered as a singleton so the policy
/// corpus is embedded only once for the lifetime of the process.
/// </summary>
public sealed class PolicyStore
{
    private readonly ILogger<PolicyStore> _logger;
    private readonly string? _endpoint;
    private readonly string? _apiKey;
    private readonly string? _embeddingDeployment;
    private readonly bool _useMock;

    private readonly List<PolicyClauseDoc> _clauses = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private OpenAIClient? _client;
    private bool _initialized;

    public PolicyStore(IConfiguration configuration, ILogger<PolicyStore> logger)
    {
        _logger = logger;
        _endpoint = configuration["AZURE_OPENAI_ENDPOINT"];
        _apiKey = configuration["AZURE_OPENAI_API_KEY"];
        _embeddingDeployment = configuration["AZURE_OPENAI_EMBEDDING_DEPLOYMENT"];

        _useMock = string.IsNullOrWhiteSpace(_endpoint)
                   || string.IsNullOrWhiteSpace(_apiKey)
                   || string.IsNullOrWhiteSpace(_embeddingDeployment);
    }

    /// <summary>True when no Azure OpenAI credentials are configured (deterministic fallback).</summary>
    public bool IsMockMode => _useMock;

    /// <summary>Number of policy clauses currently loaded.</summary>
    public int ClauseCount => _clauses.Count;

    /// <summary>
    /// Loads the policy JSON files and (in real mode) embeds every clause once. Safe to call
    /// repeatedly — initialization runs only on the first call.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized)
            {
                return;
            }

            LoadClausesFromDisk();

            if (!_useMock)
            {
                try
                {
                    _client = new OpenAIClient(new Uri(_endpoint!), new AzureKeyCredential(_apiKey!));
                    foreach (var clause in _clauses)
                    {
                        ct.ThrowIfCancellationRequested();
                        clause.Embedding = await EmbedAsync(clause.SearchText, ct);
                    }
                    _logger.LogInformation("PolicyStore embedded {Count} clauses via Azure OpenAI.", _clauses.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "PolicyStore embedding failed; falling back to keyword matching.");
                    foreach (var clause in _clauses)
                    {
                        clause.Embedding = null;
                    }
                }
            }
            else
            {
                _logger.LogInformation(
                    "PolicyStore running in mock mode (no Azure OpenAI embedding config); {Count} clauses loaded.",
                    _clauses.Count);
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Returns the policy clause most relevant to <paramref name="query"/>, or null if no
    /// clauses are loaded. Uses cosine similarity over embeddings when available, otherwise a
    /// deterministic keyword-overlap score.
    /// </summary>
    public async Task<PolicyClauseMatch?> RetrieveAsync(
        string query,
        string? policyName = null,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct);

        var candidates = _clauses
            .Where(c => policyName is null
                        || string.Equals(c.PolicyName, policyName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        float[]? queryEmbedding = null;
        if (!_useMock && _client is not null && candidates.All(c => c.Embedding is not null))
        {
            try
            {
                queryEmbedding = await EmbedAsync(query, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PolicyStore query embedding failed; using keyword matching for this query.");
            }
        }

        PolicyClauseDoc best = candidates[0];
        double bestScore = double.NegativeInfinity;

        foreach (var clause in candidates)
        {
            double score = queryEmbedding is not null && clause.Embedding is not null
                ? CosineSimilarity(queryEmbedding, clause.Embedding)
                : KeywordScore(query, clause);

            if (score > bestScore)
            {
                bestScore = score;
                best = clause;
            }
        }

        return new PolicyClauseMatch(best.ClauseId, best.Title, best.ClauseText, bestScore);
    }

    private async Task<float[]> EmbedAsync(string text, CancellationToken ct)
    {
        var options = new EmbeddingsOptions(_embeddingDeployment, new[] { text });
        Response<Embeddings> response = await _client!.GetEmbeddingsAsync(options, ct);
        return response.Value.Data[0].Embedding.ToArray();
    }

    private void LoadClausesFromDisk()
    {
        _clauses.Clear();

        var policiesDir = LocatePoliciesDirectory();
        if (policiesDir is null)
        {
            _logger.LogWarning("PolicyStore could not locate a Policies directory; no clauses loaded.");
            return;
        }

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (var file in Directory.EnumerateFiles(policiesDir, "*.json"))
        {
            var policyName = Path.GetFileNameWithoutExtension(file).ToUpperInvariant();
            try
            {
                var json = File.ReadAllText(file);
                var entries = JsonSerializer.Deserialize<List<PolicyJsonEntry>>(json, jsonOptions);
                if (entries is null)
                {
                    continue;
                }

                foreach (var e in entries)
                {
                    if (string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.ClauseText))
                    {
                        continue;
                    }

                    _clauses.Add(new PolicyClauseDoc(
                        policyName,
                        e.Id,
                        e.Title ?? e.Id,
                        e.ClauseText,
                        e.ExampleViolations ?? Array.Empty<string>()));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PolicyStore failed to parse policy file {File}.", file);
            }
        }
    }

    /// <summary>
    /// Probes a handful of likely locations (relative to the app's base/working directory and
    /// their parents) for the folder that holds the policy JSON files.
    /// </summary>
    private static string? LocatePoliciesDirectory()
    {
        var roots = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var root in roots)
        {
            var dir = new DirectoryInfo(root);
            for (int depth = 0; depth < 8 && dir is not null; depth++)
            {
                foreach (var candidate in new[]
                {
                    Path.Combine(dir.FullName, "Policies"),
                    Path.Combine(dir.FullName, "src", "Policies"),
                    Path.Combine(dir.FullName, "backend", "src", "Policies"),
                })
                {
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                dir = dir.Parent;
            }
        }
        return null;
    }

    private static double CosineSimilarity(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        int length = Math.Min(a.Count, b.Count);
        double dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        if (magA == 0 || magB == 0)
        {
            return 0;
        }
        return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
    }

    /// <summary>
    /// Deterministic fallback relevance score: counts how many distinct query tokens appear in
    /// the clause's searchable text, normalized by the query token count.
    /// </summary>
    private static double KeywordScore(string query, PolicyClauseDoc clause)
    {
        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0)
        {
            return 0;
        }

        var clauseTokens = Tokenize(clause.SearchText);
        double overlap = queryTokens.Count(clauseTokens.Contains);
        return overlap / queryTokens.Count;
    }

    private static HashSet<string> Tokenize(string text)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split(
                     new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '(', ')', '"', '\'', '/', '-', '_' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length > 2)
            {
                tokens.Add(raw);
            }
        }
        return tokens;
    }

    /// <summary>Shape of one entry in the src/Policies/*.json files.</summary>
    private sealed class PolicyJsonEntry
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? ClauseText { get; set; }
        public string[]? ExampleViolations { get; set; }
    }
}
