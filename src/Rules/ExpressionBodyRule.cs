using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsLint.Rules;

sealed class ExpressionBodyRule : IRule
{
    public string Id => "CS011";

    static readonly string[] Keys =
    [
        "csharp_style_expression_bodied_methods",
        "csharp_style_expression_bodied_constructors",
        "csharp_style_expression_bodied_operators",
        "csharp_style_expression_bodied_properties",
        "csharp_style_expression_bodied_indexers",
        "csharp_style_expression_bodied_accessors",
        "csharp_style_expression_bodied_lambdas",
        "csharp_style_expression_bodied_local_functions",
    ];

    public bool AppliesTo(FileConfig config) =>
        Keys.Any(config.Properties.ContainsKey);

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var node in root.DescendantNodes())
            CheckNode(filePath, node, config, diagnostics);

        return diagnostics;
    }

    static void CheckNode(string filePath, SyntaxNode node, FileConfig config, List<Diagnostic> diagnostics)
    {
        switch (node)
        {
            case MethodDeclarationSyntax m:
                Check(filePath, m.Body, m.ExpressionBody, m.Identifier.GetLocation(),
                    "csharp_style_expression_bodied_methods", "method", config, diagnostics);
                break;

            case ConstructorDeclarationSyntax c:
                Check(filePath, c.Body, c.ExpressionBody, c.Identifier.GetLocation(),
                    "csharp_style_expression_bodied_constructors", "constructor", config, diagnostics);
                break;

            case OperatorDeclarationSyntax op:
                Check(filePath, op.Body, op.ExpressionBody, op.OperatorToken.GetLocation(),
                    "csharp_style_expression_bodied_operators", "operator", config, diagnostics);
                break;

            case PropertyDeclarationSyntax p when p.AccessorList == null || IsSingleGetterProperty(p):
                Check(filePath, GetPropertyBlock(p), p.ExpressionBody, p.Identifier.GetLocation(),
                    "csharp_style_expression_bodied_properties", "property", config, diagnostics);
                break;

            case IndexerDeclarationSyntax idx:
                Check(filePath, null, idx.ExpressionBody, idx.ThisKeyword.GetLocation(),
                    "csharp_style_expression_bodied_indexers", "indexer", config, diagnostics);
                break;

            case AccessorDeclarationSyntax acc:
                Check(filePath, acc.Body, acc.ExpressionBody, acc.Keyword.GetLocation(),
                    "csharp_style_expression_bodied_accessors", "accessor", config, diagnostics);
                break;

            case LocalFunctionStatementSyntax lf:
                Check(filePath, lf.Body, lf.ExpressionBody, lf.Identifier.GetLocation(),
                    "csharp_style_expression_bodied_local_functions", "local function", config, diagnostics);
                break;
        }
    }

    static void Check(
        string filePath,
        BlockSyntax? blockBody,
        ArrowExpressionClauseSyntax? expressionBody,
        Location identifierLoc,
        string key, string memberKind,
        FileConfig config, List<Diagnostic> diagnostics)
    {
        if (!StyleHelper.TryGet(config, key, out var setting, out var severity))
            return;

        var loc = identifierLoc.GetLineSpan();
        var (line, col) = (loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1);

        if (setting == "true" && blockBody != null && HasSingleReturnOrExpression(blockBody))
        {
            diagnostics.Add(StyleHelper.Make(filePath, line, col, "CS011",
                $"Prefer expression body for {memberKind} ({key} = true).", severity));
        }
        else if (setting == "false" && expressionBody != null)
        {
            diagnostics.Add(StyleHelper.Make(filePath, line, col, "CS011",
                $"Prefer block body for {memberKind} ({key} = false).", severity));
        }
        else if (setting == "when_on_single_line" && blockBody != null &&
                 HasSingleReturnOrExpression(blockBody) && IsSingleLine(blockBody))
        {
            diagnostics.Add(StyleHelper.Make(filePath, line, col, "CS011",
                $"Prefer expression body for single-line {memberKind} ({key} = when_on_single_line).", severity));
        }
    }

    static bool HasSingleReturnOrExpression(BlockSyntax block)
    {
        var statements = block.Statements;
        if (statements.Count == 1 && statements[0] is ReturnStatementSyntax r)
            return r.Expression != null;
        if (statements.Count == 1 && statements[0] is ExpressionStatementSyntax)
            return true;
        return false;
    }

    static bool IsSingleLine(BlockSyntax block)
    {
        var span = block.GetLocation().GetLineSpan();
        return span.StartLinePosition.Line == span.EndLinePosition.Line ||
               block.Statements.Count <= 1 && span.EndLinePosition.Line - span.StartLinePosition.Line <= 2;
    }

    static bool IsSingleGetterProperty(PropertyDeclarationSyntax p) =>
        p.AccessorList?.Accessors.Count == 1 &&
        p.AccessorList.Accessors[0].Keyword.IsKind(SyntaxKind.GetKeyword);

    static BlockSyntax? GetPropertyBlock(PropertyDeclarationSyntax p) =>
        p.AccessorList?.Accessors.FirstOrDefault()?.Body;
}
