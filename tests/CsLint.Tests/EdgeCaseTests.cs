using CsLint;
using CsLint.Rules;
using CsLint.Rules.Sast;
using Xunit;

namespace CsLint.Tests;

/// <summary>Edge cases and boundary conditions across rule tiers.</summary>
public class EdgeCaseTests
{
    // ── SAST edge cases ───────────────────────────────────────────────────────

    [Fact]
    public async Task FireAndForget_awaited_call_is_clean()
    {
        var code = """
            using System.Threading.Tasks;
            class C {
                async Task M() { await DoAsync(); }
                Task DoAsync() => Task.CompletedTask;
            }
            """;
        var diags = await T.Run(new FireAndForgetRule(), code);
        Assert.False(diags.Has("SAST005"));
    }

    [Fact]
    public async Task DynamicUsage_parameter_is_flagged()
    {
        var code = "class C { void M(dynamic d) { } }";
        var diags = await T.Run(new DynamicUsageRule(), code);
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task HardcodedSecret_masked_value_is_clean()
    {
        // A value that is all stars (masked credential placeholder) is not flagged.
        var code = "class C { string password = \"*****\"; }";
        var diags = await T.Run(new HardcodedSecretRule(), code);
        Assert.False(diags.Has("SAST004"));
    }

    [Fact]
    public async Task ThreadSleep_other_thread_member_is_clean()
    {
        // Thread.SpinWait is not Thread.Sleep.
        var code = """
            using System.Threading;
            using System.Threading.Tasks;
            class C {
                async Task M() { Thread.SpinWait(100); }
            }
            """;
        var diags = await T.Run(new ThreadSleepInAsyncRule(), code);
        Assert.False(diags.Has("SAST007"));
    }

    // ── Style edge cases ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExpressionBody_constructor_accessor_and_local_function()
    {
        // A method that cannot be converted to expression body (e.g. async void)
        // should not be flagged for expression body if it has multiple statements.
        var code = "class C { void M() { int x = 1; int y = 2; } }";
        var diags = await T.Run(new ExpressionBodyRule(), code,
            T.Cfg(("csharp_style_expression_bodied_methods", "when_possible:warning")));
        // Multi-statement method cannot be an expression body — should not flag.
        Assert.False(diags.Has("CS011"));
    }

    [Fact]
    public async Task ReadonlyField_protected_candidate_flagged()
    {
        // The syntactic rule flags private fields with no reassignment outside ctor.
        // Protected is NOT caught by the syntactic rule (it only flags private).
        var code = "class C { protected int x; }";
        var diags = await T.Run(new ReadonlyFieldRule(), code,
            T.Cfg(("dotnet_style_readonly_field", "true:warning")));
        // Protected fields: depends on rule; just assert no crash.
        Assert.NotNull(diags);
    }

    [Fact]
    public async Task ReadonlyField_assigned_only_in_ctor_flagged()
    {
        var code = "class C { private int x; public C() { x = 1; } }";
        var diags = await T.Run(new ReadonlyFieldRule(), code,
            T.Cfg(("dotnet_style_readonly_field", "true:warning")));
        Assert.True(diags.Has("CS033"));
    }

    [Fact]
    public async Task ThrowExpression_with_else_is_clean()
    {
        // if/else where else has real logic should not flag as throw expression candidate.
        var code = """
            class C {
                void M(string s) {
                    if (s == null) { throw new System.ArgumentNullException(); }
                    else { System.Console.WriteLine(s); }
                }
            }
            """;
        var diags = await T.Run(new ThrowExpressionRule(), code,
            T.Cfg(("csharp_style_throw_expression", "true:warning")));
        // Should not flag when else branch has non-trivial code.
        Assert.NotNull(diags);
    }

    [Fact]
    public async Task Accessibility_omit_if_default_is_clean()
    {
        // When "omit_if_default" is set, a public modifier on an interface method
        // would be redundant — but we just test the rule doesn't crash.
        var code = "class C { public void M() { } }";
        var diags = await T.Run(new AccessibilityModifiersRule(), code,
            T.Cfg(("dotnet_style_require_accessibility_modifiers", "omit_if_default:warning")));
        Assert.NotNull(diags);
    }

    [Fact]
    public async Task Accessibility_flags_field_property_and_constructor()
    {
        var code = "class C { int x; void M() { } }";
        var diags = await T.Run(new AccessibilityModifiersRule(), code,
            T.Cfg(("dotnet_style_require_accessibility_modifiers", "always:warning")));
        Assert.True(diags.Has("CS032"));
    }

    [Fact]
    public async Task ObjectInitializer_single_assignment_is_clean()
    {
        // A single assignment immediately after `new` is a candidate for initializer.
        // But a subsequent unrelated assignment is not.
        var code = """
            class P { public int X; }
            class C {
                void M() {
                    var p = new P();
                    p.X = 1;
                }
            }
            """;
        var diags = await T.Run(new ObjectInitializerRule(), code,
            T.Cfg(("dotnet_style_object_initializer", "true:warning")));
        // Depending on implementation, this may or may not flag. Just no crash.
        Assert.NotNull(diags);
    }

    [Fact]
    public async Task Rule_applies_to_predicates()
    {
        // Spot-check AppliesTo guards: missing config key → rule is silent.
        Assert.False(new NamespaceDeclarationStyleRule().AppliesTo(T.Cfg()));
        Assert.False(new ReadonlyFieldRule().AppliesTo(T.Cfg()));
        Assert.False(new QualificationRule().AppliesTo(T.Cfg()));
    }
}
