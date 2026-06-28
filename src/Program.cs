using CsLint;
using CsLint.Rules.Opinionated;

var options = ParseOptions(args);

if (options.ShowHelp) { PrintHelp(); return 0; }
if (options.InstallHook) return InstallGitHook(options.Root);

if (options.DeepMode && !SemanticEngine.TryRegisterMsBuild(out var msbuildError))
{
    Console.Error.WriteLine($"Cannot load MSBuild: {msbuildError}");
    Console.Error.WriteLine("Ensure the .NET SDK is installed. Falling back to syntactic analysis.");
    options.DeepMode = false;
}

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
    return 2;
}

options.Strict                = options.Strict || config.Strict;
options.FlagMagicNumbers      = options.FlagMagicNumbers && config.ScanMagicNumbers;
options.FlagBoolFlags         = options.FlagBoolFlags && config.ScanBoolFlags;
options.FlagCancellationToken = options.FlagCancellationToken && config.ScanCancellation;

var scanConfig = new ScanConfig(
    FlagMagicNumbers:             options.FlagMagicNumbers,
    FlagBooleanParameters:        options.FlagBoolFlags,
    FlagMissingCancellationToken: options.FlagCancellationToken);

var engine = new LintEngine(scanConfig);

if (options.ExplainFile != null)
{
    Console.WriteLine(engine.ExplainFile(options.ExplainFile));
    return 0;
}

var mode    = DetermineMode(options);
var targets = ResolveTargets(options);
if (targets == null) return 2;

var summary = await engine.LintFilesAsync(targets, mode, options.Fix);

IReadOnlyList<Diagnostic> allDiagnostics = summary.Diagnostics.ToList();

if (options.DeepMode && options.ProjectPath != null)
{
    var semanticEngine = new SemanticEngine(engine.Loader);
    var semanticDiags  = await semanticEngine.AnalyzeProjectAsync(
        options.ProjectPath, verbose: options.Verbose);
    allDiagnostics = [..allDiagnostics, ..semanticDiags];
}

Reporter.Write(allDiagnostics, options.Format, options.Root);

var totalFiles = summary.FilesChecked;
var errors     = allDiagnostics.Count(d => d.Severity == Severity.Error);
var warnings   = allDiagnostics.Count(d => d.Severity == Severity.Warning);

Console.WriteLine($"Checked {totalFiles} file{(totalFiles != 1 ? "s" : "")}. " +
                  $"Mode: {ModeLabel(mode)}" +
                  (options.DeepMode ? " + semantic" : "") + ".");

if (errors > 0 || (options.Strict && warnings > 0))
{
    if (options.Strict && warnings > 0 && errors == 0)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("--strict: warnings treated as errors.");
        Console.ResetColor();
    }
    return 1;
}

return 0;

static LintMode DetermineMode(CliOptions opts)
{
    if (opts.ScanMode) return LintMode.All;
    if (opts.SastMode) return LintMode.EditorConfigAndSast;
    return LintMode.EditorConfig;
}

static string ModeLabel(LintMode mode) => mode switch
{
    LintMode.EditorConfigAndSast => "editorconfig+sast",
    LintMode.All                 => "editorconfig+sast+scan",
    _                            => "editorconfig"
};

static IEnumerable<string>? ResolveTargets(CliOptions opts)
{
    if (opts.Files.Count > 0) return opts.Files;

    if (opts.Global)
    {
        return Directory.EnumerateFiles(opts.Root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsGenerated(f));
    }

    if (!GitResolver.IsGitRepo(opts.Root))
    {
        Console.Error.WriteLine(
            "Not a git repository. Use --global to lint all files, or pass explicit paths.");
        return null;
    }

    var changed = opts.Unstaged
        ? GitResolver.GetChangedFiles(opts.Root)
        : GitResolver.GetStagedFiles(opts.Root);

    if (changed.Count == 0)
    {
        Console.WriteLine("No staged .cs files.");
        return [];
    }

    return changed;
}

static bool IsGenerated(string path) =>
    path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
    path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
    path.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar);

static int InstallGitHook(string root)
{
    if (!GitResolver.IsGitRepo(root))
    {
        Console.Error.WriteLine("Not a git repository.");
        return 2;
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

    if (!OperatingSystem.IsWindows())
        System.Diagnostics.Process.Start("chmod", $"+x {hookPath}")?.WaitForExit();

    Console.WriteLine($"Installed pre-commit hook at {hookPath}");
    return 0;
}

static CliOptions ParseOptions(string[] args)
{
    var opts = new CliOptions();
    var positional = new List<string>();

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--help" or "-h":       opts.ShowHelp = true; break;
            case "--global" or "-g":     opts.Global = true; break;
            case "--unstaged":           opts.Unstaged = true; break;
            case "--fix":                opts.Fix = true; break;
            case "--strict" or "-s":     opts.Strict = true; break;
            case "--verbose" or "-v":    opts.Verbose = true; break;
            case "--install-hook":       opts.InstallHook = true; break;
            case "--sast":               opts.SastMode = true; break;
            case "--scan":               opts.ScanMode = true; break;
            case "--deep":               opts.DeepMode = true; break;
            case "--no-magic-numbers":   opts.FlagMagicNumbers = false; break;
            case "--no-bool-flags":      opts.FlagBoolFlags = false; break;
            case "--no-cancellation":    opts.FlagCancellationToken = false; break;

            case "--explain":
                if (++i < args.Length) opts.ExplainFile = Path.GetFullPath(args[i]);
                break;
            case "--project" or "-p":
                if (++i < args.Length) opts.ProjectPath = Path.GetFullPath(args[i]);
                break;
            case "--format" or "-f":
                if (++i < args.Length && Enum.TryParse<OutputFormat>(args[i], true, out var fmt))
                    opts.Format = fmt;
                break;
            case "--root" or "-r":
                if (++i < args.Length) opts.Root = Path.GetFullPath(args[i]);
                break;
            case "--config":
                if (++i < args.Length) opts.ConfigPath = Path.GetFullPath(args[i]);
                break;

            default:
                if (!args[i].StartsWith('-'))
                    positional.Add(Path.GetFullPath(args[i]));
                break;
        }
    }

    if (opts.ProjectPath != null) opts.DeepMode = true;
    if (opts.ScanMode) opts.SastMode = true;
    opts.Files = positional;
    return opts;
}

static void PrintHelp()
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

        CONFIG
          --config <f>  Path to a .dependably-check JSON file. When omitted, it is discovered
                        by walking up from --root to the repo boundary. The `cslint` (and
                        shared `common`) section can set `strict` and the `scan` toggles;
                        CLI flags above take precedence. See SUITE.md for the schema.

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

sealed class CliOptions
{
    public bool ShowHelp      { get; set; }
    public bool Global        { get; set; }
    public bool Unstaged      { get; set; }
    public bool Fix           { get; set; }
    public bool Strict        { get; set; }
    public bool Verbose       { get; set; }
    public bool InstallHook   { get; set; }
    public bool SastMode      { get; set; }
    public bool ScanMode      { get; set; }
    public bool DeepMode      { get; set; }
    public string? ExplainFile { get; set; }
    public string? ProjectPath { get; set; }
    public OutputFormat Format { get; set; } = OutputFormat.Text;
    public string Root         { get; set; } = Directory.GetCurrentDirectory();
    public string? ConfigPath  { get; set; }
    public List<string> Files  { get; set; } = [];

    public bool FlagMagicNumbers      { get; set; } = true;
    public bool FlagBoolFlags         { get; set; } = true;
    public bool FlagCancellationToken { get; set; } = true;
}
