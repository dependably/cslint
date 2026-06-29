using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsLint.Rules;

sealed class NamespaceDeclarationStyleRule : IRule
{
    public string Id => "CS020";

    public bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("csharp_style_namespace_declarations");

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        if (!StyleHelper.TryGet(config, "csharp_style_namespace_declarations",
            out var setting, out var severity))
            return [];

        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        var blockNs = root.DescendantNodes().OfType<NamespaceDeclarationSyntax>().ToList();
        var fileNs = root.DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().ToList();

        if (setting == "file_scoped" && blockNs.Count > 0)
        {
            foreach (var ns in blockNs)
            {
                var loc = ns.NamespaceKeyword.GetLocation().GetLineSpan();
                diagnostics.Add(StyleHelper.Make(filePath,
                    loc.StartLinePosition.Line + 1, 1, Id,
                    "Prefer file-scoped namespace declaration (csharp_style_namespace_declarations = file_scoped).",
                    severity));
            }
        }
        else if (setting == "block_scoped" && fileNs.Count > 0)
        {
            foreach (var ns in fileNs)
            {
                var loc = ns.NamespaceKeyword.GetLocation().GetLineSpan();
                diagnostics.Add(StyleHelper.Make(filePath,
                    loc.StartLinePosition.Line + 1, 1, Id,
                    "Prefer block-scoped namespace declaration (csharp_style_namespace_declarations = block_scoped).",
                    severity));
            }
        }

        return diagnostics;
    }
}

sealed class PatternMatchingRule : IRule
{
    public string Id => "CS021";

    public bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("csharp_style_pattern_matching_over_is_with_cast_check") ||
        config.Properties.ContainsKey("csharp_style_pattern_matching_over_as_with_null_check") ||
        config.Properties.ContainsKey("csharp_style_prefer_not_pattern") ||
        config.Properties.ContainsKey("csharp_style_prefer_pattern_matching");

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        CheckIsWithCast(filePath, root, config, diagnostics);
        CheckAsWithNullCheck(filePath, root, config, diagnostics);
        CheckNotPattern(filePath, root, config, diagnostics);

        return diagnostics;
    }

    void CheckIsWithCast(
        string filePath, SyntaxNode root, FileConfig config, List<Diagnostic> diagnostics)
    {
        if (!StyleHelper.TryGet(config, "csharp_style_pattern_matching_over_is_with_cast_check",
            out var val, out var sev) || val != "true")
            return;

        foreach (var ifStmt in root.DescendantNodes().OfType<IfStatementSyntax>().Where(IsIsCastPattern))
        {
            var loc = ifStmt.IfKeyword.GetLocation().GetLineSpan();
            diagnostics.Add(StyleHelper.Make(filePath,
                loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                "Prefer pattern matching over 'is' check + cast (csharp_style_pattern_matching_over_is_with_cast_check = true).",
                sev));
        }
    }

    void CheckAsWithNullCheck(
        string filePath, SyntaxNode root, FileConfig config, List<Diagnostic> diagnostics)
    {
        if (!StyleHelper.TryGet(config, "csharp_style_pattern_matching_over_as_with_null_check",
            out var val, out var sev) || val != "true")
            return;

        foreach (var local in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()
                     .Where(IsAsNullCheckPattern))
        {
            var loc = local.GetLocation().GetLineSpan();
            diagnostics.Add(StyleHelper.Make(filePath,
                loc.StartLinePosition.Line + 1, 1, Id,
                "Prefer pattern matching over 'as' + null check (csharp_style_pattern_matching_over_as_with_null_check = true).",
                sev));
        }
    }

    void CheckNotPattern(
        string filePath, SyntaxNode root, FileConfig config, List<Diagnostic> diagnostics)
    {
        if (!StyleHelper.TryGet(config, "csharp_style_prefer_not_pattern",
            out var val, out var sev) || val != "true")
            return;

        foreach (var prefix in root.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>()
                     .Where(p => p.IsKind(SyntaxKind.LogicalNotExpression)
                                 && p.Operand is IsPatternExpressionSyntax))
        {
            var loc = prefix.GetLocation().GetLineSpan();
            diagnostics.Add(StyleHelper.Make(filePath,
                loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                "Prefer 'is not' pattern over '!is' expression (csharp_style_prefer_not_pattern = true).",
                sev));
        }
    }

    static bool IsIsCastPattern(IfStatementSyntax ifStmt)
    {
        if (ifStmt.Condition is not BinaryExpressionSyntax bin ||
            !bin.IsKind(SyntaxKind.IsExpression))
            return false;

        var varName = bin.Left is IdentifierNameSyntax id ? id.Identifier.Text : null;
        if (varName == null) return false;

        return ContainsCastOfVariable(ifStmt.Statement, varName, bin.Right.ToString());
    }

    static bool ContainsCastOfVariable(StatementSyntax body, string varName, string typeName)
    {
        return body.DescendantNodes()
            .OfType<CastExpressionSyntax>()
            .Any(c => c.Type.ToString() == typeName &&
                      c.Expression is IdentifierNameSyntax id2 &&
                      id2.Identifier.Text == varName);
    }

    static bool IsAsNullCheckPattern(LocalDeclarationStatementSyntax local)
    {
        if (local.Declaration.Variables.Count != 1) return false;
        var variable = local.Declaration.Variables[0];
        if (variable.Initializer?.Value is not BinaryExpressionSyntax bin ||
            !bin.IsKind(SyntaxKind.AsExpression))
            return false;

        var localName = variable.Identifier.Text;
        var parent = local.Parent;
        if (parent == null) return false;

        var siblings = parent.ChildNodes().ToList();
        var idx = siblings.IndexOf(local);

        const int maxLookaheadStatements = 3;
        return siblings.Skip(idx + 1).Take(maxLookaheadStatements)
            .OfType<IfStatementSyntax>()
            .Any(ifStmt =>
                ifStmt.Condition is BinaryExpressionSyntax cond &&
                (cond.IsKind(SyntaxKind.EqualsExpression) || cond.IsKind(SyntaxKind.NotEqualsExpression)) &&
                (cond.Left is IdentifierNameSyntax lId && lId.Identifier.Text == localName ||
                 cond.Right is IdentifierNameSyntax rId && rId.Identifier.Text == localName));
    }
}

sealed class ThrowExpressionRule : IRule
{
    public string Id => "CS022";

    public bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("csharp_style_throw_expression");

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        if (!StyleHelper.TryGet(config, "csharp_style_throw_expression",
            out var setting, out var severity) || setting != "true")
            return [];

        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var ifStmt in root.DescendantNodes().OfType<IfStatementSyntax>().Where(IsNullThrowGuard))
        {
            var loc = ifStmt.IfKeyword.GetLocation().GetLineSpan();
            diagnostics.Add(StyleHelper.Make(filePath,
                loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                "Prefer throw expression over null-check-then-throw (csharp_style_throw_expression = true).",
                severity));
        }

        return diagnostics;
    }

    static bool IsNullThrowGuard(IfStatementSyntax ifStmt)
    {
        if (ifStmt.Else != null) return false;
        if (ifStmt.Condition is not BinaryExpressionSyntax cond) return false;
        if (!cond.IsKind(SyntaxKind.EqualsExpression) && !cond.IsKind(SyntaxKind.NotEqualsExpression)) return false;
        if (!IsNullLiteral(cond.Left) && !IsNullLiteral(cond.Right)) return false;
        var body = ifStmt.Statement;
        if (body is BlockSyntax block) body = block.Statements.Count == 1 ? block.Statements[0] : null;
        return body is ThrowStatementSyntax;
    }

    static bool IsNullLiteral(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression);
}

sealed class ConditionalDelegateCallRule : IRule
{
    public string Id => "CS023";

    public bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("csharp_style_conditional_delegate_call");

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        if (!StyleHelper.TryGet(config, "csharp_style_conditional_delegate_call",
            out var setting, out var severity) || setting != "true")
            return [];

        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var ifStmt in root.DescendantNodes().OfType<IfStatementSyntax>())
        {
            if (!IsNullCheckThenInvoke(ifStmt, out var invokedName)) continue;

            var loc = ifStmt.IfKeyword.GetLocation().GetLineSpan();
            diagnostics.Add(StyleHelper.Make(filePath,
                loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                $"Prefer '{invokedName}?.Invoke(...)' over null check + invoke (csharp_style_conditional_delegate_call = true).",
                severity));
        }

        return diagnostics;
    }

    static bool IsNullCheckThenInvoke(IfStatementSyntax ifStmt, out string? invokedName)
    {
        invokedName = null;
        if (ifStmt.Else != null) return false;
        if (ifStmt.Condition is not BinaryExpressionSyntax cond) return false;
        if (!cond.IsKind(SyntaxKind.NotEqualsExpression)) return false;

        string? checkedName = null;
        if (IsNullLiteral(cond.Right) && cond.Left is IdentifierNameSyntax leftId)
            checkedName = leftId.Identifier.Text;
        else if (IsNullLiteral(cond.Left) && cond.Right is IdentifierNameSyntax rightId)
            checkedName = rightId.Identifier.Text;

        if (checkedName == null) return false;

        var body = ifStmt.Statement;
        if (body is BlockSyntax block)
            body = block.Statements.Count == 1 ? block.Statements[0] : null;

        if (body is ExpressionStatementSyntax exprStmt &&
            exprStmt.Expression is InvocationExpressionSyntax invocation &&
            invocation.Expression is IdentifierNameSyntax invId &&
            invId.Identifier.Text == checkedName)
        {
            invokedName = checkedName;
            return true;
        }

        return false;
    }

    static bool IsNullLiteral(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression);
}

sealed class UnusedValueRule : IRule
{
    public string Id => "CS024";

    public bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("csharp_style_unused_value_expression_statement_preference") ||
        config.Properties.ContainsKey("csharp_style_unused_value_assignment_preference");

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        if (StyleHelper.TryGet(config, "csharp_style_unused_value_assignment_preference",
            out var assignVal, out var assignSev) && assignVal == "discard_variable")
        {
            foreach (var local in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
                foreach (var variable in local.Declaration.Variables)
                    CheckUnusedLocal(filePath, local, variable, assignSev, diagnostics);
        }

        return diagnostics;
    }

    void CheckUnusedLocal(
        string filePath, LocalDeclarationStatementSyntax local,
        VariableDeclaratorSyntax variable, Severity severity, List<Diagnostic> diagnostics)
    {
        var name = variable.Identifier.Text;
        if (name.StartsWith('_') || variable.Initializer == null) return;

        // Search the whole enclosing scope, not just local.Parent: in top-level statements each
        // statement is its own GlobalStatement, so a use in a *later* statement (e.g. a fluent
        // builder chain referenced lines below) is a sibling, not a descendant — missing it
        // produced false "unused" reports.
        SyntaxNode? scope = local.FirstAncestorOrSelf<BlockSyntax>();
        scope ??= local.FirstAncestorOrSelf<CompilationUnitSyntax>();
        if (scope == null) return;

        bool usedAfter = scope.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(id => id.Identifier.Text == name && id.SpanStart > local.Span.End);
        if (usedAfter) return;

        var loc = variable.Identifier.GetLocation().GetLineSpan();
        diagnostics.Add(StyleHelper.Make(filePath,
            loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
            $"Unused variable '{name}'. Prefer discard '_' (csharp_style_unused_value_assignment_preference = discard_variable).",
            severity));
    }
}
