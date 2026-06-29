using CsLint.Rules.Opinionated;

namespace CsLint;

/// <summary>
/// CLI orchestration for cslint: argument parsing, target resolution, and the main run loop.
/// Kept out of the top-level <c>Program.cs</c> so the logic lives in a named namespace
/// (testable, and free of the entry-point's synthesized wrapper).
/// </summary>
static class Cli
{
    // Process exit code for an unusable invocation: bad config, or no git repo without --global.
    const int ExitUsageError = 2;

    public static async Task<int> RunAsync(string[] args)
    {
        var options = ParseOptions(args);

        if (options.ShowHelp) { PrintHelp(); return 0; }
        if (options.InstallHook) return InstallGitHook(options.Root);

        EnsureMsBuildOrFallback(options);

        // Shared .dependably-check config (repo root). CLI flags win: --strict / --no-* can only
        // further restrict what the file allows.
        CsLintConfig config;
        try
        {
            config = CsLintConfig.Load(options.ConfigPath, options.Root);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            Console.Error.WriteLine($"Config error: {ex.Message}");
            return ExitUsageError;
        }

        ApplyConfig(options, config);

        var scanConfig = new ScanConfig(
            FlagMagicNumbers: options.FlagMagicNumbers,
            FlagBooleanParameters: options.FlagBoolFlags,
            FlagMissingCancellationToken: options.FlagCancellationToken);

        var engine = new LintEngine(scanConfig);

        if (options.ExplainFile != null)
        {
            Console.WriteLine(engine.ExplainFile(options.ExplainFile));
            return 0;
        }

        var mode = DetermineMode(options);
        var (resolved, targets) = ResolveTargets(options);
        if (!resolved) return ExitUsageError;

        var summary = await engine.LintFilesAsync(targets, mode, options.Fix);
        var allDiagnostics = await CollectDiagnosticsAsync(engine, options, summary);

        return ReportAndExit(allDiagnostics, options, summary, mode);
    }

    // --deep needs MSBuild registered before any MSBuild type loads; if that fails, warn and
    // fall back to syntactic analysis.
    static void EnsureMsBuildOrFallback(CliOptions options)
    {
        if (options.DeepMode && !SemanticEngine.TryRegisterMsBuild(out var msbuildError))
        {
            Console.Error.WriteLine($"Cannot load MSBuild: {msbuildError}");
            Console.Error.WriteLine("Ensure the .NET SDK is installed. Falling back to syntactic analysis.");
            options.DeepMode = false;
        }
    }

    // Fold the .dependably-check config into the options. CLI flags only ever restrict further:
    // --strict forces strict on; a --no-* flag (already off here) can't be re-enabled by the file.
    static void ApplyConfig(CliOptions options, CsLintConfig config)
    {
        options.Strict = options.Strict || config.Strict;
        options.FlagMagicNumbers = options.FlagMagicNumbers && config.ScanMagicNumbers;
        options.FlagBoolFlags = options.FlagBoolFlags && config.ScanBoolFlags;
        options.FlagCancellationToken = options.FlagCancellationToken && config.ScanCancellation;
        options.Exclude = [.. options.Exclude, .. config.Exclude]; // CLI globs + config globs
    }

    // Syntactic findings, plus Roslyn semantic diagnostics when --deep --project is in play.
    static async Task<IReadOnlyList<Diagnostic>> CollectDiagnosticsAsync(
        LintEngine engine, CliOptions options, Summary summary)
    {
        IReadOnlyList<Diagnostic> allDiagnostics = summary.Diagnostics.ToList();

        if (options.DeepMode && options.ProjectPath != null)
        {
            var semanticEngine = new SemanticEngine(engine.Loader);
            var semanticDiags = await semanticEngine.AnalyzeProjectAsync(
                options.ProjectPath, verbose: options.Verbose);
            allDiagnostics = [.. allDiagnostics, .. semanticDiags];
        }

        return allDiagnostics;
    }

    // Print the report + summary line and return the process exit code (1 on errors, or on
    // warnings under --strict).
    static int ReportAndExit(
        IReadOnlyList<Diagnostic> allDiagnostics, CliOptions options, Summary summary, LintMode mode)
    {
        Reporter.Write(allDiagnostics, options.Format, options.Root);

        var totalFiles = summary.FilesChecked;
        var errors = allDiagnostics.Count(d => d.Severity == Severity.Error);
        var warnings = allDiagnostics.Count(d => d.Severity == Severity.Warning);

        Console.WriteLine($"Checked {totalFiles} file{(totalFiles != 1 ? "s" : "")}. " +
                          $"Mode: {ModeLabel(mode)}" +
                          (options.DeepMode ? " + semantic" : "") + ".");

        if (errors == 0 && !(options.Strict && warnings > 0)) return 0;

        if (options.Strict && warnings > 0 && errors == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("--strict: warnings treated as errors.");
            Console.ResetColor();
        }

        return 1;
    }

    public static LintMode DetermineMode(CliOptions opts)
    {
        if (opts.ScanMode) return LintMode.All;
        if (opts.SastMode) return LintMode.EditorConfigAndSast;
        return LintMode.EditorConfig;
    }

    public static string ModeLabel(LintMode mode) => mode switch
    {
        LintMode.EditorConfigAndSast => "editorconfig+sast",
        LintMode.All => "editorconfig+sast+scan",
        _ => "editorconfig"
    };

    /// <summary>
    /// Resolves the set of files to lint. <c>Resolved == false</c> signals a fatal error
    /// (caller should exit 2); an empty <c>Targets</c> with <c>Resolved == true</c> just means
    /// there was nothing to lint.
    /// </summary>
    public static (bool Resolved, IEnumerable<string> Targets) ResolveTargets(CliOptions opts)
    {
        if (opts.Files.Count > 0) return (true, Filter(opts.Files, opts));

        if (opts.Global)
        {
            return (true, Filter(
                Directory.EnumerateFiles(opts.Root, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !IsGenerated(f)),
                opts));
        }

        if (!GitResolver.IsGitRepo(opts.Root))
        {
            Console.Error.WriteLine(
                "Not a git repository. Use --global to lint all files, or pass explicit paths.");
            return (false, []);
        }

        var changed = opts.Unstaged
            ? GitResolver.GetChangedFiles(opts.Root)
            : GitResolver.GetStagedFiles(opts.Root);

        if (changed.Count == 0)
        {
            Console.WriteLine("No staged .cs files.");
            return (true, []);
        }

        return (true, Filter(changed, opts));
    }

    // Drop files matching any --exclude / .dependably-check exclude glob.
    public static IEnumerable<string> Filter(IEnumerable<string> files, CliOptions opts) =>
        opts.Exclude.Count == 0
            ? files
            : files.Where(f => !PathFilter.IsExcluded(f, opts.Root, opts.Exclude));

    public static bool IsGenerated(string path) =>
        path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
        path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
        path.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar);

    public static int InstallGitHook(string root)
    {
        if (!GitResolver.IsGitRepo(root))
        {
            Console.Error.WriteLine("Not a git repository.");
            return ExitUsageError;
        }

        var (existing, hooksDir) = GitResolver.GetHooksInfo(root);
        var hookPath = Path.Combine(hooksDir, "pre-commit");

        if (existing != null)
        {
            Console.WriteLine($"A pre-commit hook already exists at {hookPath}.");
            Console.Write("Overwrite? [y/N] ");
            if (Console.ReadLine()?.Trim().ToLowerInvariant() != "y")
            {
                Console.WriteLine("Aborted.");
                return 0;
            }
        }

        Directory.CreateDirectory(hooksDir);
        File.WriteAllText(hookPath, """
            #!/bin/sh
            if git diff --cached --name-only | grep -q '\.editorconfig$'; then
                echo "error: .editorconfig is staged. Review config changes before committing." >&2
                exit 1
            fi
            cslint --sast --strict --format text
            exit $?
            """);

        // Mark the hook executable via the managed API rather than spawning `chmod` off PATH (S4036).
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(hookPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        Console.WriteLine($"Installed pre-commit hook at {hookPath}");
        return 0;
    }

    public static CliOptions ParseOptions(string[] args)
    {
        var opts = new CliOptions();
        var positional = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            if (ParseFlag(opts, args[i])) continue;
            i = ParseValueOption(opts, args, i, positional);
        }

        if (opts.ProjectPath != null) opts.DeepMode = true;
        if (opts.ScanMode) opts.SastMode = true;
        opts.Files = positional;
        return opts;
    }

    // Boolean switches (no argument). Returns true if the token was consumed here.
    static bool ParseFlag(CliOptions opts, string arg)
    {
        switch (arg)
        {
            case "--help" or "-h": opts.ShowHelp = true; return true;
            case "--global" or "-g": opts.Global = true; return true;
            case "--unstaged": opts.Unstaged = true; return true;
            case "--fix": opts.Fix = true; return true;
            case "--strict" or "-s": opts.Strict = true; return true;
            case "--verbose" or "-v": opts.Verbose = true; return true;
            case "--install-hook": opts.InstallHook = true; return true;
            case "--sast": opts.SastMode = true; return true;
            case "--scan": opts.ScanMode = true; return true;
            case "--deep": opts.DeepMode = true; return true;
            case "--no-magic-numbers": opts.FlagMagicNumbers = false; return true;
            case "--no-bool-flags": opts.FlagBoolFlags = false; return true;
            case "--no-cancellation": opts.FlagCancellationToken = false; return true;
            default: return false;
        }
    }

    // Flags that consume the following argument, mapped to the setter applied to that value.
    static readonly Dictionary<string, Action<CliOptions, string>> ValueOptions = new()
    {
        ["--explain"] = (o, v) => o.ExplainFile = Path.GetFullPath(v),
        ["--project"] = (o, v) => o.ProjectPath = Path.GetFullPath(v),
        ["-p"] = (o, v) => o.ProjectPath = Path.GetFullPath(v),
        ["--format"] = SetFormat,
        ["-f"] = SetFormat,
        ["--root"] = (o, v) => o.Root = Path.GetFullPath(v),
        ["-r"] = (o, v) => o.Root = Path.GetFullPath(v),
        ["--config"] = (o, v) => o.ConfigPath = Path.GetFullPath(v),
        ["--exclude"] = (o, v) => o.Exclude.Add(v),
    };

    static void SetFormat(CliOptions opts, string value)
    {
        if (Enum.TryParse<OutputFormat>(value, true, out var fmt)) opts.Format = fmt;
    }

    // Options that take a following value (and bare positional paths). Returns the (possibly
    // advanced) index after consuming any value argument.
    static int ParseValueOption(CliOptions opts, string[] args, int i, List<string> positional)
    {
        if (ValueOptions.TryGetValue(args[i], out var apply))
        {
            if (++i < args.Length) apply(opts, args[i]);
            return i;
        }

        if (!args[i].StartsWith('-'))
            positional.Add(Path.GetFullPath(args[i]));
        return i;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            cslint v4 - C# code quality gate

            USAGE
              cslint [options] [files...]

            MODES
              (default)     EditorConfig enforcement only (syntactic, fast)
              --sast        + SAST: empty catches, SQL injection, fire-and-forget, console output,
                              hardcoded secrets, pragma suppression, Thread.Sleep in async, dynamic
              --deep        + semantic analysis via Roslyn compiler (requires --project)
              --scan        + opinionated pattern scan (implies --sast). For quantitative
                              metrics (complexity, coupling, maintainability) use codemetrics.

            FILES
              (default)     Staged .cs files (git diff --cached)
              --global, -g  All .cs files under root (skips bin/obj)
              --unstaged    Include unstaged changes
              --exclude <g> Skip paths matching glob (repeatable; substring if no wildcard).
                            Also read from .dependably-check (common.exclude / cslint.exclude).

            DEEP MODE
              --project, -p Path to .csproj or .sln (implies --deep)
                            Run after dotnet build. Enables dotnet_diagnostic.CSXXXX.severity.

            OUTPUT
              --format, -f  text (default) | json | github
              --strict, -s  Treat warnings as errors (exit 1)
              --fix         Auto-fix EditorConfig violations where possible
              --explain <f> Show which rules apply to a file and why

            SCAN OPTIONS (--scan)
              --no-magic-numbers    Disable OP004 (magic numbers)
              --no-bool-flags       Disable OP005 (boolean flag arguments)
              --no-cancellation     Disable OP006 (missing CancellationToken)

            SUPPRESSING / RETUNING FINDINGS
              Any rule's severity can be set per file/glob from .editorconfig:
                dotnet_diagnostic.SAST002.severity = none      # silence (e.g. console output in a CLI)
                dotnet_diagnostic.OP004.severity   = error     # promote
              Levels: none/silent (drop) | suggestion | warning | error.

            CONFIG
              --config <f>  Path to a .dependably-check JSON file. When omitted, it is discovered
                            by walking up from --root to the repo boundary. The `cslint` (and
                            shared `common`) section can set `strict` and the `scan` toggles;
                            CLI flags above take precedence.

            OTHER
              --install-hook  Install pre-commit hook (runs --sast --strict)
              --verbose, -v   Show workspace diagnostics in --deep mode
              --root, -r      Project root (default: cwd)
              --help, -h      Show this help

            RULE IDs
              EC001-EC006   Universal editorconfig (indent, trailing ws, newline, EOL, line length, charset)
              FMT           Roslyn formatter (csharp_space_*, csharp_indent_*, csharp_new_line_*)
              CS010-CS040   C# and .NET style rules
              CS010-S, CS033-S  Semantic variants (--deep only)
              SAST001-008   Security and safety checks
              OP004-006     Opinionated pattern checks: magic numbers, flag args, CancellationToken (--scan)
            """);
    }
}

sealed class CliOptions
{
    public bool ShowHelp { get; set; }
    public bool Global { get; set; }
    public bool Unstaged { get; set; }
    public bool Fix { get; set; }
    public bool Strict { get; set; }
    public bool Verbose { get; set; }
    public bool InstallHook { get; set; }
    public bool SastMode { get; set; }
    public bool ScanMode { get; set; }
    public bool DeepMode { get; set; }
    public string? ExplainFile { get; set; }
    public string? ProjectPath { get; set; }
    public OutputFormat Format { get; set; } = OutputFormat.Text;
    public string Root { get; set; } = Directory.GetCurrentDirectory();
    public string? ConfigPath { get; set; }
    public List<string> Files { get; set; } = [];
    public List<string> Exclude { get; set; } = [];

    public bool FlagMagicNumbers { get; set; } = true;
    public bool FlagBoolFlags { get; set; } = true;
    public bool FlagCancellationToken { get; set; } = true;
}
