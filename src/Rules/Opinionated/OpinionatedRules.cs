using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsEdLint.Rules.Opinionated;

record ScanConfig(
    int MaxMethodLines              = 100,
    int MaxCyclomaticComplexity     = 15,
    int MaxNestingDepth             = 4,
    int MaxParameters               = 5,
    bool FlagMagicNumbers           = true,
    bool FlagBooleanParameters      = true,
    bool FlagMissingCancellationToken = true);

sealed class GodFunctionRule : IRule
{
    readonly ScanConfig _config;
    public GodFunctionRule(ScanConfig config) => _config = config;

    public string Id => "OP001";
    public RuleCategory Category => RuleCategory.Opinionated;
    public bool AppliesTo(FileConfig _) => true;

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig _)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (method.Body == null && method.ExpressionBody == null) continue;

            var span       = method.GetLocation().GetLineSpan();
            var lineCount  = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
            var complexity = ComputeCyclomaticComplexity(method);

            if (lineCount > _config.MaxMethodLines)
            {
                diagnostics.Add(new(filePath, span.StartLinePosition.Line + 1, 1, Id,
                    $"Method '{method.Identifier.Text}' is {lineCount} lines (max {_config.MaxMethodLines}). Consider decomposing.",
                    Severity.Warning));
            }
            else if (complexity > _config.MaxCyclomaticComplexity)
            {
                diagnostics.Add(new(filePath, span.StartLinePosition.Line + 1, 1, Id,
                    $"Method '{method.Identifier.Text}' has cyclomatic complexity {complexity} (max {_config.MaxCyclomaticComplexity}).",
                    Severity.Warning));
            }
        }

        return diagnostics;
    }

    static int ComputeCyclomaticComplexity(MethodDeclarationSyntax method)
    {
        int complexity = 1;
        foreach (var node in method.DescendantNodes())
        {
            switch (node.Kind())
            {
                case SyntaxKind.IfStatement:
                case SyntaxKind.ElseClause:
                case SyntaxKind.ForStatement:
                case SyntaxKind.ForEachStatement:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.DoStatement:
                case SyntaxKind.CaseSwitchLabel:
                case SyntaxKind.CasePatternSwitchLabel:
                case SyntaxKind.WhenClause:
                case SyntaxKind.CatchClause:
                case SyntaxKind.ConditionalExpression:
                case SyntaxKind.CoalesceExpression:
                case SyntaxKind.LogicalAndExpression:
                case SyntaxKind.LogicalOrExpression:
                    complexity++;
                    break;
            }
        }
        return complexity;
    }
}

sealed class DeepNestingRule : IRule
{
    readonly ScanConfig _config;
    public DeepNestingRule(ScanConfig config) => _config = config;

    public string Id => "OP002";
    public RuleCategory Category => RuleCategory.Opinionated;
    public bool AppliesTo(FileConfig _) => true;

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig _)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();
        var reported = new HashSet<int>();

        foreach (var node in root.DescendantNodes())
        {
            if (!IsNestingNode(node)) continue;

            int depth = CountNestingDepth(node);
            if (depth <= _config.MaxNestingDepth) continue;

            var loc  = node.GetLocation().GetLineSpan();
            var line = loc.StartLinePosition.Line + 1;
            if (!reported.Add(line)) continue;

            var methodName = node.Ancestors().OfType<MethodDeclarationSyntax>()
                .FirstOrDefault()?.Identifier.Text ?? "?";

            diagnostics.Add(new(filePath, line, 1, Id,
                $"Nesting depth {depth} exceeds maximum {_config.MaxNestingDepth} in '{methodName}'. Flatten using early returns or extracted methods.",
                Severity.Warning));
        }

        return diagnostics;
    }

    static bool IsNestingNode(SyntaxNode node) => node is
        IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or
        WhileStatementSyntax or DoStatementSyntax or SwitchStatementSyntax or
        TryStatementSyntax or LockStatementSyntax or UsingStatementSyntax;

    static int CountNestingDepth(SyntaxNode node)
    {
        int depth = 0;
        var current = node.Parent;
        while (current != null)
        {
            if (IsNestingNode(current)) depth++;
            if (current is MethodDeclarationSyntax or LocalFunctionStatementSyntax) break;
            current = current.Parent;
        }
        return depth + 1;
    }
}

sealed class LongParameterListRule : IRule
{
    readonly ScanConfig _config;
    public LongParameterListRule(ScanConfig config) => _config = config;

    public string Id => "OP003";
    public RuleCategory Category => RuleCategory.Opinionated;
    public bool AppliesTo(FileConfig _) => true;

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig _)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var count = method.ParameterList.Parameters.Count;
            if (count <= _config.MaxParameters) continue;

            var loc = method.Identifier.GetLocation().GetLineSpan();
            diagnostics.Add(new(filePath,
                loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                $"Method '{method.Identifier.Text}' has {count} parameters (max {_config.MaxParameters}). Consider a parameter object.",
                Severity.Warning));
        }

        return diagnostics;
    }
}

sealed class MagicNumberRule : IRule
{
    readonly ScanConfig _config;
    public MagicNumberRule(ScanConfig config) => _config = config;

    public string Id => "OP004";
    public RuleCategory Category => RuleCategory.Opinionated;
    public bool AppliesTo(FileConfig _) => _config.FlagMagicNumbers;

    static readonly HashSet<string> AllowedValues = new(StringComparer.Ordinal)
        { "0", "1", "-1", "2", "100", "1000" };

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig _)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!literal.IsKind(SyntaxKind.NumericLiteralExpression)) continue;
            var text = literal.Token.Text;
            if (AllowedValues.Contains(text)) continue;
            if (IsInConstDeclaration(literal) || IsInAttributeArgument(literal) || IsInEnumMember(literal)) continue;

            var loc = literal.GetLocation().GetLineSpan();
            diagnostics.Add(new(filePath,
                loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                $"Magic number '{text}' — extract to a named constant.",
                Severity.Warning));
        }

        return diagnostics;
    }

    static bool IsInConstDeclaration(SyntaxNode node) =>
        node.Ancestors().OfType<LocalDeclarationStatementSyntax>().Any(l => l.Modifiers.Any(SyntaxKind.ConstKeyword)) ||
        node.Ancestors().OfType<FieldDeclarationSyntax>().Any(f => f.Modifiers.Any(SyntaxKind.ConstKeyword));

    static bool IsInAttributeArgument(SyntaxNode node) =>
        node.Ancestors().OfType<AttributeArgumentSyntax>().Any();

    static bool IsInEnumMember(SyntaxNode node) =>
        node.Ancestors().OfType<EnumMemberDeclarationSyntax>().Any();
}

sealed class BooleanParameterRule : IRule
{
    readonly ScanConfig _config;
    public BooleanParameterRule(ScanConfig config) => _config = config;

    public string Id => "OP005";
    public RuleCategory Category => RuleCategory.Opinionated;
    public bool AppliesTo(FileConfig _) => _config.FlagBooleanParameters;

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig _)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (method.Modifiers.Any(SyntaxKind.PrivateKeyword)) continue;

            foreach (var param in method.ParameterList.Parameters)
            {
                if (param.Type is not PredefinedTypeSyntax predefined) continue;
                if (!predefined.Keyword.IsKind(SyntaxKind.BoolKeyword)) continue;

                var loc = param.GetLocation().GetLineSpan();
                diagnostics.Add(new(filePath,
                    loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                    $"Boolean parameter '{param.Identifier.Text}' in '{method.Identifier.Text}' is a flag argument smell. Consider two methods or an enum/options type.",
                    Severity.Warning));
            }
        }

        return diagnostics;
    }
}

sealed class MissingCancellationTokenRule : IRule
{
    readonly ScanConfig _config;
    public MissingCancellationTokenRule(ScanConfig config) => _config = config;

    public string Id => "OP006";
    public RuleCategory Category => RuleCategory.Opinionated;
    public bool AppliesTo(FileConfig _) => _config.FlagMissingCancellationToken;

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig _)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!method.Modifiers.Any(SyntaxKind.AsyncKeyword)) continue;
            if (!IsPublicOrInternal(method)) continue;

            var returnType = method.ReturnType.ToString();
            if (!returnType.Contains("Task") && !returnType.Contains("ValueTask")) continue;

            var hasToken = method.ParameterList.Parameters
                .Any(p => p.Type?.ToString().Contains("CancellationToken") == true);

            if (!hasToken)
            {
                var loc = method.Identifier.GetLocation().GetLineSpan();
                diagnostics.Add(new(filePath,
                    loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                    $"Public async method '{method.Identifier.Text}' does not accept a CancellationToken. Consider adding one as the last parameter.",
                    Severity.Warning));
            }
        }

        return diagnostics;
    }

    static bool IsPublicOrInternal(MethodDeclarationSyntax method) =>
        method.Modifiers.Any(SyntaxKind.PublicKeyword) ||
        method.Modifiers.Any(SyntaxKind.InternalKeyword);
}
