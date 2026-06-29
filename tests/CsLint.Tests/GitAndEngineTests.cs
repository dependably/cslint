using CsLint;
using Xunit;

namespace CsLint.Tests;

static class Git
{
    public static void Run(string dir, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git", args)
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
    }

    public static string InitRepo()
    {
        var dir = T.TempDir();
        Run(dir, "init -q");
        Run(dir, "config user.email test@example.com");
        Run(dir, "config user.name Test");
        Run(dir, "config commit.gpgsign false");
        return dir;
    }
}

public class GitResolverTests
{
    [Fact]
    public void IsGitRepo_false_for_plain_dir()
    {
        var dir = T.TempDir();
        try { Assert.False(GitResolver.IsGitRepo(dir)); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IsGitRepo_true_for_repo()
    {
        var dir = Git.InitRepo();
        try { Assert.True(GitResolver.IsGitRepo(dir)); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void GetStagedFiles_returns_added_cs_files()
    {
        var dir = Git.InitRepo();
        try
        {
            File.WriteAllText(Path.Combine(dir, "A.cs"), "class A { }");
            File.WriteAllText(Path.Combine(dir, "B.txt"), "text");
            Git.Run(dir, "add A.cs B.txt");

            var staged = GitResolver.GetStagedFiles(dir);
            Assert.Contains(staged, p => p.EndsWith("A.cs"));
            Assert.DoesNotContain(staged, p => p.EndsWith("B.txt"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void GetChangedFiles_includes_unstaged()
    {
        var dir = Git.InitRepo();
        try
        {
            var file = Path.Combine(dir, "A.cs");
            File.WriteAllText(file, "class A { }");
            Git.Run(dir, "add A.cs");
            Git.Run(dir, "commit -q -m init");
            File.WriteAllText(file, "class A { int x; }"); // unstaged modification

            var changed = GitResolver.GetChangedFiles(dir);
            Assert.Contains(changed, p => p.EndsWith("A.cs"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void GetHooksInfo_points_at_hooks_dir()
    {
        var dir = Git.InitRepo();
        try
        {
            var (hook, hooksDir) = GitResolver.GetHooksInfo(dir);
            Assert.Null(hook); // none installed yet
            Assert.Contains("hooks", hooksDir);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class LintEngineTests
{
    static (string dir, string file) Project(string editorConfig, string source, string name = "Foo.cs")
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, ".editorconfig"), editorConfig);
        var file = Path.Combine(dir, name);
        File.WriteAllText(file, source);
        return (dir, file);
    }

    [Fact]
    public async Task LintFile_applies_editorconfig_rules()
    {
        var (dir, file) = Project("root = true\n[*.cs]\nindent_style = space\n", "\tclass C { }");
        try
        {
            var diags = await new LintEngine().LintFileAsync(file, LintMode.EditorConfig);
            Assert.Contains(diags, d => d.Rule == "EC001");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task LintFile_runs_sast_in_sast_mode()
    {
        var (dir, file) = Project("root = true\n", "class C { void M() { try { } catch { } } }");
        try
        {
            var diags = await new LintEngine().LintFileAsync(file, LintMode.EditorConfigAndSast);
            Assert.Contains(diags, d => d.Rule == "SAST001");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task LintFile_runs_opinionated_in_all_mode()
    {
        var (dir, file) = Project("root = true\n", "class C { int M() { return 42; } }");
        try
        {
            var diags = await new LintEngine().LintFileAsync(file, LintMode.All);
            Assert.Contains(diags, d => d.Rule == "OP004");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Severity_override_none_suppresses_finding()
    {
        var (dir, file) = Project(
            "root = true\n[*.cs]\ndotnet_diagnostic.SAST001.severity = none\n",
            "class C { void M() { try { } catch { } } }");
        try
        {
            var diags = await new LintEngine().LintFileAsync(file, LintMode.EditorConfigAndSast);
            Assert.DoesNotContain(diags, d => d.Rule == "SAST001");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Severity_override_error_promotes_finding()
    {
        var (dir, file) = Project(
            "root = true\n[*.cs]\ndotnet_diagnostic.OP004.severity = error\n",
            "class C { int M() { return 42; } }");
        try
        {
            var diags = await new LintEngine().LintFileAsync(file, LintMode.All);
            Assert.Contains(diags, d => d.Rule == "OP004" && d.Severity == Severity.Error);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task LintFile_missing_returns_empty()
    {
        var diags = await new LintEngine().LintFileAsync("/no/such/file.cs", LintMode.All);
        Assert.Empty(diags);
    }

    [Fact]
    public async Task LintFiles_counts_files()
    {
        var (dir, file) = Project("root = true\n", "class C { }");
        try
        {
            var summary = await new LintEngine().LintFilesAsync([file], LintMode.EditorConfig);
            Assert.Equal(1, summary.FilesChecked);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ExplainFile_lists_rules()
    {
        var (dir, file) = Project("root = true\n[*.cs]\nindent_style = space\n", "class C { }");
        try
        {
            var text = new LintEngine().ExplainFile(file);
            Assert.Contains("EC001", text);
            Assert.Contains("SAST", text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Summary_exposes_counts()
    {
        var summary = new Summary(
        [
            new("a", 1, 1, "X", "m", Severity.Error),
            new("a", 2, 1, "Y", "m", Severity.Warning),
        ], 1);
        Assert.Equal(1, summary.ErrorCount);
        Assert.Equal(1, summary.WarningCount);
        Assert.True(summary.HasErrors);
    }
}
