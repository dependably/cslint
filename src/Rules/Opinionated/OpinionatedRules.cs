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

    // Numeric values that are universally understood and need no named constant.
    // Matching is numeric (not textual) so that float/decimal spellings such as 1.0 or 1f
    // are treated as equivalent to their integer counterparts.
    static readonly HashSet<double> AllowedNumerics = new() { 0.0, 1.0, -1.0, 2.0, 100.0, 1000.0 };

    static bool IsAllowedValue(string tokenText)
    {
        // Strip the standard C# numeric-literal suffixes before parsing.
        var s = tokenText.TrimEnd('f', 'F', 'd', 'D', 'm', 'M', 'l', 'L', 'u', 'U');
        return double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
            && AllowedNumerics.Contains(v);
    }

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit)
    {
        var filePath = unit.Path;
        var root = await unit.Tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!literal.IsKind(SyntaxKind.NumericLiteralExpression)) continue;
            var text = literal.Token.Text;
            if (IsAllowedValue(text)) continue;
            if (IsInConstDeclaration(literal) || IsInAttributeArgument(literal) || IsInEnumMember(literal)
                || IsInMemberInitializer(literal) || IsInNamedArgument(literal)) continue;

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

    // A literal passed as a named argument is already self-documenting through the parameter name
    // — e.g. `BCrypt.HashPassword(password, workFactor: 12)`. Only the NEAREST enclosing
    // ArgumentSyntax is consulted (matching the boundary-stopping precedent of IsInMemberInitializer)
    // so that positional literals in nested calls — `Outer(policy: Inner(3, 500))` — are still
    // flagged; only the literal that IS the named-arg value (i.e. its direct parent argument has a
    // NameColon) is suppressed.
    static bool IsInNamedArgument(SyntaxNode node) =>
        node.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault()?.NameColon != null;
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
            if (!DeclaresOwnSignature(method)) continue;

            foreach (var param in method.ParameterList.Parameters)
            {
                if (!IsFlagParameter(param)) continue;

                var loc = param.GetLocation().GetLineSpan();
                diagnostics.Add(new(filePath,
                    loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                    $"Boolean parameter '{param.Identifier.Text}' in '{method.Identifier.Text}' is a flag argument smell. Consider two methods or an enum/options type.",
                    Severity.Warning));
            }
        }

        return diagnostics;
    }

    // A private method, an override, or an explicit interface implementation did not freely choose
    // its own signature — the bool parameter was decided by the base type / interface (e.g. the
    // canonical `protected override void Dispose(bool disposing)`) — so it isn't a flag-arg smell.
    // A method with no accessibility modifier in a class or struct is implicitly private and is also
    // excluded; however, a method with no modifier in an interface is implicitly public API surface
    // and is still flagged.
    static bool DeclaresOwnSignature(MethodDeclarationSyntax method)
    {
        if (method.Modifiers.Any(SyntaxKind.PrivateKeyword)) return false;
        if (method.Modifiers.Any(SyntaxKind.OverrideKeyword)) return false;
        if (method.ExplicitInterfaceSpecifier != null) return false;

        // A method with no accessibility modifier outside an interface is implicitly private.
        bool hasAccessibilityModifier = method.Modifiers.Any(m =>
            m.IsKind(SyntaxKind.PublicKeyword)
            || m.IsKind(SyntaxKind.InternalKeyword)
            || m.IsKind(SyntaxKind.ProtectedKeyword));
        if (!hasAccessibilityModifier && method.Parent is not InterfaceDeclarationSyntax)
            return false;

        return true;
    }

    // A by-value `bool` parameter is the flag-arg smell; out/ref/in bool is not (e.g. a TryXxx's
    // `out bool`).
    static bool IsFlagParameter(ParameterSyntax param) =>
        param.Type is PredefinedTypeSyntax predefined
        && predefined.Keyword.IsKind(SyntaxKind.BoolKeyword)
        && !param.Modifiers.Any(m => m.IsKind(SyntaxKind.OutKeyword)
            || m.IsKind(SyntaxKind.RefKeyword) || m.IsKind(SyntaxKind.InKeyword));
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
