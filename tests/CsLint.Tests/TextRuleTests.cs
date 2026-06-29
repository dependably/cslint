using CsLint;
using CsLint.Rules;
using Xunit;

namespace CsLint.Tests;

public class TextRuleTests
{
    [Fact]
    public async Task EC001_flags_tab_when_spaces_expected()
    {
        var diags = await T.Run(new IndentStyleRule(), "\tint x = 1;\n",
            T.Cfg(("indent_style", "space"), ("indent_size", "4")));
        Assert.True(diags.Has("EC001"));
    }

    [Fact]
    public async Task EC001_flags_spaces_when_tabs_expected()
    {
        var diags = await T.Run(new IndentStyleRule(), "    int x = 1;\n",
            T.Cfg(("indent_style", "tab"), ("indent_size", "4")));
        Assert.True(diags.Has("EC001"));
    }

    [Fact]
    public async Task EC001_clean_when_spaces_match()
    {
        var diags = await T.Run(new IndentStyleRule(), "    int x = 1;\n",
            T.Cfg(("indent_style", "space"), ("indent_size", "4")));
        Assert.False(diags.Has("EC001"));
    }

    [Fact]
    public void EC001_does_not_apply_without_indent_style()
    {
        Assert.False(new IndentStyleRule().AppliesTo(T.Cfg(("x", "y"))));
    }

    [Fact]
    public async Task EC001_fix_converts_tabs_to_spaces()
    {
        var path = T.WriteCs("\tint x = 1;\n");
        try
        {
            var changed = await new IndentStyleRule().FixAsync(path,
                T.Cfg(("indent_style", "space"), ("indent_size", "2")));
            Assert.True(changed);
            Assert.StartsWith("  int", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EC001_fix_converts_spaces_to_tabs()
    {
        var path = T.WriteCs("    int x = 1;\n");
        try
        {
            var changed = await new IndentStyleRule().FixAsync(path,
                T.Cfg(("indent_style", "tab"), ("tab_width", "4")));
            Assert.True(changed);
            Assert.StartsWith("\t", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EC002_flags_trailing_whitespace()
    {
        var diags = await T.Run(new TrailingWhitespaceRule(), "int x = 1;   \n",
            T.Cfg(("trim_trailing_whitespace", "true")));
        Assert.True(diags.Has("EC002"));
    }

    [Fact]
    public async Task EC002_clean_without_trailing_whitespace()
    {
        var diags = await T.Run(new TrailingWhitespaceRule(), "int x = 1;\n",
            T.Cfg(("trim_trailing_whitespace", "true")));
        Assert.False(diags.Has("EC002"));
    }

    [Fact]
    public async Task EC002_fix_trims_trailing_whitespace()
    {
        var path = T.WriteCs("a   \nb\t\n");
        try
        {
            Assert.True(await new TrailingWhitespaceRule().FixAsync(path,
                T.Cfg(("trim_trailing_whitespace", "true"))));
            Assert.Equal("a\nb\n", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EC003_flags_missing_final_newline()
    {
        var diags = await T.Run(new FinalNewlineRule(), "int x = 1;",
            T.Cfg(("insert_final_newline", "true")));
        Assert.True(diags.Has("EC003"));
    }

    [Fact]
    public async Task EC003_flags_unwanted_final_newline()
    {
        var diags = await T.Run(new FinalNewlineRule(), "int x = 1;\n",
            T.Cfg(("insert_final_newline", "false")));
        Assert.True(diags.Has("EC003"));
    }

    [Fact]
    public async Task EC003_fix_adds_final_newline()
    {
        var path = T.WriteCs("abc");
        try
        {
            Assert.True(await new FinalNewlineRule().FixAsync(path,
                T.Cfg(("insert_final_newline", "true"))));
            Assert.EndsWith("\n", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EC004_flags_crlf_when_lf_expected()
    {
        var diags = await T.Run(new LineEndingRule(), "a\r\nb\n",
            T.Cfg(("end_of_line", "lf")));
        Assert.True(diags.Has("EC004"));
    }

    [Fact]
    public async Task EC004_flags_lf_when_crlf_expected()
    {
        var diags = await T.Run(new LineEndingRule(), "a\nb\n",
            T.Cfg(("end_of_line", "crlf")));
        Assert.True(diags.Has("EC004"));
    }

    [Fact]
    public async Task EC004_fix_normalises_to_crlf()
    {
        var path = T.WriteCs("a\nb\n");
        try
        {
            Assert.True(await new LineEndingRule().FixAsync(path, T.Cfg(("end_of_line", "crlf"))));
            Assert.Contains("\r\n", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EC005_flags_long_line()
    {
        var diags = await T.Run(new LineLengthRule(), new string('x', 50) + "\n",
            T.Cfg(("max_line_length", "10")));
        Assert.True(diags.Has("EC005"));
    }

    [Fact]
    public void EC005_does_not_apply_when_off()
    {
        Assert.False(new LineLengthRule().AppliesTo(T.Cfg(("max_line_length", "off"))));
    }

    [Fact]
    public async Task EC006_flags_bom_when_utf8_expected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cslint_{Guid.NewGuid():N}.cs");
        File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a' });
        try
        {
            var diags = await new CharsetRule().AnalyzeAsync(path, T.Cfg(("charset", "utf-8")));
            Assert.True(diags.Has("EC006"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EC006_flags_missing_bom_when_required()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cslint_{Guid.NewGuid():N}.cs");
        File.WriteAllBytes(path, new byte[] { (byte)'a' });
        try
        {
            var diags = await new CharsetRule().AnalyzeAsync(path, T.Cfg(("charset", "utf-8-bom")));
            Assert.True(diags.Has("EC006"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task EC006_fix_adds_then_strips_bom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cslint_{Guid.NewGuid():N}.cs");
        File.WriteAllBytes(path, new byte[] { (byte)'a' });
        try
        {
            Assert.True(await new CharsetRule().FixAsync(path, T.Cfg(("charset", "utf-8-bom"))));
            Assert.Equal(0xEF, File.ReadAllBytes(path)[0]);
            Assert.True(await new CharsetRule().FixAsync(path, T.Cfg(("charset", "utf-8"))));
            Assert.NotEqual(0xEF, File.ReadAllBytes(path)[0]);
        }
        finally { File.Delete(path); }
    }
}
