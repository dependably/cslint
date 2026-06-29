namespace CsLint;

static class GitResolver
{
    public static IReadOnlyList<string> GetStagedFiles(string root)
    {
        var output = Run("diff --cached --name-only --diff-filter=d", root);
        return ParsePaths(output, root);
    }

    public static IReadOnlyList<string> GetUnstagedFiles(string root)
    {
        var output = Run("diff --name-only --diff-filter=d", root);
        return ParsePaths(output, root);
    }

    public static IReadOnlyList<string> GetChangedFiles(string root)
    {
        var staged = GetStagedFiles(root);
        var unstaged = GetUnstagedFiles(root);
        return staged.Union(unstaged).ToList();
    }

    public static bool IsGitRepo(string root)
    {
        try
        {
            Run("rev-parse --git-dir", root);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static (string? Hook, string HooksDir) GetHooksInfo(string root)
    {
        var hooksDir = Run("rev-parse --git-path hooks", root).Trim();
        var absHooksDir = Path.IsPathRooted(hooksDir)
            ? hooksDir
            : Path.Combine(root, hooksDir);
        var hook = Path.Combine(absHooksDir, "pre-commit");
        return (File.Exists(hook) ? hook : null, absHooksDir);
    }

    static List<string> ParsePaths(string output, string root)
    {
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(rel => Path.GetFullPath(Path.Combine(root, rel)))
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(File.Exists)
            .ToList();
    }

    // Resolve git to an absolute path once, from PATH, rather than letting Process search PATH
    // at launch — so the executable we run is the one we picked, not whatever a later-prepended
    // PATH entry resolves to (S4036 / CWE-426 untrusted search path).
    static readonly string GitExecutable = ResolveGitExecutable();

    static string ResolveGitExecutable()
    {
        var exe = OperatingSystem.IsWindows() ? "git.exe" : "git";
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch (ArgumentException)
            {
                // Skip malformed PATH entries (invalid path characters).
            }
        }

        return exe; // Not found on PATH; fall back to OS resolution at launch.
    }

    static string Run(string arguments, string workingDir)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(GitExecutable, arguments)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var err = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"git {arguments} failed: {err}");
        }

        return output;
    }
}
