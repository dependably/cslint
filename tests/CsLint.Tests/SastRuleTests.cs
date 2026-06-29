using CsLint.Rules.Sast;
using Xunit;

namespace CsLint.Tests;

public class SastRuleTests
{
    [Fact]
    public async Task SAST001_flags_empty_catch()
    {
        var code = "class C { void M() { try { } catch { } } }";
        var diags = await T.Run(new EmptyCatchRule(), code);
        Assert.True(diags.Has("SAST001"));
    }

    [Fact]
    public async Task SAST001_flags_discard_only_catch()
    {
        var code = "class C { void M() { try { } catch (System.Exception e) { _ = e; } } }";
        var diags = await T.Run(new EmptyCatchRule(), code);
        Assert.True(diags.Has("SAST001"));
    }

    [Fact]
    public async Task SAST001_clean_when_catch_handles()
    {
        var code = "class C { void M() { try { } catch (System.Exception e) { throw; } } }";
        var diags = await T.Run(new EmptyCatchRule(), code);
        Assert.False(diags.Has("SAST001"));
    }

    [Fact]
    public async Task SAST002_flags_console_writeline()
    {
        var code = "class C { void M() { Console.WriteLine(\"hi\"); } }";
        var diags = await T.Run(new ConsoleOutputRule(), code);
        Assert.True(diags.Has("SAST002"));
    }

    [Fact]
    public async Task SAST002_ignores_test_files()
    {
        var code = "class C { void M() { Console.WriteLine(\"hi\"); } }";
        var path = T.WriteCs(code, ext: "Test.cs");
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
        var code = """
            class C {
                void M(string id) {
                    var db = new object();
                    // db.ExecuteSqlRaw($"SELECT * FROM t WHERE id={id}");
                    _ = $"ExecuteSqlRaw SELECT * WHERE id={id}";
                }
            }
            """;
        // The SQL injection rule looks for interpolated strings in SQL method calls.
        // Use a snippet that matches the rule's pattern.
        var sqlCode = """
            class C {
                void M(string id) {
                    ExecuteSqlRaw($"SELECT * FROM Users WHERE Id = {id}");
                }
                void ExecuteSqlRaw(string s) { }
            }
            """;
        var diags = await T.Run(new SqlInjectionRule(), sqlCode);
        Assert.True(diags.Has("SAST003"));
    }

    [Fact]
    public async Task SAST003_clean_with_constant_sql()
    {
        var code = """
            class C {
                void M() {
                    ExecuteSqlRaw("SELECT * FROM Users");
                }
                void ExecuteSqlRaw(string s) { }
            }
            """;
        var diags = await T.Run(new SqlInjectionRule(), code);
        Assert.False(diags.Has("SAST003"));
    }

    [Fact]
    public async Task SAST004_flags_hardcoded_secret_variable()
    {
        var code = "class C { string password = \"abc123\"; }";
        var diags = await T.Run(new HardcodedSecretRule(), code);
        Assert.True(diags.Has("SAST004"));
    }

    [Fact]
    public async Task SAST004_flags_secret_assignment()
    {
        var code = "class C { void M() { var apiKey = \"secret-value-here\"; } }";
        var diags = await T.Run(new HardcodedSecretRule(), code);
        Assert.True(diags.Has("SAST004"));
    }

    [Fact]
    public async Task SAST004_flags_placeholder_value()
    {
        var code = "class C { string secret = \"CHANGE_ME\"; }";
        var diags = await T.Run(new HardcodedSecretRule(), code);
        Assert.True(diags.Has("SAST004"));
    }

    [Fact]
    public async Task SAST005_flags_fire_and_forget()
    {
        var code = """
            using System.Threading.Tasks;
            class C {
                async System.Threading.Tasks.Task M() {
                    DoAsync();
                }
                System.Threading.Tasks.Task DoAsync() => System.Threading.Tasks.Task.CompletedTask;
            }
            """;
        var diags = await T.Run(new FireAndForgetRule(), code);
        Assert.True(diags.Has("SAST005"));
    }

    [Fact]
    public async Task SAST005_flags_discarded_async()
    {
        var code = """
            using System.Threading.Tasks;
            class C {
                async Task M() {
                    _ = DoAsync();
                }
                Task DoAsync() => Task.CompletedTask;
            }
            """;
        var diags = await T.Run(new FireAndForgetRule(), code);
        Assert.True(diags.Has("SAST005"));
    }

    [Fact]
    public async Task SAST006_flags_unjustified_pragma()
    {
        var code = "#pragma warning disable CS0168\nclass C { }";
        var diags = await T.Run(new PragmaDisableRule(), code);
        Assert.True(diags.Has("SAST006"));
    }

    [Fact]
    public async Task SAST006_clean_with_justification()
    {
        // A justification comment on the same line suppresses the finding.
        var code = "#pragma warning disable CS0168 // by design: variable reserved for future use\nclass C { }";
        var diags = await T.Run(new PragmaDisableRule(), code);
        Assert.False(diags.Has("SAST006"));
    }

    [Fact]
    public async Task SAST007_flags_thread_sleep_in_async()
    {
        var code = """
            using System.Threading;
            using System.Threading.Tasks;
            class C {
                async Task M() { Thread.Sleep(100); }
            }
            """;
        var diags = await T.Run(new ThreadSleepInAsyncRule(), code);
        Assert.True(diags.Has("SAST007"));
    }

    [Fact]
    public async Task SAST007_clean_in_sync_method()
    {
        var code = """
            using System.Threading;
            class C {
                void M() { Thread.Sleep(100); }
            }
            """;
        var diags = await T.Run(new ThreadSleepInAsyncRule(), code);
        Assert.False(diags.Has("SAST007"));
    }

    [Fact]
    public async Task SAST008_flags_dynamic_local()
    {
        var code = "class C { void M() { dynamic x = 1; } }";
        var diags = await T.Run(new DynamicUsageRule(), code);
        Assert.True(diags.Has("SAST008"));
    }

    [Fact]
    public async Task Sast_rules_apply_to_any_file()
    {
        // AppliesTo always returns true for SAST rules (they don't need editorconfig keys).
        Assert.True(new EmptyCatchRule().AppliesTo(T.Cfg()));
        Assert.True(new ConsoleOutputRule().AppliesTo(T.Cfg()));
        Assert.True(new SqlInjectionRule().AppliesTo(T.Cfg()));
        Assert.True(new HardcodedSecretRule().AppliesTo(T.Cfg()));
        Assert.True(new FireAndForgetRule().AppliesTo(T.Cfg()));
        Assert.True(new PragmaDisableRule().AppliesTo(T.Cfg()));
        Assert.True(new ThreadSleepInAsyncRule().AppliesTo(T.Cfg()));
        Assert.True(new DynamicUsageRule().AppliesTo(T.Cfg()));
    }
}
