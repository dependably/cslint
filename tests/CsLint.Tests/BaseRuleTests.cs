using CsLint;
using CsLint.Rules;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CsLint.Tests;

public class BaseRuleTests
{
    sealed class ProbeSyntaxRule : SyntaxRule
    {
        public override string Id => "PROBE-SYNTAX";
        public override bool AppliesTo(FileConfig config) => true;
        protected override IReadOnlyList<Diagnostic> Analyze(
            string filePath, SyntaxNode root, FileConfig config) =>
            [new(filePath, 1, 1, Id, "probe", Severity.Warning)];
    }

    sealed class ProbeTextRule : TextRule
    {
        public override string Id => "PROBE-TEXT";
        public override bool AppliesTo(FileConfig config) => true;
        protected override IReadOnlyList<Diagnostic> Analyze(
            string filePath, string text, FileConfig config) =>
            text.Contains("bad")
                ? [Error(filePath, 1, 1, Id, "bad found"), Warn(filePath, 2, 1, Id, "warn")]
                : [];

        protected override string? ApplyFix(string text, FileConfig config) =>
            text.Replace("bad", "good");
    }

    [Fact]
    public async Task SyntaxRule_parses_and_analyzes()
    {
        var diags = await T.Run(new ProbeSyntaxRule(), "class C { }");
        Assert.True(diags.Has("PROBE-SYNTAX"));
        Assert.Equal(RuleCategory.EditorConfig, new ProbeSyntaxRule().Category);
    }

    [Fact]
    public async Task TextRule_error_and_warn_helpers()
    {
        var diags = await T.Run(new ProbeTextRule(), "this is bad code");
        Assert.Contains(diags, d => d.Severity == Severity.Error);
        Assert.Contains(diags, d => d.Severity == Severity.Warning);
    }

    [Fact]
    public async Task TextRule_fix_rewrites_when_changed()
    {
        var path = T.WriteCs("a bad line");
        try
        {
            Assert.True(await new ProbeTextRule().FixAsync(path, T.Cfg()));
            Assert.Contains("good", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task TextRule_fix_returns_false_when_unchanged()
    {
        var path = T.WriteCs("nothing here");
        try
        {
            Assert.False(await new ProbeTextRule().FixAsync(path, T.Cfg()));
        }
        finally { File.Delete(path); }
    }
}
