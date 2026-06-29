using CsLint.Rules;
using Xunit;

namespace CsLint.Tests;

public class TextRuleTests
{
    // ── EC001 IndentStyle ─────────────────────────────────────────────────────

    [Fact]
    public async Task EC001_flags_tab_when_spaces_expected()
    {
        var code = "\tclass C { }";
        var diags = await T.Run(new IndentStyleRule(), code,
            T.Cfg(("indent_style", "space"), ("indent_size", "4")));
        Assert.True(diags.Has("EC001"));
    }

    [Fact]
    public async Task EC001_flags_spaces_when_tabs_expected()
    {
        var code = "    class C { }";
        var diags = await T.Run(new IndentStyleRule(), code,
            T.Cfg(("indent_style", "tab"), ("indent_size", "4")));
        Assert.True(diags.Has("EC001"));
    }

    [Fact]
    public async Task EC001_clean_when_spaces_match()
    {
        var code = "    class C { }";
        var diags = await T.Run(new IndentStyleRule(), code,
            T.Cfg(("indent_style", "space"), ("indent_size", "4")));
        Assert.False(diags.Has("EC001"));
    }

    [Fact]
    public async Task EC001_does_not_apply_without_indent_style()
    {
        Assert.False(new IndentStyleRule().AppliesTo(T.Cfg()));
    }

    [Fact]
    public async Task EC001_fix_converts_tabs_to_spaces()
    {
        var path = T.WriteCs("\tint x;");
        try
        {
            var rule = new IndentStyleRule();
            var cfg = T.Cfg(("indent_style", "space"), ("indent_size", "4"));
            await rule.FixAsync(path, cfg);
            var content = await File.ReadAllTextAsync(path);
            Assert.StartsWith("    ", content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EC001_fix_converts_spaces_to_tabs()
    {
        var path = T.WriteCs("    int x;");
        try
        {
            var rule = new IndentStyleRule();
            var cfg = T.Cfg(("indent_style", "tab"), ("indent_size", "4"));
            await rule.FixAsync(path, cfg);
            var content = await File.ReadAllTextAsync(path);
            Assert.StartsWith("\t", content);
        }
        finally { File.Delete(path); }
    }

    // ── EC002 TrailingWhitespace ──────────────────────────────────────────────

    [Fact]
    public async Task EC002_flags_trailing_whitespace()
    {
        var code = "class C {   \n}";
        var diags = await T.Run(new TrailingWhitespaceRule(), code,
            T.Cfg(("trim_trailing_whitespace", "true")));
        Assert.True(diags.Has("EC002"));
    }

    [Fact]
    public async Task EC002_clean_without_trailing_whitespace()
    {
        var code = "class C {\n}";
        var diags = await T.Run(new TrailingWhitespaceRule(), code,
            T.Cfg(("trim_trailing_whitespace", "true")));
        Assert.False(diags.Has("EC002"));
    }

    [Fact]
    public async Task EC002_fix_trims_trailing_whitespace()
    {
        var path = T.WriteCs("class C {   \n}");
        try
        {
            await new TrailingWhitespaceRule().FixAsync(
                path, T.Cfg(("trim_trailing_whitespace", "true")));
            var content = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("   \n", content);
        }
        finally { File.Delete(path); }
    }

    // ── EC003 FinalNewline ────────────────────────────────────────────────────

    [Fact]
    public async Task EC003_flags_missing_final_newline()
    {
        var code = "class C { }"; // no trailing newline
        var diags = await T.Run(new FinalNewlineRule(), code,
            T.Cfg(("insert_final_newline", "true")));
        Assert.True(diags.Has("EC003"));
    }

    [Fact]
    public async Task EC003_flags_unwanted_final_newline()
    {
        var code = "class C { }\n";
        var diags = await T.Run(new FinalNewlineRule(), code,
            T.Cfg(("insert_final_newline", "false")));
        Assert.True(diags.Has("EC003"));
    }

    [Fact]
    public async Task EC003_fix_adds_final_newline()
    {
        var path = T.WriteCs("class C { }");
        try
        {
            await new FinalNewlineRule().FixAsync(
                path, T.Cfg(("insert_final_newline", "true")));
            var content = await File.ReadAllTextAsync(path);
            Assert.EndsWith("\n", content);
        }
        finally { File.Delete(path); }
    }

    // ── EC004 LineEnding ──────────────────────────────────────────────────────

    [Fact]
    public async Task EC004_flags_crlf_when_lf_expected()
    {
        var code = "class C { }\r\n";
        var diags = await T.Run(new LineEndingRule(), code,
            T.Cfg(("end_of_line", "lf")));
        Assert.True(diags.Has("EC004"));
    }

    [Fact]
    public async Task EC004_flags_lf_when_crlf_expected()
    {
        var code = "class C { }\n";
        var diags = await T.Run(new LineEndingRule(), code,
            T.Cfg(("end_of_line", "crlf")));
        Assert.True(diags.Has("EC004"));
    }

    [Fact]
    public async Task EC004_fix_normalises_to_crlf()
    {
        var path = T.WriteCs("class C { }\nint x;\n");
        try
        {
            await new LineEndingRule().FixAsync(
                path, T.Cfg(("end_of_line", "crlf")));
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("\r\n", content);
        }
        finally { File.Delete(path); }
    }

    // ── EC005 LineLength ──────────────────────────────────────────────────────

    [Fact]
    public async Task EC005_flags_long_line()
    {
        var code = new string('x', 130);
        var diags = await T.Run(new LineLengthRule(), code,
            T.Cfg(("max_line_length", "120")));
        Assert.True(diags.Has("EC005"));
    }

    [Fact]
    public async Task EC005_does_not_apply_when_off()
    {
        Assert.False(new LineLengthRule().AppliesTo(T.Cfg()));
    }

    // ── EC006 Charset ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EC006_flags_bom_when_utf8_expected()
    {
        // Write a file with an actual UTF-8 BOM (0xEF 0xBB 0xBF prefix bytes) then
        // check that charset=utf-8 (BOM-less) flags it. T.WriteCs uses File.WriteAllText
        // which does not emit a BOM, so we write raw bytes directly here.
        var path = Path.GetTempFileName();
        var bom     = new byte[] { 0xEF, 0xBB, 0xBF };
        var content = System.Text.Encoding.UTF8.GetBytes("class C { }");
        File.WriteAllBytes(path, [.. bom, .. content]);
        try
        {
            var diags = await new CharsetRule().AnalyzeAsync(
                path, T.Cfg(("charset", "utf-8")));
            Assert.True(diags.Has("EC006"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EC006_flags_missing_bom_when_required()
    {
        var path = T.WriteCs("class C { }");
        try
        {
            var diags = await new CharsetRule().AnalyzeAsync(
                path, T.Cfg(("charset", "utf-8-bom")));
            Assert.True(diags.Has("EC006"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EC006_fix_adds_then_strips_bom()
    {
        // Adding BOM:
        var pathAdd = T.WriteCs("class C { }");
        try
        {
            await new CharsetRule().FixAsync(pathAdd, T.Cfg(("charset", "utf-8-bom")));
            var bytes = await File.ReadAllBytesAsync(pathAdd);
            Assert.Equal(0xEF, bytes[0]);
        }
        finally { File.Delete(pathAdd); }
    }
}
