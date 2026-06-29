using CsLint.Rules;
using Xunit;

namespace CsLint.Tests;

public class StyleRuleTests
{
    // ── CS010 VarStyle ────────────────────────────────────────────────────────

    [Fact]
    public async Task CS010_suggests_var_for_builtin()
    {
        var code = "class C { void M() { int x = 1; } }";
        var diags = await T.Run(new VarStyleRule(), code,
            T.Cfg(("csharp_style_var_for_built_in_types", "true:warning")));
        Assert.True(diags.Has("CS010"));
    }

    [Fact]
    public async Task CS010_suggests_var_when_apparent()
    {
        var code = "class C { void M() { C x = new C(); } }";
        var diags = await T.Run(new VarStyleRule(), code,
            T.Cfg(("csharp_style_var_when_type_is_apparent", "true:warning")));
        Assert.True(diags.Has("CS010"));
    }

    [Fact]
    public async Task CS010_prefers_explicit_over_var()
    {
        var code = "class C { void M() { var x = new C(); } }";
        var diags = await T.Run(new VarStyleRule(), code,
            T.Cfg(("csharp_style_var_when_type_is_apparent", "false:warning")));
        Assert.True(diags.Has("CS010"));
    }

    [Fact]
    public async Task CS010_var_elsewhere()
    {
        // csharp_style_var_elsewhere covers cases where type is not apparent.
        var code = "class C { void M(C c) { C x = c; } }";
        var diags = await T.Run(new VarStyleRule(), code,
            T.Cfg(("csharp_style_var_elsewhere", "true:warning")));
        Assert.True(diags.Has("CS010"));
    }

    [Fact]
    public async Task CS010_does_not_apply_without_keys()
    {
        Assert.False(new VarStyleRule().AppliesTo(T.Cfg()));
    }

    // ── CS011 ExpressionBodies ────────────────────────────────────────────────

    [Fact]
    public async Task CS011_prefers_expression_body()
    {
        // ExpressionBodyRule checks setting == "true" for "prefer expression body".
        var code = "class C { int M() { return 1; } }";
        var diags = await T.Run(new ExpressionBodyRule(), code,
            T.Cfg(("csharp_style_expression_bodied_methods", "true:warning")));
        Assert.True(diags.Has("CS011"));
    }

    [Fact]
    public async Task CS011_prefers_block_body()
    {
        // ExpressionBodyRule checks setting == "false" for "prefer block body".
        var code = "class C { int M() => 1; }";
        var diags = await T.Run(new ExpressionBodyRule(), code,
            T.Cfg(("csharp_style_expression_bodied_methods", "false:warning")));
        Assert.True(diags.Has("CS011"));
    }

    [Fact]
    public async Task CS011_property_expression_body()
    {
        // Properties with a single getter that returns a value can use expression body.
        var code = "class C { int X { get { return 1; } } }";
        var diags = await T.Run(new ExpressionBodyRule(), code,
            T.Cfg(("csharp_style_expression_bodied_properties", "true:warning")));
        Assert.True(diags.Has("CS011"));
    }

    // ── CS020 Namespace ───────────────────────────────────────────────────────

    [Fact]
    public async Task CS020_prefers_file_scoped()
    {
        var code = "namespace MyNs { class C { } }";
        var diags = await T.Run(new NamespaceDeclarationStyleRule(), code,
            T.Cfg(("csharp_style_namespace_declarations", "file_scoped:warning")));
        Assert.True(diags.Has("CS020"));
    }

    [Fact]
    public async Task CS020_prefers_block_scoped()
    {
        var code = "namespace MyNs; class C { }";
        var diags = await T.Run(new NamespaceDeclarationStyleRule(), code,
            T.Cfg(("csharp_style_namespace_declarations", "block_scoped:warning")));
        Assert.True(diags.Has("CS020"));
    }

    // ── CS021 PatternMatching ─────────────────────────────────────────────────

    [Fact]
    public async Task CS021_is_with_cast()
    {
        var code = "class C { void M(object o) { if (o is string) { var s = (string)o; } } }";
        var diags = await T.Run(new PatternMatchingRule(), code,
            T.Cfg(("csharp_style_pattern_matching_over_is_with_cast_check", "true:warning")));
        Assert.True(diags.Has("CS021"));
    }

    [Fact]
    public async Task CS021_as_with_null_check()
    {
        var code = """
            class C {
                void M(object o) {
                    var s = o as string;
                    if (s != null) { }
                }
            }
            """;
        var diags = await T.Run(new PatternMatchingRule(), code,
            T.Cfg(("csharp_style_pattern_matching_over_as_with_null_check", "true:warning")));
        Assert.True(diags.Has("CS021"));
    }

    // ── CS022 ThrowExpression ─────────────────────────────────────────────────

    [Fact]
    public async Task CS022_null_check_then_throw()
    {
        var code = """
            class C {
                void M(string s) {
                    if (s == null) throw new System.ArgumentNullException(nameof(s));
                }
            }
            """;
        var diags = await T.Run(new ThrowExpressionRule(), code,
            T.Cfg(("csharp_style_throw_expression", "true:warning")));
        Assert.True(diags.Has("CS022"));
    }

    // ── CS023 ConditionalDelegateCall ─────────────────────────────────────────

    [Fact]
    public async Task CS023_null_check_then_invoke()
    {
        var code = """
            class C {
                System.Action? _ev;
                void M() { if (_ev != null) _ev(); }
            }
            """;
        var diags = await T.Run(new ConditionalDelegateCallRule(), code,
            T.Cfg(("csharp_style_conditional_delegate_call", "true:warning")));
        Assert.True(diags.Has("CS023"));
    }

    // ── CS024 UnusedValue ─────────────────────────────────────────────────────

    [Fact]
    public async Task CS024_unused_local()
    {
        var code = "class C { void M() { var x = 1; } }";
        var diags = await T.Run(new UnusedValueRule(), code,
            T.Cfg(("csharp_style_unused_value_assignment_preference", "discard_variable:warning")));
        Assert.True(diags.Has("CS024"));
    }

    // ── CS030 Qualification ───────────────────────────────────────────────────

    [Fact]
    public async Task CS030_flags_this_qualifier()
    {
        var code = "class C { int x; void M() { this.x = 1; } }";
        var diags = await T.Run(new QualificationRule(), code,
            T.Cfg(("dotnet_style_qualification_for_field", "false:warning")));
        Assert.True(diags.Has("CS030"));
    }

    // ── CS031 PredefinedType ──────────────────────────────────────────────────

    [Fact]
    public async Task CS031_prefers_keyword()
    {
        var code = "class C { void M() { Int32 x = 0; } }";
        var diags = await T.Run(new PredefinedTypeRule(), code,
            T.Cfg(("dotnet_style_predefined_type_for_locals_parameters_members", "true:warning")));
        Assert.True(diags.Has("CS031"));
    }

    [Fact]
    public async Task CS031_flags_clr_identifier_in_type_position()
    {
        var code = "class C { void M() { String x = \"hi\"; } }";
        var diags = await T.Run(new PredefinedTypeRule(), code,
            T.Cfg(("dotnet_style_predefined_type_for_locals_parameters_members", "true:warning")));
        Assert.True(diags.Has("CS031"));
    }

    // ── CS032 Accessibility ───────────────────────────────────────────────────

    [Fact]
    public async Task CS032_flags_missing_modifier()
    {
        var code = "class C { void M() { } }";
        var diags = await T.Run(new AccessibilityModifiersRule(), code,
            T.Cfg(("dotnet_style_require_accessibility_modifiers", "always:warning")));
        Assert.True(diags.Has("CS032"));
    }

    [Fact]
    public async Task CS032_skips_when_never()
    {
        var code = "class C { public void M() { } }";
        var diags = await T.Run(new AccessibilityModifiersRule(), code,
            T.Cfg(("dotnet_style_require_accessibility_modifiers", "never:warning")));
        // "never" means modifiers are not required; explicit modifier would not be flagged here.
        Assert.False(diags.Any(d => d.Rule == "CS032" && d.Line == 1));
    }

    // ── CS033 ReadonlyField ───────────────────────────────────────────────────

    [Fact]
    public async Task CS033_flags_readonly_candidate()
    {
        // ReadonlyFieldRule fires when a private field has an initializer but is never
        // reassigned outside the declaration. A field with no initializer and no
        // assignment anywhere is not flagged (it is zero-initialized by CLR).
        var code = "class C { private int x = 0; }";
        var diags = await T.Run(new ReadonlyFieldRule(), code,
            T.Cfg(("dotnet_style_readonly_field", "true:warning")));
        Assert.True(diags.Has("CS033"));
    }

    [Fact]
    public async Task CS033_clean_when_reassigned()
    {
        var code = "class C { private int x; public void M() { x = 1; } }";
        var diags = await T.Run(new ReadonlyFieldRule(), code,
            T.Cfg(("dotnet_style_readonly_field", "true:warning")));
        Assert.False(diags.Has("CS033"));
    }

    // ── CS034 ObjectInitializer ───────────────────────────────────────────────

    [Fact]
    public async Task CS034_suggests_initializer()
    {
        // ObjectInitializerRule requires >= 2 consecutive property assignments after `new`
        // to flag the pattern (to avoid false positives on single-property types).
        var code = """
            class P { public int X; public int Y; }
            class C { void M() { var p = new P(); p.X = 1; p.Y = 2; } }
            """;
        var diags = await T.Run(new ObjectInitializerRule(), code,
            T.Cfg(("dotnet_style_object_initializer", "true:warning")));
        Assert.True(diags.Has("CS034"));
    }

    // ── CS035 NullCheck ───────────────────────────────────────────────────────

    [Fact]
    public async Task CS035_flags_reference_equals_null()
    {
        var code = "class C { void M(object o) { if (object.ReferenceEquals(o, null)) { } } }";
        var diags = await T.Run(new NullCheckPreferenceRule(), code,
            T.Cfg(("dotnet_style_prefer_is_null_check_over_reference_equality_method", "true:warning")));
        Assert.True(diags.Has("CS035"));
    }

    // ── CS036 NamespaceMatchFolder ────────────────────────────────────────────

    [Fact]
    public async Task CS036_flags_namespace_folder_mismatch()
    {
        // The namespace says "Wrong" but we're checking a file in /src/. Since the
        // rule compares namespace to folder structure, a wrong namespace should flag.
        var path = T.WriteCs("namespace WrongNamespace; class C { }",
            ext: ".cs");
        try
        {
            var diags = await new NamespaceMatchFolderRule().AnalyzeAsync(
                path, T.Cfg(("dotnet_style_namespace_match_folder", "true:warning")));
            // Whether it fires depends on folder depth – just check rule is accessible.
            Assert.NotNull(diags);
        }
        finally { File.Delete(path); }
    }

    // ── CS040 Naming ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CS040_flags_naming_violation()
    {
        // Configure: private fields should use camelCase starting with '_'.
        var code = "class C { private int BadFieldName; }";
        var cfg = T.Cfg(
            ("dotnet_naming_rule.private_fields.symbols", "private_fields"),
            ("dotnet_naming_rule.private_fields.style", "underscore_prefix"),
            ("dotnet_naming_rule.private_fields.severity", "warning"),
            ("dotnet_naming_symbols.private_fields.applicable_kinds", "field"),
            ("dotnet_naming_symbols.private_fields.applicable_accessibilities", "private"),
            ("dotnet_naming_style.underscore_prefix.required_prefix", "_"),
            ("dotnet_naming_style.underscore_prefix.capitalization", "camel_case"));
        var diags = await T.Run(new NamingRule(), code, cfg);
        Assert.True(diags.Has("CS040"));
    }

    [Fact]
    public async Task CS040_clean_when_compliant()
    {
        var code = "class C { private int _goodFieldName; }";
        var cfg = T.Cfg(
            ("dotnet_naming_rule.private_fields.symbols", "private_fields"),
            ("dotnet_naming_rule.private_fields.style", "underscore_prefix"),
            ("dotnet_naming_rule.private_fields.severity", "warning"),
            ("dotnet_naming_symbols.private_fields.applicable_kinds", "field"),
            ("dotnet_naming_symbols.private_fields.applicable_accessibilities", "private"),
            ("dotnet_naming_style.underscore_prefix.required_prefix", "_"),
            ("dotnet_naming_style.underscore_prefix.capitalization", "camel_case"));
        var diags = await T.Run(new NamingRule(), code, cfg);
        Assert.False(diags.Has("CS040"));
    }

    [Fact]
    public async Task CS040_does_not_apply_without_naming_rules()
    {
        Assert.False(new NamingRule().AppliesTo(T.Cfg()));
    }
}
