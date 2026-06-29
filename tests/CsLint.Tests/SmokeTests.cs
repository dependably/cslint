using CsLint.Rules;
using CsLint.Rules.Sast;
using Xunit;

namespace CsLint.Tests;

/// <summary>
/// Fast sanity checks that exercise the most common happy paths without standing
/// up a full MSBuild workspace.
/// </summary>
public class SmokeTests
{
    [Fact]
    public void StyleHelper_parses_value_and_severity()
    {
        var (value, severity, suppress) = StyleHelper.Parse("file_scoped:warning");
        Assert.Equal("file_scoped", value);
        Assert.Equal(Severity.Warning, severity);
        Assert.False(suppress);
    }

    [Fact]
    public async Task EmptyCatchRule_flags_empty_catch()
    {
        var code = "class C { void M() { try { } catch { } } }";
        var diags = await T.Run(new EmptyCatchRule(), code);
        Assert.NotEmpty(diags);
        Assert.All(diags, d => Assert.Equal("SAST001", d.Rule));
    }
}
