using System.Reflection;
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

        if (options.UnknownOption != null)
        {
            Console.Error.WriteLine($"unknown option: '{options.UnknownOption}'");
            return ExitUsageError;
        }

        if (options.OptionError != null)
        {
            Console.Error.WriteLine(options.OptionError);
            return ExitUsageError;
        }

        if (options.ShowHelp) { PrintHelp(); return 0; }
        if (options.ShowVersion) { PrintVersion(); return 0; }
        if (options.InstallHook) return InstallGitHook(options.Root);

        // --deep without --project silently degraded to syntactic-only analysis; reject it early.
        if (options.DeepMode && options.ProjectPath == null)
        {
            Console.Error.WriteLine("--deep requires --project <path>");
            return ExitUsageError;
        }

        EnsureMsBuildOrFallback(options);

        // Shared .dependably-check config (repo root). CLI flags win: the scan toggles and the
        // gate live in the file, and a CLI --fail-on overrides the file's gate entirely.
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

    // Fold the .dependably-check config into the options. The opinionated scan rules are toggled
    // by the file's `scan` section (and by .editorconfig severity overrides). The CI gate comes
    // from CLI --fail-on, which wins outright; otherwise the default (errors gate) plus the
    // file's `strict` (warnings gate too) applies.
    static void ApplyConfig(CliOptions options, CsLintConfig config)
    {
        options.FlagMagicNumbers = options.FlagMagicNumbers && config.ScanMagicNumbers;
        options.FlagBoolFlags = options.FlagBoolFlags && config.ScanBoolFlags;
        options.FlagCancellationToken = options.FlagCancellationToken && config.ScanCancellation;
        options.Exclude = [.. options.Exclude, .. config.Exclude]; // CLI globs + config globs

        if (options.FailOn.Count == 0)
        {
            // No CLI gate: errors always gate; the config's `strict` adds warning-gating.
            options.FailOn.Add(new FailOnRule(FailOnKind.Severity, RankHigh));
            if (config.Strict)
                options.FailOn.Add(new FailOnRule(FailOnKind.Severity, RankLow));
        }
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

    // Print the report + summary line and return the process exit code: 1 when the --fail-on
    // gate trips, else 0.
    static int ReportAndExit(
        IReadOnlyList<Diagnostic> allDiagnostics, CliOptions options, Summary summary, LintMode mode)
    {
        var totalFiles = summary.FilesChecked;
        var errors = allDiagnostics.Count(d => d.Severity == Severity.Error);
        var warnings = allDiagnostics.Count(d => d.Severity == Severity.Warning);
        var infos = allDiagnostics.Count(d => d.Severity == Severity.Info);

        // Compute the real exit code up front so the JSON envelope's summary.exitCode can carry
        // the exact value the process will return.
        var tripped = GateTripped(options.FailOn, errors, warnings, infos, allDiagnostics.Count);
        var exitCode = tripped ? 1 : 0;

        Reporter.Write(allDiagnostics, options.Format, options.Root, totalFiles, exitCode, Version());

        // The human summary line must not land on stdout for machine formats, or it corrupts
        // the JSON document / GitHub-annotation stream. Send it to stderr there instead.
        var summaryWriter = options.Format == OutputFormat.Human ? Console.Out : Console.Error;
        summaryWriter.WriteLine($"Checked {totalFiles} file{(totalFiles != 1 ? "s" : "")}. " +
                          $"Mode: {ModeLabel(mode)}" +
                          (options.DeepMode && options.ProjectPath != null ? " + semantic" : "") + ".");

        // When the gate trips without any errors, the trigger (warnings, or a count threshold) is
        // not otherwise obvious from the report — say so explicitly.
        if (tripped && errors == 0)
        {
            const string note = "--fail-on: gate tripped (no errors, but a severity/count threshold was breached).";
            if (options.Format == OutputFormat.Human)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(note);
                Console.ResetColor();
            }
            else
            {
                Console.Error.WriteLine(note);
            }
        }

        return exitCode;
    }

    // Evaluate the CI gate: exit 1 if ANY --fail-on rule trips. A severity rule trips when the
    // most severe finding present is at-or-above the rule's level (cslint ranks error=high,
    // warning=low, info=info); a count rule trips when the total finding count exceeds its threshold.
    static bool GateTripped(IReadOnlyList<FailOnRule> rules, int errors, int warnings, int infos, int total)
    {
        int maxRank = MaxSeverityRank(errors, warnings, infos);
        return rules.Any(r => r.Kind == FailOnKind.Severity
            ? maxRank >= r.Threshold
            : total > r.Threshold);
    }

    // The rank of the most severe finding present (or -1 when there are none).
    static int MaxSeverityRank(int errors, int warnings, int infos)
    {
        if (errors > 0) return RankHigh;
        if (warnings > 0) return RankLow;
        if (infos > 0) return RankInfo;
        return -1;
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
        if (opts.Files.Count > 0)
            return ResolveExplicitTargets(opts);

        if (opts.Global)
        {
            // A typo'd --root must fail loudly via the usage-error path, not throw an unhandled
            // DirectoryNotFoundException out of EnumerateFiles.
            if (!Directory.Exists(opts.Root))
            {
                Console.Error.WriteLine(
                    $"Root directory does not exist: {opts.Root}. Pass an existing path with --root.");
                return (false, []);
            }

            return (true, Filter(
                Directory.EnumerateFiles(opts.Root, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !IsGeneratedTarget(f)),
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

    // Resolve explicitly-passed paths (positional args), expanding globs. A literal path that
    // matches nothing is a fatal "path not found"; an unmatched wildcard just contributes nothing.
    static (bool Resolved, IEnumerable<string> Targets) ResolveExplicitTargets(CliOptions opts)
    {
        var expanded = new List<string>();
        var notFound = new List<string>();
        foreach (var path in opts.Files)
        {
            var matches = PathFilter.ExpandTarget(path).ToList();
            if (matches.Count == 0 && !path.Contains('*') && !path.Contains('?'))
                notFound.Add(path);
            expanded.AddRange(matches);
        }

        if (notFound.Count > 0)
        {
            foreach (var p in notFound)
                Console.Error.WriteLine($"Path not found: {p}");
            return (false, []);
        }

        return (true, Filter(expanded.Where(f => !IsGeneratedTarget(f)).Distinct(), opts));
    }

    // Drop files matching any --exclude / .dependably-check exclude glob.
    public static IEnumerable<string> Filter(IEnumerable<string> files, CliOptions opts) =>
        opts.Exclude.Count == 0
            ? files
            : files.Where(f => !PathFilter.IsExcluded(f, opts.Root, opts.Exclude));

    // Filename suffixes emitted by code generators / tooling; these sources are never hand-edited
    // and linting them just produces noise.
    static readonly string[] GeneratedSuffixes =
    [
        ".designer.cs", ".g.cs", ".g.i.cs", ".generated.cs",
        ".assemblyinfo.cs", ".globalusings.g.cs",
    ];

    // Cheap, IO-free check: build/VCS directories and well-known generated filename suffixes.
    public static bool IsGenerated(string path) =>
        path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
        path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
        path.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar) ||
        GeneratedSuffixes.Any(s => path.EndsWith(s, StringComparison.OrdinalIgnoreCase));

    // The full generated-file test used when enumerating real files on disk: the path/suffix
    // check above, plus a sniff of the file head for an `<auto-generated` marker (the convention
    // many emitters use, e.g. `// <auto-generated/>`).
    static bool IsGeneratedTarget(string path) =>
        IsGenerated(path) || HasAutoGeneratedHeader(path);

    static bool HasAutoGeneratedHeader(string path)
    {
        // Emitters put the <auto-generated marker in the file header; sniffing the first few
        // lines is enough and avoids reading large hand-written sources end to end.
        const int headerLinesToSniff = 5;
        try
        {
            using var reader = new StreamReader(path);
            for (int line = 0; line < headerLinesToSniff; line++)
            {
                var text = reader.ReadLine();
                if (text == null) break;
                if (text.Contains("<auto-generated", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (IOException) { /* unreadable file: let the normal lint path surface it */ }
        catch (UnauthorizedAccessException) { /* same: defer to the normal lint path */ }

        return false;
    }

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
            cslint --sast --fail-on severity=warning --format human
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
            case "--version": opts.ShowVersion = true; return true;
            case "--global" or "-g": opts.Global = true; return true;
            case "--unstaged": opts.Unstaged = true; return true;
            case "--fix": opts.Fix = true; return true;
            case "--verbose" or "-v": opts.Verbose = true; return true;
            case "--install-hook": opts.InstallHook = true; return true;
            case "--sast": opts.SastMode = true; return true;
            case "--scan": opts.ScanMode = true; return true;
            case "--deep": opts.DeepMode = true; return true;
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
        ["--fail-on"] = ParseFailOn,
    };

    static void SetFormat(CliOptions opts, string value)
    {
        var fmt = ParseFormat(value);
        if (fmt is null)
            opts.OptionError ??= $"invalid --format '{value}' (expected human|json|github)";
        else
            opts.Format = fmt.Value;
    }

    // Explicit switch rather than Enum.TryParse: TryParse accepts numeric strings
    // ("--format 1") and we want only the documented tokens.
    static OutputFormat? ParseFormat(string value) => value.Trim().ToLowerInvariant() switch
    {
        "human" => OutputFormat.Human,
        "json" => OutputFormat.Json,
        "github" => OutputFormat.GitHub,
        _ => null,
    };

    // The shared suite severity ladder (info=0 .. critical=4). cslint itself only emits two of
    // these — an error is `high`, a warning is `low` — but --fail-on accepts the full ladder.
    const int RankInfo = 0;
    const int RankLow = 1;
    const int RankModerate = 2;
    const int RankHigh = 3;
    const int RankCritical = 4;

    // Parse one `--fail-on <key>=<value>` rule (repeatable). Recognised keys:
    //   severity=<critical|high|moderate|low|info>  (also accepts the raw words error/warning)
    //   count=<N>
    // A malformed key or value records an OptionError so the run exits 2 (usage error).
    static void ParseFailOn(CliOptions opts, string value)
    {
        var eq = value.IndexOf('=');
        if (eq <= 0)
        {
            opts.OptionError ??= $"invalid --fail-on '{value}' (expected key=value, e.g. severity=warning)";
            return;
        }

        var key = value[..eq].Trim().ToLowerInvariant();
        var val = value[(eq + 1)..].Trim();
        switch (key)
        {
            case "severity":
                var rank = SeverityRank(val);
                if (rank is null)
                    opts.OptionError ??= $"invalid --fail-on severity '{val}' (expected critical|high|moderate|low|info)";
                else
                    opts.FailOn.Add(new FailOnRule(FailOnKind.Severity, rank.Value));
                break;
            case "count":
                if (int.TryParse(val, out var n) && n >= 0)
                    opts.FailOn.Add(new FailOnRule(FailOnKind.Count, n));
                else
                    opts.OptionError ??= $"invalid --fail-on count '{val}' (expected a non-negative integer)";
                break;
            default:
                opts.OptionError ??= $"unknown --fail-on key '{key}' (expected 'severity' or 'count')";
                break;
        }
    }

    // Map a severity token to its rank on the shared suite ladder (info=0 .. critical=4). Accepts
    // the canonical ladder words plus the P2a raw words error (=high) and warning (=low).
    static int? SeverityRank(string token) => token.Trim().ToLowerInvariant() switch
    {
        "critical" => RankCritical,
        "high" or "error" => RankHigh,
        "moderate" => RankModerate,
        "low" or "warning" => RankLow,
        "info" => RankInfo,
        _ => null,
    };

    // Options that take a following value (and bare positional paths). Returns the (possibly
    // advanced) index after consuming any value argument.
    static int ParseValueOption(CliOptions opts, string[] args, int i, List<string> positional)
    {
        if (ValueOptions.TryGetValue(args[i], out var apply))
        {
            if (++i >= args.Length)
            {
                opts.OptionError ??= $"option '{args[i - 1]}' requires a value";
                return i;
            }
            apply(opts, args[i]);
            return i;
        }

        if (args[i].StartsWith('-'))
        {
            // Unrecognized token that looks like a flag (e.g. a typo'd --strict, or --bogus).
            // Record it so the caller can reject the invocation instead of silently ignoring it,
            // which would otherwise exit 0 with the intended behavior quietly disabled.
            opts.UnknownOption ??= args[i];
            return i;
        }

        positional.Add(Path.GetFullPath(args[i]));
        return i;
    }

    /// <summary>The tool version, read from the assembly's informational/package version.</summary>
    public static string Version()
    {
        var asm = typeof(Cli).Assembly;
        // Prefer the informational version (maps to &lt;Version&gt; in the csproj); strip any
        // build/SourceLink metadata suffix (e.g. "4.1.0+abc123") for a clean display string.
        var informational = asm
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return asm.GetName().Version?.ToString() ?? "unknown";
    }

    public static void PrintVersion() => Console.WriteLine($"cslint {Version()}");

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
              --format, -f  human (default) | json | github
              --fix         Auto-fix EditorConfig violations where possible
              --explain <f> Show which rules apply to a file and why

            CI GATE
              --fail-on <k>=<v>  Exit 1 when the gate trips (repeatable). Keys:
                severity=<critical|high|moderate|low|info>  Trip if any finding is at-or-above the
                                                            level (cslint: error=high, warning=low).
                count=<N>                                   Trip when total findings exceed N.
              Default (no --fail-on): errors gate (exit 1), warnings don't.
              `--fail-on severity=warning` gates on warnings too (the former --strict).

            DISABLING / RETUNING FINDINGS
              Any rule's severity can be set per file/glob from .editorconfig:
                dotnet_diagnostic.SAST002.severity = none      # silence (e.g. console output in a CLI)
                dotnet_diagnostic.OP004.severity   = none      # disable OP004 (magic numbers)
                dotnet_diagnostic.OP004.severity   = error     # or promote it
              Levels: none/silent (drop) | suggestion | warning | error.
              The opinionated scan rules (OP004/005/006) can also be toggled off in
              .dependably-check (cslint.scan.{magicNumbers,boolFlags,cancellation}).

            CONFIG
              --config <f>  Path to a .dependably-check JSON file. When omitted, it is discovered
                            by walking up from --root to the repo boundary. The `cslint` (and
                            shared `common`) section can set `strict` (warnings gate too) and the
                            `scan` toggles; a CLI --fail-on overrides the file's gate.

            OTHER
              --install-hook  Install pre-commit hook (runs --sast --fail-on severity=warning)
              --verbose, -v   Show workspace diagnostics in --deep mode
              --root, -r      Project root (default: cwd)
              --help, -h      Show this help
              --version       Print the cslint version

            RULE IDs
              EC001-EC006   Universal editorconfig (indent, trailing ws, newline, EOL, line length, charset)
              FMT           Roslyn formatter (csharp_space_*, csharp_indent_*, csharp_new_line_*)
              CS010-CS040   C# and .NET style rules
              CS010-S, CS033-S  Semantic variants (--deep only)
              IDE0005       Unused using directives (--deep only)
              SAST001-008   Security and safety checks
              OP004-006     Opinionated pattern checks: magic numbers, flag args, CancellationToken (--scan)
            """);
    }
}

sealed class CliOptions
{
    public bool ShowHelp { get; set; }
    public bool ShowVersion { get; set; }
    public bool Global { get; set; }
    public bool Unstaged { get; set; }
    public bool Fix { get; set; }
    public bool Verbose { get; set; }
    public bool InstallHook { get; set; }
    public bool SastMode { get; set; }
    public bool ScanMode { get; set; }
    public bool DeepMode { get; set; }
    public string? ExplainFile { get; set; }
    public string? ProjectPath { get; set; }
    public OutputFormat Format { get; set; } = OutputFormat.Human;
    public string Root { get; set; } = Directory.GetCurrentDirectory();
    public string? ConfigPath { get; set; }
    public List<string> Files { get; set; } = [];
    public List<string> Exclude { get; set; } = [];

    // The CI gate rules from --fail-on (repeatable). Empty after parsing means "use the default"
    // (errors gate; the config's `strict` may add warning-gating) — filled in by ApplyConfig.
    public List<FailOnRule> FailOn { get; set; } = [];

    // First unrecognized token that looked like a flag (starts with '-'). Non-null => usage error.
    public string? UnknownOption { get; set; }

    // First malformed option value (e.g. a bad --fail-on key/value). Non-null => usage error.
    public string? OptionError { get; set; }

    public bool FlagMagicNumbers { get; set; } = true;
    public bool FlagBoolFlags { get; set; } = true;
    public bool FlagCancellationToken { get; set; } = true;
}

// The kind of CI gate a --fail-on rule expresses.
enum FailOnKind { Severity, Count }

// One --fail-on gate rule. For Severity, Threshold is a ladder rank (info=0 .. critical=4); for
// Count, Threshold is the maximum allowed number of findings (trip when total exceeds it).
readonly record struct FailOnRule(FailOnKind Kind, int Threshold);
