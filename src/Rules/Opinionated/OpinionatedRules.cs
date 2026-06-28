using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsLint.Rules.Opinionated;

// cslint's opinionated tier covers categorical, pass/fail *pattern* smells only
// (magic numbers, flag arguments, missing CancellationToken). Quantitative *metric*
// gates — method length, cyclomatic complexity, nesting depth, parameter count — were
// removed in v4.0.0 and are now owned by the `codemetrics` tool, which measures them more
// rigorously. Keep this file metric-free.
record ScanConfig(
    bool FlagMagicNumbers           = true,
    bool FlagBooleanParameters      = true,
    bool FlagMissingCancellationToken = true);

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
