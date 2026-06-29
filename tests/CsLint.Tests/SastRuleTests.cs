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
            var diags = await new ConsoleOutputRule().AnalyzeAsync(path, T.Cfg());
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

    [Fact]
    public async Task SAST004_flags_hardcoded_secret_variable()
    {
        var diags = await T.Run(new HardcodedSecretRule(),
            "class C { void M() { string apiKey = \"sk_live_abc123\"; } }");
        Assert.True(diags.Has("SAST004"));
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

    [Fact]
    public async Task SAST008_flags_dynamic_local()
    {
        var diags = await T.Run(new DynamicUsageRule(),
            "class C { void M() { dynamic x = 1; } }");
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public void Sast_rules_apply_to_any_file()
    {
        Assert.True(new EmptyCatchRule().AppliesTo(T.Cfg()));
        Assert.Equal(CsLint.Rules.RuleCategory.Sast, new EmptyCatchRule().Category);
    }
}
