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
    bool FlagMagicNumbers = true,
    bool FlagBooleanParameters = true,
    bool FlagMissingCancellationToken = true);

sealed class MagicNumberRule : IRule
{
    readonly ScanConfig _config;
    public MagicNumberRule(ScanConfig config) => _config = config;

    public string Id => "OP004";
    public RuleCategory Category => RuleCategory.Opinionated;
    public bool AppliesTo(FileConfig config) => _config.FlagMagicNumbers;

    static readonly HashSet<string> AllowedValues = new(StringComparer.Ordinal)
        { "0", "1", "-1", "2", "100", "1000" };

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit)
    {
        var filePath = unit.Path;
        var root = await unit.Tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!literal.IsKind(SyntaxKind.NumericLiteralExpression)) continue;
            var text = literal.Token.Text;
            if (AllowedValues.Contains(text)) continue;
            if (IsInConstDeclaration(literal) || IsInAttributeArgument(literal) || IsInEnumMember(literal)
                || IsInMemberInitializer(literal)) continue;

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

    // A literal that is the default value of a named member (field/property/parameter) is already
    // named by that member — e.g. `TokenCacheDuration { get; set; } = TimeSpan.FromMinutes(55)`.
    // Extracting it to a separate constant adds nothing, so it isn't a "magic number". The walk
    // stops at the first statement/body boundary so literals in method bodies still count.
    static bool IsInMemberInitializer(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case PropertyDeclarationSyntax:
                case FieldDeclarationSyntax:
                case ParameterSyntax:
                    return true;
                case StatementSyntax:
                case ArrowExpressionClauseSyntax:
                case AccessorDeclarationSyntax:
                    return false;
            }
        }
        return false;
    }
}

sealed class BooleanParameterRule : IRule
{
    readonly ScanConfig _config;
    public BooleanParameterRule(ScanConfig config) => _config = config;

    public string Id => "OP005";
    public RuleCategory Category => RuleCategory.Opinionated;
    public bool AppliesTo(FileConfig config) => _config.FlagBooleanParameters;

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit)
    {
        var filePath = unit.Path;
        var root = await unit.Tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (method.Modifiers.Any(SyntaxKind.PrivateKeyword)) continue;

            // An overridden or explicitly-implemented method did not choose its own signature —
            // the bool parameter was decided by the base type / interface (e.g. the canonical
            // `protected override void Dispose(bool disposing)`), so it isn't a flag-arg smell.
            if (method.Modifiers.Any(SyntaxKind.OverrideKeyword)) continue;
            if (method.ExplicitInterfaceSpecifier != null) continue;

            foreach (var param in method.ParameterList.Parameters)
            {
                if (param.Type is not PredefinedTypeSyntax predefined) continue;
                if (!predefined.Keyword.IsKind(SyntaxKind.BoolKeyword)) continue;
                // out/ref/in bool are not flag arguments (e.g. a TryXxx's `out bool`).
                if (param.Modifiers.Any(m => m.IsKind(SyntaxKind.OutKeyword)
                        || m.IsKind(SyntaxKind.RefKeyword) || m.IsKind(SyntaxKind.InKeyword)))
                    continue;

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
    public bool AppliesTo(FileConfig config) => _config.FlagMissingCancellationToken;

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit)
    {
        var filePath = unit.Path;
        var root = await unit.Tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!method.Modifiers.Any(SyntaxKind.AsyncKeyword)) continue;
            if (!IsPublicOrInternal(method)) continue;
            if (SignatureIsFrameworkConstrained(method) || HasAmbientCancellation(method)) continue;

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

    // The author can't add a CancellationToken parameter to a signature they don't own: an
    // override or explicit interface implementation, or a contract that takes no parameters
    // (IAsyncDisposable.DisposeAsync, an async iterator's MoveNextAsync).
    static bool SignatureIsFrameworkConstrained(MethodDeclarationSyntax method) =>
        method.Modifiers.Any(SyntaxKind.OverrideKeyword)
        || method.ExplicitInterfaceSpecifier != null
        || method.Identifier.Text is "DisposeAsync" or "MoveNextAsync";

    // A method already handed a context that exposes a cancellation token (HttpContext via
    // RequestAborted, an ASP.NET filter context via HttpContext.RequestAborted) doesn't need a
    // separate CancellationToken parameter — e.g. middleware InvokeAsync(HttpContext).
    static bool HasAmbientCancellation(MethodDeclarationSyntax method) =>
        method.ParameterList.Parameters.Any(p =>
        {
            var t = p.Type?.ToString() ?? "";
            return t.Contains("HttpContext")
                || t.EndsWith("FilterContext", StringComparison.Ordinal)
                || t.EndsWith("ExecutingContext", StringComparison.Ordinal)
                || t.EndsWith("ExecutedContext", StringComparison.Ordinal)
                || t.EndsWith("ExceptionContext", StringComparison.Ordinal);
        });
}
