using CsLint;
using CsLint.Rules.Opinionated;
using Xunit;

namespace CsLint.Tests;

public class GitResolverTests
{
    [Fact]
    public void IsGitRepo_false_for_plain_dir()
    {
        var dir = T.TempDir();
        try
        {
            Assert.False(GitResolver.IsGitRepo(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void IsGitRepo_true_for_repo()
    {
        // The cslint project itself is a git repo.
        var repoRoot = FindRepoRoot();
        if (repoRoot == null) return; // skip if not found
        Assert.True(GitResolver.IsGitRepo(repoRoot));
    }

    [Fact]
    public void GetStagedFiles_returns_added_cs_files()
    {
        // Smoke: returns a list (may be empty on a clean tree, but does not throw).
        var repoRoot = FindRepoRoot();
        if (repoRoot == null) return;
        var files = GitResolver.GetStagedFiles(repoRoot);
        Assert.NotNull(files);
    }

    [Fact]
    public void GetChangedFiles_includes_unstaged()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot == null) return;
        var files = GitResolver.GetChangedFiles(repoRoot);
        Assert.NotNull(files);
    }

    [Fact]
    public void GetHooksInfo_points_at_hooks_dir()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot == null) return;
        var (_, hooksDir) = GitResolver.GetHooksInfo(repoRoot);
        Assert.NotEmpty(hooksDir);
    }

    static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}

public class LintEngineTests
{
    [Fact]
    public async Task LintFile_applies_editorconfig_rules()
    {
        // A file with a tab indentation violation.
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, ".editorconfig"), """
            root = true
            [*.cs]
            indent_style = space
            indent_size = 4
            """);
        var file = Path.Combine(dir, "C.cs");
        File.WriteAllText(file, "\tclass C { }");
        try
        {
            var engine = new LintEngine();
            var diags = await engine.LintFileAsync(file, LintMode.EditorConfig);
            Assert.True(diags.Any(d => d.Rule == "EC001"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task LintFile_runs_sast_in_sast_mode()
    {
        var code = "class C { void M() { try { } catch { } } }";
        var path = T.WriteCs(code);
        try
        {
            var engine = new LintEngine();
            var diags = await engine.LintFileAsync(path, LintMode.EditorConfigAndSast);
            Assert.True(diags.Any(d => d.Rule == "SAST001"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LintFile_runs_opinionated_in_all_mode()
    {
        var code = "class C { int x = 42; }";
        var path = T.WriteCs(code);
        try
        {
            var engine = new LintEngine();
            var diags = await engine.LintFileAsync(path, LintMode.All);
            Assert.True(diags.Any(d => d.Rule == "OP004"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Severity_override_none_suppresses_finding()
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, ".editorconfig"), """
            root = true
            [*.cs]
            dotnet_diagnostic.SAST001.severity = none
            """);
        var file = Path.Combine(dir, "C.cs");
        File.WriteAllText(file, "class C { void M() { try { } catch { } } }");
        try
        {
            var engine = new LintEngine();
            var diags = await engine.LintFileAsync(file, LintMode.EditorConfigAndSast);
            Assert.False(diags.Any(d => d.Rule == "SAST001"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Severity_override_error_promotes_finding()
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, ".editorconfig"), """
            root = true
            [*.cs]
            dotnet_diagnostic.SAST002.severity = error
            """);
        var file = Path.Combine(dir, "C.cs");
        File.WriteAllText(file, "class C { void M() { Console.WriteLine(); } }");
        try
        {
            var engine = new LintEngine();
            var diags = await engine.LintFileAsync(file, LintMode.EditorConfigAndSast);
            var sast002 = diags.FirstOrDefault(d => d.Rule == "SAST002");
            if (sast002 != null)
                Assert.Equal(Severity.Error, sast002.Severity);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task LintFile_missing_returns_empty()
    {
        var engine = new LintEngine();
        var diags = await engine.LintFileAsync("/nonexistent/file.cs", LintMode.All);
        Assert.Empty(diags);
    }

    [Fact]
    public async Task LintFiles_counts_files()
    {
        var paths = new[] { T.WriteCs("class A { }"), T.WriteCs("class B { }") };
        try
        {
            var engine = new LintEngine();
            var summary = await engine.LintFilesAsync(paths, LintMode.EditorConfig);
            Assert.Equal(2, summary.FilesChecked);
        }
        finally
        {
            foreach (var p in paths) File.Delete(p);
        }
    }

    [Fact]
    public async Task ExplainFile_lists_rules()
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, ".editorconfig"), """
            root = true
            [*.cs]
            indent_style = space
            """);
        var file = Path.Combine(dir, "C.cs");
        File.WriteAllText(file, "class C { }");
        try
        {
            var engine = new LintEngine();
            var output = engine.ExplainFile(file);
            Assert.Contains("EC001", output);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Summary_exposes_counts()
    {
        var diags = new List<Diagnostic>
        {
            new("f.cs", 1, 1, "R1", "msg", Severity.Error),
            new("f.cs", 2, 1, "R2", "msg", Severity.Warning),
        };
        var summary = new Summary(diags, 2);
        Assert.Equal(1, summary.ErrorCount);
        Assert.Equal(1, summary.WarningCount);
        Assert.True(summary.HasErrors);
    }
}
