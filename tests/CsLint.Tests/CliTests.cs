using CsLint;
using Xunit;

namespace CsLint.Tests;

public class CliTests
{
    [Fact]
    public void ParseOptions_boolean_flags()
    {
        var o = Cli.ParseOptions(["--global", "--fix", "--strict", "--verbose", "--unstaged"]);
        Assert.True(o.Global);
        Assert.True(o.Fix);
        Assert.True(o.Strict);
        Assert.True(o.Verbose);
        Assert.True(o.Unstaged);
    }

    [Fact]
    public void ParseOptions_scan_implies_sast()
    {
        var o = Cli.ParseOptions(["--scan"]);
        Assert.True(o.ScanMode);
        Assert.True(o.SastMode);
    }

    [Fact]
    public void ParseOptions_project_implies_deep()
    {
        var o = Cli.ParseOptions(["--project", "foo.csproj"]);
        Assert.True(o.DeepMode);
        Assert.NotNull(o.ProjectPath);
        Assert.EndsWith("foo.csproj", o.ProjectPath);
    }

    [Fact]
    public void ParseOptions_format_value()
    {
        Assert.Equal(OutputFormat.Json, Cli.ParseOptions(["--format", "json"]).Format);
        Assert.Equal(OutputFormat.GitHub, Cli.ParseOptions(["-f", "github"]).Format);
    }

    [Fact]
    public void ParseOptions_no_flags_disable_scans()
    {
        var o = Cli.ParseOptions(["--no-magic-numbers", "--no-bool-flags", "--no-cancellation"]);
        Assert.False(o.FlagMagicNumbers);
        Assert.False(o.FlagBoolFlags);
        Assert.False(o.FlagCancellationToken);
    }

    [Fact]
    public void ParseOptions_exclude_and_positional()
    {
        var o = Cli.ParseOptions(["--exclude", "gen/**", "Foo.cs"]);
        Assert.Contains("gen/**", o.Exclude);
        Assert.Single(o.Files);
        Assert.EndsWith("Foo.cs", o.Files[0]);
    }

    [Fact]
    public void ParseOptions_help_and_install_hook()
    {
        Assert.True(Cli.ParseOptions(["-h"]).ShowHelp);
        Assert.True(Cli.ParseOptions(["--install-hook"]).InstallHook);
    }

    [Fact]
    public void DetermineMode_maps_options()
    {
        Assert.Equal(LintMode.All, Cli.DetermineMode(Cli.ParseOptions(["--scan"])));
        Assert.Equal(LintMode.EditorConfigAndSast, Cli.DetermineMode(Cli.ParseOptions(["--sast"])));
        Assert.Equal(LintMode.EditorConfig, Cli.DetermineMode(Cli.ParseOptions([])));
    }

    [Fact]
    public void ModeLabel_strings()
    {
        Assert.Equal("editorconfig", Cli.ModeLabel(LintMode.EditorConfig));
        Assert.Equal("editorconfig+sast", Cli.ModeLabel(LintMode.EditorConfigAndSast));
        Assert.Equal("editorconfig+sast+scan", Cli.ModeLabel(LintMode.All));
    }

    [Fact]
    public void ResolveTargets_explicit_files()
    {
        var o = Cli.ParseOptions(["Foo.cs", "Bar.cs"]);
        var (resolved, targets) = Cli.ResolveTargets(o);
        Assert.True(resolved);
        Assert.Equal(2, targets.Count());
    }

    [Fact]
    public void ResolveTargets_global_enumerates_dir()
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, "A.cs"), "class A { }");
        try
        {
            var o = Cli.ParseOptions(["--global", "--root", dir]);
            var (resolved, targets) = Cli.ResolveTargets(o);
            Assert.True(resolved);
            Assert.Contains(targets, p => p.EndsWith("A.cs"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ResolveTargets_global_excludes_glob()
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, "A.cs"), "class A { }");
        try
        {
            var o = Cli.ParseOptions(["--global", "--root", dir, "--exclude", "A.cs"]);
            var (_, targets) = Cli.ResolveTargets(o);
            Assert.DoesNotContain(targets, p => p.EndsWith("A.cs"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ResolveTargets_non_git_fails()
    {
        var dir = T.TempDir();
        try
        {
            var o = Cli.ParseOptions(["--root", dir]);
            var (resolved, _) = Cli.ResolveTargets(o);
            Assert.False(resolved);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IsGenerated_detects_build_dirs()
    {
        var sep = Path.DirectorySeparatorChar;
        Assert.True(Cli.IsGenerated($"x{sep}obj{sep}y.cs"));
        Assert.True(Cli.IsGenerated($"x{sep}bin{sep}y.cs"));
        Assert.False(Cli.IsGenerated($"x{sep}src{sep}y.cs"));
    }

    [Fact]
    public async Task RunAsync_help_prints_usage()
    {
        var output = await CaptureRun(["--help"]);
        Assert.Equal(0, output.Code);
        Assert.Contains("cslint v4", output.Text);
    }

    [Fact]
    public async Task RunAsync_global_reports_and_exits_zero()
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, ".editorconfig"),
            "root = true\n[*.cs]\nindent_style = space\n");
        File.WriteAllText(Path.Combine(dir, "A.cs"), "\tclass A { }"); // tab -> EC001 warning
        try
        {
            var output = await CaptureRun(["--global", "--root", dir]);
            Assert.Equal(0, output.Code);
            Assert.Contains("Checked 1 file", output.Text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task RunAsync_strict_fails_on_warnings()
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, ".editorconfig"),
            "root = true\n[*.cs]\nindent_style = space\n");
        File.WriteAllText(Path.Combine(dir, "A.cs"), "\tclass A { }");
        try
        {
            var output = await CaptureRun(["--global", "--strict", "--root", dir]);
            Assert.Equal(1, output.Code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task RunAsync_explain_prints_rules()
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, ".editorconfig"),
            "root = true\n[*.cs]\nindent_style = space\n");
        var file = Path.Combine(dir, "A.cs");
        File.WriteAllText(file, "class A { }");
        try
        {
            var output = await CaptureRun(["--explain", file, "--root", dir]);
            Assert.Equal(0, output.Code);
            Assert.Contains("EC001", output.Text);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task RunAsync_bad_config_exits_two()
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, ".dependably-check"), "{ broken");
        File.WriteAllText(Path.Combine(dir, "A.cs"), "class A { }");
        try
        {
            var output = await CaptureRun(["--global", "--root", dir, "--config",
                Path.Combine(dir, ".dependably-check")]);
            Assert.Equal(2, output.Code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task RunAsync_install_hook_writes_file()
    {
        var dir = Git.InitRepo();
        try
        {
            var output = await CaptureRun(["--install-hook", "--root", dir]);
            Assert.Equal(0, output.Code);
            Assert.True(File.Exists(Path.Combine(dir, ".git", "hooks", "pre-commit")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task RunAsync_install_hook_non_git_exits_two()
    {
        var dir = T.TempDir();
        try
        {
            var output = await CaptureRun(["--install-hook", "--root", dir]);
            Assert.Equal(2, output.Code);
        }
        finally { Directory.Delete(dir, true); }
    }

    record RunResult(int Code, string Text);

    static async Task<RunResult> CaptureRun(string[] args)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        int code;
        try { code = await Cli.RunAsync(args); }
        finally { Console.SetOut(original); }
        return new RunResult(code, writer.ToString());
    }
}
