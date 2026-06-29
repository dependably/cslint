using CsLint.Rules.Opinionated;
using Xunit;

namespace CsLint.Tests;

public class OpinionatedRuleTests
{
    static readonly ScanConfig On = new(true, true, true);

    [Fact]
    public async Task OP004_flags_magic_number()
    {
        var diags = await T.Run(new MagicNumberRule(On), "class C { int M() { return 42; } }");
        Assert.True(diags.Has("OP004"));
    }

    [Fact]
    public async Task OP004_allows_common_values()
    {
        var diags = await T.Run(new MagicNumberRule(On), "class C { int M() { return 1; } }");
        Assert.False(diags.Has("OP004"));
    }

    [Fact]
    public async Task OP004_ignores_const()
    {
        var diags = await T.Run(new MagicNumberRule(On), "class C { const int X = 42; }");
        Assert.False(diags.Has("OP004"));
    }

    [Fact]
    public async Task OP004_ignores_enum_member()
    {
        var diags = await T.Run(new MagicNumberRule(On), "enum E { A = 42 }");
        Assert.False(diags.Has("OP004"));
    }

    [Fact]
    public void OP004_disabled_when_flag_off()
    {
        Assert.False(new MagicNumberRule(new ScanConfig(FlagMagicNumbers: false)).AppliesTo(T.Cfg()));
    }

    // Regression: a literal that is the default value of a named member is already named by it.
    [Fact]
    public async Task OP004_ignores_property_initializer_default()
    {
        var diags = await T.Run(new MagicNumberRule(On), "class C { public int TtlMinutes { get; set; } = 55; }");
        Assert.False(diags.Has("OP004"));
    }

    [Fact]
    public async Task OP004_ignores_field_and_parameter_defaults()
    {
        var diags = await T.Run(new MagicNumberRule(On),
            "class C { int _retries = 5; void M(int timeout = 30) { } }");
        Assert.False(diags.Has("OP004"));
    }

    [Fact]
    public async Task OP004_still_flags_in_method_body()
    {
        var diags = await T.Run(new MagicNumberRule(On), "class C { int M() { return 42 * 7; } }");
        Assert.True(diags.Has("OP004"));
    }

    [Fact]
    public async Task OP005_flags_public_bool_parameter()
    {
        var diags = await T.Run(new BooleanParameterRule(On),
            "class C { public void M(bool flag) { } }");
        Assert.True(diags.Has("OP005"));
    }

    [Fact]
    public async Task OP005_ignores_private_method()
    {
        var diags = await T.Run(new BooleanParameterRule(On),
            "class C { private void M(bool flag) { } }");
        Assert.False(diags.Has("OP005"));
    }

    [Fact]
    public async Task OP005_ignores_out_bool()
    {
        var diags = await T.Run(new BooleanParameterRule(On),
            "class C { public void M(out bool ok) { ok = true; } }");
        Assert.False(diags.Has("OP005"));
    }

    // Regression: the canonical dispose pattern's bool is inherited from the base, not a smell.
    [Fact]
    public async Task OP005_ignores_dispose_override()
    {
        var diags = await T.Run(new BooleanParameterRule(On),
            "class C : System.IO.Stream { protected override void Dispose(bool disposing) { } }");
        Assert.False(diags.Has("OP005"));
    }

    [Fact]
    public async Task OP005_ignores_explicit_interface_impl()
    {
        // Only the explicit implementation is present (the interface isn't declared here), so the
        // single method under test is the `void I.M(bool b)` impl — whose signature it can't change.
        var diags = await T.Run(new BooleanParameterRule(On),
            "class C { void I.M(bool b) { } }");
        Assert.False(diags.Has("OP005"));
    }

    [Fact]
    public async Task OP006_flags_async_without_cancellation_token()
    {
        var diags = await T.Run(new MissingCancellationTokenRule(On),
            "using System.Threading.Tasks; class C { public async Task M() { await Task.Yield(); } }");
        Assert.True(diags.Has("OP006"));
    }

    [Fact]
    public async Task OP006_clean_with_cancellation_token()
    {
        var diags = await T.Run(new MissingCancellationTokenRule(On),
            "using System.Threading; using System.Threading.Tasks; " +
            "class C { public async Task M(CancellationToken ct) { await Task.Yield(); } }");
        Assert.False(diags.Has("OP006"));
    }

    [Fact]
    public async Task OP006_ignores_non_async()
    {
        var diags = await T.Run(new MissingCancellationTokenRule(On),
            "class C { public void M() { } }");
        Assert.False(diags.Has("OP006"));
    }

    // Regression: a method handed an HttpContext (or filter context) already has cancellation via
    // RequestAborted; middleware InvokeAsync must keep its framework signature.
    [Fact]
    public async Task OP006_ignores_method_with_httpcontext()
    {
        var diags = await T.Run(new MissingCancellationTokenRule(On),
            "using System.Threading.Tasks; class C { public async Task InvokeAsync(HttpContext ctx) { await Task.Yield(); } }");
        Assert.False(diags.Has("OP006"));
    }

    [Fact]
    public async Task OP006_ignores_filter_context_signature()
    {
        var diags = await T.Run(new MissingCancellationTokenRule(On),
            "using System.Threading.Tasks; class C { public async Task OnAuthorizationAsync(AuthorizationFilterContext context) { await Task.Yield(); } }");
        Assert.False(diags.Has("OP006"));
    }

    // Regression: an override / IAsyncDisposable.DisposeAsync signature isn't the author's to change.
    [Fact]
    public async Task OP006_ignores_override_and_dispose_async()
    {
        var diags = await T.Run(new MissingCancellationTokenRule(On),
            "using System.Threading.Tasks; class C { public override async ValueTask DisposeAsync() { await Task.Yield(); } }");
        Assert.False(diags.Has("OP006"));
    }

    [Fact]
    public void Opinionated_category_is_set()
    {
        Assert.Equal(CsLint.Rules.RuleCategory.Opinionated, new MagicNumberRule(On).Category);
    }
}
