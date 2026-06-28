namespace CsLint.Rules;

static class StyleHelper
{
    public static (string Value, Severity Severity, bool Suppress) Parse(string raw)
    {
        var colon = raw.LastIndexOf(':');
        if (colon < 0) return (raw.Trim().ToLowerInvariant(), Severity.Warning, false);

        var value = raw[..colon].Trim().ToLowerInvariant();
        var severityStr = raw[(colon + 1)..].Trim().ToLowerInvariant();

        if (severityStr is "none" or "silent")
            return (value, Severity.Warning, true);

        var severity = severityStr switch
        {
            "error" => Severity.Error,
            _ => Severity.Warning
        };

        return (value, severity, false);
    }

    public static bool TryGet(FileConfig config, string key,
        out string value, out Severity severity)
    {
        value = "";
        severity = Severity.Warning;

        if (!config.Properties.TryGetValue(key, out var raw))
            return false;

        var (v, s, suppress) = Parse(raw);
        if (suppress) return false;

        value = v;
        severity = s;
        return true;
    }

    public static Diagnostic Make(string file, int line, int col,
        string rule, string message, Severity severity) =>
        new(file, line, col, rule, message, severity);

    public static (int Line, int Col) LineCol(
        Microsoft.CodeAnalysis.FileLinePositionSpan span) =>
        (span.StartLinePosition.Line + 1, span.StartLinePosition.Character + 1);
}
