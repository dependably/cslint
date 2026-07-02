using CsLint.Rules.Sast;
using Xunit;

namespace CsLint.Tests;

public class SastRuleTests
{
    [Fact]
    public async Task SAST001_flags_empty_catch()
    {
        var diags = await T.Run(new EmptyCatchRule(),
            "class C { void M() { try { int.Parse(\"1\"); } catch { } } }");
        Assert.True(diags.Has("SAST001"));
    }

    [Fact]
    public async Task SAST001_flags_discard_only_catch()
    {
        var diags = await T.Run(new EmptyCatchRule(),
            "class C { void M() { try { } catch (System.Exception e) { _ = e; } } }");
        Assert.True(diags.Has("SAST001"));
    }

    [Fact]
    public async Task SAST001_clean_when_catch_handles()
    {
        var diags = await T.Run(new EmptyCatchRule(),
            "class C { void M() { try { } catch (System.Exception e) { System.Console.WriteLine(e); } } }");
        Assert.False(diags.Has("SAST001"));
    }

    // Regression: a typed catch, an exception filter, or a documented (commented) catch is a
    // deliberate scoped decision — not the silent catch-all this rule targets.
    [Fact]
    public async Task SAST001_ignores_typed_empty_catch()
    {
        var diags = await T.Run(new EmptyCatchRule(),
            "class C { void M() { try { } catch (System.FormatException) { } } }");
        Assert.False(diags.Has("SAST001"));
    }

    [Fact]
    public async Task SAST001_ignores_filtered_empty_catch()
    {
        var diags = await T.Run(new EmptyCatchRule(),
            "class C { void M() { try { } catch (System.Exception e) when (e.Message.Length > 0) { } } }");
        Assert.False(diags.Has("SAST001"));
    }

    [Fact]
    public async Task SAST001_ignores_commented_catch_all()
    {
        var diags = await T.Run(new EmptyCatchRule(),
            "class C { void M() { try { } catch { /* best-effort; documented intent */ } } }");
        Assert.False(diags.Has("SAST001"));
    }

    // Regression (Roslyn 4.8 -> 4.14): stacked C# 13 constructs — here a `partial` property,
    // a `params ReadOnlySpan<T>` parameter, and an `allows ref struct` type constraint — broke
    // Roslyn 4.8's (C# 12 parser) error recovery. On 4.8 the parser skipped the malformed
    // constraint and dropped the method body's catch clause from the tree entirely, so SAST001
    // silently failed to fire on a real empty catch buried in modern syntax (verified: this
    // snippet returns MISSED on 4.8.0). The C# 13 parser in 4.14 keeps the tree intact and the
    // finding is reported. This test FAILS on 4.8.0 and passes on 4.14.0.
    [Fact]
    public async Task SAST001_fires_amid_csharp13_syntax()
    {
        var diags = await T.Run(new EmptyCatchRule(),
            "partial class C\n"
            + "{\n"
            + "    public partial string Name { get; }\n"
            + "    void Log<T>(params System.ReadOnlySpan<T> items) where T : allows ref struct\n"
            + "    {\n"
            + "        try { int.Parse(\"1\"); } catch { }\n"
            + "    }\n"
            + "}\n");
        Assert.True(diags.Has("SAST001"));
    }

    [Fact]
    public async Task SAST002_flags_console_writeline()
    {
        var diags = await T.Run(new ConsoleOutputRule(),
            "class C { void M() { System.Console.WriteLine(\"hi\"); } }");
        Assert.True(diags.Has("SAST002"));
    }

    [Fact]
    public async Task SAST002_ignores_test_files()
    {
        var path = T.WriteCs("class C { void M() { System.Console.WriteLine(1); } }",
            "_Tests.cs");
        try
        {
            var diags = await new ConsoleOutputRule().AnalyzeAsync(T.Unit(path));
            Assert.False(diags.Has("SAST002"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SAST003_flags_interpolated_sql()
    {
        var diags = await T.Run(new SqlInjectionRule(),
            "class C { void M(string id) { db.ExecuteSqlRaw($\"select {id}\"); } }");
        Assert.True(diags.Has("SAST003"));
    }

    [Fact]
    public async Task SAST003_clean_with_constant_sql()
    {
        var diags = await T.Run(new SqlInjectionRule(),
            "class C { void M() { db.ExecuteSqlRaw(\"select 1\"); } }");
        Assert.False(diags.Has("SAST003"));
    }

    // Regression: new SqlCommand($"...{id}") must be flagged — was missed because the rule
    // only examined InvocationExpressionSyntax, not ObjectCreationExpressionSyntax.
    [Fact]
    public async Task SAST003_flags_sqlcommand_constructor_with_interpolated_string()
    {
        var diags = await T.Run(new SqlInjectionRule(),
            "class C { void M(string userId) { var cmd = new SqlCommand($\"SELECT * FROM Users WHERE Id = {userId}\"); } }");
        Assert.True(diags.Has("SAST003"));
    }

    // Regression: implicit new($"...{id}") on a SqlCommand variable must also be flagged.
    [Fact]
    public async Task SAST003_flags_implicit_sqlcommand_constructor_with_interpolated_string()
    {
        var diags = await T.Run(new SqlInjectionRule(),
            "class C { void M(string userId) { SqlCommand cmd = new($\"DELETE FROM Users WHERE Id = {userId}\"); } }");
        Assert.True(diags.Has("SAST003"));
    }

    // Negative: constructor with a constant SQL string — no interpolation holes, must not flag.
    [Fact]
    public async Task SAST003_clean_sqlcommand_constructor_with_constant_string()
    {
        var diags = await T.Run(new SqlInjectionRule(),
            "class C { void M() { var cmd = new SqlCommand(\"SELECT 1\"); } }");
        Assert.False(diags.Has("SAST003"));
    }

    // Negative: a non-command constructor receiving an interpolated string must not flag
    // (e.g. ArgumentException, StringBuilder, etc.).
    [Fact]
    public async Task SAST003_clean_non_command_constructor_with_interpolated_string()
    {
        var diags = await T.Run(new SqlInjectionRule(),
            "class C { void M(string name) { throw new ArgumentException($\"bad value: {name}\"); } }");
        Assert.False(diags.Has("SAST003"));
    }

    // Regression: cmd.CommandText = $"...{id}" assignment must be flagged.
    [Fact]
    public async Task SAST003_flags_commandtext_interpolated_assignment()
    {
        var diags = await T.Run(new SqlInjectionRule(),
            "class C { void M(SqlCommand cmd, string userId) { cmd.CommandText = $\"SELECT * FROM Users WHERE Id = {userId}\"; } }");
        Assert.True(diags.Has("SAST003"));
    }

    // Negative: cmd.CommandText with a constant string must not flag.
    [Fact]
    public async Task SAST003_clean_commandtext_constant_assignment()
    {
        var diags = await T.Run(new SqlInjectionRule(),
            "class C { void M(SqlCommand cmd) { cmd.CommandText = \"SELECT 1\"; } }");
        Assert.False(diags.Has("SAST003"));
    }

    // Mixed partial-failure: a file containing both safe and unsafe patterns — only the unsafe
    // ones must be flagged, ensuring no false negatives or false positives bleed through.
    [Fact]
    public async Task SAST003_mixed_file_flags_only_unsafe_patterns()
    {
        const string code = """
            class C
            {
                void Safe(string userId)
                {
                    db.ExecuteSqlRaw("SELECT * FROM Users WHERE Id = {0}", userId);
                    var safe = new SqlCommand("SELECT 1");
                    SqlCommand safeCtor = new("SELECT 1");
                }

                void Unsafe(string userId)
                {
                    db.ExecuteSqlRaw($"SELECT * FROM Users WHERE Id = {userId}");
                    var cmd1 = new SqlCommand($"SELECT * FROM Users WHERE Id = {userId}");
                    SqlCommand cmd2 = new($"DELETE FROM Users WHERE Id = {userId}");
                    cmd1.CommandText = $"UPDATE Users SET Name='x' WHERE Id = {userId}";
                }
            }
            """;
        var diags = await T.Run(new SqlInjectionRule(), code);
        // 4 unsafe patterns must all be detected
        var sast003 = diags.Where(d => d.Rule == "SAST003").ToList();
        Assert.Equal(4, sast003.Count);
    }

    [Fact]
    public async Task SAST004_flags_hardcoded_secret_variable()
    {
        var diags = await T.Run(new HardcodedSecretRule(),
            "class C { void M() { string apiKey = \"sk_live_abc123\"; } }");
        Assert.True(diags.Has("SAST004"));
    }

    // Regression: test fixtures legitimately embed credential-shaped literals; skip them like SAST002.
    [Fact]
    public async Task SAST004_ignores_test_files()
    {
        var path = T.WriteCs("class C { void M() { string apiKey = \"sk_live_abc123\"; } }", "_Tests.cs");
        try
        {
            var diags = await new HardcodedSecretRule().AnalyzeAsync(T.Unit(path));
            Assert.False(diags.Has("SAST004"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SAST004_flags_secret_assignment()
    {
        var diags = await T.Run(new HardcodedSecretRule(),
            "class C { string password; void M() { password = \"hunter2!\"; } }");
        Assert.True(diags.Has("SAST004"));
    }

    [Fact]
    public async Task SAST004_flags_placeholder_value()
    {
        var diags = await T.Run(new HardcodedSecretRule(),
            "class C { string s; void M() { s = \"changeme\"; } }");
        Assert.True(diags.Has("SAST004"));
    }

    // Regression: token/key-named constants commonly hold non-credentials — a permission
    // scope, a numeric default, or a scheme identifier. None of these are secrets.
    [Theory]
    [InlineData("public const string ManageOwnTokens = \"tokens:manage_own\";")]   // scope
    [InlineData("public const string MaxActiveTokensPerTenant = \"1000\";")]        // numeric
    [InlineData("internal const string ApiTokenScheme = \"ApiToken\";")]            // scheme id
    [InlineData("internal const string NuGetApiKeyScheme = \"NuGetApiKey\";")]      // scheme id
    [InlineData("public const string TypeTokenCreate = \"tenant.token.create\";")]  // event type
    public async Task SAST004_does_not_flag_non_secret_constants(string member)
    {
        var diags = await T.Run(new HardcodedSecretRule(), $"class C {{ {member} }}");
        Assert.False(diags.Has("SAST004"));
    }

    [Fact]
    public async Task SAST005_flags_fire_and_forget()
    {
        var diags = await T.Run(new FireAndForgetRule(),
            "class C { void M() { DoWorkAsync(); } }");
        Assert.True(diags.Has("SAST005"));
    }

    [Fact]
    public async Task SAST005_flags_discarded_async()
    {
        var diags = await T.Run(new FireAndForgetRule(),
            "class C { void M() { _ = DoWorkAsync(); } }");
        Assert.True(diags.Has("SAST005"));
    }

    [Fact]
    public async Task SAST005_flags_chained_discarded_configureawait()
    {
        // GetMethodName returns "ConfigureAwait" (not *Async) for the outer call;
        // the chain must be unwrapped to still recognise the discarded async task.
        var diags = await T.Run(new FireAndForgetRule(),
            "class C { void M() { _ = DoWorkAsync().ConfigureAwait(false); } }");
        Assert.True(diags.Has("SAST005"));
    }

    [Fact]
    public async Task SAST005_flags_chained_statement_configureawait()
    {
        var diags = await T.Run(new FireAndForgetRule(),
            "class C { void M() { DoWorkAsync().ConfigureAwait(false); } }");
        Assert.True(diags.Has("SAST005"));
    }

    [Fact]
    public async Task SAST005_does_not_flag_explicit_continuation()
    {
        // ContinueWith is an explicit fault-observation continuation, not a
        // transparent wrapper — leave it un-flagged to avoid false positives.
        var diags = await T.Run(new FireAndForgetRule(),
            "class C { void M() { _ = DoWorkAsync().ContinueWith(t => { }); } }");
        Assert.False(diags.Has("SAST005"));
    }

    [Fact]
    public async Task SAST005_does_not_flag_non_async_chain()
    {
        var diags = await T.Run(new FireAndForgetRule(),
            "class C { void M() { _ = DoWork().ConfigureAwait(false); } }");
        Assert.False(diags.Has("SAST005"));
    }

    [Fact]
    public async Task SAST006_flags_unjustified_pragma()
    {
        var diags = await T.Run(new PragmaDisableRule(),
            "#pragma warning disable CS0168\nclass C { }\n");
        Assert.True(diags.Has("SAST006"));
    }

    [Fact]
    public async Task SAST006_clean_with_justification()
    {
        var diags = await T.Run(new PragmaDisableRule(),
            "#pragma warning disable CS0168 // intentionally unused during migration\nclass C { }\n");
        Assert.False(diags.Has("SAST006"));
    }

    // Regression: a justification comment on the line directly above the pragma counts too —
    // that is the common convention, not just a trailing same-line comment.
    [Fact]
    public async Task SAST006_clean_with_preceding_comment()
    {
        var diags = await T.Run(new PragmaDisableRule(),
            "// taint is a false positive here; the input never reaches the path\n"
            + "#pragma warning disable SCS0018\nclass C { }\n");
        Assert.False(diags.Has("SAST006"));
    }

    [Fact]
    public async Task SAST007_flags_thread_sleep_in_async()
    {
        var diags = await T.Run(new ThreadSleepInAsyncRule(),
            "using System.Threading; class C { async System.Threading.Tasks.Task M() { Thread.Sleep(10); } }");
        Assert.True(diags.Has("SAST007"));
    }

    [Fact]
    public async Task SAST007_clean_in_sync_method()
    {
        var diags = await T.Run(new ThreadSleepInAsyncRule(),
            "using System.Threading; class C { void M() { Thread.Sleep(10); } }");
        Assert.False(diags.Has("SAST007"));
    }

    // Regression (#4): a SYNCHRONOUS lambda inside an async method runs synchronously on its
    // own execution context (e.g. Task.Run(() => Thread.Sleep(...))), so blocking is acceptable.
    // The old ancestor walk (OfType<MethodDeclarationSyntax>) passed straight through the lambda
    // and flagged this as a false positive.
    [Fact]
    public async Task SAST007_clean_sync_lambda_in_async_method()
    {
        var diags = await T.Run(new ThreadSleepInAsyncRule(),
            "using System.Threading; using System.Threading.Tasks; "
            + "class C { async Task M() { await Task.Run(() => Thread.Sleep(10)); } }");
        Assert.False(diags.Has("SAST007"));
    }

    // Regression (#4): an ASYNC lambda IS an async context, even inside a non-async method.
    // The old walk never matched lambdas at all, making this a false negative.
    [Fact]
    public async Task SAST007_flags_async_lambda_in_sync_method()
    {
        var diags = await T.Run(new ThreadSleepInAsyncRule(),
            "using System; using System.Threading; using System.Threading.Tasks; "
            + "class C { void M() { Func<Task> f = async () => { Thread.Sleep(10); }; } }");
        Assert.True(diags.Has("SAST007"));
    }

    // Regression (#4): a synchronous LOCAL FUNCTION inside an async method runs synchronously;
    // the old walk climbed past it to the async method and produced a false positive.
    [Fact]
    public async Task SAST007_clean_sync_local_function_in_async_method()
    {
        var diags = await T.Run(new ThreadSleepInAsyncRule(),
            "using System.Threading; using System.Threading.Tasks; "
            + "class C { async Task M() { void Local() { Thread.Sleep(10); } Local(); await Task.Yield(); } }");
        Assert.False(diags.Has("SAST007"));
    }

    // Regression (#4): an async local function is an async context.
    [Fact]
    public async Task SAST007_flags_async_local_function()
    {
        var diags = await T.Run(new ThreadSleepInAsyncRule(),
            "using System.Threading; using System.Threading.Tasks; "
            + "class C { void M() { async Task Local() { Thread.Sleep(10); await Task.Yield(); } _ = Local(); } }");
        Assert.True(diags.Has("SAST007"));
    }

    [Fact]
    public async Task SAST008_flags_dynamic_local()
    {
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { void M() { dynamic x = 1; } }");
        Assert.True(diags.Has("SAST008"));
    }

    // Regression tests for missed type positions (Ticket #10)

    [Fact]
    public async Task SAST008_flags_dynamic_cast()
    {
        // (dynamic)obj — CastExpressionSyntax parent; previously not flagged
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { void M(object obj) { var x = (dynamic)obj; } }");
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_flags_dynamic_as_expression()
    {
        // obj as dynamic — BinaryExpressionSyntax (AsExpression) parent; previously not flagged
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { void M(object obj) { var x = obj as dynamic; } }");
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_flags_dynamic_type_argument()
    {
        // List<dynamic> — TypeArgumentListSyntax parent; previously not flagged
        var diags = await T.Run(new DynamicUsageRule(),
            "using System.Collections.Generic; class C { void M() { var x = new List<dynamic>(); } }");
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_flags_dynamic_array()
    {
        // dynamic[] — ArrayTypeSyntax parent; previously not flagged
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { void M() { dynamic[] arr = new dynamic[3]; } }");
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_flags_dynamic_foreach()
    {
        // foreach (dynamic d in ...) — ForEachStatementSyntax parent; previously not flagged
        var diags = await T.Run(new DynamicUsageRule(),
            "using System.Collections.Generic; class C { void M(IEnumerable<object> items) { foreach (dynamic d in items) { } } }");
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_clean_when_dynamic_is_variable_name_in_member_access()
    {
        // A local named "dynamic" used as a member access target should not be flagged —
        // its parent is MemberAccessExpressionSyntax, which is not a type position.
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { void M() { var dynamic = new System.Object(); var r = dynamic.ToString(); } }");
        Assert.False(diags.Has("SAST008"));
    }

    // False-positive slot-position regression tests (Ticket #10 follow-up)
    // These must NOT produce SAST008: the identifier named "dynamic" is NOT in a type position.

    [Fact]
    public async Task SAST008_clean_when_dynamic_is_cast_operand()
    {
        // (string)dynamic — dynamic is the *operand* of the cast, not the type being cast to.
        // CastExpressionSyntax.Expression == node; CastExpressionSyntax.Type != node.
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { void M(object dynamic) { var x = (string)dynamic; } }");
        Assert.False(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_clean_when_dynamic_is_as_left_operand()
    {
        // dynamic as string — dynamic is the *left* operand of 'as', not the target type.
        // BinaryExpressionSyntax.Left == node; BinaryExpressionSyntax.Right != node.
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { void M(object dynamic) { var x = dynamic as string; } }");
        Assert.False(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_clean_when_dynamic_is_foreach_collection()
    {
        // foreach (var x in dynamic) — dynamic is the *collection expression*, not the iteration-variable type.
        // ForEachStatementSyntax.Expression == node; ForEachStatementSyntax.Type != node.
        var diags = await T.Run(new DynamicUsageRule(),
            "using System.Collections.Generic; class C { void M(IEnumerable<string> dynamic) { foreach (var x in dynamic) { } } }");
        Assert.False(diags.Has("SAST008"));
    }

    // Regression tests for missed type positions (Ticket #17)

    [Fact]
    public async Task SAST008_flags_nullable_dynamic()
    {
        // dynamic? — NullableTypeSyntax parent, ElementType slot; previously not flagged
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { dynamic? Field; }");
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_flags_local_function_dynamic_return()
    {
        // dynamic Local() — LocalFunctionStatementSyntax parent, ReturnType slot; previously not flagged
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { void M() { dynamic Local() => 1; } }");
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_clean_when_dynamic_is_local_function_name()
    {
        // void dynamic() — 'dynamic' is the method name (SyntaxToken), not the return type node.
        // LocalFunctionStatementSyntax.ReturnType != node when the identifier fills the name slot.
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { void M() { void dynamic() { } } }");
        Assert.False(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_flags_tuple_element_dynamic_type()
    {
        // (dynamic x, int y) as a type — TupleElementSyntax parent, Type slot; previously not flagged
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { (dynamic x, int y) M() => (1, 2); }");
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_clean_when_dynamic_is_tuple_value()
    {
        // (dynamic, 1) as a value expression — IdentifierNameSyntax inside a TupleExpressionSyntax
        // argument, not a TupleElementSyntax type slot.
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { void M(object dynamic) { var t = (dynamic, 1); } }");
        Assert.False(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_flags_declaration_expression_out_dynamic()
    {
        // M(out dynamic d) — DeclarationExpressionSyntax parent, Type slot; previously not flagged.
        // The helper takes `out object` so the ONLY dynamic in the snippet is the declaration
        // expression — otherwise the helper's own `out dynamic` parameter satisfies
        // Has("SAST008") via the pre-existing ParameterSyntax arm and the test cannot pin
        // the DeclarationExpressionSyntax arm.
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { void M() { Parse(out dynamic d); } void Parse(out object x) { x = 1; } }");
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_clean_when_dynamic_is_out_argument_reference()
    {
        // M(out dynamic) — 'dynamic' is an identifier reference passed as an out argument,
        // not a type in a declaration expression. ArgumentSyntax parent, not DeclarationExpressionSyntax.
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { void M(object dynamic) { Parse(out dynamic); } void Parse(out object x) { x = null; } }");
        Assert.False(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_flags_delegate_dynamic_return()
    {
        // delegate dynamic D() — DelegateDeclarationSyntax parent, ReturnType slot; previously not flagged
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { delegate dynamic Fetcher(); }");
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_clean_when_dynamic_is_delegate_name()
    {
        // delegate void dynamic() — 'dynamic' fills the identifier name slot (SyntaxToken),
        // not the ReturnType node. DelegateDeclarationSyntax.ReturnType != node here.
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { delegate void dynamic(); }");
        Assert.False(diags.Has("SAST008"));
    }

    // Mixed partial-failure: a file with a mix of the five new type positions and adjacent
    // non-type slots — only the type-position occurrences must be flagged.
    [Fact]
    public async Task SAST008_mixed_new_type_positions_flags_only_type_slots()
    {
        const string code = """
            class C
            {
                // Type positions — must flag (5 occurrences)
                dynamic? NullableField;
                delegate dynamic Fetcher();
                (dynamic x, int y) TupleReturn() => (1, 2);

                void UseDynamic()
                {
                    dynamic Local() => 42;
                    Parse(out dynamic d);
                }

                void Parse(out dynamic x) { x = 1; }

                // Non-type positions — must NOT flag
                void Clean(object dynamic)
                {
                    var t = (dynamic, 1);
                    Parse(out dynamic);
                }
            }
            """;
        var diags = await T.Run(new DynamicUsageRule(), code);
        var sast008 = diags.Where(d => d.Rule == "SAST008").ToList();
        // Exactly 6: dynamic? field + delegate return + tuple-element (dynamic x) + local-fn
        // return + out-declaration (out dynamic d) + Parse's own `out dynamic x` parameter.
        // Exact count (not >=) so deleting any single arm — or adding an over-broad one that
        // flags the non-type slots in Clean() — fails the test.
        Assert.Equal(6, sast008.Count);
    }

    // Regression tests for missed type positions (Ticket #18)

    [Fact]
    public async Task SAST008_flags_indexer_dynamic_return()
    {
        // dynamic this[int i] — IndexerDeclarationSyntax parent, Type slot; previously not flagged.
        // The snippet contains no other 'dynamic' occurrence so this test pins exclusively the
        // IndexerDeclarationSyntax arm.
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { dynamic this[int i] => null; }");
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_clean_when_dynamic_is_indexer_parameter_name()
    {
        // dynamic is used as the *name* of an indexer parameter (a SyntaxToken on ParameterSyntax),
        // not as a type annotation. ParameterSyntax.Type is 'int', not the 'dynamic' identifier,
        // so the ParameterSyntax arm must not fire. The return type is 'int' and does fire on
        // the ParameterSyntax arm's Type slot for the int-typed parameter — but 'dynamic' itself
        // only appears as the parameter name token, which is not an IdentifierNameSyntax node.
        // Net effect: zero SAST008 diagnostics (the identifier "dynamic" never becomes an
        // IdentifierNameSyntax in that position).
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { int this[int dynamic] => dynamic; }");
        Assert.False(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_flags_operator_dynamic_return()
    {
        // public static dynamic operator +(C a, C b) — OperatorDeclarationSyntax, ReturnType slot;
        // previously not flagged. The snippet contains no other 'dynamic' so this pins the
        // OperatorDeclarationSyntax arm.
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { public static dynamic operator +(C a, C b) => a; }");
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_flags_default_expression_dynamic()
    {
        // default(dynamic) — DefaultExpressionSyntax parent, Type slot; previously not flagged.
        // DefaultExpressionSyntax has no expression-value child that could hold an IdentifierNameSyntax,
        // so there is no adjacent non-type slot to test; comment documents the reasoning.
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { void M() { var x = default(dynamic); } }");
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_flags_lambda_explicit_return_type()
    {
        // dynamic (int x) => x — ParenthesizedLambdaExpressionSyntax, ReturnType slot (C# 10+);
        // previously not flagged. The expression body 'x' is an int parameter, not 'dynamic',
        // so this snippet pins the ReturnType arm exclusively.
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { void M() { var f = dynamic (int x) => x; } }");
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task SAST008_clean_when_dynamic_is_lambda_body_expression()
    {
        // dynamic (int x) => dynamic — the body 'dynamic' is in ParenthesizedLambdaExpressionSyntax.
        // ExpressionBody / Body slot, NOT ReturnType. The slot check ReturnType == node guards this.
        // Only the return-type 'dynamic' (1 occurrence) should flag; the body 'dynamic' must not.
        // Total expected: 1 (the return type position only).
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { void M(object dynamic) { var f = dynamic (int x) => dynamic; } }");
        var sast008 = diags.Where(d => d.Rule == "SAST008").ToList();
        // Exactly 1: the return-type slot only. The body 'dynamic' is in expression position.
        Assert.Single(sast008);
    }

    [Fact]
    public async Task SAST008_flags_ref_dynamic_return()
    {
        // ref dynamic M() — RefTypeSyntax parent, Type slot; previously not flagged because
        // MethodDeclarationSyntax.ReturnType points to the RefTypeSyntax node, not the 'dynamic'
        // identifier directly. The snippet has one parameter 'dynamic[] arr' which uses ArrayTypeSyntax
        // (already covered). Exactly 2 diagnostics: the ref-return 'dynamic' (RefTypeSyntax arm)
        // + the array element type 'dynamic' (ArrayTypeSyntax arm).
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { ref dynamic M(dynamic[] arr) => ref arr[0]; }");
        var sast008 = diags.Where(d => d.Rule == "SAST008").ToList();
        Assert.Equal(2, sast008.Count);
    }

    // Mixed partial-failure: a file covering all five new type positions (Ticket #18) plus the
    // adjacent non-type slots. Only the type-position occurrences must be flagged.
    [Fact]
    public async Task SAST008_mixed_issue18_type_positions_flags_only_type_slots()
    {
        const string code = """
            using System;
            class C
            {
                // Type positions — must flag (6 occurrences)
                dynamic this[int i] => null;                              // IndexerDeclarationSyntax.Type
                public static dynamic operator +(C a, C b) => a;         // OperatorDeclarationSyntax.ReturnType
                ref dynamic RefReturn(dynamic[] arr) => ref arr[0];      // RefTypeSyntax.Type (+ ArrayTypeSyntax.ElementType below)

                void UseDynamic()
                {
                    var d1 = default(dynamic);                            // DefaultExpressionSyntax.Type
                    Func<int, dynamic> f = dynamic (int x) => x;         // ParenthesizedLambdaExpressionSyntax.ReturnType
                }

                // Non-type position — must NOT flag
                void Clean(object dynamic)
                {
                    var body = dynamic (int x) => dynamic;                // body 'dynamic' is ExpressionBody slot
                }
            }
            """;
        var diags = await T.Run(new DynamicUsageRule(), code);
        var sast008 = diags.Where(d => d.Rule == "SAST008").ToList();
        // Exactly 8:
        //   indexer return (1)            — IndexerDeclarationSyntax.Type
        //   operator return (1)           — OperatorDeclarationSyntax.ReturnType
        //   ref dynamic return (1)        — RefTypeSyntax.Type
        //   dynamic[] element type (1)    — ArrayTypeSyntax.ElementType (RefReturn parameter)
        //   default(dynamic) (1)          — DefaultExpressionSyntax.Type
        //   Func<int, dynamic> type arg (1) — TypeArgumentListSyntax (pre-existing arm; covers the field decl)
        //   lambda return type UseDynamic (1) — ParenthesizedLambdaExpressionSyntax.ReturnType
        //   lambda return type Clean (1)  — ReturnType slot of Clean's lambda; body 'dynamic' excluded
        // The body 'dynamic' in Clean (ExpressionBody slot) must NOT flag — that's the key guard.
        // Exact count (not >=) so removing any arm or adding an over-broad one changes the count.
        Assert.Equal(8, sast008.Count);
    }

    [Fact]
    public void Sast_rules_apply_to_any_file()
    {
        Assert.True(new EmptyCatchRule().AppliesTo(T.Cfg()));
        Assert.Equal(CsLint.Rules.RuleCategory.Sast, new EmptyCatchRule().Category);
    }
}
