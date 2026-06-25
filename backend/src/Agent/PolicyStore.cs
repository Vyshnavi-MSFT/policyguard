// Agent/PolicyStore.cs — Person F
// Loads policy clauses from the Policies/*.json files, embeds each one, and
// Retrieve()s the most relevant clause for a finding via cosine similarity.
// Runs in mock mode (deterministic fake embeddings) when no Azure key is set,
// so the pipeline works offline. The LLM/embeddings never touch raw data.

using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using OpenAI.Embeddings;

namespace PolicyGuard.Api.Agent;

/// <summary>A single policy rule loaded from a Policies/*.json file.</summary>
public record PolicyClause(
    string Id,
    string Title,
    string ClauseText,
    string[] ExampleViolations);

/// <summary>A clause paired with its precomputed embedding vector.</summary>
internal sealed class StoredClause
{
    public required string PolicyId { get; init; }   // "gdpr", "hipaa", "secrets" (file name)
    public required PolicyClause Clause { get; init; }
    public required float[] Vector { get; init; }
}

public sealed class PolicyStore
{
    private readonly List<StoredClause> _store = new();
    private readonly EmbeddingClient? _embeddingClient;
    private readonly bool _mockMode;

    public PolicyStore(IConfiguration config)
    {
        var endpoint = config["AZURE_OPENAI_ENDPOINT"];
        var apiKey = config["AZURE_OPENAI_API_KEY"];
        var deployment = config["AZURE_OPENAI_EMBEDDING_DEPLOYMENT"];

        // No key -> mock mode (deterministic fake embeddings, fully offline).
        _mockMode = string.IsNullOrWhiteSpace(apiKey)
                    || string.IsNullOrWhiteSpace(endpoint)
                    || string.IsNullOrWhiteSpace(deployment);

        if (!_mockMode)
        {
            var azure = new AzureOpenAIClient(new Uri(endpoint!), new AzureKeyCredential(apiKey!));
            _embeddingClient = azure.GetEmbeddingClient(deployment);
        }
    }

    public bool IsMockMode => _mockMode;

    public int ClauseCount => _store.Count;

    /// <summary>
    /// Load every *.json file in the policies folder and embed each clause once.
    /// Call this at startup before any Retrieve().
    /// </summary>
    public async Task LoadAsync(string policiesFolder)
    {
        if (!Directory.Exists(policiesFolder))
            throw new DirectoryNotFoundException($"Policies folder not found: {policiesFolder}");

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        foreach (var file in Directory.GetFiles(policiesFolder, "*.json"))
        {
            var policyId = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            var json = await File.ReadAllTextAsync(file);
            var clauses = JsonSerializer.Deserialize<List<PolicyClause>>(json, options) ?? new();

            foreach (var clause in clauses)
            {
                var vector = await EmbedAsync(BuildEmbeddingText(clause));
                _store.Add(new StoredClause { PolicyId = policyId, Clause = clause, Vector = vector });
            }
        }
    }

    /// <summary>
    /// Return the clause most relevant to <paramref name="query"/>.
    /// Optionally restrict to a single policy (e.g. "gdpr") chosen by the user.
    /// </summary>
    public async Task<PolicyClause?> RetrieveAsync(string query, string? policyId = null)
    {
        if (_store.Count == 0) return null;

        var queryVector = await EmbedAsync(query);

        return _store
            .Where(s => policyId == null || s.PolicyId.Equals(policyId, StringComparison.OrdinalIgnoreCase))
            .Select(s => (s.Clause, Score: CosineSimilarity(queryVector, s.Vector)))
            .OrderByDescending(x => x.Score)
            .Select(x => x.Clause)
            .FirstOrDefault();
    }

    private static string BuildEmbeddingText(PolicyClause clause) =>
        $"{clause.Title}. {clause.ClauseText} Examples: {string.Join("; ", clause.ExampleViolations)}";

    private async Task<float[]> EmbedAsync(string text)
    {
        if (_mockMode) return MockEmbed(text);

        var result = await _embeddingClient!.GenerateEmbeddingAsync(text);
        return result.Value.ToFloats().ToArray();
    }

    /// <summary>Cosine similarity of two equal-length vectors. Pure math — unit-testable.</summary>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must be the same length.");

        float dot = 0f, magA = 0f, magB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        return dot / (MathF.Sqrt(magA) * MathF.Sqrt(magB) + 1e-9f);
    }

    /// <summary>
    /// Deterministic fake embedding so the store works without Azure keys.
    /// Bag-of-characters into a fixed 64-dim vector — good enough for demo retrieval.
    /// </summary>
    private static float[] MockEmbed(string text)
    {
        var v = new float[64];
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                v[ch % 64] += 1f;
        }
        return v;
    }
}
