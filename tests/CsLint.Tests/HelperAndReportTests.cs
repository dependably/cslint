using CsLint;
using CsLint.Rules;
using Xunit;

namespace CsLint.Tests;

public class StyleHelperTests
{
    [Theory]
    [InlineData("true",             "true",        false)]
    [InlineData("TRUE:warning",     "true",        false)]
    [InlineData("file_scoped:error","file_scoped", false)]
    [InlineData("x:none",           "x",           true)]
    [InlineData("y:silent",         "y",           true)]
    public void Parse_extracts_value_and_suppression(string raw, string value, bool suppress)
    {
        var (v, _, s) = StyleHelper.Parse(raw);
        Assert.Equal(value, v);
        Assert.Equal(suppress, s);
    }

    [Fact]
    public void Parse_maps_error_severity()
    {
        var (_, sev, _) = StyleHelper.Parse("file_scoped:error");
        Assert.Equal(Severity.Error, sev);
    }

    [Fact]
    public void TryGet_returns_false_when_missing()
    {
        var result = StyleHelper.TryGet(T.Cfg(), "csharp_style_namespace_declarations",
            out _, out _);
        Assert.False(result);
    }

    [Fact]
    public void TryGet_returns_false_when_suppressed()
    {
        var result = StyleHelper.TryGet(
            T.Cfg(("csharp_style_namespace_declarations", "file_scoped:none")),
            "csharp_style_namespace_declarations", out _, out _);
        Assert.False(result);
    }

    [Fact]
    public void TryGet_returns_value_and_severity()
    {
        var ok = StyleHelper.TryGet(
            T.Cfg(("csharp_style_namespace_declarations", "file_scoped:error")),
            "csharp_style_namespace_declarations", out var val, out var sev);
        Assert.True(ok);
        Assert.Equal("file_scoped", val);
        Assert.Equal(Severity.Error, sev);
    }

    [Fact]
    public void LineCol_is_one_based()
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("class C { }");
        var root = tree.GetRoot();
        var span = root.GetLocation().GetLineSpan();
        var (line, col) = StyleHelper.LineCol(span);
        Assert.Equal(1, line);
        Assert.Equal(1, col);
    }
}

public class ReporterTests
{
    [Fact]
    public void Text_reports_no_violations()
    {
        var output = T.CaptureOut(() =>
            Reporter.Write([], OutputFormat.Text, "/"));
        Assert.Contains("No violations", output);
    }

    [Fact]
    public void Text_groups_by_category_and_counts()
    {
        var diags = new List<Diagnostic>
        {
            new("/src/f.cs", 1, 1, "EC001", "indent",  Severity.Warning),
            new("/src/f.cs", 2, 1, "SAST001", "catch", Severity.Error),
        };
        var output = T.CaptureOut(() =>
            Reporter.Write(diags, OutputFormat.Text, "/"));
        Assert.Contains("EC001", output);
        Assert.Contains("SAST001", output);
    }

    [Fact]
    public void Json_uses_camelCase_and_lowercase_severity()
    {
        var diags = new List<Diagnostic>
        {
            new("/f.cs", 1, 1, "EC001", "msg", Severity.Warning),
        };
        var output = T.CaptureOut(() =>
            Reporter.Write(diags, OutputFormat.Json, "/"));
        Assert.Contains("\"severity\"", output);
        Assert.Contains("warning", output);
    }

    [Fact]
    public void GitHub_emits_workflow_commands()
    {
        var diags = new List<Diagnostic>
        {
            new("/src/f.cs", 5, 3, "SAST001", "empty catch", Severity.Error),
        };
        var output = T.CaptureOut(() =>
            Reporter.Write(diags, OutputFormat.GitHub, "/"));
        Assert.Contains("::error ", output);
        Assert.Contains("SAST001", output);
    }
}

public class PathFilterTests
{
    [Fact]
    public void No_globs_means_not_excluded()
    {
        Assert.False(PathFilter.IsExcluded("/root/src/C.cs", "/root", []));
    }

    [Fact]
    public void Substring_match_excludes()
    {
        Assert.True(PathFilter.IsExcluded("/root/Generated/C.cs", "/root",
            ["Generated"]));
    }

    [Fact]
    public void Star_glob_matches_extension()
    {
        Assert.True(PathFilter.IsExcluded("/root/src/C.g.cs", "/root",
            ["*.g.cs"]));
    }

    [Fact]
    public void Doublestar_glob_matches_nested()
    {
        Assert.True(PathFilter.IsExcluded("/root/a/b/c/File.cs", "/root",
            ["**/b/**"]));
    }

    [Fact]
    public void Question_mark_matches_single_char()
    {
        Assert.True(PathFilter.IsExcluded("/root/src/C1.cs", "/root",
            ["src/C?.cs"]));
    }

    [Fact]
    public void Non_matching_glob_is_not_excluded()
    {
        Assert.False(PathFilter.IsExcluded("/root/src/C.cs", "/root",
            ["**/Generated/**"]));
    }

    [Fact]
    public void Blank_glob_is_skipped()
    {
        Assert.False(PathFilter.IsExcluded("/root/src/C.cs", "/root",
            ["", "  "]));
    }
}
