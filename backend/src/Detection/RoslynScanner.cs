// Detection/RoslynScanner.cs — Person C
// Parse C#/.NET syntax trees (Microsoft.CodeAnalysis) for structural PII problems:
// PII in logging calls, sensitive variable names assigned plaintext, PII into HTTP calls.
// detected_by = "roslyn".

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PolicyGuard.Api.Models;

namespace PolicyGuard.Api.Detection;

/// <summary>
/// C#/.NET-aware scanner. Parses the source into a syntax tree and walks it to find
/// structural personal-data problems that regex cannot reliably catch:
/// <list type="bullet">
///   <item>PII passed into a logging call, e.g. <c>logger.LogInformation(user.Email)</c>.</item>
///   <item>Sensitive variable names assigned a plaintext string literal, e.g. <c>var password = "hunter2";</c>.</item>
///   <item>PII passed into an HTTP client call, e.g. <c>http.PostAsync(url, user.Ssn)</c>.</item>
/// </list>
/// Detection only — severity, policy citation, and the proposed fix are added later by the
/// LLM reasoning step. Intended for C# sources; non-C# files should fall back to the regex scanner.
/// </summary>
public sealed class RoslynScanner : IScanner
{
    private const string DetectedByTag = "roslyn";

    /// <inheritdoc />
    public Task<List<Finding>> ScanAsync(SourceInput input, CancellationToken cancellationToken = default)
    {
        var findings = new List<Finding>();
        if (string.IsNullOrEmpty(input.Content))
        {
            return Task.FromResult(findings);
        }

        SyntaxTree tree = CSharpSyntaxTree.ParseText(input.Content, cancellationToken: cancellationToken);
        SyntaxNode root = tree.GetRoot(cancellationToken);
        string[] lines = input.Content.Split('\n');

        var walker = new PiiWalker(input.FileName, lines, findings);
        walker.Visit(root);

        return Task.FromResult(findings);
    }

    /// <summary>
    /// Maps a sensitive identifier or member name (e.g. "Email", "password", "ssn") to a
    /// <c>DataType</c>. Returns <c>null</c> when the name is not recognised as sensitive.
    /// More specific names are checked before generic ones.
    /// </summary>
    private static string? GuessDataType(string identifier)
    {
        string id = identifier.ToLowerInvariant();

        if (id.Contains("password") || id.Contains("passwd") || id == "pwd") return "PASSWORD";
        if (id.Contains("apikey") || id.Contains("api_key")) return "API_KEY";
        if (id.Contains("privatekey") || id.Contains("private_key")) return "PRIVATE_KEY";
        if (id.Contains("secret")) return "SECRET";
        if (id.Contains("token")) return "TOKEN";
        if (id.Contains("ssn") || id.Contains("socialsecurity")) return "SSN";
        if (id.Contains("creditcard") || id.Contains("cardnumber")) return "CREDIT_CARD";
        if (id.Contains("email")) return "EMAIL";
        if (id.Contains("phone")) return "PHONE";
        if (id.Contains("address")) return "ADDRESS";

        return null;
    }

    /// <summary>
    /// Builds a detection-stage <see cref="Finding"/>. Severity, the policy citation, the
    /// explanation, and the proposed fix are intentionally left blank — they are populated
    /// downstream by the LLM reasoning step (Person F).
    /// </summary>
    private static Finding BuildFinding(string fileName, int line, int column, string snippet, string dataType) =>
        new(
            Id: Guid.NewGuid().ToString(),
            SourceType: "code",
            Location: $"{fileName}:{line}:{column}",
            Snippet: snippet,
            DataType: dataType,
            Severity: "",
            PolicyClauseId: "",
            PolicyClauseText: "",
            Explanation: "",
            ProposedFix: null,
            DetectedBy: DetectedByTag,
            Status: "pending");

    /// <summary>
    /// Walks the C# syntax tree collecting structural PII findings.
    /// </summary>
    private sealed class PiiWalker : CSharpSyntaxWalker
    {
        private static readonly HashSet<string> HttpMethods = new(StringComparer.Ordinal)
        {
            "GetAsync", "PostAsync", "PutAsync", "DeleteAsync", "PatchAsync",
            "SendAsync", "PostAsJsonAsync", "PutAsJsonAsync", "GetStringAsync",
        };

        private readonly string _fileName;
        private readonly string[] _lines;
        private readonly List<Finding> _findings;

        public PiiWalker(string fileName, string[] lines, List<Finding> findings)
        {
            _fileName = fileName;
            _lines = lines;
            _findings = findings;
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (node.Expression is MemberAccessExpressionSyntax member &&
                node.ArgumentList.Arguments.Count > 0)
            {
                string method = member.Name.Identifier.Text;
                bool isLogging = IsLoggingCall(member, method);
                bool isHttp = HttpMethods.Contains(method);

                if (isLogging || isHttp)
                {
                    (string DataType, string Name)? hit = FindSensitiveReference(node.ArgumentList);
                    if (hit is not null)
                    {
                        AddFinding(node, hit.Value.DataType);
                    }
                }
            }

            base.VisitInvocationExpression(node);
        }

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            if (IsStringLiteral(node.Initializer?.Value))
            {
                string? dataType = GuessDataType(node.Identifier.Text);
                if (dataType is not null)
                {
                    AddFinding(node, dataType);
                }
            }

            base.VisitVariableDeclarator(node);
        }

        public override void VisitAssignmentExpression(AssignmentExpressionSyntax node)
        {
            if (IsStringLiteral(node.Right))
            {
                string? name = node.Left switch
                {
                    IdentifierNameSyntax id => id.Identifier.Text,
                    MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                    _ => null,
                };

                string? dataType = name is null ? null : GuessDataType(name);
                if (dataType is not null)
                {
                    AddFinding(node, dataType);
                }
            }

            base.VisitAssignmentExpression(node);
        }

        private static bool IsLoggingCall(MemberAccessExpressionSyntax member, string method)
        {
            // ILogger extension methods: LogInformation, LogError, LogWarning, LogDebug, Log...
            if (method.StartsWith("Log", StringComparison.Ordinal))
            {
                return true;
            }

            // Console.Write / Console.WriteLine
            return method is "Write" or "WriteLine"
                && member.Expression is IdentifierNameSyntax target
                && target.Identifier.Text == "Console";
        }

        private static bool IsStringLiteral(ExpressionSyntax? expression) =>
            expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression);

        /// <summary>
        /// Searches an expression (and everything nested within it, e.g. interpolated strings)
        /// for the first identifier or member name that looks like personal data.
        /// </summary>
        private static (string DataType, string Name)? FindSensitiveReference(SyntaxNode node)
        {
            foreach (SyntaxNode descendant in node.DescendantNodesAndSelf())
            {
                string? name = descendant switch
                {
                    MemberAccessExpressionSyntax ma => ma.Name.Identifier.Text,
                    IdentifierNameSyntax id => id.Identifier.Text,
                    _ => null,
                };

                if (name is not null && GuessDataType(name) is { } dataType)
                {
                    return (dataType, name);
                }
            }

            return null;
        }

        private void AddFinding(SyntaxNode node, string dataType)
        {
            FileLinePositionSpan span = node.GetLocation().GetLineSpan();
            int line = span.StartLinePosition.Line + 1;       // 1-based line
            int column = span.StartLinePosition.Character + 1; // 1-based column

            string snippet = line >= 1 && line <= _lines.Length
                ? _lines[line - 1].TrimEnd('\r').Trim()
                : node.ToString();

            _findings.Add(BuildFinding(_fileName, line, column, snippet, dataType));
        }
    }
}
