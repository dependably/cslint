namespace CsLint;

/// <summary>
/// Cycle-safe recursive enumeration of <c>.cs</c> files, used for <c>--global</c> and directory
/// targets. It exists because <see cref="Directory.EnumerateFiles(string, string, SearchOption)"/>
/// with <see cref="SearchOption.AllDirectories"/> follows directory symlinks/junctions — the
/// source of the <c>node_modules/node_modules/…</c> cycles that made a single run walk 84,020
/// files and report the same finding dozens of times. This walker instead:
/// <list type="bullet">
///   <item>never descends into a directory symlink/junction (a reparse point), and tracks the
///   canonical path of every directory it enters so a cycle can never revisit one; and</item>
///   <item>prunes a built-in set of directory names (<c>node_modules</c>, <c>bin</c>, <c>obj</c>,
///   <c>.git</c>, <c>.claude</c>, <c>packages</c>) even under <c>--global</c>, so vendored
///   dependencies, build output, VCS metadata, and throwaway agent worktrees don't drown
///   first-party code. Pass <paramref name="applyDefaultExcludes"/> = <c>false</c> (the
///   <c>--no-default-excludes</c> escape hatch) to walk everything.</item>
/// </list>
/// The returned file paths are de-duplicated by canonical path so a file reachable by more than
/// one route is linted (and reported) once.
/// </summary>
static class FileWalker
{
    // Directory names pruned by default even under --global: vendored dependencies, build output,
    // VCS metadata, and throwaway agent worktrees — never hand-edited first-party source.
    public static readonly IReadOnlyList<string> DefaultExcludedDirectories =
        ["node_modules", "bin", "obj", ".git", ".claude", "packages"];

    static readonly HashSet<string> DefaultExcludedSet =
        new(DefaultExcludedDirectories, StringComparer.OrdinalIgnoreCase);

    /// <summary>The result of a walk: the <c>.cs</c> files to lint, and how many were skipped
    /// because they lived under a default-excluded directory.</summary>
    public sealed record Result(IReadOnlyList<string> Files, int SkippedFiles);

    public static Result Walk(string root, bool applyDefaultExcludes)
    {
        var files = new List<string>();
        var seenFiles = new HashSet<string>(StringComparer.Ordinal);
        // Canonical paths of directories already entered — a second line of defence against cycles
        // on top of never following symlinks, and it also stops two routes to the same real
        // directory from enumerating its files twice.
        var visitedDirs = new HashSet<string>(StringComparer.Ordinal);
        int skipped = 0;

        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            if (!visitedDirs.Add(Canonicalize(dir))) continue;

            foreach (var file in SafeEnumerateFiles(dir))
                if (seenFiles.Add(Canonicalize(file)))
                    files.Add(file);

            foreach (var sub in SafeEnumerateDirectories(dir))
            {
                // Never follow a directory symlink/junction: that is how the node_modules cycles
                // formed. A skipped symlink is not counted (its real target is walked elsewhere,
                // or is itself excluded).
                if (IsReparsePoint(sub)) continue;

                if (applyDefaultExcludes && DefaultExcludedSet.Contains(Path.GetFileName(sub)))
                {
                    skipped += CountCSharpFiles(sub, visitedDirs);
                    continue;
                }

                stack.Push(sub);
            }
        }

        return new Result(files, skipped);
    }

    // Count (but do not collect) the .cs files under a pruned directory so the run can report
    // "Skipped N files in default-excluded paths". Shares the caller's visited-directory set and
    // never follows symlinks, so a pruned node_modules with internal cycles can't blow up.
    static int CountCSharpFiles(string dir, HashSet<string> visitedDirs)
    {
        int count = 0;
        var stack = new Stack<string>();
        stack.Push(dir);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visitedDirs.Add(Canonicalize(current))) continue;

            count += SafeEnumerateFiles(current).Count();

            foreach (var sub in SafeEnumerateDirectories(current))
                if (!IsReparsePoint(sub))
                    stack.Push(sub);
        }

        return count;
    }

    static IEnumerable<string> SafeEnumerateFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*.cs"); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return []; }
    }

    static IEnumerable<string> SafeEnumerateDirectories(string dir)
    {
        try { return Directory.EnumerateDirectories(dir); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return []; }
    }

    static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return true; }
    }

    // Resolve to a canonical, symlink-free absolute path for de-duplication. Falls back to the
    // normalized full path when the target can't be resolved (e.g. a broken link or a race).
    static string Canonicalize(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var resolved = File.ResolveLinkTarget(full, returnFinalTarget: true)?.FullName
                        ?? Directory.ResolveLinkTarget(full, returnFinalTarget: true)?.FullName;
            return resolved ?? full;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return Path.GetFullPath(path);
        }
    }
}
