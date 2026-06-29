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
    public void Text_reports_no_violations()
    {
        var output = T.CaptureOut(() => Reporter.Write([], OutputFormat.Text, "/repo"));
        Assert.Contains("No violations found.", output);
    }

    [Fact]
    public void Text_groups_by_category_and_counts()
    {
        var output = T.CaptureOut(() => Reporter.Write(Sample(), OutputFormat.Text, "/repo"));
        Assert.Contains("EditorConfig", output);
        Assert.Contains("SAST", output);
        Assert.Contains("Scan", output);
        Assert.Contains("1 error", output);
        Assert.Contains("2 warning", output);
    }

    [Fact]
    public void Json_uses_camelCase_and_lowercase_severity()
    {
        var output = T.CaptureOut(() => Reporter.Write(Sample(), OutputFormat.Json, "/repo"));
        Assert.Contains("\"rule\"", output);
        Assert.Contains("\"severity\"", output);
        Assert.Contains("warning", output);
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
