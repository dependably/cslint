using CsLint;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CsLint.Tests;

public class SemanticEngineTests
{
    static (SyntaxNode Root, SemanticModel Model) Compile(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var refs = new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) };
        var comp = CSharpCompilation.Create("t", new[] { tree }, refs);
        return (tree.GetRoot(), comp.GetSemanticModel(tree));
    }

    // ── CheckReadonlyFields ────────────────────────────────────────────────────

    [Fact]
    public void CheckReadonlyFields_flags_field_only_set_in_ctor()
    {
        var (root, model) = Compile(
            "class C { private int x; public C() { x = 1; } }");
        var diags = CsLint.SemanticEngine.CheckReadonlyFields(
            "C.cs", root, model, T.Cfg(("dotnet_style_readonly_field", "true")));
        Assert.Contains(diags, d => d.Rule == "CS033-S");
    }

    [Fact]
    public void CheckReadonlyFields_skips_field_written_outside_ctor()
    {
        var (root, model) = Compile(
            "class C { private int x; public void M() { x = 1; } }");
        var diags = CsLint.SemanticEngine.CheckReadonlyFields(
            "C.cs", root, model, T.Cfg(("dotnet_style_readonly_field", "true")));
        Assert.DoesNotContain(diags, d => d.Rule == "CS033-S");
    }

    [Fact]
    public void CheckReadonlyFields_disabled_returns_empty()
    {
        var (root, model) = Compile("class C { private int x; }");
        Assert.Empty(CsLint.SemanticEngine.CheckReadonlyFields("C.cs", root, model, T.Cfg()));
    }

    // ── CheckVarStyle ──────────────────────────────────────────────────────────

    [Fact]
    public void CheckVarStyle_flags_builtin_type()
    {
        var (root, model) = Compile("class C { void M() { int x = 1; } }");
        var diags = CsLint.SemanticEngine.CheckVarStyle(
            "C.cs", root, model, T.Cfg(("csharp_style_var_for_built_in_types", "true")));
        Assert.Contains(diags, d => d.Rule == "CS010-S");
    }

    [Fact]
    public void CheckVarStyle_flags_apparent_type()
    {
        var (root, model) = Compile("class C { void M() { C x = new C(); } }");
        var diags = CsLint.SemanticEngine.CheckVarStyle(
            "C.cs", root, model, T.Cfg(("csharp_style_var_when_type_is_apparent", "true")));
        Assert.Contains(diags, d => d.Rule == "CS010-S");
    }

    [Fact]
    public void CheckVarStyle_clean_when_disabled()
    {
        var (root, model) = Compile("class C { void M() { int x = 1; } }");
        Assert.Empty(CsLint.SemanticEngine.CheckVarStyle("C.cs", root, model, T.Cfg()));
    }

    // ── CheckUnusedUsings ──────────────────────────────────────────────────────

    [Fact]
    public void CheckUnusedUsings_flags_unused_using()
    {
        // System.Text is a valid namespace in the runtime, but no types from it are used here.
        var (_, model) = Compile("using System.Text;\nclass C { }");
        var diags = CsLint.SemanticEngine.CheckUnusedUsings(
            "C.cs", model,
            T.Cfg(("dotnet_diagnostic.IDE0005.severity", "warning")));
        Assert.Contains(diags, d => d.Rule == "IDE0005");
    }

    [Fact]
    public void CheckUnusedUsings_clean_when_no_usings()
    {
        var (_, model) = Compile("class C { }");
        var diags = CsLint.SemanticEngine.CheckUnusedUsings(
            "C.cs", model,
            T.Cfg(("dotnet_diagnostic.IDE0005.severity", "warning")));
        Assert.Empty(diags);
    }

    [Fact]
    public void CheckUnusedUsings_disabled_when_no_key()
    {
        // Gate: key absent → rule is silent regardless of code content.
        var (_, model) = Compile("using System.Text;\nclass C { }");
        var diags = CsLint.SemanticEngine.CheckUnusedUsings("C.cs", model, T.Cfg());
        Assert.Empty(diags);
    }

    [Fact]
    public void CheckUnusedUsings_suppressed_by_none()
    {
        var (_, model) = Compile("using System.Text;\nclass C { }");
        var diags = CsLint.SemanticEngine.CheckUnusedUsings(
            "C.cs", model,
            T.Cfg(("dotnet_diagnostic.IDE0005.severity", "none")));
        Assert.Empty(diags);
    }

    [Fact]
    public void CheckUnusedUsings_suppressed_by_silent()
    {
        var (_, model) = Compile("using System.Text;\nclass C { }");
        var diags = CsLint.SemanticEngine.CheckUnusedUsings(
            "C.cs", model,
            T.Cfg(("dotnet_diagnostic.IDE0005.severity", "silent")));
        Assert.Empty(diags);
    }

    [Fact]
    public void CheckUnusedUsings_error_severity_when_configured()
    {
        var (_, model) = Compile("using System.Text;\nclass C { }");
        var diags = CsLint.SemanticEngine.CheckUnusedUsings(
            "C.cs", model,
            T.Cfg(("dotnet_diagnostic.IDE0005.severity", "error")));
        Assert.Contains(diags, d => d.Rule == "IDE0005" && d.Severity == Severity.Error);
    }

    [Fact]
    public void CheckUnusedUsings_warning_severity_by_default()
    {
        var (_, model) = Compile("using System.Text;\nclass C { }");
        var diags = CsLint.SemanticEngine.CheckUnusedUsings(
            "C.cs", model,
            T.Cfg(("dotnet_diagnostic.IDE0005.severity", "warning")));
        Assert.All(diags.Where(d => d.Rule == "IDE0005"),
            d => Assert.Equal(Severity.Warning, d.Severity));
    }

    [Fact]
    public void CheckUnusedUsings_reports_correct_line()
    {
        // The using is on line 1 (1-based). Check line reported is 1.
        var (_, model) = Compile("using System.Text;\nclass C { }");
        var diags = CsLint.SemanticEngine.CheckUnusedUsings(
            "C.cs", model,
            T.Cfg(("dotnet_diagnostic.IDE0005.severity", "warning")));
        Assert.Contains(diags, d => d.Rule == "IDE0005" && d.Line == 1);
    }

    // ── GetEffectiveSeverity ───────────────────────────────────────────────────

    [Fact]
    public void GetEffectiveSeverity_maps_compiler_and_overrides()
    {
        var (_, model) = Compile("class C { void M() { int unused = nonexistent; } }");
        var compErr = model.Compilation.GetDiagnostics()
            .First(d => d.Severity == DiagnosticSeverity.Error);

        // Without an override, an error stays an error.
        Assert.Equal(Severity.Error,
            CsLint.SemanticEngine.GetEffectiveSeverity(compErr, new()));

        // An override of "none" suppresses it.
        var overrides = new Dictionary<string, Severity?> { [compErr.Id] = null };
        Assert.Null(CsLint.SemanticEngine.GetEffectiveSeverity(compErr, overrides));
    }

    // ── TryRegisterMsBuild ─────────────────────────────────────────────────────

    [Fact]
    public void TryRegisterMsBuild_returns_bool()
    {
        // Smoke: registration either succeeds or reports an error string, never throws.
        var ok = CsLint.SemanticEngine.TryRegisterMsBuild(out var error);
        Assert.True(ok || error != null);
    }
}
