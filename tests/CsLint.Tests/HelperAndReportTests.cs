using CsLint;
using CsLint.Rules;
using Xunit;

namespace CsLint.Tests;

public class StyleHelperTests
{
    [Theory]
    [InlineData("true", "true", false)]
    [InlineData("TRUE:warning", "true", false)]
    [InlineData("file_scoped:error", "file_scoped", false)]
    [InlineData("x:none", "x", true)]
    [InlineData("y:silent", "y", true)]
    public void Parse_extracts_value_and_suppression(string raw, string value, bool suppress)
    {
        var (v, _, s) = StyleHelper.Parse(raw);
        Assert.Equal(value, v);
        Assert.Equal(suppress, s);
    }

    [Fact]
    public void Parse_maps_error_severity()
    {
        var (_, sev, _) = StyleHelper.Parse("true:error");
        Assert.Equal(Severity.Error, sev);
    }

    [Fact]
    public void TryGet_returns_false_when_missing()
    {
        Assert.False(StyleHelper.TryGet(T.Cfg(), "k", out _, out _));
    }

    [Fact]
    public void TryGet_returns_false_when_suppressed()
    {
        Assert.False(StyleHelper.TryGet(T.Cfg(("k", "true:none")), "k", out _, out _));
    }

    [Fact]
    public void TryGet_returns_value_and_severity()
    {
        Assert.True(StyleHelper.TryGet(T.Cfg(("k", "true:error")), "k", out var v, out var sev));
        Assert.Equal("true", v);
        Assert.Equal(Severity.Error, sev);
    }

    [Fact]
    public void LineCol_is_one_based()
    {
        var path = T.WriteCs("class C { }");
        try
        {
            var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("class C { }");
            var span = tree.GetRoot().GetLocation().GetLineSpan();
            var (line, col) = StyleHelper.LineCol(span);
            Assert.Equal(1, line);
            Assert.Equal(1, col);
        }
        finally { File.Delete(path); }
    }
}

public class ReporterTests
{
    static IReadOnlyList<Diagnostic> Sample() =>
    [
        new("/repo/A.cs", 3, 5, "EC001", "indent", Severity.Warning),
        new("/repo/B.cs", 7, 1, "SAST001", "empty catch", Severity.Error),
        new("/repo/B.cs", 9, 1, "OP004", "magic", Severity.Warning),
    ];

    [Fact]
    public void Human_reports_no_violations()
    {
        var output = T.CaptureOut(() => Reporter.Write([], OutputFormat.Human, "/repo"));
        Assert.Contains("No violations found.", output);
    }

    [Fact]
    public void Human_groups_by_category_and_counts()
    {
        var output = T.CaptureOut(() => Reporter.Write(Sample(), OutputFormat.Human, "/repo"));
        Assert.Contains("editorconfig", output);
        Assert.Contains("sast", output);
        Assert.Contains("opinionated", output);
        Assert.Contains("1 error", output);
        Assert.Contains("2 warning", output);
    }

    [Fact]
    public void Human_uses_ladder_severity_words()
    {
        // Per-finding severity word follows the shared ladder: error -> high, warning -> low.
        var output = T.CaptureOut(() => Reporter.Write(Sample(), OutputFormat.Human, "/repo"));
        Assert.Contains("high", output);
        Assert.Contains("low", output);
    }

    [Fact]
    public void Json_emits_shared_envelope()
    {
        var output = T.CaptureOut(() =>
            Reporter.Write(Sample(), OutputFormat.Json, "/repo", scanned: 7, exitCode: 1, toolVersion: "9.9.9"));

        using var doc = System.Text.Json.JsonDocument.Parse(output);
        var root = doc.RootElement;

        Assert.Equal("cslint", root.GetProperty("tool").GetString());
        Assert.Equal("9.9.9", root.GetProperty("toolVersion").GetString());
        Assert.Equal("1.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("/repo", root.GetProperty("target").GetString());

        var summary = root.GetProperty("summary");
        Assert.Equal(7, summary.GetProperty("scanned").GetInt32());
        Assert.Equal(3, summary.GetProperty("findings").GetInt32());
        Assert.Equal(1, summary.GetProperty("exitCode").GetInt32());

        var bySev = summary.GetProperty("bySeverity");
        Assert.Equal(1, bySev.GetProperty("high").GetInt32());   // SAST001 error
        Assert.Equal(2, bySev.GetProperty("low").GetInt32());    // EC001 + OP004 warnings
        Assert.Equal(0, bySev.GetProperty("critical").GetInt32());
        Assert.Equal(0, bySev.GetProperty("moderate").GetInt32());
        Assert.Equal(0, bySev.GetProperty("info").GetInt32());

        var findings = root.GetProperty("findings");
        Assert.Equal(3, findings.GetArrayLength());
        // summary.findings must equal findings.length, never a truncated subset.
        Assert.Equal(findings.GetArrayLength(), summary.GetProperty("findings").GetInt32());

        var first = findings[0];
        // Findings are grouped by category rank: editorconfig (EC001) comes first.
        Assert.Equal("low", first.GetProperty("severity").GetString());
        Assert.Equal("EC001", first.GetProperty("ruleId").GetString());
        Assert.Equal("editorconfig", first.GetProperty("category").GetString());
        Assert.Equal("indent", first.GetProperty("message").GetString());
        Assert.False(first.GetProperty("location").GetProperty("file").GetString()!.Length == 0);
        Assert.Equal(3, first.GetProperty("location").GetProperty("line").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, first.GetProperty("remediation").ValueKind);

        // The SAST finding maps error -> high and category sast.
        var sast = findings.EnumerateArray().First(f => f.GetProperty("ruleId").GetString() == "SAST001");
        Assert.Equal("high", sast.GetProperty("severity").GetString());
        Assert.Equal("sast", sast.GetProperty("category").GetString());
        // The opinionated finding maps OP* -> opinionated.
        var op = findings.EnumerateArray().First(f => f.GetProperty("ruleId").GetString() == "OP004");
        Assert.Equal("opinionated", op.GetProperty("category").GetString());
    }

    [Fact]
    public void GitHub_emits_workflow_commands()
    {
        var output = T.CaptureOut(() => Reporter.Write(Sample(), OutputFormat.GitHub, "/repo"));
        Assert.Contains("::error file=", output);
        Assert.Contains("::warning file=", output);
        Assert.Contains("[SAST001]", output);
    }
}

public class PathFilterTests
{
    static readonly string Root = Path.Combine(Path.GetTempPath(), "root");

    static string P(string rel) => Path.Combine(Root, rel.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void No_globs_means_not_excluded() =>
        Assert.False(PathFilter.IsExcluded(P("a/b.cs"), Root, []));

    [Fact]
    public void Substring_match_excludes() =>
        Assert.True(PathFilter.IsExcluded(P("src/Generated/x.cs"), Root, ["Generated"]));

    [Fact]
    public void Star_glob_matches_extension() =>
        Assert.True(PathFilter.IsExcluded(P("a/b.cs"), Root, ["*.cs"]));

    [Fact]
    public void Doublestar_glob_matches_nested() =>
        Assert.True(PathFilter.IsExcluded(P("a/b/c/d.cs"), Root, ["**/d.cs"]));

    [Fact]
    public void Question_mark_matches_single_char() =>
        Assert.True(PathFilter.IsExcluded(P("a/b.cs"), Root, ["?.cs"]));

    [Fact]
    public void Non_matching_glob_is_not_excluded() =>
        Assert.False(PathFilter.IsExcluded(P("a/b.cs"), Root, ["*.txt"]));

    [Fact]
    public void Blank_glob_is_skipped() =>
        Assert.False(PathFilter.IsExcluded(P("a/b.cs"), Root, ["   "]));
}
