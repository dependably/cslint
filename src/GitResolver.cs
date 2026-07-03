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

    /// <summary>
    /// Returns the subset of <paramref name="files"/> (absolute paths) that git's ignore rules
    /// exclude, by piping them through <c>git check-ignore</c>. Delegating to git means nested
    /// <c>.gitignore</c> files, negation (<c>!</c>), anchoring, and <c>**</c> are all honored
    /// exactly as git does — semantics a hand-rolled matcher gets subtly wrong. Returns an empty
    /// set when <paramref name="root"/> is not a git repo, git is unavailable, or git errors, so
    /// the caller cleanly falls back to the built-in directory excludes alone.
    /// </summary>
    public static IReadOnlySet<string> GetIgnoredFiles(string root, IReadOnlyList<string> files)
    {
        var ignored = new HashSet<string>(StringComparer.Ordinal);
        if (files.Count == 0 || !IsGitRepo(root))
            return ignored;

        try
        {
            // --stdin -z: read NUL-separated paths on stdin, echo the ignored ones back NUL-separated
            // (no shell quoting of odd path characters). git check-ignore echoes each path verbatim,
            // so the absolute paths we send come back byte-for-byte and match by ordinal equality.
            var input = string.Join('\0', files);
            var (output, exitCode) = RunWithInput("check-ignore --stdin -z", root, input);

            // Exit 0 = at least one path ignored; 1 = none ignored (both are success). 128 = a real
            // error (e.g. not a work tree) — treat as "no gitignore data" and fall back.
            if (exitCode == 128)
                return ignored;

            foreach (var path in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                ignored.Add(path);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // git could not be launched: no gitignore filtering, caller falls back.
        }

        return ignored;
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

    // Run git with data on stdin, returning (stdout, exitCode) without throwing on a non-zero
    // exit (check-ignore uses exit 1 to mean "nothing matched", which is not an error). stdout is
    // drained asynchronously while stdin is written so a large pipe in both directions can't
    // deadlock on a full OS buffer.
    static (string Output, int ExitCode) RunWithInput(string arguments, string workingDir, string stdin)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(GitExecutable, arguments)
        {
            WorkingDirectory = workingDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        process.StandardInput.Write(stdin);
        process.StandardInput.Close();
        process.WaitForExit();

        return (outputTask.GetAwaiter().GetResult(), process.ExitCode);
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
