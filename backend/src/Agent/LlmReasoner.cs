// Agent/LlmReasoner.cs — Person F
// For each Finding: send snippet + retrieved policy clause to Azure OpenAI, get STRICT JSON:
// { is_violation, severity, fix_tool, fix_args, policy_clause_id, explanation }.
// Validate against a C# record; retry once on parse failure. LLM never touches raw data.
// TODO: implement.
