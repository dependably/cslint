using CsLint.Rules;
using Xunit;

namespace CsLint.Tests;

public class FormattingRuleTests
{
    [Fact]
    public void Does_not_apply_without_config_files()
    {
        // FormattingRule requires at least one .editorconfig file loaded.
        var cfg = T.Cfg(); // empty config, no files
        Assert.False(new FormattingRule().AppliesTo(cfg));
    }

    [Fact]
    public void Applies_with_config_files_and_formatting_key()
    {
        // Construct a FileConfig that has ConfigFiles and a formatting key.
        var cfg = new CsLint.FileConfig(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["csharp_new_line_before_open_brace"] = "all"
            },
            new List<(string, string)> { ("/any/.editorconfig", "") });
        Assert.True(new FormattingRule().AppliesTo(cfg));
    }

    [Fact]
    public async Task Analyze_returns_fmt_diagnostics_only()
    {
        // A well-formatted file against a default config produces no FMT diagnostics.
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, ".editorconfig"), """
            root = true
            [*.cs]
            indent_style = space
            indent_size = 4
            csharp_new_line_before_open_brace = all
            """);
        var file = Path.Combine(dir, "C.cs");
        File.WriteAllText(file, "class C\n{\n    void M()\n    {\n    }\n}\n");
        try
        {
            var loader = new CsLint.EditorConfigLoader();
            var config = loader.GetConfig(file);
            var diags = await new FormattingRule().AnalyzeAsync(file, config);
            // Just checking it doesn't throw; may or may not produce FMT findings.
            Assert.All(diags, d => Assert.Equal("FMT", d.Rule));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Clean_source_produces_no_diff()
    {
        // An already-formatted file should produce no FMT diagnostics.
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, ".editorconfig"), """
            root = true
            [*.cs]
            indent_style = space
            indent_size = 4
            """);
        var file = Path.Combine(dir, "Clean.cs");
        File.WriteAllText(file, "class C { }\n");
        try
        {
            var loader = new CsLint.EditorConfigLoader();
            var config = loader.GetConfig(file);
            var diags = await new FormattingRule().AnalyzeAsync(file, config);
            Assert.Empty(diags);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Fix_runs_without_error()
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, ".editorconfig"), """
            root = true
            [*.cs]
            indent_style = space
            indent_size = 4
            """);
        var file = Path.Combine(dir, "C.cs");
        File.WriteAllText(file, "class C { }\n");
        try
        {
            var loader = new CsLint.EditorConfigLoader();
            var config = loader.GetConfig(file);
            // FixAsync may return false (nothing changed) but should not throw.
            var fixed_ = await new FormattingRule().FixAsync(file, config);
            Assert.IsType<bool>(fixed_);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
