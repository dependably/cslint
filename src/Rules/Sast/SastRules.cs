using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsLint.Rules.Sast;

sealed class EmptyCatchRule : IRule
{
    public string Id => "SAST001";
    public RuleCategory Category => RuleCategory.Sast;
    public bool AppliesTo(FileConfig config) => true;

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit)
    {
        var filePath = unit.Path;
        var root = await unit.Tree.GetRootAsync();
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

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit)
    {
        var filePath = unit.Path;
        if (TestFileHeuristic.IsTestFile(filePath)) return [];

        var root = await unit.Tree.GetRootAsync();
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

}

/// <summary>
/// Shared "is this a test file?" heuristic. Test fixtures legitimately contain console output
/// (SAST002) and credential-shaped literals (SAST004), so those rules skip them rather than
/// drown real code in noise.
/// </summary>
static class TestFileHeuristic
{
    internal static bool IsTestFile(string path)
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

    // Well-known ADO.NET / ORM command types whose constructor arguments are SQL strings.
    static readonly HashSet<string> SqlCommandTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SqlCommand", "SqliteCommand", "NpgsqlCommand",
        "MySqlCommand", "OracleCommand", "OdbcCommand", "OleDbCommand",
    };

    // Falls back to a suffix heuristic so that less-common drivers (e.g. SnowflakeCommand,
    // BigQueryCommand) are still caught without requiring an exhaustive allow-list.
    static bool IsSqlCommandType(string typeName) =>
        SqlCommandTypes.Contains(typeName) ||
        typeName.EndsWith("Command", StringComparison.OrdinalIgnoreCase) ||
        typeName.EndsWith("DataAdapter", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit)
    {
        var filePath = unit.Path;
        var root = await unit.Tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        // 1. Method invocations: db.ExecuteSqlRaw($"...{id}"), cmd.ExecuteReader(), etc.
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

        // 2. Constructor calls: new SqlCommand($"...{id}") and new($"...{id}") (implicit form).
        //    Both derive from BaseObjectCreationExpressionSyntax; ArgumentList is nullable there.
        foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
        {
            var typeName = ResolveCreatedTypeName(creation);
            if (typeName == null || !IsSqlCommandType(typeName)) continue;

            var args = creation.ArgumentList?.Arguments;
            if (args == null || args.Value.Count == 0) continue;

            if (args.Value[0].Expression is InterpolatedStringExpressionSyntax interpolated &&
                interpolated.Contents.OfType<InterpolationSyntax>().Any())
            {
                var loc = creation.GetLocation().GetLineSpan();
                diagnostics.Add(new(filePath,
                    loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                    $"Possible SQL injection: interpolated string passed to {typeName} constructor. Use parameterised queries.",
                    Severity.Error));
            }
        }

        // 3. CommandText assignment: cmd.CommandText = $"...{id}"
        foreach (var assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is not MemberAccessExpressionSyntax mem) continue;
            if (!mem.Name.Identifier.Text.Equals("CommandText", StringComparison.Ordinal)) continue;

            if (assignment.Right is InterpolatedStringExpressionSyntax interpolated &&
                interpolated.Contents.OfType<InterpolationSyntax>().Any())
            {
                var loc = assignment.GetLocation().GetLineSpan();
                diagnostics.Add(new(filePath,
                    loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                    "Possible SQL injection: interpolated string assigned to CommandText. Use parameterised queries.",
                    Severity.Error));
            }
        }

        return diagnostics;
    }

    // Returns the simple type name for explicit new T(...) or resolves it from the declared
    // variable type for implicit new(...).
    static string? ResolveCreatedTypeName(BaseObjectCreationExpressionSyntax creation)
    {
        if (creation is ObjectCreationExpressionSyntax explicit_)
        {
            // new SqlCommand(...) → type is the IdentifierNameSyntax / QualifiedNameSyntax
            return explicit_.Type switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                QualifiedNameSyntax q => q.Right.Identifier.Text,
                _ => null
            };
        }

        // ImplicitObjectCreationExpressionSyntax: new(...) — resolve from the declared variable.
        // The parent chain is: EqualsValueClauseSyntax → VariableDeclaratorSyntax → VariableDeclarationSyntax.
        if (creation.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax decl } })
        {
            return decl.Type switch
            {
                IdentifierNameSyntax id => id.Identifier.Text,
                QualifiedNameSyntax q => q.Right.Identifier.Text,
                _ => null
            };
        }

        return null;
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

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit)
    {
        var filePath = unit.Path;
        // Test fixtures legitimately embed credential-shaped literals (fake connection strings,
        // tokens, api-keys); skip them like SAST002 does, to avoid error-level noise on test code.
        if (TestFileHeuristic.IsTestFile(filePath)) return [];

        var root = await unit.Tree.GetRootAsync();
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
    // Above this length a single PascalCase letter-only word is too long to be a plausible
    // scheme/enum identifier, so we stop treating it as obviously-non-secret.
    const int MaxIdentifierWordLength = 40;

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
        if (v.Length <= MaxIdentifierWordLength && char.IsUpper(v[0]) && v.All(char.IsLetter)) return true;
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

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit)
    {
        var filePath = unit.Path;
        var root = await unit.Tree.GetRootAsync();
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
        var methodName = GetAsyncChainMethodName(invocation);
        if (methodName == null) return;
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
        var methodName = GetAsyncChainMethodName(invocation);
        if (methodName == null) return;

        var loc = assignment.GetLocation().GetLineSpan();
        diagnostics.Add(new(filePath, loc.StartLinePosition.Line + 1, 1, Id,
            $"Fire-and-forget via discard: '{methodName}' result discarded without awaiting; exceptions are silently lost. Await the task or observe it explicitly.",
            Severity.Warning));
    }

    static bool IsInsideAwaitExpression(SyntaxNode node) =>
        node.Parent is AwaitExpressionSyntax ||
        node.Parent is ExpressionStatementSyntax p &&
        p.Parent?.DescendantNodes().OfType<AwaitExpressionSyntax>().Any() == true;

    // Returns the name of the inner-most *Async method in the invocation chain, or null.
    // Exception-transparent wrappers such as .ConfigureAwait are unwrapped so that
    // 'DoWorkAsync().ConfigureAwait(false)' is still recognised as fire-and-forget.
    // Explicit fault-handling continuations (.ContinueWith) are treated as deliberate
    // observation of the task and are intentionally left un-flagged.
    static string? GetAsyncChainMethodName(InvocationExpressionSyntax invocation)
    {
        var current = invocation;
        while (true)
        {
            var name = GetMethodName(current);
            if (name != null && name.EndsWith("Async", StringComparison.Ordinal))
                return name;

            // Only keep unwrapping through exception-transparent wrappers.
            if (name != null && ExceptionTransparentWrappers.Contains(name) &&
                current.Expression is MemberAccessExpressionSyntax member &&
                member.Expression is InvocationExpressionSyntax inner)
            {
                current = inner;
                continue;
            }

            return null;
        }
    }

    static readonly HashSet<string> ExceptionTransparentWrappers = new(StringComparer.Ordinal)
    {
        "ConfigureAwait",
    };

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

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit)
    {
        var filePath = unit.Path;
        var source = unit.Text;
        var root = await unit.Tree.GetRootAsync();
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
                || prev.StartsWith('*')   // middle/last line of a block comment
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

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit)
    {
        var filePath = unit.Path;
        var root = await unit.Tree.GetRootAsync();
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

    // Determines whether the Thread.Sleep runs on an async execution context. The relevant
    // context is the NEAREST enclosing function-like node, not just any ancestor method: a
    // lambda / anonymous method / local function establishes its own execution context. A
    // synchronous lambda or local function nested inside an async method runs synchronously
    // (e.g. Task.Run(() => Thread.Sleep(...))), so blocking there is acceptable — while an
    // async lambda or async local function inside a sync method IS an async context.
    static bool IsInsideAsyncMethod(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax lambda:
                    // covers parenthesized/simple lambdas and `delegate {}` anonymous methods
                    return lambda.Modifiers.Any(SyntaxKind.AsyncKeyword)
                        || lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);
                case LocalFunctionStatementSyntax local:
                    return local.Modifiers.Any(SyntaxKind.AsyncKeyword);
                case MethodDeclarationSyntax method:
                    return method.Modifiers.Any(SyntaxKind.AsyncKeyword);
            }
        }
        return false;
    }
}

sealed class DynamicUsageRule : IRule
{
    public string Id => "SAST008";
    public RuleCategory Category => RuleCategory.Sast;
    public bool AppliesTo(FileConfig config) => true;

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit)
    {
        var filePath = unit.Path;
        var root = await unit.Tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var node in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (node.Identifier.Text != "dynamic") continue;

            if (IsInTypePosition(node))
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

    // Returns true when the 'dynamic' identifier appears in a type position — i.e. it is being
    // used as a type annotation rather than as a plain identifier/variable reference.
    //
    // Each arm is constrained to the specific child slot that carries the type, so that an
    // identifier merely NAMED "dynamic" in a non-type slot is never falsely flagged.
    //
    // Parent kinds and the type slot checked:
    //   VariableDeclarationSyntax  — .Type:            dynamic x = …
    //   ParameterSyntax            — .Type:            void M(dynamic d)   (name is a SyntaxToken)
    //   PropertyDeclarationSyntax  — .Type:            dynamic Prop { … }  (name is a SyntaxToken)
    //   MethodDeclarationSyntax    — .ReturnType:      dynamic M()         (name is a SyntaxToken)
    //   CastExpressionSyntax       — .Type:            (dynamic)obj        (not the operand)
    //   ArrayTypeSyntax            — .ElementType:     dynamic[]
    //   TypeArgumentListSyntax     — .Arguments:       List<dynamic>
    //   ForEachStatementSyntax     — .Type:            foreach (dynamic d in …) (not the collection)
    //   BinaryExpressionSyntax     — .Right + AsExpr:  obj as dynamic      (not the left operand)
    //
    // ReturnStatementSyntax is intentionally absent: `return dynamic;` has dynamic as an expression,
    // not a type annotation, so including it would produce false positives on variables named dynamic.
    static bool IsInTypePosition(IdentifierNameSyntax node) =>
        node.Parent switch
        {
            VariableDeclarationSyntax v => v.Type == node,
            ParameterSyntax p => p.Type == node,
            PropertyDeclarationSyntax p => p.Type == node,
            MethodDeclarationSyntax m => m.ReturnType == node,
            CastExpressionSyntax c => c.Type == node,
            ArrayTypeSyntax a => a.ElementType == node,
            TypeArgumentListSyntax t => t.Arguments.Contains(node),
            ForEachStatementSyntax fe => fe.Type == node,
            BinaryExpressionSyntax bin => bin.IsKind(SyntaxKind.AsExpression) && bin.Right == node,
            _ => false
        };
}
