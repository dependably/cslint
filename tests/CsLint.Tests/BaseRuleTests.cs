using CsLint;
using CsLint.Rules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CsLint.Tests;

public class BaseRuleTests
{
    // A minimal SyntaxRule implementation for testing the base class pipeline.
    sealed class TestSyntaxRule : SyntaxRule
    {
        public override string Id => "TEST001";

        public override bool AppliesTo(FileConfig config) =>
            config.Properties.ContainsKey("test_key");

        protected override IReadOnlyList<Diagnostic> Analyze(
            string filePath, SyntaxNode root, FileConfig config) =>
            [new(filePath, 1, 1, Id, "test finding", Severity.Warning)];
    }

    // A minimal TextRule that rewrites the text.
    sealed class TestTextRule : TextRule
    {
        public override string Id => "TEST002";
        public override bool AppliesTo(FileConfig _) => true;

        protected override IReadOnlyList<Diagnostic> Analyze(
            string filePath, string text, FileConfig config) =>
            [Warn(filePath, 1, 1, Id, "warn"),
             Error(filePath, 2, 1, Id, "error")];

        protected override string? ApplyFix(string text, FileConfig config) =>
            text.Contains("FIXME") ? text.Replace("FIXME", "FIXED") : null;
    }

    [Fact]
    public async Task SyntaxRule_parses_and_analyzes()
    {
        var rule = new TestSyntaxRule();
        var diags = await T.Run(rule, "class C { }", T.Cfg(("test_key", "true")));
        Assert.Contains(diags, d => d.Rule == "TEST001");
    }

    [Fact]
    public void TextRule_error_and_warn_helpers()
    {
        var rule = new TestTextRule();
        // Both Warn() and Error() helpers produce diagnostics with the right severity.
        var path = T.WriteCs("class C { }");
        try
        {
            var diags = rule.AnalyzeAsync(path, T.Cfg()).GetAwaiter().GetResult();
            Assert.Contains(diags, d => d.Severity == Severity.Warning);
            Assert.Contains(diags, d => d.Severity == Severity.Error);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task TextRule_fix_rewrites_when_changed()
    {
        var path = T.WriteCs("FIXME here");
        try
        {
            var rule = new TestTextRule();
            var wasFixed = await rule.FixAsync(path, T.Cfg());
            Assert.True(wasFixed);
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("FIXED", content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task TextRule_fix_returns_false_when_unchanged()
    {
        var path = T.WriteCs("no issues here");
        try
        {
            var rule = new TestTextRule();
            var wasFixed = await rule.FixAsync(path, T.Cfg());
            Assert.False(wasFixed);
        }
        finally { File.Delete(path); }
    }
}
