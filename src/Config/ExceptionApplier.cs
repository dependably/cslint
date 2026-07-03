using CsLint;

namespace CsLint.Config;

/// <summary>
/// Applies the resolved <c>.dependably</c> exceptions to cslint's diagnostics: suppresses
/// matched findings so they no longer print or gate the run (spec §6). cslint diagnostics are
/// file+rule based (there is no symbol/id on a <see cref="Diagnostic"/>), so <c>path</c> is the
/// meaningful selector; <c>symbol</c>/<c>id</c> selectors simply never match a cslint finding.
/// Suppressed findings are removed from the kept set; unused and expired exceptions are
/// reported so they don't rot. This layers on top of the <c>.editorconfig</c> severity
/// suppression that already happens upstream in <see cref="LintEngine"/> (union of both).
/// </summary>
static class ExceptionApplier
{
    public sealed record Result(IReadOnlyList<Diagnostic> Kept, int Suppressed, IReadOnlyList<string> Notices);

    /// <summary>
    /// Filter <paramref name="diagnostics"/> through <paramref name="exceptions"/>. A diagnostic is
    /// suppressed when a live (non-expired) exception matches it by rule + path (relative to
    /// <paramref name="root"/>). Returns the kept diagnostics, the suppressed count, and the
    /// stderr notices (suppressed count, unused exceptions, expired exceptions).
    /// </summary>
    public static Result Apply(
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<DependablyException> exceptions,
        string root,
        DateOnly? todayOverride = null)
    {
        if (exceptions.Count == 0)
        {
            return new Result(diagnostics, 0, []);
        }

        var today = todayOverride ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var live = exceptions.Where(e => !DependablyExceptions.IsExpired(e, today)).ToList();
        var expired = exceptions.Where(e => DependablyExceptions.IsExpired(e, today)).ToList();
        var used = new HashSet<DependablyException>();

        var kept = new List<Diagnostic>();
        var suppressed = 0;
        foreach (var d in diagnostics)
        {
            var target = new ExceptionTarget(d.Rule, Path: RelativePath(root, d.File));
            var hit = live.FirstOrDefault(e => DependablyExceptions.Matches(e, target));
            if (hit is not null)
            {
                used.Add(hit);
                suppressed++;
            }
            else
            {
                kept.Add(d);
            }
        }

        var notices = new List<string>();
        if (suppressed > 0)
        {
            notices.Add($"{suppressed} finding(s) suppressed by exceptions");
        }

        foreach (var ex in live.Where(e => !used.Contains(e)))
        {
            notices.Add($"unused exception for rule \"{ex.Rule}\" — {ex.Reason}");
        }

        foreach (var ex in expired)
        {
            notices.Add($"exception expired {ex.Expires:yyyy-MM-dd} for rule \"{ex.Rule}\" — {ex.Reason}");
        }

        return new Result(kept, suppressed, notices);
    }

    // Match the same file rendering the reporter uses: relative to the run root, forward slashes.
    // A path outside root (or an unrooted root) falls back to the raw file, still slash-normalized.
    private static string RelativePath(string root, string file)
    {
        try
        {
            return Path.GetRelativePath(root, file).Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return file.Replace('\\', '/');
        }
    }
}
