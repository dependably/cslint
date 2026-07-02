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

    [Fact]
    public void Sast_rules_apply_to_any_file()
    {
        Assert.True(new EmptyCatchRule().AppliesTo(T.Cfg()));
        Assert.Equal(CsLint.Rules.RuleCategory.Sast, new EmptyCatchRule().Category);
    }
}
