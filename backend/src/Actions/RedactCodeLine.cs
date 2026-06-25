// Actions/RedactCodeLine.cs — Person D (deterministic fix, NO LLM)
// Redact a single offending line of source code (e.g. a hardcoded secret or a
// line that logs PII). Replaces the line's content with a redaction note while
// preserving the line's indentation and the surrounding file. Must be unit-tested.
using System;
using System.IO;

namespace PolicyGuard.Actions;

public static class RedactCodeLine
{
    /// <summary>
    /// Returns <paramref name="source"/> with line <paramref name="lineNumber"/> (1-based)
    /// replaced by a redaction comment. Operates on text so it is trivial to unit-test.
    /// Indentation, the file's other lines, and line endings (LF / CRLF) are preserved.
    /// </summary>
    /// <param name="source">The full source file text.</param>
    /// <param name="lineNumber">The 1-based line to redact (matches Finding locations like "file.cs:42").</param>
    /// <param name="reason">Optional short reason appended to the redaction note.</param>
    public static string Redact(string source, int lineNumber, string? reason = null)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (lineNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(lineNumber), "Line numbers are 1-based.");

        var lines = source.Split('\n');
        int idx = lineNumber - 1;
        if (idx >= lines.Length)
            throw new ArgumentOutOfRangeException(nameof(lineNumber),
                $"Line {lineNumber} is out of range; the source has {lines.Length} line(s).");

        var raw = lines[idx];
        bool crlf = raw.EndsWith('\r');                 // preserve Windows line endings
        var content = crlf ? raw[..^1] : raw;

        var indent = content[..(content.Length - content.TrimStart().Length)]; // preserve indentation
        var note = string.IsNullOrWhiteSpace(reason)
            ? "// [REDACTED by PolicyGuard]"
            : $"// [REDACTED by PolicyGuard: {reason.Trim()}]";

        lines[idx] = indent + note + (crlf ? "\r" : string.Empty);
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Reads <paramref name="path"/>, redacts line <paramref name="lineNumber"/>, and writes it back.
    /// </summary>
    public static void ApplyToFile(string path, int lineNumber, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("File path is required.", nameof(path));

        var source = File.ReadAllText(path);
        File.WriteAllText(path, Redact(source, lineNumber, reason));
    }
}
