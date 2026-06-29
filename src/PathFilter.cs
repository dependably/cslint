using System.Text;
using System.Text.RegularExpressions;

namespace CsLint;

/// <summary>
/// Matches file paths against exclude globs (from --exclude and the .dependably-check
/// <c>exclude</c> arrays). Semantics match the rest of the suite: a pattern with no wildcard
/// is a substring match; otherwise it is a glob (<c>**</c> = any path, <c>*</c> = within a
/// segment, <c>?</c> = one char) matched against the path relative to the lint root.
/// </summary>
static class PathFilter
{
    // Bound every regex match so a pathological glob-derived pattern cannot hang the linter (S6444).
    static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public static bool IsExcluded(string path, string root, IReadOnlyList<string> globs)
    {
        if (globs.Count == 0) return false;

        var rel = Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');

        foreach (var raw in globs)
        {
            var pattern = raw.Replace('\\', '/').Trim();
            if (pattern.Length == 0) continue;

            if (!pattern.Contains('*') && !pattern.Contains('?'))
            {
                if (rel.Contains(pattern, StringComparison.Ordinal)) return true;
            }
            else if (Regex.IsMatch(rel, "(^|/)" + GlobToRegex(pattern) + "$", RegexOptions.None, RegexTimeout))
            {
                return true;
            }
        }

        return false;
    }

    static string GlobToRegex(string glob)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < glob.Length && glob[i + 1] == '*') { sb.Append(".*"); i++; }
                    else sb.Append("[^/]*");
                    break;
                case '?': sb.Append('.'); break;
                default: sb.Append(Regex.Escape(c.ToString())); break;
            }
        }
        return sb.ToString();
    }
}
