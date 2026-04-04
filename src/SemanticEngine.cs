using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Build.Locator;

namespace CsEdLint;

class SemanticEngine
{
    readonly EditorConfigLoader _loader;

    public SemanticEngine(EditorConfigLoader loader) => _loader = loader;

    public static bool TryRegisterMsBuild(out string? error)
    {
        try
        {
            if (!MSBuildLocator.IsRegistered)
                MSBuildLocator.RegisterDefaults();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeProjectAsync(
        string projectPath, bool verbose = false)
    {
        if (!File.Exists(projectPath))
        {
            Console.Error.WriteLine($"Project file not found: {projectPath}");
            return [];
        }

        using var workspace = MSBuildWorkspace.Create();

        if (verbose)
            workspace.WorkspaceFailed += (_, e) =>
                Console.Error.WriteLine($"  workspace: {e.Diagnostic.Message}");

        Project project;
        Compilation? compilation;

        try
        {
            project     = await workspace.OpenProjectAsync(projectPath);
            compilation = await project.GetCompilationAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load project: {ex.Message}");
            Console.Error.WriteLine("Ensure the project has been restored (dotnet restore) before running --deep.");
            return [];
        }

        if (compilation == null) return [];

        var diagnostics      = new List<Diagnostic>();
        var severityOverrides = BuildSeverityOverrides(project);

        foreach (var d in compilation.GetDiagnostics()
            .Where(d => d.Location.IsInSource)
            .Where(d => d.Severity != Microsoft.CodeAnalysis.DiagnosticSeverity.Hidden))
        {
            var effectiveSeverity = GetEffectiveSeverity(d, severityOverrides);
            if (effectiveSeverity == null) continue;

            var loc = d.Location.GetLineSpan();
            if (!loc.IsValid) continue;

            diagnostics.Add(new(
                loc.Path,
                loc.StartLinePosition.Line + 1,
                loc.StartLinePosition.Character + 1,
                d.Id,
                d.GetMessage(),
                effectiveSeverity.Value));
        }

        diagnostics.AddRange(await RunSemanticStyleRulesAsync(project));
        return diagnostics;
    }

    async Task<IReadOnlyList<Diagnostic>> RunSemanticStyleRulesAsync(Project project)
    {
        var diagnostics = new List<Diagnostic>();

        foreach (var document in project.Documents)
        {
            if (document.FilePath == null ||
                !document.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                continue;

            var semanticModel = await document.GetSemanticModelAsync();
            if (semanticModel == null) continue;

            var root = await document.GetSyntaxRootAsync();
            if (root == null) continue;

            var config = _loader.GetConfig(document.FilePath);
            diagnostics.AddRange(CheckReadonlyFields(document.FilePath, root, semanticModel, config));
            diagnostics.AddRange(CheckVarStyle(document.FilePath, root, semanticModel, config));
        }

        return diagnostics;
    }

    static IReadOnlyList<Diagnostic> CheckReadonlyFields(
        string filePath, SyntaxNode root, SemanticModel model, FileConfig config)
    {
        if (!config.Properties.TryGetValue("dotnet_style_readonly_field", out var val) ||
            !val.Contains("true")) return [];

        var diagnostics = new List<Diagnostic>();

        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            var mods = field.Modifiers;
            if (mods.Any(SyntaxKind.ReadOnlyKeyword) || mods.Any(SyntaxKind.ConstKeyword) ||
                mods.Any(SyntaxKind.StaticKeyword)) continue;
            if (!mods.Any(SyntaxKind.PrivateKeyword)) continue;

            foreach (var variable in field.Declaration.Variables)
            {
                var symbol = model.GetDeclaredSymbol(variable) as IFieldSymbol;
                if (symbol == null) continue;

                bool writtenInNonCtor = root.DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()
                    .Where(a => SymbolEqualityComparer.Default.Equals(
                        model.GetSymbolInfo(a.Left).Symbol, symbol))
                    .Any(a => !a.Ancestors().OfType<ConstructorDeclarationSyntax>().Any());

                if (!writtenInNonCtor)
                {
                    var loc = variable.Identifier.GetLocation().GetLineSpan();
                    diagnostics.Add(new(filePath,
                        loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1,
                        "CS033-S",
                        $"Field '{variable.Identifier.Text}' is only assigned in constructors; declare as 'readonly' (semantic).",
                        Severity.Warning));
                }
            }
        }

        return diagnostics;
    }

    static IReadOnlyList<Diagnostic> CheckVarStyle(
        string filePath, SyntaxNode root, SemanticModel model, FileConfig config)
    {
        var diagnostics    = new List<Diagnostic>();
        var varForBuiltin  = config.Properties.TryGetValue("csharp_style_var_for_built_in_types",  out var vfb) && vfb.Contains("true");
        var varWhenApparent = config.Properties.TryGetValue("csharp_style_var_when_type_is_apparent", out var vwa) && vwa.Contains("true");

        foreach (var local in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            var decl = local.Declaration;
            if (decl.Type.IsVar || decl.Variables.Count != 1) continue;
            var variable = decl.Variables[0];
            if (variable.Initializer == null) continue;

            var typeInfo = model.GetTypeInfo(decl.Type);
            if (typeInfo.Type == null) continue;

            var isBuiltin  = typeInfo.Type.SpecialType != SpecialType.None;
            var isApparent = IsTypeApparentFromInit(variable.Initializer.Value, model);

            if (varForBuiltin && isBuiltin)
            {
                var loc = decl.Type.GetLocation().GetLineSpan();
                diagnostics.Add(new(filePath,
                    loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1,
                    "CS010-S",
                    $"Use 'var' instead of '{typeInfo.Type.ToDisplayString()}' (semantic: csharp_style_var_for_built_in_types).",
                    Severity.Warning));
            }
            else if (varWhenApparent && isApparent)
            {
                var loc = decl.Type.GetLocation().GetLineSpan();
                diagnostics.Add(new(filePath,
                    loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1,
                    "CS010-S",
                    $"Use 'var': type '{typeInfo.Type.ToDisplayString()}' is apparent from the initializer (semantic).",
                    Severity.Warning));
            }
        }

        return diagnostics;
    }

    static bool IsTypeApparentFromInit(ExpressionSyntax init, SemanticModel model)
    {
        var typeInfo = model.GetTypeInfo(init);
        return init is
            ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax or
            CastExpressionSyntax or ArrayCreationExpressionSyntax
            && typeInfo.Type != null;
    }

    Dictionary<string, Severity?> BuildSeverityOverrides(Project project)
    {
        var overrides = new Dictionary<string, Severity?>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in project.Documents)
        {
            if (document.FilePath == null) continue;
            var config = _loader.GetConfig(document.FilePath);

            foreach (var (key, value) in config.Properties)
            {
                if (!key.StartsWith("dotnet_diagnostic.", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = key.Split('.');
                if (parts.Length < 3 || parts[2] != "severity") continue;

                overrides[parts[1].ToUpperInvariant()] = value.ToLowerInvariant() switch
                {
                    "error"            => (Severity?)Severity.Error,
                    "warning"          => Severity.Warning,
                    "none" or "silent" => null,
                    _                  => Severity.Warning
                };
            }
        }

        return overrides;
    }

    static Severity? GetEffectiveSeverity(
        Microsoft.CodeAnalysis.Diagnostic d,
        Dictionary<string, Severity?> overrides)
    {
        if (overrides.TryGetValue(d.Id, out var overridden)) return overridden;
        return d.Severity switch
        {
            DiagnosticSeverity.Error   => Severity.Error,
            DiagnosticSeverity.Warning => Severity.Warning,
            _                          => null
        };
    }
}
