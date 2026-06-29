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

    // Per-file analysis context, threaded through CheckNode/Check so the member-check
    // helper stays well under the parameter-count limit (S107).
    sealed record Context(string FilePath, FileConfig Config, List<Diagnostic> Diagnostics);

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit)
    {
        var filePath = unit.Path;
        var config = unit.Config;
        var root = await unit.Tree.GetRootAsync();
        var ctx = new Context(filePath, config, new List<Diagnostic>());

        foreach (var node in root.DescendantNodes())
            CheckNode(ctx, node);

        return ctx.Diagnostics;
    }

    static void CheckNode(Context ctx, SyntaxNode node)
    {
        switch (node)
        {
            case MethodDeclarationSyntax m:
                Check(ctx, m.Body, m.ExpressionBody, m.Identifier.GetLocation(),
                    "csharp_style_expression_bodied_methods", "method");
                break;

            case ConstructorDeclarationSyntax c:
                Check(ctx, c.Body, c.ExpressionBody, c.Identifier.GetLocation(),
                    "csharp_style_expression_bodied_constructors", "constructor");
                break;

            case OperatorDeclarationSyntax op:
                Check(ctx, op.Body, op.ExpressionBody, op.OperatorToken.GetLocation(),
                    "csharp_style_expression_bodied_operators", "operator");
                break;

            case PropertyDeclarationSyntax p when p.AccessorList == null || IsSingleGetterProperty(p):
                Check(ctx, GetPropertyBlock(p), p.ExpressionBody, p.Identifier.GetLocation(),
                    "csharp_style_expression_bodied_properties", "property");
                break;

            case IndexerDeclarationSyntax idx:
                Check(ctx, null, idx.ExpressionBody, idx.ThisKeyword.GetLocation(),
                    "csharp_style_expression_bodied_indexers", "indexer");
                break;

            case AccessorDeclarationSyntax acc:
                Check(ctx, acc.Body, acc.ExpressionBody, acc.Keyword.GetLocation(),
                    "csharp_style_expression_bodied_accessors", "accessor");
                break;

            case LocalFunctionStatementSyntax lf:
                Check(ctx, lf.Body, lf.ExpressionBody, lf.Identifier.GetLocation(),
                    "csharp_style_expression_bodied_local_functions", "local function");
                break;
        }
    }

    static void Check(
        Context ctx,
        BlockSyntax? blockBody,
        ArrowExpressionClauseSyntax? expressionBody,
        Location identifierLoc,
        string key, string memberKind)
    {
        if (!StyleHelper.TryGet(ctx.Config, key, out var setting, out var severity))
            return;

        var loc = identifierLoc.GetLineSpan();
        var (line, col) = (loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1);

        if (setting == "true" && blockBody != null && HasSingleReturnOrExpression(blockBody))
        {
            ctx.Diagnostics.Add(StyleHelper.Make(ctx.FilePath, line, col, "CS011",
                $"Prefer expression body for {memberKind} ({key} = true).", severity));
        }
        else if (setting == "false" && expressionBody != null)
        {
            ctx.Diagnostics.Add(StyleHelper.Make(ctx.FilePath, line, col, "CS011",
                $"Prefer block body for {memberKind} ({key} = false).", severity));
        }
        else if (setting == "when_on_single_line" && blockBody != null &&
                 HasSingleReturnOrExpression(blockBody) && IsSingleLine(blockBody))
        {
            ctx.Diagnostics.Add(StyleHelper.Make(ctx.FilePath, line, col, "CS011",
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
        // A near-single-line block: one statement spanning at most this many lines.
        const int maxSingleLineSpan = 2;
        return span.StartLinePosition.Line == span.EndLinePosition.Line ||
               block.Statements.Count <= 1 && span.EndLinePosition.Line - span.StartLinePosition.Line <= maxSingleLineSpan;
    }

    static bool IsSingleGetterProperty(PropertyDeclarationSyntax p) =>
        p.AccessorList?.Accessors.Count == 1 &&
        p.AccessorList.Accessors[0].Keyword.IsKind(SyntaxKind.GetKeyword);

    static BlockSyntax? GetPropertyBlock(PropertyDeclarationSyntax p) =>
        p.AccessorList?.Accessors.FirstOrDefault()?.Body;
}
