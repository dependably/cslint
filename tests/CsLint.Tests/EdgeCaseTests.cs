using CsLint.Rules;
using CsLint.Rules.Sast;
using Xunit;

namespace CsLint.Tests;

public class EdgeCaseTests
{
    [Fact]
    public async Task FireAndForget_awaited_call_is_clean()
    {
        var diags = await T.Run(new FireAndForgetRule(),
            "using System.Threading.Tasks; class C { async Task M() { await DoAsync(); } " +
            "Task DoAsync() => Task.CompletedTask; }");
        Assert.False(diags.Has("SAST005"));
    }

    [Fact]
    public async Task DynamicUsage_parameter_is_flagged()
    {
        var diags = await T.Run(new DynamicUsageRule(), "class C { void M(dynamic d) { } }");
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task HardcodedSecret_masked_value_is_clean()
    {
        var diags = await T.Run(new HardcodedSecretRule(),
            "class C { void M() { string password = \"****\"; } }");
        Assert.False(diags.Has("SAST004"));
    }

    [Fact]
    public async Task ThreadSleep_other_thread_member_is_clean()
    {
        var diags = await T.Run(new ThreadSleepInAsyncRule(),
            "using System.Threading; class C { async System.Threading.Tasks.Task M() " +
            "{ Thread.Yield(); } }");
        Assert.False(diags.Has("SAST007"));
    }

    [Fact]
    public async Task ExpressionBody_constructor_accessor_and_local_function()
    {
        var ctor = await T.Run(new ExpressionBodyRule(),
            "class C { int x; C() { x = 1; } }",
            T.Cfg(("csharp_style_expression_bodied_constructors", "true")));
        Assert.True(ctor.Has("CS011"));

        var accessor = await T.Run(new ExpressionBodyRule(),
            "class C { int P { get { return 1; } set { } } }",
            T.Cfg(("csharp_style_expression_bodied_accessors", "true")));
        Assert.True(accessor.Has("CS011"));

        var local = await T.Run(new ExpressionBodyRule(),
            "class C { void M() { int L() { return 1; } L(); } }",
            T.Cfg(("csharp_style_expression_bodied_local_functions", "true")));
        Assert.True(local.Has("CS011"));
    }

    [Fact]
    public async Task ReadonlyField_protected_candidate_flagged()
    {
        var diags = await T.Run(new ReadonlyFieldRule(),
            "class C { protected int x = 5; }",
            T.Cfg(("dotnet_style_readonly_field", "true")));
        Assert.True(diags.Has("CS033"));
    }

    [Fact]
    public async Task ReadonlyField_assigned_only_in_ctor_flagged()
    {
        var diags = await T.Run(new ReadonlyFieldRule(),
            "class C { private int x; public C() { x = 1; } }",
            T.Cfg(("dotnet_style_readonly_field", "true")));
        Assert.True(diags.Has("CS033"));
    }

    [Fact]
    public async Task ThrowExpression_with_else_is_clean()
    {
        var diags = await T.Run(new ThrowExpressionRule(),
            "class C { void M(object o) { if (o == null) throw new System.Exception(); else { } } }",
            T.Cfg(("csharp_style_throw_expression", "true")));
        Assert.False(diags.Has("CS022"));
    }

    [Fact]
    public async Task Accessibility_omit_if_default_is_clean()
    {
        var diags = await T.Run(new AccessibilityModifiersRule(),
            "class C { void M() { } }",
            T.Cfg(("dotnet_style_require_accessibility_modifiers", "omit_if_default")));
        Assert.False(diags.Has("CS032"));
    }

    [Fact]
    public async Task Accessibility_flags_field_property_and_constructor()
    {
        var diags = await T.Run(new AccessibilityModifiersRule(),
            "class C { int F; int P { get; set; } C() { } }",
            T.Cfg(("dotnet_style_require_accessibility_modifiers", "always")));
        Assert.True(diags.Count(d => d.Rule == "CS032") >= 3);
    }

    [Fact]
    public async Task ObjectInitializer_single_assignment_is_clean()
    {
        var diags = await T.Run(new ObjectInitializerRule(),
            "class C { public int A; void M() { var c = new C(); c.A = 1; } }",
            T.Cfg(("dotnet_style_object_initializer", "true")));
        Assert.False(diags.Has("CS034"));
    }

    [Fact]
    public void Rule_applies_to_predicates()
    {
        Assert.True(new DynamicUsageRule().AppliesTo(T.Cfg()));
        Assert.True(new ExpressionBodyRule().AppliesTo(
            T.Cfg(("csharp_style_expression_bodied_methods", "true"))));
        Assert.False(new ExpressionBodyRule().AppliesTo(T.Cfg(("x", "y"))));
        Assert.True(new QualificationRule().AppliesTo(
            T.Cfg(("dotnet_style_qualification_for_method", "false"))));
    }
}
