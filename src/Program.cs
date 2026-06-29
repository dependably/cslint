using CsLint;
using CsLint.Rules.Opinionated;

var options = Cli.ParseOptions(args);

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
options.Exclude               = [.. options.Exclude, .. config.Exclude]; // CLI globs + config globs

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

var mode    = Cli.DetermineMode(options);
var targets = Cli.ResolveTargets(options);
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
                  $"Mode: {Cli.ModeLabel(mode)}" +
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
          CS011         Expression body preferences
          SAST001-SAST008  Security / reliability patterns
          OP004-OP006   Opinionated scan rules
          IDE0005       Unused using directives (requires --deep)
        """);
}
