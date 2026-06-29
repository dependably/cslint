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
    public void ParseOptions_unknown_flag_is_a_usage_error()
    {
        // A bogus or typo'd flag must be rejected, not silently ignored (which would exit 0 and
        // quietly disable the intended behavior, e.g. a mistyped --strict).
        Assert.Equal("--bogus", Cli.ParseOptions(["--bogus"]).UnknownOption);
        Assert.Equal("--stict", Cli.ParseOptions(["--stict"]).UnknownOption); // typo'd --strict

        // Valid flags, valued-flag values, and positional paths are not treated as unknown.
        var ok = Cli.ParseOptions(["--strict", "--format", "json", "Foo.cs"]);
        Assert.Null(ok.UnknownOption);
        Assert.True(ok.Strict);
        Assert.Single(ok.Files);

        // The reported unknown option is the first one encountered.
        Assert.Equal("--bogus", Cli.ParseOptions(["--bogus", "--alsobad"]).UnknownOption);
    }

    [Fact]
    public void ParseOptions_help_and_install_hook()
    {
        Assert.True(Cli.ParseOptions(["-h"]).ShowHelp);
        Assert.True(Cli.ParseOptions(["--install-hook"]).InstallHook);
    }

    [Fact]
    public void ParseOptions_version_is_a_recognized_flag()
    {
        // --version must be a real flag, not an unknown option (which would exit 2).
        var o = Cli.ParseOptions(["--version"]);
        Assert.True(o.ShowVersion);
        Assert.Null(o.UnknownOption);
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
        var dir = T.TempDir();
        var foo = Path.Combine(dir, "Foo.cs");
        var bar = Path.Combine(dir, "Bar.cs");
        File.WriteAllText(foo, "class Foo { }");
        File.WriteAllText(bar, "class Bar { }");
        try
        {
            var o = Cli.ParseOptions([foo, bar]);
            var (resolved, targets) = Cli.ResolveTargets(o);
            Assert.True(resolved);
            Assert.Equal(2, targets.Count());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ResolveTargets_missing_path_is_an_error()
    {
        // A literal path that does not exist must fail loudly, not silently lint nothing.
        var o = Cli.ParseOptions([Path.Combine(T.TempDir(), "DoesNotExist.cs")]);
        var (resolved, _) = Cli.ResolveTargets(o);
        Assert.False(resolved);
    }

    [Fact]
    public void ResolveTargets_expands_directory_argument()
    {
        var dir = T.TempDir();
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "A.cs"), "class A { }");
        File.WriteAllText(Path.Combine(dir, "sub", "B.cs"), "class B { }");
        try
        {
            var o = Cli.ParseOptions([dir]);
            var (resolved, targets) = Cli.ResolveTargets(o);
            Assert.True(resolved);
            var list = targets.ToList();
            Assert.Contains(list, p => p.EndsWith("A.cs"));
            Assert.Contains(list, p => p.EndsWith("B.cs"));
        }
        finally { Directory.Delete(dir, true); }
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
    public void ResolveTargets_global_missing_root_fails_without_throwing()
    {
        // Regression: a typo'd --root used to throw an unhandled DirectoryNotFoundException out of
        // EnumerateFiles; it must instead fail via the usage-error path (Resolved == false).
        var missing = Path.Combine(Path.GetTempPath(), $"cslint_nope_{Guid.NewGuid():N}");
        var o = Cli.ParseOptions(["--global", "--root", missing]);
        var (resolved, _) = Cli.ResolveTargets(o);
        Assert.False(resolved);
    }

    [Fact]
    public void ResolveTargets_global_excludes_generated_files()
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, "Normal.cs"), "class A { }");
        File.WriteAllText(Path.Combine(dir, "Foo.Designer.cs"), "class B { }");
        File.WriteAllText(Path.Combine(dir, "AutoGen.cs"),
            "// <auto-generated/>\nclass C { }");
        try
        {
            var o = Cli.ParseOptions(["--global", "--root", dir]);
            var (resolved, targets) = Cli.ResolveTargets(o);
            Assert.True(resolved);
            var list = targets.ToList();
            Assert.Contains(list, p => p.EndsWith("Normal.cs"));
            Assert.DoesNotContain(list, p => p.EndsWith("Foo.Designer.cs"));
            Assert.DoesNotContain(list, p => p.EndsWith("AutoGen.cs"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IsGenerated_detects_generated_suffixes()
    {
        Assert.True(Cli.IsGenerated("Foo.Designer.cs"));
        Assert.True(Cli.IsGenerated("Foo.g.cs"));
        Assert.True(Cli.IsGenerated("Foo.g.i.cs"));
        Assert.True(Cli.IsGenerated("Foo.generated.cs"));
        Assert.True(Cli.IsGenerated("Foo.AssemblyInfo.cs")); // case-insensitive suffix match
        Assert.True(Cli.IsGenerated("GlobalUsings.g.cs"));
        Assert.False(Cli.IsGenerated("Foo.cs"));
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
    public async Task RunAsync_version_prints_and_exits_zero()
    {
        var output = await CaptureRun(["--version"]);
        Assert.Equal(0, output.Code);
        Assert.Contains("cslint", output.Text);
        // The reported version must match the assembly version (e.g. "4.1.0"), not be empty.
        Assert.Contains(Cli.Version(), output.Text);
        Assert.Matches(@"\d+\.\d+", output.Text); // a real version number, not "unknown"
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
