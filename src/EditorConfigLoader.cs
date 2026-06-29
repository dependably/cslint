using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace CsLint;

record EditorConfigSection(string Pattern, IReadOnlyDictionary<string, string> Properties);

record FileConfig(
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<(string Path, string Content)> ConfigFiles);

class EditorConfigLoader
{
    // GetConfig is invoked concurrently from LintEngine.LintFilesAsync (Parallel.ForEachAsync),
    // so the cache must be thread-safe; a plain Dictionary corrupts under concurrent writes.
    readonly ConcurrentDictionary<string, (List<EditorConfigSection> Sections, string Content)> _cache = new();

    public FileConfig GetConfig(string filePath)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var configFilesList = new List<(string, string)>();
        var configFiles = FindConfigFiles(Path.GetDirectoryName(filePath)!);

        foreach (var configFile in configFiles)
        {
            var (sections, content) = LoadConfigFile(configFile);
            configFilesList.Add((configFile, content));
            var configDir = Path.GetDirectoryName(configFile)!;
            var relativePath = GetRelativePath(configDir, filePath);

            foreach (var section in sections.Where(s => GlobMatches(s.Pattern, relativePath)))
                foreach (var (key, value) in section.Properties)
                    merged[key] = value;
        }

        return new FileConfig(merged, configFilesList);
    }

    static List<string> FindConfigFiles(string startDir)
    {
        var files = new List<string>();
        var dir = startDir;

        while (true)
        {
            var candidate = Path.Combine(dir, ".editorconfig");
            bool isRoot = false;

            if (File.Exists(candidate))
            {
                files.Add(candidate);
                isRoot = CheckIsRoot(candidate);
            }

            if (isRoot) break;

            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }

        files.Reverse();
        return files;
    }

    static bool CheckIsRoot(string configFile)
    {
        foreach (var line in File.ReadLines(configFile))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('[')) break;
            if (trimmed.Equals("root = true", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("root=true", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    (List<EditorConfigSection> Sections, string Content) LoadConfigFile(string configFile) =>
        _cache.GetOrAdd(configFile, static file =>
        {
            var content = File.ReadAllText(file);
            return (Parse(content.Split('\n')), content);
        });

    static List<EditorConfigSection> Parse(string[] lines)
    {
        var sections = new List<EditorConfigSection>();
        string? currentPattern = null;
        var currentProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] is '#' or ';') continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (currentPattern != null)
                    sections.Add(new EditorConfigSection(currentPattern, currentProps));
                currentPattern = line[1..^1];
                currentProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            if (currentPattern != null && TryParseProperty(line, out var key, out var value))
                currentProps[key] = value;
        }

        if (currentPattern != null)
            sections.Add(new EditorConfigSection(currentPattern, currentProps));

        return sections;
    }

    static bool TryParseProperty(string line, out string key, out string value)
    {
        key = "";
        value = "";

        var eq = line.IndexOf('=');
        if (eq < 0) return false;

        key = line[..eq].Trim();
        value = line[(eq + 1)..].Trim();
        var commentIdx = value.IndexOf(" #");
        if (commentIdx >= 0) value = value[..commentIdx].Trim();

        return !key.Equals("root", StringComparison.OrdinalIgnoreCase);
    }

    static string GetRelativePath(string configDir, string filePath) =>
        Path.GetRelativePath(configDir, filePath).Replace('\\', '/');

    static bool GlobMatches(string pattern, string path)
    {
        if (pattern == "*") return true;
        return ExpandBraces(pattern).Any(p => MatchSingle(p, path));
    }

    static IEnumerable<string> ExpandBraces(string pattern)
    {
        var start = pattern.IndexOf('{');
        if (start < 0) return [pattern];
        var end = FindMatchingBrace(pattern, start);
        if (end < 0) return [pattern];
        var prefix = pattern[..start];
        var suffix = pattern[(end + 1)..];
        return SplitBraceChoices(pattern[(start + 1)..end])
            .SelectMany(c => ExpandBraces(prefix + c + suffix));
    }

    static int FindMatchingBrace(string s, int open)
    {
        int depth = 0;
        for (int i = open; i < s.Length; i++)
        {
            if (s[i] == '{') depth++;
            else if (s[i] == '}' && --depth == 0) return i;
        }
        return -1;
    }

    static List<string> SplitBraceChoices(string inner)
    {
        var choices = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '{') depth++;
            else if (inner[i] == '}') depth--;
            else if (inner[i] == ',' && depth == 0)
            {
                choices.Add(inner[start..i]);
                start = i + 1;
            }
        }
        choices.Add(inner[start..]);
        return choices;
    }

    // Bound every regex match so a pathological glob-derived pattern cannot hang the linter (S6444).
    static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    static bool MatchSingle(string pattern, string path) =>
        Regex.IsMatch(path, GlobToRegex(pattern),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    static string GlobToRegex(string glob)
    {
        var sb = new System.Text.StringBuilder("^");
        if (!glob.Contains('/')) sb.Append("(?:.*/)?");

        int i = 0;
        while (i < glob.Length)
            i = AppendGlobToken(sb, glob, i);

        sb.Append('$');
        return sb.ToString();
    }

    static int AppendGlobToken(System.Text.StringBuilder sb, string glob, int i)
    {
        char c = glob[i];
        if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
        {
            sb.Append(".*");
            i += 2;
            if (i < glob.Length && glob[i] == '/') i++;
            return i;
        }
        if (c == '*') { sb.Append("[^/]*"); return i + 1; }
        if (c == '?') { sb.Append("[^/]"); return i + 1; }
        if (c == '[') return AppendCharClass(sb, glob, i);

        sb.Append(Regex.Escape(c.ToString()));
        return i + 1;
    }

    static int AppendCharClass(System.Text.StringBuilder sb, string glob, int i)
    {
        var end = glob.IndexOf(']', i + 1);
        if (end <= i)
        {
            sb.Append(Regex.Escape(glob[i].ToString()));
            return i + 1;
        }

        var cls = glob[(i + 1)..end];
        var neg = cls.StartsWith('!') ? "^" : "";
        var body = cls.StartsWith('!') ? cls[1..] : cls;
        sb.Append('[').Append(neg).Append(Regex.Escape(body)).Append(']');
        return end + 1;
    }
}
