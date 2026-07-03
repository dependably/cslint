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

    // A sample that adds one Info-severity finding (e.g. from a suggestion-level naming rule).
    static IReadOnlyList<Diagnostic> SampleWithInfo() =>
    [
        new("/repo/A.cs", 3, 5, "EC001", "indent", Severity.Warning),
        new("/repo/B.cs", 7, 1, "SAST001", "empty catch", Severity.Error),
        new("/repo/C.cs", 2, 1, "CS040", "naming", Severity.Info),
    ];

    [Fact]
    public void Human_reports_no_violations()
    {
        var output = T.CaptureOut(() => Reporter.Write([], OutputFormat.Human, "/repo"));
        Assert.Contains("No violations found.", output);
    }

    [Fact]
    public void Human_groups_by_severity_and_counts()
    {
        var output = T.CaptureOut(() => Reporter.Write(Sample(), OutputFormat.Human, "/repo"));
        // Sections are keyed by severity now, not category.
        Assert.Contains("errors", output);
        Assert.Contains("warnings", output);
        Assert.Contains("1 error", output);
        Assert.Contains("2 warning", output);
        // The per-rule frequency table lists every rule id that fired.
        Assert.Contains("Findings by rule:", output);
        Assert.Contains("EC001", output);
        Assert.Contains("SAST001", output);
        Assert.Contains("OP004", output);
    }

    [Fact]
    public void Human_uses_canonical_severity_words()
    {
        // Per-finding severity word uses the canonical vocabulary: error / warning (not high/low).
        var output = T.CaptureOut(() => Reporter.Write(Sample(), OutputFormat.Human, "/repo"));
        Assert.Contains("error", output);
        Assert.Contains("warning", output);
        // The old shared-ladder words must not leak into the human per-finding lines.
        Assert.DoesNotContain("high", output);
        Assert.DoesNotContain(" low ", output);
    }

    [Fact]
    public void Human_orders_errors_before_warnings()
    {
        // Regression pin: a lone error must surface before the warnings, not be buried by the old
        // category-first ordering (editorconfig warnings sorted ahead of the sast error).
        var output = T.CaptureOut(() => Reporter.Write(Sample(), OutputFormat.Human, "/repo"));
        var errorIdx = output.IndexOf("[SAST001]", StringComparison.Ordinal);   // the one error
        var warnIdx = output.IndexOf("[EC001]", StringComparison.Ordinal);      // a warning
        Assert.True(errorIdx >= 0 && warnIdx >= 0);
        Assert.True(errorIdx < warnIdx, "the error finding must be printed before the warning finding");
    }

    [Fact]
    public void Human_collapses_repeated_rule_and_caps_output()
    {
        // 25 findings of one rule across many files: the report must collapse the repeats into a
        // "+N more <RULE> in M files" note rather than printing all 25.
        var many = Enumerable.Range(0, 25)
            .Select(i => new Diagnostic($"/repo/F{i}.cs", 1, 1, "EC001", "indent", Severity.Warning))
            .ToList();
        var output = T.CaptureOut(() => Reporter.Write(many, OutputFormat.Human, "/repo"));
        Assert.Contains("more EC001 in", output);
        Assert.Contains("Findings by rule:", output);

        // --max-findings caps the printed count; the overflow note names how to see them all.
        var capped = T.CaptureOut(() => Reporter.Write(many, OutputFormat.Human, "/repo", maxFindings: 3));
        Assert.Contains("more finding", capped);
        Assert.Contains("--no-limit", capped);
    }

    [Fact]
    public void Human_no_limit_prints_every_finding_uncollapsed()
    {
        // --no-limit disables both the cap and the per-rule collapse: all 15 lines are printed and
        // no collapse note appears.
        var many = Enumerable.Range(0, 15)
            .Select(i => new Diagnostic($"/repo/F{i}.cs", 1, 1, "EC001", "indent", Severity.Warning))
            .ToList();
        var output = T.CaptureOut(() => Reporter.Write(many, OutputFormat.Human, "/repo", noLimit: true));
        Assert.DoesNotContain("more EC001 in", output);
        // Every file's finding line is present.
        Assert.Equal(15, System.Text.RegularExpressions.Regex.Matches(output, @"\[EC001\]").Count);
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
        Assert.NotEmpty(first.GetProperty("location").GetProperty("file").GetString()!);
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

    // Reporter must emit correct labels and counts for Info severity.

    [Fact]
    public void Human_info_finding_shows_info_label_and_summary()
    {
        // Reporter uses the suite ladder: Info → "info" label (Cyan), and the summary must
        // include the info count when infos > 0.
        var output = T.CaptureOut(() => Reporter.Write(SampleWithInfo(), OutputFormat.Human, "/repo"));
        Assert.Contains("info", output);        // per-finding "info" severity word
        Assert.Contains("1 info", output);      // summary segment "1 info"
        // The error and warning counts must still be correct.
        Assert.Contains("1 error", output);
        Assert.Contains("1 warning", output);
    }

    [Fact]
    public void Json_info_finding_populates_bySeverity_info()
    {
        // bySeverity.info must be 1 (not 0) when there is an Info-severity finding, and the
        // ladder word for each finding must be "info" (not folded into warnings).
        var output = T.CaptureOut(() =>
            Reporter.Write(SampleWithInfo(), OutputFormat.Json, "/repo",
                scanned: 3, exitCode: 0, toolVersion: "9.9.9"));

        using var doc = System.Text.Json.JsonDocument.Parse(output);
        var root = doc.RootElement;

        var bySev = root.GetProperty("summary").GetProperty("bySeverity");
        Assert.Equal(1, bySev.GetProperty("high").GetInt32());   // SAST001 error
        Assert.Equal(1, bySev.GetProperty("low").GetInt32());    // EC001 warning
        Assert.Equal(1, bySev.GetProperty("info").GetInt32());   // CS040 info

        // The CS040 finding must carry severity = "info" in the findings array.
        var findings = root.GetProperty("findings");
        var cs040 = findings.EnumerateArray()
            .First(f => f.GetProperty("ruleId").GetString() == "CS040");
        Assert.Equal("info", cs040.GetProperty("severity").GetString());
    }

    [Fact]
    public void GitHub_emits_notice_for_info_finding()
    {
        // Info-severity findings must emit ::notice, not ::warning or ::error.
        var output = T.CaptureOut(() => Reporter.Write(SampleWithInfo(), OutputFormat.GitHub, "/repo"));
        Assert.Contains("::notice file=", output);
        Assert.Contains("[CS040]", output);
        // error and warning annotations must still appear for the other findings.
        Assert.Contains("::error file=", output);
        Assert.Contains("::warning file=", output);
    }

    [Fact]
    public void GitHub_emits_workflow_commands()
    {
        var output = T.CaptureOut(() => Reporter.Write(Sample(), OutputFormat.GitHub, "/repo"));
        Assert.Contains("::error file=", output);
        Assert.Contains("::warning file=", output);
        Assert.Contains("[SAST001]", output);
    }

    [Fact]
    public void GitHub_emits_relative_paths()
    {
        // Sample() diagnostics are rooted under "/repo"; annotations must use
        // workspace-relative paths so GitHub Actions can attach them inline to PR files.
        // Must produce e.g. "file=A.cs", never the absolute "file=/repo/A.cs".
        var output = T.CaptureOut(() => Reporter.Write(Sample(), OutputFormat.GitHub, "/repo"));
        Assert.Contains("file=A.cs", output);
        Assert.Contains("file=B.cs", output);
        // Absolute paths must not appear.
        Assert.DoesNotContain("file=/repo/", output);
    }

    [Fact]
    public void GitHub_encodes_comma_in_file_path()
    {
        // A comma in the file path would split the property list and break the runner's parser.
        // The fix must produce %2C so the runner sees one file property, not two.
        var diagnostics = new Diagnostic[]
        {
            new("/repo/file,name.cs", 1, 1, "EC001", "indent", Severity.Error),
        };
        var output = T.CaptureOut(() => Reporter.Write(diagnostics, OutputFormat.GitHub, "/repo"));
        // The file value must contain %2C, never a raw comma between file= and ,line=
        Assert.Contains("%2C", output);
        Assert.DoesNotContain("file=/repo/file,name.cs,line=", output);
    }

    [Fact]
    public void GitHub_encodes_percent_in_file_path()
    {
        // A literal '%' in the file path must be encoded first (%25) so subsequent
        // replacements never double-encode already-encoded sequences.
        var diagnostics = new Diagnostic[]
        {
            new("/repo/file%20name.cs", 2, 3, "SAST001", "issue", Severity.Warning),
        };
        var output = T.CaptureOut(() => Reporter.Write(diagnostics, OutputFormat.GitHub, "/repo"));
        Assert.Contains("%25", output);
        // The raw % must not survive unescaped in the property section.
        Assert.DoesNotContain("file=/repo/file%20name.cs,", output);
    }

    [Fact]
    public void GitHub_encodes_comma_and_percent_together()
    {
        // Mixed case: path contains both ',' and '%'; both must be encoded.
        var diagnostics = new Diagnostic[]
        {
            new("/repo/file%,name.cs", 5, 10, "OP004", "magic", Severity.Warning),
        };
        var output = T.CaptureOut(() => Reporter.Write(diagnostics, OutputFormat.GitHub, "/repo"));
        Assert.Contains("%25", output);
        Assert.Contains("%2C", output);
    }

    // Regression tests for GitHub Actions annotation escaping.

    [Fact]
    public void GitHub_escapes_newline_in_message()
    {
        // A message containing a raw newline must produce a single output line.
        var diag = new Diagnostic("/repo/A.cs", 1, 1, "CS010", "line1\nline2", Severity.Warning);
        var output = T.CaptureOut(() => Reporter.Write([diag], OutputFormat.GitHub, "/repo"));

        // Must be exactly one annotation line (the trailing newline from WriteLine is the only
        // real newline; everything inside the message must be percent-encoded).
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Contains("%0A", lines[0]);
        Assert.DoesNotContain("line2\n", lines[0]);
    }

    [Fact]
    public void GitHub_escapes_carriage_return_in_message()
    {
        var diag = new Diagnostic("/repo/A.cs", 1, 1, "CS010", "part1\rpart2", Severity.Warning);
        var output = T.CaptureOut(() => Reporter.Write([diag], OutputFormat.GitHub, "/repo"));

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.Contains("%0D", lines[0]);
    }

    [Fact]
    public void GitHub_escapes_percent_in_message()
    {
        // A literal '%' in the message must be escaped so the runner does not mis-decode it.
        var diag = new Diagnostic("/repo/A.cs", 1, 1, "CS010", "100% done", Severity.Warning);
        var output = T.CaptureOut(() => Reporter.Write([diag], OutputFormat.GitHub, "/repo"));

        Assert.Contains("%25", output);
        Assert.DoesNotContain("100% done", output);
    }

    [Fact]
    public void GitHub_escapes_comma_in_file_property()
    {
        // A comma in the file path would break the property list parsing.
        var diag = new Diagnostic("/repo/a,b/C.cs", 1, 1, "EC001", "indent", Severity.Warning);
        var output = T.CaptureOut(() => Reporter.Write([diag], OutputFormat.GitHub, "/repo"));

        // The file= property value must not contain a raw comma.
        var annotationLine = output.Split('\n')[0];
        var fileValue = annotationLine.Split("file=")[1].Split(",line=")[0];
        Assert.DoesNotContain(",", fileValue);
        Assert.Contains("%2C", fileValue);
    }

    [Fact]
    public void GitHub_escapes_colon_in_file_property()
    {
        // A colon in the file path (e.g. Windows drive letter C:/) must be escaped.
        var diag = new Diagnostic("C:/repo/A.cs", 1, 1, "EC001", "indent", Severity.Warning);
        var output = T.CaptureOut(() => Reporter.Write([diag], OutputFormat.GitHub, "/repo"));

        // The file= property value must have the colon encoded.
        var annotationLine = output.Split('\n')[0];
        var fileValue = annotationLine.Split("file=")[1].Split(",line=")[0];
        Assert.Contains("%3A", fileValue);
    }

    [Fact]
    public void GitHub_mixed_newline_and_percent_escaping_is_single_line()
    {
        // Verify combined escaping: % first so %0A itself does not get double-encoded.
        var diag = new Diagnostic("/repo/A.cs", 2, 3, "SAST001",
            "use 100% safe\nand another line", Severity.Error);
        var output = T.CaptureOut(() => Reporter.Write([diag], OutputFormat.GitHub, "/repo"));

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.StartsWith("::error file=", lines[0]);
        Assert.Contains("%25", lines[0]);
        Assert.Contains("%0A", lines[0]);
        // %0A itself must not have had its % double-encoded to %250A
        Assert.DoesNotContain("%250A", lines[0]);
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
    public void Question_mark_does_not_match_path_separator() =>
        Assert.False(PathFilter.IsExcluded(P("a/b.cs"), Root, ["a?b.cs"]));

    [Fact]
    public void Non_matching_glob_is_not_excluded() =>
        Assert.False(PathFilter.IsExcluded(P("a/b.cs"), Root, ["*.txt"]));

    [Fact]
    public void Blank_glob_is_skipped() =>
        Assert.False(PathFilter.IsExcluded(P("a/b.cs"), Root, ["   "]));
}

/// <summary>
/// Regression tests for bare relative globs (*.cs, **/*.cs) must enumerate the
/// current directory, not the filesystem root.
/// </summary>
public class PathFilterExpandGlobTests : IDisposable
{
    // A scratch directory we control; a single .cs file lives inside it.
    readonly string _dir;
    readonly string _savedDir;
    const string FileName = "Sample.cs";

    public PathFilterExpandGlobTests()
    {
        _dir = T.TempDir();
        File.WriteAllText(Path.Combine(_dir, FileName), "// test");
        // Switch cwd so bare globs resolve against our temp dir.
        _savedDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_dir);
    }

    public void Dispose()
    {
        Directory.SetCurrentDirectory(_savedDir);
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
        GC.SuppressFinalize(this);
    }

    // Normalise to absolute path using the current cwd at call time (which is _dir after setup).
    static string Canonical(string path) => Path.GetFullPath(path);

    [Fact]
    public void BareStarGlob_enumerates_current_directory()
    {
        // A bare "*.cs" glob must enumerate the current directory and return Sample.cs,
        // rather than anchoring at "/".
        // Use the expected absolute path constructed from the resolved cwd to avoid symlink mismatches.
        var expected = Path.Combine(Directory.GetCurrentDirectory(), FileName);
        var results = PathFilter.ExpandTarget("*.cs")
            .Select(Canonical).ToList();
        Assert.Contains(expected, results);
    }

    [Fact]
    public void DoubleStarGlob_enumerates_current_directory()
    {
        // **/*.cs should also anchor at cwd, not at "/".
        var expected = Path.Combine(Directory.GetCurrentDirectory(), FileName);
        var results = PathFilter.ExpandTarget("**/*.cs")
            .Select(Canonical).ToList();
        Assert.Contains(expected, results);
    }
}
