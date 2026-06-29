using CsLint.Rules.Opinionated;
using Xunit;

namespace CsLint.Tests;

public class OpinionatedRuleTests
{
    static ScanConfig AllOn  => new(true, true, true);
    static ScanConfig AllOff => new(false, false, false);

    // ── OP004 MagicNumber ─────────────────────────────────────────────────────

    [Fact]
    public async Task OP004_flags_magic_number()
    {
        var code = "class C { int x = 42; }";
        var diags = await T.Run(new MagicNumberRule(AllOn), code);
        Assert.True(diags.Has("OP004"));
    }

    [Fact]
    public async Task OP004_allows_common_values()
    {
        // 0, 1, -1, 2, 100, 1000 are in the allowed set.
        var code = "class C { void M() { int x = 0; int y = 1; int z = 2; } }";
        var diags = await T.Run(new MagicNumberRule(AllOn), code);
        Assert.False(diags.Has("OP004"));
    }

    [Fact]
    public async Task OP004_ignores_const()
    {
        var code = "class C { const int Max = 42; }";
        var diags = await T.Run(new MagicNumberRule(AllOn), code);
        Assert.False(diags.Has("OP004"));
    }

    [Fact]
    public async Task OP004_ignores_enum_member()
    {
        var code = "enum E { A = 42 }";
        var diags = await T.Run(new MagicNumberRule(AllOn), code);
        Assert.False(diags.Has("OP004"));
    }

    [Fact]
    public async Task OP004_disabled_when_flag_off()
    {
        Assert.False(new MagicNumberRule(AllOff).AppliesTo(T.Cfg()));
    }

    // ── OP005 BooleanParameter ────────────────────────────────────────────────

    [Fact]
    public async Task OP005_flags_public_bool_parameter()
    {
        var code = "class C { public void M(bool flag) { } }";
        var diags = await T.Run(new BooleanParameterRule(AllOn), code);
        Assert.True(diags.Has("OP005"));
    }

    [Fact]
    public async Task OP005_ignores_private_method()
    {
        var code = "class C { private void M(bool flag) { } }";
        var diags = await T.Run(new BooleanParameterRule(AllOn), code);
        Assert.False(diags.Has("OP005"));
    }

    [Fact]
    public async Task OP005_ignores_out_bool()
    {
        var code = "class C { public bool TryGet(out bool result) { result = true; return true; } }";
        var diags = await T.Run(new BooleanParameterRule(AllOn), code);
        Assert.False(diags.Has("OP005"));
    }

    // ── OP006 MissingCancellationToken ────────────────────────────────────────

    [Fact]
    public async Task OP006_flags_async_without_cancellation_token()
    {
        var code = """
            using System.Threading.Tasks;
            class C {
                public async Task DoWorkAsync() { await Task.CompletedTask; }
            }
            """;
        var diags = await T.Run(new MissingCancellationTokenRule(AllOn), code);
        Assert.True(diags.Has("OP006"));
    }

    [Fact]
    public async Task OP006_clean_with_cancellation_token()
    {
        var code = """
            using System.Threading;
            using System.Threading.Tasks;
            class C {
                public async Task DoWorkAsync(CancellationToken ct) { await Task.CompletedTask; }
            }
            """;
        var diags = await T.Run(new MissingCancellationTokenRule(AllOn), code);
        Assert.False(diags.Has("OP006"));
    }

    [Fact]
    public async Task OP006_ignores_non_async()
    {
        var code = "class C { public void M() { } }";
        var diags = await T.Run(new MissingCancellationTokenRule(AllOn), code);
        Assert.False(diags.Has("OP006"));
    }

    [Fact]
    public async Task Opinionated_category_is_set()
    {
        Assert.Equal(CsLint.Rules.RuleCategory.Opinionated, new MagicNumberRule(AllOn).Category);
        Assert.Equal(CsLint.Rules.RuleCategory.Opinionated, new BooleanParameterRule(AllOn).Category);
        Assert.Equal(CsLint.Rules.RuleCategory.Opinionated, new MissingCancellationTokenRule(AllOn).Category);
    }
}
