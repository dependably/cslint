using CsLint.Rules;
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

    // Regression: textual allow-list missed float spellings such as 1.0 (numeric-equiv of 1).
    [Fact]
    public async Task OP004_allows_float_equivalent_of_allow_listed_integer()
    {
        var diags = await T.Run(new MagicNumberRule(On), "class C { bool M(double x) { return x >= 1.0; } }");
        Assert.False(diags.Has("OP004"));
    }

    // Positive control: a non-allow-listed float must still be flagged.
    [Fact]
    public async Task OP004_still_flags_non_allow_listed_float()
    {
        var diags = await T.Run(new MagicNumberRule(On), "class C { bool M(double x) { return x >= 3.7; } }");
        Assert.True(diags.Has("OP004"));
    }

    // Regression: a literal in a named-argument position is already self-documenting.
    [Fact]
    public async Task OP004_ignores_named_argument()
    {
        var diags = await T.Run(new MagicNumberRule(On),
            "class C { void M() { DoWork(workFactor: 12); } void DoWork(int workFactor) { } }");
        Assert.False(diags.Has("OP004"));
    }

    // Positive control: same literal in a positional argument must still be flagged.
    [Fact]
    public async Task OP004_still_flags_positional_argument()
    {
        var diags = await T.Run(new MagicNumberRule(On),
            "class C { void M() { DoWork(12); } void DoWork(int workFactor) { } }");
        Assert.True(diags.Has("OP004"));
    }

    // Regression: a positional literal inside a call that is itself passed as a
    // named argument must still be flagged — only the literal that IS the named-arg value is exempt.
    // `Outer(policy: Inner(3, 500))` — the `500` is positional to Inner, not the named arg itself.
    [Fact]
    public async Task OP004_still_flags_positional_literal_nested_inside_named_argument_call()
    {
        var diags = await T.Run(new MagicNumberRule(On),
            "class C { void M() { Outer(policy: Inner(3, 500)); } " +
            "void Outer(int policy) { } int Inner(int a, int b) => a + b; }");
        Assert.True(diags.Has("OP004"));
    }

    // Regression: a positional literal in a lambda body passed as a named argument
    // must still be flagged — the lambda body is not itself the named-arg value.
    [Fact]
    public async Task OP004_still_flags_positional_literal_in_lambda_passed_as_named_argument()
    {
        var diags = await T.Run(new MagicNumberRule(On),
            "class C { void M() { Configure(callback: () => DoWork(42)); } " +
            "void Configure(System.Action callback) { } void DoWork(int x) { } }");
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

    // Regression: a method with no access modifier in a class is implicitly private and must
    // not be flagged — only API surface (public/internal/protected) warrants the smell.
    [Fact]
    public async Task OP005_ignores_implicitly_private_method_in_class()
    {
        var diags = await T.Run(new BooleanParameterRule(On),
            "class C { void M(bool flag) { } }");
        Assert.False(diags.Has("OP005"));
    }

    // Regression: an interface method with no modifier is implicitly public API surface and
    // must still be flagged.
    [Fact]
    public async Task OP005_flags_interface_method_without_modifier()
    {
        var diags = await T.Run(new BooleanParameterRule(On),
            "interface I { void M(bool flag); }");
        Assert.True(diags.Has("OP005"));
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

    // Regression: an implicit interface implementation's bool parameter is dictated by the
    // interface contract, not freely chosen — the implementing class's method must not add a
    // second diagnostic beyond the one already raised for the interface declaration itself.
    [Fact]
    public async Task OP005_ignores_implicit_implementation_of_locally_declared_interface()
    {
        // The interface designer chose the bool parameter (the interface method is correctly
        // flagged as a design-time smell). The implementing class cannot change that signature,
        // so its method must not produce a second OP005 finding.
        const string code =
            "interface IEmailStore { void SetEmailConfirmed(bool confirmed); } " +
            "class UserStore : IEmailStore { public void SetEmailConfirmed(bool confirmed) { } }";
        var diags = await T.Run(new BooleanParameterRule(On), code);
        // Exactly one OP005: the interface definition. The implementing method is excluded.
        Assert.Single(diags.Where(d => d.Rule == "OP005").ToList());
    }

    // Anti-over-broadening guard: a public bool-flag method in a class that lists an
    // external interface in its base list (not declared in the same file) must still be flagged
    // because we cannot verify the match without semantic analysis.
    [Fact]
    public async Task OP005_still_flags_when_implemented_interface_is_not_declared_in_same_file()
    {
        // IDisposable is not declared in this snippet, so the rule cannot confirm the method is
        // an interface implementation — it must remain a flagged smell.
        const string code =
            "class C : System.IDisposable { public void Process(bool useCache) { } public void Dispose() { } }";
        var diags = await T.Run(new BooleanParameterRule(On), code);
        Assert.True(diags.Has("OP005"));
    }

    // Regression: ASP.NET Identity store Set*Async methods with a bool
    // parameter on a class implementing an I*Store interface are dictated by the framework contract
    // and must not fire OP005. The interface is from an external assembly; the heuristic detects
    // the pattern via method name (Set*Async) + bool param + I*Store in the base list.
    // Mutation pin: this test FAILS on code that lacks IsKnownIdentityStoreContractMethod.
    [Fact]
    public async Task OP005_ignores_set_async_bool_on_istore_implementing_class()
    {
        const string code =
            "using System.Threading; using System.Threading.Tasks; " +
            "class UserStore : IUserEmailStore<User>, IUserTwoFactorStore<User> { " +
            "public Task SetEmailConfirmedAsync(User user, bool confirmed, CancellationToken ct) => Task.CompletedTask; " +
            "public Task SetTwoFactorEnabledAsync(User user, bool enabled, CancellationToken ct) => Task.CompletedTask; }";
        var diags = await T.Run(new BooleanParameterRule(On), code);
        Assert.False(diags.Has("OP005"));
    }

    // Anti-over-broadening guard for Finding 1: a Set*Async bool on a class that does NOT implement
    // any I*Store interface is still a smell and must fire.
    [Fact]
    public async Task OP005_still_flags_set_async_bool_when_no_istore_in_base_list()
    {
        const string code =
            "using System.Threading; using System.Threading.Tasks; " +
            "class ReportService { " +
            "public Task SetArchivedAsync(object report, bool archived, CancellationToken ct) => Task.CompletedTask; }";
        var diags = await T.Run(new BooleanParameterRule(On), code);
        Assert.True(diags.Has("OP005"));
    }

    // Boundary / mutation-pin for the Identity-store gate: a Set*Async(bool) whose name is NOT in
    // the closed allowlist must still fire, even when the class lists an I*Store in its base list.
    // A class implementing IEventStore (or any other user-defined I*Store) owns its own API surface;
    // the bool parameter is not dictated by an ASP.NET Identity contract.
    // This test FAILS on the over-broad heuristic (StartsWith("Set") && EndsWith("Async"))
    // and PASSES after narrowing to the exact-name allowlist.
    [Fact]
    public async Task OP005_still_flags_non_identity_set_async_bool_on_unrelated_istore()
    {
        const string code =
            "using System.Threading; using System.Threading.Tasks; " +
            "class WidgetStore : IEventStore { " +
            "public Task SetVerboseAsync(bool verbose, CancellationToken ct) => Task.CompletedTask; }";
        var diags = await T.Run(new BooleanParameterRule(On), code);
        Assert.True(diags.Has("OP005"));
    }

    // Regression: name+count-only matching of in-file interface methods
    // suppressed unrelated bool-flag overloads whose parameter types differ from those of the matched
    // interface method. An overload whose parameter types do NOT match the interface must still flag.
    // Mutation pin: this test FAILS on code that matches by name+count only (no type check).
    [Fact]
    public async Task OP005_still_flags_bool_overload_when_interface_has_int_overload_at_same_arity()
    {
        // IX declares Foo(int a). C.Foo(int a) is the implicit implementation (correctly suppressed).
        // C.Foo(bool b) has the same name and arity but a different type — it is an owned method and
        // must still produce an OP005 finding.
        const string code =
            "interface IX { void Foo(int a); } " +
            "class C : IX { public void Foo(int a) { } public void Foo(bool b) { } }";
        var diags = await T.Run(new BooleanParameterRule(On), code);
        Assert.True(diags.Has("OP005"));
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

    // --- Test-code suppression for OP004/OP006 (moonlitlabs/cslint#26) ---------------------------

    // Runs a rule against source written to a file with the given name, so path-based test-file
    // detection (TestFileHeuristic) is exercised. Optionally seeds an .editorconfig override.
    static async Task<IReadOnlyList<Diagnostic>> RunNamed(
        IRule rule, string fileName, string code, FileConfig? config = null)
    {
        var dir = T.TempDir();
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, code);
        try { return await rule.AnalyzeAsync(T.Unit(path, config)); }
        finally { Directory.Delete(dir, true); }
    }

    // Regression pin (#26): a framework-invoked [Fact] method is called with no arguments and so
    // cannot declare a CancellationToken — OP006 must not flag it. FAILS on the old rule.
    [Theory]
    [InlineData("Fact")]
    [InlineData("Theory")]
    [InlineData("Test")]
    [InlineData("TestMethod")]
    public async Task OP006_ignores_test_attributed_method(string attribute)
    {
        var code = "using System.Threading.Tasks; class C { " +
                   $"[{attribute}] public async Task M() {{ await Task.Yield(); }} }}";
        // A non-test file name isolates the attribute suppression from the path heuristic.
        var diags = await RunNamed(new MissingCancellationTokenRule(On), "Widget.cs", code);
        Assert.False(diags.Has("OP006"));
    }

    // Control: a plain public async method in a non-test file is still flagged.
    [Fact]
    public async Task OP006_still_flags_untagged_async_method_in_production_file()
    {
        var code = "using System.Threading.Tasks; class C { public async Task M() { await Task.Yield(); } }";
        var diags = await RunNamed(new MissingCancellationTokenRule(On), "Widget.cs", code);
        Assert.True(diags.Has("OP006"));
    }

    // Regression pin (#26): OP006 defaults off in a test file (path heuristic) even without a
    // test attribute — reusing SAST002's TestFileHeuristic. FAILS on the old rule.
    [Fact]
    public async Task OP006_suppressed_in_test_file_by_path()
    {
        var code = "using System.Threading.Tasks; class C { public async Task M() { await Task.Yield(); } }";
        var diags = await RunNamed(new MissingCancellationTokenRule(On), "WidgetTests.cs", code);
        Assert.False(diags.Has("OP006"));
    }

    // Regression pin (#26): OP004 defaults off in test files (literal expected values are idiomatic).
    [Fact]
    public async Task OP004_suppressed_in_test_file_by_path()
    {
        var diags = await RunNamed(new MagicNumberRule(On), "WidgetTests.cs",
            "class C { int M() { return 42; } }");
        Assert.False(diags.Has("OP004"));
    }

    // The test-file default is overridable: an explicit .editorconfig severity re-enables the rule.
    [Theory]
    [InlineData("OP004", "class C { int M() { return 42; } }")]
    [InlineData("OP006", "using System.Threading.Tasks; class C { public async Task M() { await Task.Yield(); } }")]
    public async Task Opinionated_test_file_suppression_overridable_via_editorconfig(string ruleId, string code)
    {
        IRule rule = ruleId == "OP004" ? new MagicNumberRule(On) : new MissingCancellationTokenRule(On);
        var cfg = T.Cfg(($"dotnet_diagnostic.{ruleId}.severity", "warning"));
        var diags = await RunNamed(rule, "WidgetTests.cs", code, cfg);
        Assert.True(diags.Has(ruleId));
    }

    // The removed --no-magic-numbers / --no-bool-flags / --no-cancellation flags lose nothing:
    // OP004/005/006 stay disable-able per file via .editorconfig `severity = none`, applied by the
    // engine's ApplySeverityOverride for every rule. (The .dependably-check `scan` toggles are the
    // other path; the ScanConfig(false,...) unit tests above already cover that.)
    [Theory]
    [InlineData("OP004", "class C { int M() { return 42; } }")]
    [InlineData("OP005", "class C { public void M(bool flag) { } }")]
    [InlineData("OP006", "using System.Threading.Tasks; class C { public async Task M() { await Task.Yield(); } }")]
    public async Task Opinionated_rule_disabled_via_editorconfig_severity_none(string ruleId, string code)
    {
        var dir = T.TempDir();
        var file = Path.Combine(dir, "A.cs");
        File.WriteAllText(file, code);
        try
        {
            // Control: with no override the rule fires in --scan (LintMode.All). A fresh engine per
            // phase keeps EditorConfigLoader's per-path cache from serving a stale .editorconfig.
            File.WriteAllText(Path.Combine(dir, ".editorconfig"), "root = true\n[*.cs]\n");
            var withRule = await new LintEngine().LintFileAsync(file, LintMode.All);
            Assert.True(withRule.Has(ruleId));

            // `severity = none` drops the finding entirely.
            File.WriteAllText(Path.Combine(dir, ".editorconfig"),
                $"root = true\n[*.cs]\ndotnet_diagnostic.{ruleId}.severity = none\n");
            var disabled = await new LintEngine().LintFileAsync(file, LintMode.All);
            Assert.False(disabled.Has(ruleId));
        }
        finally { Directory.Delete(dir, true); }
    }
}
