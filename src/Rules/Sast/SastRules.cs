using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsLint.Rules.Sast;

sealed class EmptyCatchRule : IRule
{
    public string Id => "SAST001";
    public RuleCategory Category => RuleCategory.Sast;
    public bool AppliesTo(FileConfig config) => true;

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var clause in root.DescendantNodes().OfType<CatchClauseSyntax>())
        {
            if (clause.Block.Statements.Count == 0)
            {
                // Only a bare, unexplained catch-all is a silent swallow. A *typed* catch or an
                // exception *filter* (`when`) is a deliberate, scoped decision, and a comment in
                // the body documents the rationale — none of those are the bug this rule targets.
                bool isBareCatchAll = clause.Declaration == null && clause.Filter == null;
                if (isBareCatchAll && !HasComment(clause.Block))
                {
                    var loc = clause.CatchKeyword.GetLocation().GetLineSpan();
                    diagnostics.Add(new(filePath,
                        loc.StartLinePosition.Line + 1, 1, Id,
                        "Empty catch block silently swallows exceptions.",
                        Severity.Error));
                }
            }
            else if (IsOnlyDiscard(clause))
            {
                var loc = clause.CatchKeyword.GetLocation().GetLineSpan();
                diagnostics.Add(new(filePath,
                    loc.StartLinePosition.Line + 1, 1, Id,
                    "Catch block only discards the exception with no logging or rethrow.",
                    Severity.Warning));
            }
        }

        return diagnostics;
    }

    // True if the block carries any comment (between its braces) documenting intent.
    static bool HasComment(BlockSyntax block)
    {
        static bool IsComment(SyntaxTrivia t) =>
            t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia);
        return block.OpenBraceToken.TrailingTrivia.Any(IsComment)
            || block.CloseBraceToken.LeadingTrivia.Any(IsComment)
            || block.DescendantTrivia().Any(IsComment);
    }

    static bool IsOnlyDiscard(CatchClauseSyntax clause)
    {
        if (clause.Block.Statements.Count != 1) return false;
        var stmt = clause.Block.Statements[0];
        return stmt is ExpressionStatementSyntax expr &&
               expr.Expression is AssignmentExpressionSyntax assign &&
               assign.Left is IdentifierNameSyntax id &&
               id.Identifier.Text == "_";
    }
}

sealed class ConsoleOutputRule : IRule
{
    public string Id => "SAST002";
    public RuleCategory Category => RuleCategory.Sast;
    public bool AppliesTo(FileConfig config) => true;

    static readonly HashSet<string> Methods = new(StringComparer.Ordinal) { "WriteLine", "Write", "Error", "Out" };
    static readonly HashSet<string> Classes = new(StringComparer.Ordinal) { "Console", "Debug", "Trace" };

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        if (IsTestFile(filePath)) return [];

        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax mem) continue;
            var methodName = mem.Name.Identifier.Text;
            if (!Methods.Contains(methodName)) continue;

            string className;
            if (mem.Expression is IdentifierNameSyntax classId)
                className = classId.Identifier.Text;
            else if (mem.Expression is MemberAccessExpressionSyntax outer)
                className = outer.Name.Identifier.Text;
            else continue;

            if (!Classes.Contains(className)) continue;

            var loc = invocation.GetLocation().GetLineSpan();
            diagnostics.Add(new(filePath,
                loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                $"{className}.{methodName}() left in production code path.",
                Severity.Warning));
        }

        return diagnostics;
    }

    static bool IsTestFile(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith("Test", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith("Spec", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(Path.DirectorySeparatorChar + "Tests" + Path.DirectorySeparatorChar) ||
               path.Contains(Path.DirectorySeparatorChar + "Specs" + Path.DirectorySeparatorChar);
    }
}

sealed class SqlInjectionRule : IRule
{
    public string Id => "SAST003";
    public RuleCategory Category => RuleCategory.Sast;
    public bool AppliesTo(FileConfig config) => true;

    static readonly HashSet<string> DangerousMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "ExecuteSqlRaw", "ExecuteSqlRawAsync", "FromSqlRaw", "FromSqlRawAsync",
        "ExecuteNonQuery", "ExecuteScalar", "ExecuteReader",
        "ExecuteNonQueryAsync", "ExecuteScalarAsync", "ExecuteReaderAsync",
        "CreateCommand",
    };

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var methodName = GetMethodName(invocation);
            if (methodName == null || !DangerousMethods.Contains(methodName)) continue;

            var args = invocation.ArgumentList.Arguments;
            if (args.Count == 0) continue;

            if (args[0].Expression is InterpolatedStringExpressionSyntax interpolated &&
                interpolated.Contents.OfType<InterpolationSyntax>().Any())
            {
                var loc = invocation.GetLocation().GetLineSpan();
                diagnostics.Add(new(filePath,
                    loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                    $"Possible SQL injection: interpolated string passed to {methodName}(). Use parameterised queries.",
                    Severity.Error));
            }
        }

        return diagnostics;
    }

    static string? GetMethodName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => null
        };
}

sealed class HardcodedSecretRule : IRule
{
    public string Id => "SAST004";
    public RuleCategory Category => RuleCategory.Sast;
    public bool AppliesTo(FileConfig config) => true;

    static readonly string[] SecretKeywords =
        ["password", "passwd", "secret", "apikey", "api_key", "token", "privatekey", "private_key", "connectionstring"];

    static readonly string[] PlaceholderValues =
        ["changeme", "password", "secret", "admin", "test", "yourdomain.com", "example.com", "todo", "fixme", "xxx",
         "your-secret-here", "your_secret_here"];

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            CheckAssignment(filePath, assignment, diagnostics);

        foreach (var init in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            CheckDeclarator(filePath, init, diagnostics);

        return diagnostics;
    }

    void CheckAssignment(string filePath, AssignmentExpressionSyntax assignment, List<Diagnostic> diagnostics)
    {
        if (assignment.Right is not LiteralExpressionSyntax lit) return;
        if (!lit.IsKind(SyntaxKind.StringLiteralExpression)) return;

        var lhsName = GetAssigneeName(assignment.Left)?.ToLowerInvariant();
        if (lhsName == null) return;

        var value = lit.Token.ValueText;
        var loc = assignment.GetLocation().GetLineSpan();

        if (SecretKeywords.Any(kw => lhsName.Contains(kw)) && value.Length > 0 && !IsEmpty(value)
            && !IsLikelyNonSecretValue(value))
        {
            diagnostics.Add(new(filePath, loc.StartLinePosition.Line + 1, 1, Id,
                $"Possible hardcoded credential in '{GetAssigneeName(assignment.Left)}'.", Severity.Error));
        }
        else if (PlaceholderValues.Any(p => value.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add(new(filePath, loc.StartLinePosition.Line + 1, 1, Id,
                $"Placeholder value '{value}' left in source.", Severity.Warning));
        }
    }

    void CheckDeclarator(string filePath, VariableDeclaratorSyntax init, List<Diagnostic> diagnostics)
    {
        if (init.Initializer?.Value is not LiteralExpressionSyntax lit) return;
        if (!lit.IsKind(SyntaxKind.StringLiteralExpression)) return;

        var name = init.Identifier.Text.ToLowerInvariant();
        var value = lit.Token.ValueText;

        if (SecretKeywords.Any(kw => name.Contains(kw)) && value.Length > 0 && !IsEmpty(value)
            && !IsLikelyNonSecretValue(value))
        {
            var loc = init.GetLocation().GetLineSpan();
            diagnostics.Add(new(filePath, loc.StartLinePosition.Line + 1, 1, Id,
                $"Possible hardcoded credential in variable '{init.Identifier.Text}'.", Severity.Error));
        }
    }

    static bool IsEmpty(string v) => v.Replace("*", "").Replace(" ", "").Length == 0;

    // Filters out values that a token/key/secret-named constant commonly holds but which are
    // plainly not credentials: numeric defaults ("1000"), permission scopes / URIs / paths
    // ("tokens:manage_own"), and plain identifier words used as scheme names ("ApiToken").
    // A real secret carries entropy — digits mixed with symbols, base64/hex — and survives this.
    static bool IsLikelyNonSecretValue(string value)
    {
        var v = value.Trim();
        if (v.Length == 0) return true;
        if (v.All(c => char.IsDigit(c) || c is '.' or '-' or '_')) return true;     // numeric / versiony
        if (v.Contains(':') || v.Contains('/') || v.Contains(' ')) return true;     // scope / URI / path
        // A lowercase dotted identifier — reverse-DNS event types / config keys ("tenant.token.create").
        if (v.Contains('.') && v.All(c => (char.IsLetterOrDigit(c) && !char.IsUpper(c)) || c is '.' or '-' or '_'))
            return true;
        // A bare PascalCase word with no digits or symbols — a scheme/enum identifier, not a key.
        if (v.Length <= 40 && char.IsUpper(v[0]) && v.All(char.IsLetter)) return true;
        return false;
    }

    static string? GetAssigneeName(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
        _ => null
    };
}

sealed class FireAndForgetRule : IRule
{
    public string Id => "SAST005";
    public RuleCategory Category => RuleCategory.Sast;
    public bool AppliesTo(FileConfig config) => true;

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var stmt in root.DescendantNodes().OfType<ExpressionStatementSyntax>())
            CheckUnawaited(filePath, stmt, diagnostics);

        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            CheckDiscardedAsync(filePath, assignment, diagnostics);

        return diagnostics;
    }

    void CheckUnawaited(string filePath, ExpressionStatementSyntax stmt, List<Diagnostic> diagnostics)
    {
        if (stmt.Expression is not InvocationExpressionSyntax invocation) return;
        var methodName = GetMethodName(invocation);
        if (methodName == null || !methodName.EndsWith("Async", StringComparison.Ordinal)) return;
        if (IsInsideAwaitExpression(stmt)) return;

        var loc = stmt.GetLocation().GetLineSpan();
        diagnostics.Add(new(filePath, loc.StartLinePosition.Line + 1, 1, Id,
            $"Fire-and-forget: '{methodName}' result not awaited. Unhandled exceptions will be silently lost.",
            Severity.Error));
    }

    void CheckDiscardedAsync(string filePath, AssignmentExpressionSyntax assignment, List<Diagnostic> diagnostics)
    {
        if (assignment.Left is not IdentifierNameSyntax id || id.Identifier.Text != "_") return;
        if (assignment.Right is not InvocationExpressionSyntax invocation) return;
        var methodName = GetMethodName(invocation);
        if (methodName == null || !methodName.EndsWith("Async", StringComparison.Ordinal)) return;

        var loc = assignment.GetLocation().GetLineSpan();
        diagnostics.Add(new(filePath, loc.StartLinePosition.Line + 1, 1, Id,
            $"Fire-and-forget via discard: '{methodName}' exceptions discarded. Consider .ConfigureAwait or explicit error handling.",
            Severity.Warning));
    }

    static bool IsInsideAwaitExpression(SyntaxNode node) =>
        node.Parent is AwaitExpressionSyntax ||
        node.Parent is ExpressionStatementSyntax p &&
        p.Parent?.DescendantNodes().OfType<AwaitExpressionSyntax>().Any() == true;

    static string? GetMethodName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => null
        };
}

sealed class PragmaDisableRule : IRule
{
    public string Id => "SAST006";
    public RuleCategory Category => RuleCategory.Sast;
    public bool AppliesTo(FileConfig config) => true;

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var trivia in root.DescendantTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.PragmaWarningDirectiveTrivia)) continue;
            var pragma = (PragmaWarningDirectiveTriviaSyntax)trivia.GetStructure()!;
            if (!pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword)) continue;

            var lineNum = trivia.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

            if (!HasJustification(source, lineNum))
            {
                diagnostics.Add(new(filePath, lineNum, 1, Id,
                    "#pragma warning disable without a justification comment explaining why the suppression is necessary.",
                    Severity.Warning));
            }
        }

        return diagnostics;
    }

    // A justification may sit on the same line as the pragma OR on the line(s) directly above it
    // — both are common conventions. Only the same-line case enforces a minimum length; a
    // dedicated comment line above the pragma is taken as deliberate documentation.
    static bool HasJustification(string source, int lineNum)
    {
        var lines = source.Split('\n');
        if (lineNum < 1 || lineNum - 1 >= lines.Length) return false;

        if (HasTrailingComment(lines[lineNum - 1])) return true;

        for (int i = lineNum - 2; i >= 0; i--)
        {
            var prev = lines[i].Trim();
            if (prev.Length == 0) continue; // skip blank lines between the comment and the pragma
            return prev.StartsWith("//", StringComparison.Ordinal)
                || prev.StartsWith("/*", StringComparison.Ordinal)
                || prev.StartsWith("*", StringComparison.Ordinal)   // middle/last line of a block comment
                || prev.EndsWith("*/", StringComparison.Ordinal);
        }
        return false;
    }

    static bool HasTrailingComment(string line)
    {
        var commentIdx = line.IndexOf("//", StringComparison.Ordinal);
        if (commentIdx < 0) return false;
        const int minJustificationLength = 5;
        const int commentMarkerLength = 2; // "//"
        return line[(commentIdx + commentMarkerLength)..].Trim().Length > minJustificationLength;
    }
}

sealed class ThreadSleepInAsyncRule : IRule
{
    public string Id => "SAST007";
    public RuleCategory Category => RuleCategory.Sast;
    public bool AppliesTo(FileConfig config) => true;

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax mem) continue;
            if (mem.Name.Identifier.Text != "Sleep") continue;
            if (mem.Expression is not IdentifierNameSyntax cls || cls.Identifier.Text != "Thread") continue;
            if (!IsInsideAsyncMethod(invocation)) continue;

            var loc = invocation.GetLocation().GetLineSpan();
            diagnostics.Add(new(filePath,
                loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                "Thread.Sleep() inside an async method blocks the thread. Use 'await Task.Delay()' instead.",
                Severity.Error));
        }

        return diagnostics;
    }

    static bool IsInsideAsyncMethod(SyntaxNode node) =>
        node.Ancestors().OfType<MethodDeclarationSyntax>()
            .Any(m => m.Modifiers.Any(SyntaxKind.AsyncKeyword));
}

sealed class DynamicUsageRule : IRule
{
    public string Id => "SAST008";
    public RuleCategory Category => RuleCategory.Sast;
    public bool AppliesTo(FileConfig config) => true;

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var node in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (node.Identifier.Text != "dynamic") continue;

            if (node.Parent is VariableDeclarationSyntax or ParameterSyntax or
                               PropertyDeclarationSyntax or ReturnStatementSyntax or
                               MethodDeclarationSyntax)
            {
                var loc = node.GetLocation().GetLineSpan();
                diagnostics.Add(new(filePath,
                    loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                    "'dynamic' bypasses compile-time type safety. Prefer 'object', generics, or a well-typed interface.",
                    Severity.Warning));
            }
        }

        return diagnostics;
    }
}
