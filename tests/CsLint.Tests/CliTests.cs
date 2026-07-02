using CsLint;
using Xunit;

namespace CsLint.Tests;

public class CliTests
{
    [Fact]
    public void ParseOptions_boolean_flags()
    {
        var o = Cli.ParseOptions(["--global", "--fix", "--verbose", "--unstaged"]);
        Assert.True(o.Global);
        Assert.True(o.Fix);
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
        Assert.Equal(OutputFormat.Human, Cli.ParseOptions(["--format", "human"]).Format);
        Assert.Equal(OutputFormat.Json, Cli.ParseOptions(["--format", "json"]).Format);
        Assert.Equal(OutputFormat.GitHub, Cli.ParseOptions(["-f", "github"]).Format);
        // human is also the default when no --format is given.
        Assert.Equal(OutputFormat.Human, Cli.ParseOptions([]).Format);
    }

    [Fact]
    public void ParseOptions_invalid_format_is_a_usage_error()
    {
        // A bad format name must record OptionError (→ exit 2), not silently default to Human.
        Assert.NotNull(Cli.ParseOptions(["--format", "bogus"]).OptionError);
        Assert.NotNull(Cli.ParseOptions(["-f", "bogus"]).OptionError);
        // Numeric strings parse successfully via Enum.TryParse but must also be rejected — e.g.
        // "--format 2" would silently mean github; "--format 99" produces an undefined enum value.
        Assert.NotNull(Cli.ParseOptions(["--format", "2"]).OptionError);
        Assert.NotNull(Cli.ParseOptions(["--format", "99"]).OptionError);
        Assert.NotNull(Cli.ParseOptions(["--format", "0"]).OptionError);
    }

    [Fact]
    public async Task RunAsync_invalid_format_exits_two()
    {
        // End-to-end: an invalid --format value must cause exit code 2.
        var output = await CaptureRun(["--format", "bogus"]);
        Assert.Equal(2, output.Code);
        // A numeric token must also be rejected.
        var outputNumeric = await CaptureRun(["--format", "99"]);
        Assert.Equal(2, outputNumeric.Code);
    }

    [Fact]
    public void ParseOptions_removed_rule_toggle_flags_are_unknown_options()
    {
        // The old --no-magic-numbers / --no-bool-flags / --no-cancellation flags were removed (pure
        // config dupes). They must now be rejected as unknown options, not silently ignored.
        Assert.Equal("--no-magic-numbers", Cli.ParseOptions(["--no-magic-numbers"]).UnknownOption);
        Assert.Equal("--no-bool-flags", Cli.ParseOptions(["--no-bool-flags"]).UnknownOption);
        Assert.Equal("--no-cancellation", Cli.ParseOptions(["--no-cancellation"]).UnknownOption);
    }

    [Fact]
    public void ParseOptions_strict_flag_is_removed()
    {
        // --strict was replaced by `--fail-on severity=warning`; it is now an unknown option.
        Assert.Equal("--strict", Cli.ParseOptions(["--strict"]).UnknownOption);
        Assert.Equal("-s", Cli.ParseOptions(["-s"]).UnknownOption);
    }

    [Fact]
    public void ParseOptions_fail_on_severity_and_count()
    {
        var sev = Cli.ParseOptions(["--fail-on", "severity=warning"]);
        Assert.Null(sev.OptionError);
        Assert.Contains(sev.FailOn, r => r.Kind == FailOnKind.Severity && r.Threshold == 1); // warning=low

        var high = Cli.ParseOptions(["--fail-on", "severity=high"]);
        Assert.Contains(high.FailOn, r => r.Kind == FailOnKind.Severity && r.Threshold == 3);

        var count = Cli.ParseOptions(["--fail-on", "count=5"]);
        Assert.Null(count.OptionError);
        Assert.Contains(count.FailOn, r => r.Kind == FailOnKind.Count && r.Threshold == 5);

        // Repeatable: both rules accumulate.
        var both = Cli.ParseOptions(["--fail-on", "severity=low", "--fail-on", "count=10"]);
        Assert.Null(both.OptionError);
        Assert.Equal(2, both.FailOn.Count);
    }

    [Fact]
    public void ParseOptions_fail_on_bad_value_is_a_usage_error()
    {
        Assert.NotNull(Cli.ParseOptions(["--fail-on", "severity=bogus"]).OptionError);
        Assert.NotNull(Cli.ParseOptions(["--fail-on", "count=-1"]).OptionError);
        Assert.NotNull(Cli.ParseOptions(["--fail-on", "count=abc"]).OptionError);
        Assert.NotNull(Cli.ParseOptions(["--fail-on", "nonsense"]).OptionError);
        Assert.NotNull(Cli.ParseOptions(["--fail-on", "loudness=11"]).OptionError);
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
        var ok = Cli.ParseOptions(["--fail-on", "severity=warning", "--format", "json", "Foo.cs"]);
        Assert.Null(ok.UnknownOption);
        Assert.NotEmpty(ok.FailOn);
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

    // Fix #5: a value option that appears as the last argument (with no following value) must
    // record an OptionError (exit 2) instead of silently ignoring the option.
    [Fact]
    public void ParseOptions_trailing_value_option_without_value_is_a_usage_error()
    {
        // --project as the final arg: ProjectPath must not be set, and OptionError must be recorded.
        var project = Cli.ParseOptions(["--project"]);
        Assert.NotNull(project.OptionError);
        Assert.Null(project.ProjectPath);

        // Short form -p behaves the same.
        var projectShort = Cli.ParseOptions(["-p"]);
        Assert.NotNull(projectShort.OptionError);

        // --fail-on as the final arg: FailOn must be empty, OptionError must be recorded.
        // (Without this fix, FailOn stays empty and ApplyConfig silently substitutes the default
        // gate, masking a gate misconfiguration in CI.)
        var failOn = Cli.ParseOptions(["--fail-on"]);
        Assert.NotNull(failOn.OptionError);
        Assert.Empty(failOn.FailOn);

        // --exclude as the final arg: Exclude must be empty, OptionError must be recorded.
        var exclude = Cli.ParseOptions(["--exclude"]);
        Assert.NotNull(exclude.OptionError);
        Assert.Empty(exclude.Exclude);

        // --format as the final arg: Format must remain default, OptionError must be recorded.
        var format = Cli.ParseOptions(["--format"]);
        Assert.NotNull(format.OptionError);
        Assert.Equal(OutputFormat.Human, format.Format);

        // --root / -r as the final arg.
        var root = Cli.ParseOptions(["--root"]);
        Assert.NotNull(root.OptionError);

        var rootShort = Cli.ParseOptions(["-r"]);
        Assert.NotNull(rootShort.OptionError);

        // --config as the final arg.
        var config = Cli.ParseOptions(["--config"]);
        Assert.NotNull(config.OptionError);

        // --explain as the final arg.
        var explain = Cli.ParseOptions(["--explain"]);
        Assert.NotNull(explain.OptionError);
    }

    // Fix #5 (adjacent bug): --format with an unrecognized value must record OptionError (exit 2)
    // instead of silently falling back to human output.
    [Fact]
    public void ParseOptions_invalid_format_value_is_a_usage_error()
    {
        var o = Cli.ParseOptions(["--format", "bogus"]);
        Assert.NotNull(o.OptionError);
        // The format must not have been changed from the default.
        Assert.Equal(OutputFormat.Human, o.Format);

        // Valid values must still work without error.
        Assert.Null(Cli.ParseOptions(["--format", "json"]).OptionError);
        Assert.Null(Cli.ParseOptions(["--format", "github"]).OptionError);
        Assert.Null(Cli.ParseOptions(["--format", "human"]).OptionError);
        Assert.Null(Cli.ParseOptions(["-f", "json"]).OptionError);
    }

    // Fix #5: the error message must name the offending option.
    [Fact]
    public void ParseOptions_trailing_value_option_error_message_names_the_option()
    {
        var o = Cli.ParseOptions(["--project"]);
        Assert.NotNull(o.OptionError);
        Assert.Contains("--project", o.OptionError);

        var o2 = Cli.ParseOptions(["--fail-on"]);
        Assert.NotNull(o2.OptionError);
        Assert.Contains("--fail-on", o2.OptionError);
    }

    // Fix #5: RunAsync must return exit code 2 when a value option has no value.
    [Fact]
    public async Task RunAsync_trailing_value_option_exits_two()
    {
        var output = await CaptureRun(["--project"]);
        Assert.Equal(2, output.Code);

        var output2 = await CaptureRun(["--fail-on"]);
        Assert.Equal(2, output2.Code);

        var output3 = await CaptureRun(["--format"]);
        Assert.Equal(2, output3.Code);
    }

    // Fix #5 (adjacent bug): RunAsync must return exit code 2 for an invalid --format value.
    [Fact]
    public async Task RunAsync_invalid_format_value_exits_two()
    {
        var output = await CaptureRun(["--format", "bogus"]);
        Assert.Equal(2, output.Code);
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
    public async Task RunAsync_fail_on_severity_warning_fails_on_warnings()
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, ".editorconfig"),
            "root = true\n[*.cs]\nindent_style = space\n");
        File.WriteAllText(Path.Combine(dir, "A.cs"), "\tclass A { }"); // tab -> EC001 warning
        try
        {
            // `--fail-on severity=warning` gates on warnings (the former --strict behavior).
            var output = await CaptureRun(["--global", "--fail-on", "severity=warning", "--root", dir]);
            Assert.Equal(1, output.Code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task RunAsync_default_does_not_fail_on_warnings()
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, ".editorconfig"),
            "root = true\n[*.cs]\nindent_style = space\n");
        File.WriteAllText(Path.Combine(dir, "A.cs"), "\tclass A { }"); // tab -> EC001 warning only
        try
        {
            // Default gate: errors gate, warnings don't — a warning-only run exits 0.
            var output = await CaptureRun(["--global", "--root", dir]);
            Assert.Equal(0, output.Code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task RunAsync_fail_on_count_fails_when_findings_exceed_threshold()
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, ".editorconfig"),
            "root = true\n[*.cs]\nindent_style = space\n");
        File.WriteAllText(Path.Combine(dir, "A.cs"), "\tclass A { }"); // one EC001 warning
        try
        {
            // count=0 trips on any finding; count=5 does not (1 finding <= 5).
            Assert.Equal(1, (await CaptureRun(["--global", "--fail-on", "count=0", "--root", dir])).Code);
            Assert.Equal(0, (await CaptureRun(["--global", "--fail-on", "count=5", "--root", dir])).Code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task RunAsync_bad_fail_on_value_exits_two()
    {
        var output = await CaptureRun(["--global", "--fail-on", "severity=bogus"]);
        Assert.Equal(2, output.Code);
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
