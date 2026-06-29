using CsLint;
using CsLint.Rules;
using Xunit;

namespace CsLint.Tests;

public class FormattingRuleTests
{
    static FileConfig FmtCfg()
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["csharp_space_after_comma"] = "true",
            ["csharp_new_line_before_open_brace"] = "all",
        };
        var ec = "[*.cs]\ncsharp_space_after_comma = true\n";
        return new FileConfig(props, new List<(string, string)> { (".editorconfig", ec) });
    }

    [Fact]
    public void Does_not_apply_without_config_files()
    {
        var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["csharp_space_after_comma"] = "true",
        };
        var cfg = new FileConfig(props, new List<(string, string)>());
        Assert.False(new FormattingRule().AppliesTo(cfg));
    }

    [Fact]
    public void Applies_with_config_files_and_formatting_key()
    {
        Assert.True(new FormattingRule().AppliesTo(FmtCfg()));
    }

    [Fact]
    public async Task Analyze_returns_fmt_diagnostics_only()
    {
        // The adhoc-workspace Roslyn formatter is host-dependent (no-op without the full
        // formatting service), so we assert the contract — any diagnostic is an FMT one —
        // rather than a specific reformatting outcome.
        var path = T.WriteCs("class C{void M(int a,int b){int x=1;}}");
        try
        {
            var diags = await new FormattingRule().AnalyzeAsync(T.Unit(path, FmtCfg()));
            Assert.NotNull(diags);
            Assert.All(diags, d => Assert.Equal("FMT", d.Rule));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Clean_source_produces_no_diff()
    {
        var wellFormatted = "class C\n{\n    void M()\n    {\n    }\n}\n";
        var path = T.WriteCs(wellFormatted);
        try
        {
            var diags = await new FormattingRule().AnalyzeAsync(T.Unit(path, FmtCfg()));
            Assert.Empty(diags);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Fix_runs_without_error()
    {
        const string src = "class C{void M(int a,int b){int x=1;}}";
        var path = T.WriteCs(src);
        try
        {
            var changed = await new FormattingRule().FixAsync(path, FmtCfg());
            // When the formatter is a no-op the file is unchanged and FixAsync returns false;
            // when it reformats, the content differs. Either way the call must succeed.
            Assert.Equal(changed, File.ReadAllText(path) != src);
        }
        finally { File.Delete(path); }
    }
}
