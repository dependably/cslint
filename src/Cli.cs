namespace CsLint;

// Testable CLI helpers extracted from Program.cs top-level statements.
// Exposed as internal so the test project (via InternalsVisibleTo) can call them directly
// without reflection against the compiler-generated entry-point class.
internal static class Cli
{
    public static LintMode DetermineMode(CliOptions opts)
    {
        if (opts.ScanMode) return LintMode.All;
        if (opts.SastMode) return LintMode.EditorConfigAndSast;
        return LintMode.EditorConfig;
    }

    public static string ModeLabel(LintMode mode) => mode switch
    {
        LintMode.EditorConfigAndSast => "editorconfig+sast",
        LintMode.All                 => "editorconfig+sast+scan",
        _                            => "editorconfig"
    };

    public static IEnumerable<string>? ResolveTargets(CliOptions opts)
    {
        if (opts.Files.Count > 0) return Filter(opts.Files, opts);

        if (opts.Global)
        {
            return Filter(
                Directory.EnumerateFiles(opts.Root, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !IsGenerated(f)),
                opts);
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

        return Filter(changed, opts);
    }

    // Drop files matching any --exclude / .dependably-check exclude glob.
    static IEnumerable<string> Filter(IEnumerable<string> files, CliOptions opts) =>
        opts.Exclude.Count == 0
            ? files
            : files.Where(f => !PathFilter.IsExcluded(f, opts.Root, opts.Exclude));

    public static bool IsGenerated(string path) =>
        path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
        path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) ||
        path.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar);

    public static CliOptions ParseOptions(string[] args)
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
                case "--exclude":
                    if (++i < args.Length) opts.Exclude.Add(args[i]);
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
}
