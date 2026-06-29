using CsLint;
using CsLint.Rules;
using Xunit;

namespace CsLint.Tests;

public class SmokeTests
{
    [Fact]
    public void StyleHelper_parses_value_and_severity()
    {
        var (value, severity, suppress) = StyleHelper.Parse("true:error");
        Assert.Equal("true", value);
        Assert.Equal(Severity.Error, severity);
        Assert.False(suppress);
    }

    [Fact]
    public async Task EmptyCatchRule_flags_empty_catch()
    {
        var code = "class C { void M() { try { } catch { } } }";
        var diags = await T.Run(new CsLint.Rules.Sast.EmptyCatchRule(), code);
        Assert.True(diags.Has("SAST001"));
    }
}
