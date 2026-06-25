// Agent/LlmReasoner.cs — Person F
// For each already-flagged finding, sends the abstracted snippet + the retrieved
// policy clause to Azure OpenAI and gets back STRICT JSON describing the decision.
// Validates the JSON into LlmDecision; retries once on parse failure, then falls
// back to a safe default so a bad LLM response never crashes a scan.
// Runs in mock mode (canned decision) when no Azure key is set.
// The LLM only ever sees the abstraction and returns a fix NAME + args — never raw data.

using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;

namespace PolicyGuard.Api.Agent;

/// <summary>The strict JSON shape the LLM must return for each finding.</summary>
public record LlmDecision(
    bool IsViolation,
    string Severity,        // LOW | MEDIUM | HIGH | CRITICAL
    string FixTool,         // MaskColumn | DropColumn | AnonymizeColumn | RedactCodeLine
    Dictionary<string, string> FixArgs,
    string PolicyClauseId,
    string Explanation);

public sealed class LlmReasoner
{
    private readonly ChatClient? _chat;
    private readonly bool _mockMode;

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public LlmReasoner(IConfiguration config)
    {
        var endpoint = config["AZURE_OPENAI_ENDPOINT"];
        var apiKey = config["AZURE_OPENAI_API_KEY"];
        var deployment = config["AZURE_OPENAI_CHAT_DEPLOYMENT"];

        _mockMode = string.IsNullOrWhiteSpace(apiKey)
                    || string.IsNullOrWhiteSpace(endpoint)
                    || string.IsNullOrWhiteSpace(deployment);

        if (!_mockMode)
        {
            var azure = new AzureOpenAIClient(new Uri(endpoint!), new AzureKeyCredential(apiKey!));
            _chat = azure.GetChatClient(deployment);
        }
    }

    public bool IsMockMode => _mockMode;

    /// <summary>
    /// Judge a single finding against the retrieved clause and propose a fix.
    /// <paramref name="snippet"/> must be an ABSTRACTION (e.g. "a column of SSN-like values"),
    /// never raw personal data.
    /// </summary>
    public async Task<LlmDecision> ReasonAsync(string dataType, string snippet, PolicyClause clause)
    {
        if (_mockMode) return MockDecision(dataType, clause);

        var prompt = BuildPrompt(dataType, snippet, clause);
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat() // force valid JSON
        };

        // Try once, retry once on parse failure, then fall back.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var response = await _chat!.CompleteChatAsync(
                    new ChatMessage[] { new UserChatMessage(prompt) }, options);
                var text = response.Value.Content[0].Text;

                var decision = JsonSerializer.Deserialize<LlmDecision>(text, JsonOptions);
                if (decision is not null && IsValid(decision))
                    return decision;
            }
            catch (JsonException)
            {
                // fall through to retry / fallback
            }
        }

        return MockDecision(dataType, clause);
    }

    private static string BuildPrompt(string dataType, string snippet, PolicyClause clause) => $$"""
        You are a data-protection compliance assistant.
        Detected data type: {{dataType}}
        Abstracted snippet (no raw data): {{snippet}}
        Relevant policy clause [{{clause.Id}}]: {{clause.ClauseText}}

        Decide whether this is a violation of the clause. Reply with ONLY a JSON object:
        {
          "isViolation": true,
          "severity": "LOW | MEDIUM | HIGH | CRITICAL",
          "fixTool": "MaskColumn | DropColumn | AnonymizeColumn | RedactCodeLine",
          "fixArgs": { "key": "value" },
          "policyClauseId": "{{clause.Id}}",
          "explanation": "one concise sentence citing the clause"
        }
        """;

    private static bool IsValid(LlmDecision d) =>
        d.Severity is "LOW" or "MEDIUM" or "HIGH" or "CRITICAL"
        && d.FixTool is "MaskColumn" or "DropColumn" or "AnonymizeColumn" or "RedactCodeLine"
        && !string.IsNullOrWhiteSpace(d.PolicyClauseId);

    /// <summary>
    /// Deterministic canned decision used in mock mode and as the safe fallback.
    /// Maps data types to a sensible severity and fix.
    /// </summary>
    private static LlmDecision MockDecision(string dataType, PolicyClause clause)
    {
        var dt = dataType.ToUpperInvariant();

        var severity = dt switch
        {
            "SSN" or "CREDIT_CARD" or "API_KEY" or "PASSWORD" or "PRIVATE_KEY"
                or "TOKEN" or "PHI_DIAGNOSIS" or "MEDICAL_RECORD_NUMBER" => "CRITICAL",
            "EMAIL" or "PHONE" or "IP" or "NAME" or "ADDRESS" => "HIGH",
            _ => "MEDIUM"
        };

        var fixTool = dt switch
        {
            "SSN" or "CREDIT_CARD" or "PHI_DIAGNOSIS" or "MEDICAL_RECORD_NUMBER" => "DropColumn",
            "API_KEY" or "PASSWORD" or "PRIVATE_KEY" or "TOKEN" => "RedactCodeLine",
            "NAME" or "ADDRESS" => "AnonymizeColumn",
            _ => "MaskColumn"
        };

        return new LlmDecision(
            IsViolation: true,
            Severity: severity,
            FixTool: fixTool,
            FixArgs: new Dictionary<string, string> { ["target"] = "value", ["style"] = "partial" },
            PolicyClauseId: clause.Id,
            Explanation: $"{dataType} is regulated under {clause.Id} ({clause.Title}); applying {fixTool}.");
    }
}
