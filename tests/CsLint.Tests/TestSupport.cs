using CsLint;
using CsLint.Rules;

namespace CsLint.Tests;

/// <summary>Shared helpers for exercising rules against in-memory source snippets.</summary>
static class T
{
    /// <summary>Writes <paramref name="code"/> to a uniquely-named temp .cs file.</summary>
    public static string WriteCs(string code, string? ext = null)
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"cslint_{Guid.NewGuid():N}{ext ?? ".cs"}");
        File.WriteAllText(path, code);
        return path;
    }

    /// <summary>Builds a <see cref="FileConfig"/> from editorconfig-style key/value pairs.</summary>
    public static FileConfig Cfg(params (string Key, string Value)[] props) =>
        new(props.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase),
            new List<(string, string)>());

    /// <summary>Runs a rule against a source snippet and returns its diagnostics.</summary>
    public static async Task<IReadOnlyList<Diagnostic>> Run(
        IRule rule, string code, FileConfig? config = null)
    {
        var path = WriteCs(code);
        try
        {
            return await rule.AnalyzeAsync(path, config ?? Cfg());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>True if any diagnostic carries the given rule id.</summary>
    public static bool Has(this IReadOnlyList<Diagnostic> diags, string ruleId) =>
        diags.Any(d => d.Rule == ruleId);

    /// <summary>Captures everything written to <see cref="Console.Out"/> during <paramref name="action"/>.</summary>
    public static string CaptureOut(Action action)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try { action(); }
        finally { Console.SetOut(original); }
        return writer.ToString();
    }

    /// <summary>Creates a fresh temp directory and returns its path.</summary>
    public static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"cslint_t_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
