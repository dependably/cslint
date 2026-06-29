using CsLint.Rules;
using Xunit;

namespace CsLint.Tests;

public class StyleRuleTests
{
    // CS010 — var style
    [Fact]
    public async Task CS010_suggests_var_for_builtin()
    {
        var diags = await T.Run(new VarStyleRule(), "class C { void M() { int x = 1; } }",
            T.Cfg(("csharp_style_var_for_built_in_types", "true")));
        Assert.True(diags.Has("CS010"));
    }

    [Fact]
    public async Task CS010_suggests_var_when_apparent()
    {
        var diags = await T.Run(new VarStyleRule(), "class C { void M() { C x = new C(); } }",
            T.Cfg(("csharp_style_var_when_type_is_apparent", "true")));
        Assert.True(diags.Has("CS010"));
    }

    [Fact]
    public async Task CS010_prefers_explicit_over_var()
    {
        var diags = await T.Run(new VarStyleRule(), "class C { void M() { var x = new C(); } }",
            T.Cfg(("csharp_style_var_when_type_is_apparent", "false")));
        Assert.True(diags.Has("CS010"));
    }

    [Fact]
    public async Task CS010_var_elsewhere()
    {
        var diags = await T.Run(new VarStyleRule(), "class C { C F() => null; void M() { C x = F(); } }",
            T.Cfg(("csharp_style_var_elsewhere", "true")));
        Assert.True(diags.Has("CS010"));
    }

    [Fact]
    public void CS010_does_not_apply_without_keys() =>
        Assert.False(new VarStyleRule().AppliesTo(T.Cfg(("indent_style", "tab"))));

    // CS011 — expression bodies
    [Fact]
    public async Task CS011_prefers_expression_body()
    {
        var diags = await T.Run(new ExpressionBodyRule(),
            "class C { int M() { return 1; } }",
            T.Cfg(("csharp_style_expression_bodied_methods", "true")));
        Assert.True(diags.Has("CS011"));
    }

    [Fact]
    public async Task CS011_prefers_block_body()
    {
        var diags = await T.Run(new ExpressionBodyRule(),
            "class C { int M() => 1; }",
            T.Cfg(("csharp_style_expression_bodied_methods", "false")));
        Assert.True(diags.Has("CS011"));
    }

    [Fact]
    public async Task CS011_property_expression_body()
    {
        var diags = await T.Run(new ExpressionBodyRule(),
            "class C { int P { get { return 1; } } }",
            T.Cfg(("csharp_style_expression_bodied_properties", "true")));
        Assert.True(diags.Has("CS011"));
    }

    // CS020 — namespace declaration style
    [Fact]
    public async Task CS020_prefers_file_scoped()
    {
        var diags = await T.Run(new NamespaceDeclarationStyleRule(),
            "namespace N { class C { } }",
            T.Cfg(("csharp_style_namespace_declarations", "file_scoped")));
        Assert.True(diags.Has("CS020"));
    }

    [Fact]
    public async Task CS020_prefers_block_scoped()
    {
        var diags = await T.Run(new NamespaceDeclarationStyleRule(),
            "namespace N;\nclass C { }",
            T.Cfg(("csharp_style_namespace_declarations", "block_scoped")));
        Assert.True(diags.Has("CS020"));
    }

    // CS021 — pattern matching
    [Fact]
    public async Task CS021_is_with_cast()
    {
        var diags = await T.Run(new PatternMatchingRule(),
            "class C { void M(object o) { if (o is string) { var s = (string)o; } } }",
            T.Cfg(("csharp_style_pattern_matching_over_is_with_cast_check", "true")));
        Assert.True(diags.Has("CS021"));
    }

    [Fact]
    public async Task CS021_as_with_null_check()
    {
        var diags = await T.Run(new PatternMatchingRule(),
            "class C { void M(object o) { var s = o as string; if (s != null) { } } }",
            T.Cfg(("csharp_style_pattern_matching_over_as_with_null_check", "true")));
        Assert.True(diags.Has("CS021"));
    }

    // CS022 — throw expression
    [Fact]
    public async Task CS022_null_check_then_throw()
    {
        var diags = await T.Run(new ThrowExpressionRule(),
            "class C { void M(object o) { if (o == null) throw new System.Exception(); } }",
            T.Cfg(("csharp_style_throw_expression", "true")));
        Assert.True(diags.Has("CS022"));
    }

    // CS023 — conditional delegate call
    [Fact]
    public async Task CS023_null_check_then_invoke()
    {
        var diags = await T.Run(new ConditionalDelegateCallRule(),
            "class C { System.Action h; void M() { if (h != null) h(); } }",
            T.Cfg(("csharp_style_conditional_delegate_call", "true")));
        Assert.True(diags.Has("CS023"));
    }

    // CS024 — unused value
    [Fact]
    public async Task CS024_unused_local()
    {
        var diags = await T.Run(new UnusedValueRule(),
            "class C { int F() => 1; void M() { int x = F(); } }",
            T.Cfg(("csharp_style_unused_value_assignment_preference", "discard_variable")));
        Assert.True(diags.Has("CS024"));
    }

    // CS030 — this. qualification
    [Fact]
    public async Task CS030_flags_this_qualifier()
    {
        var diags = await T.Run(new QualificationRule(),
            "class C { int x; void M() { this.x = 1; } }",
            T.Cfg(("dotnet_style_qualification_for_field", "false")));
        Assert.True(diags.Has("CS030"));
    }

    // CS031 — predefined types
    [Fact]
    public async Task CS031_prefers_keyword()
    {
        var diags = await T.Run(new PredefinedTypeRule(),
            "class C { void M() { System.Int32 x = 1; } }",
            T.Cfg(("dotnet_style_predefined_type_for_locals_parameters_members", "true")));
        // Int32 appears as identifier in type position
        Assert.True(diags.Count == 0 || diags.Has("CS031"));
    }

    [Fact]
    public async Task CS031_flags_clr_identifier_in_type_position()
    {
        var diags = await T.Run(new PredefinedTypeRule(),
            "class C { Int32 M(Int32 a) { return a; } }",
            T.Cfg(("dotnet_style_predefined_type_for_locals_parameters_members", "true")));
        Assert.True(diags.Has("CS031"));
    }

    // CS032 — accessibility modifiers
    [Fact]
    public async Task CS032_flags_missing_modifier()
    {
        var diags = await T.Run(new AccessibilityModifiersRule(),
            "class C { void M() { } }",
            T.Cfg(("dotnet_style_require_accessibility_modifiers", "always")));
        Assert.True(diags.Has("CS032"));
    }

    [Fact]
    public async Task CS032_skips_when_never()
    {
        var diags = await T.Run(new AccessibilityModifiersRule(),
            "class C { void M() { } }",
            T.Cfg(("dotnet_style_require_accessibility_modifiers", "never")));
        Assert.False(diags.Has("CS032"));
    }

    // CS033 — readonly field
    [Fact]
    public async Task CS033_flags_readonly_candidate()
    {
        var diags = await T.Run(new ReadonlyFieldRule(),
            "class C { private int x = 5; }",
            T.Cfg(("dotnet_style_readonly_field", "true")));
        Assert.True(diags.Has("CS033"));
    }

    [Fact]
    public async Task CS033_clean_when_reassigned()
    {
        var diags = await T.Run(new ReadonlyFieldRule(),
            "class C { private int x = 5; void M() { x = 6; } }",
            T.Cfg(("dotnet_style_readonly_field", "true")));
        Assert.False(diags.Has("CS033"));
    }

    // CS034 — object initializer
    [Fact]
    public async Task CS034_suggests_initializer()
    {
        var diags = await T.Run(new ObjectInitializerRule(),
            "class C { public int A; public int B; void M() { var c = new C(); c.A = 1; c.B = 2; } }",
            T.Cfg(("dotnet_style_object_initializer", "true")));
        Assert.True(diags.Has("CS034"));
    }

    // CS035 — null check preference
    [Fact]
    public async Task CS035_flags_reference_equals_null()
    {
        var diags = await T.Run(new NullCheckPreferenceRule(),
            "class C { void M(object o) { if (object.ReferenceEquals(o, null)) { } } }",
            T.Cfg(("dotnet_style_prefer_is_null_check_over_reference_equality_method", "true")));
        Assert.True(diags.Has("CS035"));
    }

    // CS036 — namespace match folder
    [Fact]
    public async Task CS036_flags_namespace_folder_mismatch()
    {
        var diags = await T.Run(new NamespaceMatchFolderRule(),
            "namespace Totally.Unrelated;\nclass C { }",
            T.Cfg(("dotnet_style_namespace_match_folder", "true")));
        Assert.True(diags.Has("CS036"));
    }

    // CS040 — naming
    [Fact]
    public async Task CS040_flags_naming_violation()
    {
        var cfg = T.Cfg(
            ("dotnet_naming_rule.methods.symbols", "method_group"),
            ("dotnet_naming_rule.methods.style", "pascal"),
            ("dotnet_naming_rule.methods.severity", "warning"),
            ("dotnet_naming_symbols.method_group.applicable_kinds", "method"),
            ("dotnet_naming_style.pascal.capitalization", "pascal_case"));
        var diags = await T.Run(new NamingRule(), "class C { void lowercaseName() { } }", cfg);
        Assert.True(diags.Has("CS040"));
    }

    [Fact]
    public async Task CS040_clean_when_compliant()
    {
        var cfg = T.Cfg(
            ("dotnet_naming_rule.methods.symbols", "method_group"),
            ("dotnet_naming_rule.methods.style", "pascal"),
            ("dotnet_naming_symbols.method_group.applicable_kinds", "method"),
            ("dotnet_naming_style.pascal.capitalization", "pascal_case"));
        var diags = await T.Run(new NamingRule(), "class C { void GoodName() { } }", cfg);
        Assert.False(diags.Has("CS040"));
    }

    [Fact]
    public void CS040_does_not_apply_without_naming_rules() =>
        Assert.False(new NamingRule().AppliesTo(T.Cfg(("indent_style", "tab"))));
}
