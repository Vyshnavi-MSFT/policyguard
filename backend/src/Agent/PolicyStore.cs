// Agent/PolicyStore.cs — Person F
// Loads policy clauses from src/Policies/*.json, embeds each clause once (Azure OpenAI),
// caches the vectors, and Retrieve(query) returns the most relevant clause via cosine
// similarity (this produces the legal citation attached to a Finding).
//
// Design: the store is offline-safe. When Azure OpenAI credentials are not configured it
// transparently falls back to a deterministic keyword-overlap scorer, so the demo always
// works without network access. The LLM/embedding model never sees raw scanned data here —
// only the policy text and the short query the orchestrator builds from a Finding.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PolicyGuard.Data;
using PolicyGuard.Models;

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

/// <summary>A lightweight summary of a loaded policy (used by PoliciesController).</summary>
public sealed record PolicySummary(string Name, int ClauseCount);

/// <summary>
/// Loads, embeds, and retrieves policy clauses. Registered as a singleton so the policy
/// corpus is embedded only once for the lifetime of the process.
/// </summary>
public sealed class PolicyStore
{
    private readonly ILogger<PolicyStore> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string? _endpoint;
    private readonly string? _apiKey;
    private readonly string? _embeddingDeployment;
    private readonly bool _useMock;

    private readonly List<PolicyClauseDoc> _clauses = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private OpenAIClient? _client;
    private bool _initialized;

    public PolicyStore(IConfiguration configuration, ILogger<PolicyStore> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
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
    /// Returns a summary of every loaded policy (distinct name + clause count). Triggers the
    /// one-time load if it has not happened yet. Used to populate the frontend policy dropdown.
    /// </summary>
    public async Task<IReadOnlyList<PolicySummary>> GetPoliciesAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct);
        return _clauses
            .GroupBy(c => c.PolicyName)
            .Select(g => new PolicySummary(g.Key, g.Count()))
            .OrderBy(p => p.Name)
            .ToList();
    }

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

            try
            {
                if (!_useMock)
                {
                    _client = new OpenAIClient(new Uri(_endpoint!), new AzureKeyCredential(_apiKey!));
                }

                // Seed the Policy/PolicyClause tables and (in real mode) reuse any embeddings
                // already persisted there, only embedding clauses that are new or whose text changed.
                await SyncWithDatabaseAsync(embed: !_useMock, ct);

                if (_useMock)
                {
                    _logger.LogInformation(
                        "PolicyStore running in mock mode (no Azure OpenAI embedding config); {Count} clauses loaded.",
                        _clauses.Count);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PolicyStore initialization failed; falling back to in-memory keyword matching.");
                foreach (var clause in _clauses)
                {
                    clause.Embedding = null;
                }
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

    /// <summary>
    /// Seeds the Policy/PolicyClause tables from the loaded clauses and, when <paramref name="embed"/>
    /// is true, fills each clause's in-memory embedding — reusing the vector persisted in the
    /// database when its cached content hash still matches, and only calling Azure OpenAI for
    /// clauses that are new or whose text has changed. Newly computed vectors are written back so
    /// subsequent process starts skip the embedding cost.
    /// </summary>
    private async Task SyncWithDatabaseAsync(bool embed, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var policyByName = new Dictionary<string, Policy>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in await db.Policies.ToListAsync(ct))
        {
            policyByName.TryAdd(p.Name, p);
        }

        var clauseByKey = new Dictionary<(string PolicyId, string ClauseId), PolicyClause>();
        foreach (var c in await db.PolicyClauses.ToListAsync(ct))
        {
            clauseByKey.TryAdd((c.PolicyId, c.ClauseId), c);
        }

        int reused = 0, embedded = 0;

        foreach (var doc in _clauses)
        {
            ct.ThrowIfCancellationRequested();

            if (!policyByName.TryGetValue(doc.PolicyName, out var policy))
            {
                policy = new Policy { Name = doc.PolicyName };
                db.Policies.Add(policy);
                policyByName[doc.PolicyName] = policy;
            }

            if (!clauseByKey.TryGetValue((policy.Id, doc.ClauseId), out var row))
            {
                row = new PolicyClause
                {
                    PolicyId = policy.Id,
                    ClauseId = doc.ClauseId,
                    Title = doc.Title,
                    FullText = doc.ClauseText,
                };
                db.PolicyClauses.Add(row);
                clauseByKey[(policy.Id, doc.ClauseId)] = row;
            }
            else
            {
                row.Title = doc.Title;
                row.FullText = doc.ClauseText;
            }

            if (!embed)
            {
                continue;
            }

            var hash = ContentHash(doc.SearchText);
            var cached = TryParseEmbedding(row.EmbeddingVector, hash);
            if (cached is not null)
            {
                doc.Embedding = cached;
                reused++;
            }
            else
            {
                doc.Embedding = await EmbedAsync(doc.SearchText, ct);
                row.EmbeddingVector = SerializeEmbedding(hash, doc.Embedding);
                embedded++;
            }
        }

        await db.SaveChangesAsync(ct);

        if (embed)
        {
            _logger.LogInformation(
                "PolicyStore embeddings ready ({Total} clauses): {Reused} reused from database, {Embedded} newly embedded.",
                _clauses.Count, reused, embedded);
        }
    }

    /// <summary>Stable short hash of a clause's searchable text, used to detect text changes.</summary>
    private static string ContentHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes, 0, 4); // 8 hex chars is plenty to spot a content change
    }

    /// <summary>Serializes an embedding for storage as "{contentHash}|f0,f1,f2,...".</summary>
    private static string SerializeEmbedding(string contentHash, IReadOnlyList<float> vector)
    {
        var sb = new StringBuilder(contentHash).Append('|');
        for (int i = 0; i < vector.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(vector[i].ToString("R", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parses a stored embedding, returning null when it is missing, malformed, or its content
    /// hash no longer matches <paramref name="expectedHash"/> (i.e. the clause text changed).
    /// </summary>
    private static float[]? TryParseEmbedding(string? stored, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        int sep = stored.IndexOf('|');
        if (sep <= 0 || !string.Equals(stored[..sep], expectedHash, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = stored[(sep + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var vector = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out vector[i]))
            {
                return null;
            }
        }
        return vector;
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
