using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsLint.Rules;

sealed class VarStyleRule : IRule
{
    public string Id => "CS010";

    public bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("csharp_style_var_for_built_in_types") ||
        config.Properties.ContainsKey("csharp_style_var_when_type_is_apparent") ||
        config.Properties.ContainsKey("csharp_style_var_elsewhere");

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit)
    {
        var filePath = unit.Path;
        var config = unit.Config;
        var root = await unit.Tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var decl in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()
                     .Select(local => local.Declaration))
        {
            var variable = decl.Variables.FirstOrDefault();
            if (variable?.Initializer?.Value == null) continue;

            var isVar = decl.Type.IsVar;
            var init = variable.Initializer.Value;
            var span = decl.Type.GetLocation().GetLineSpan();
            var line = span.StartLinePosition.Line + 1;
            var col = span.StartLinePosition.Character + 1;

            if (!isVar)
                CheckExplicitType(filePath, decl.Type, init, line, col, config, diagnostics);
            else
                CheckVarType(filePath, init, line, col, config, diagnostics);
        }

        return diagnostics;
    }

    static void CheckExplicitType(
        string filePath, TypeSyntax type, ExpressionSyntax init,
        int line, int col, FileConfig config, List<Diagnostic> diagnostics)
    {
        var typeName = type.ToString();
        // Unwrap nullable (e.g. string?, int?) so that nullable built-in types are
        // routed through csharp_style_var_for_built_in_types, not var_elsewhere.
        var coreType = type is NullableTypeSyntax nts ? nts.ElementType : type;

        if (coreType is PredefinedTypeSyntax)
        {
            if (StyleHelper.TryGet(config, "csharp_style_var_for_built_in_types",
                out var val, out var sev) && val == "true")
                diagnostics.Add(StyleHelper.Make(filePath, line, col, "CS010",
                    $"Use 'var' instead of '{typeName}' (csharp_style_var_for_built_in_types = true).", sev));
            return;
        }

        if (TypeApparentFromInitializer(type, init))
        {
            if (StyleHelper.TryGet(config, "csharp_style_var_when_type_is_apparent",
                out var val, out var sev) && val == "true")
                diagnostics.Add(StyleHelper.Make(filePath, line, col, "CS010",
                    $"Use 'var' instead of '{typeName}' (csharp_style_var_when_type_is_apparent = true).", sev));
            return;
        }

        // Skip null-literal initializers: `var x = null` does not compile, so suggesting
        // var here would produce invalid code.
        if (init.IsKind(SyntaxKind.NullLiteralExpression)) return;

        if (StyleHelper.TryGet(config, "csharp_style_var_elsewhere",
            out var elseVal, out var elseSev) && elseVal == "true")
            diagnostics.Add(StyleHelper.Make(filePath, line, col, "CS010",
                $"Use 'var' instead of '{typeName}' (csharp_style_var_elsewhere = true).", elseSev));
    }

    static void CheckVarType(
        string filePath, ExpressionSyntax init,
        int line, int col, FileConfig config, List<Diagnostic> diagnostics)
    {
        if (!TypeApparentFromInitializer(null, init)) return;

        if (StyleHelper.TryGet(config, "csharp_style_var_when_type_is_apparent",
            out var val, out var sev) && val == "false")
        {
            var inferredType = InferTypeNameFromInit(init);
            diagnostics.Add(StyleHelper.Make(filePath, line, col, "CS010",
                "Prefer explicit type over 'var' here (csharp_style_var_when_type_is_apparent = false)." +
                (inferredType != null ? $" Type appears to be '{inferredType}'." : ""), sev));
        }
    }

    static bool TypeApparentFromInitializer(TypeSyntax? declaredType, ExpressionSyntax init) =>
        init switch
        {
            ObjectCreationExpressionSyntax obj =>
                declaredType == null || declaredType.ToString() == obj.Type.ToString() || declaredType.IsVar,
            ImplicitObjectCreationExpressionSyntax => true,
            CastExpressionSyntax cast =>
                declaredType == null || declaredType.ToString() == cast.Type.ToString() || declaredType.IsVar,
            LiteralExpressionSyntax lit => !lit.IsKind(SyntaxKind.NullLiteralExpression),
            ArrayCreationExpressionSyntax => true,
            _ => false
        };

    static string? InferTypeNameFromInit(ExpressionSyntax init) =>
        init switch
        {
            ObjectCreationExpressionSyntax obj => obj.Type.ToString(),
            CastExpressionSyntax cast => cast.Type.ToString(),
            _ => null
        };
}
