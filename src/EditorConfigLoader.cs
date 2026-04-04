using System.Text.RegularExpressions;

namespace CsEdLint;

record EditorConfigSection(string Pattern, IReadOnlyDictionary<string, string> Properties);

record FileConfig(
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<(string Path, string Content)> ConfigFiles);

class EditorConfigLoader
{
    readonly Dictionary<string, (List<EditorConfigSection> Sections, string Content)> _cache = new();

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

            foreach (var section in sections)
            {
                if (GlobMatches(section.Pattern, relativePath))
                {
                    foreach (var (key, value) in section.Properties)
                        merged[key] = value;
                }
            }
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

    (List<EditorConfigSection> Sections, string Content) LoadConfigFile(string configFile)
    {
        if (_cache.TryGetValue(configFile, out var cached))
            return cached;

        var content = File.ReadAllText(configFile);
        var sections = Parse(content.Split('\n'));
        var result = (sections, content);
        _cache[configFile] = result;
        return result;
    }

    static List<EditorConfigSection> Parse(string[] lines)
    {
        var sections = new List<EditorConfigSection>();
        string? currentPattern = null;
        var currentProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (currentPattern != null)
                    sections.Add(new EditorConfigSection(currentPattern, currentProps));
                currentPattern = line[1..^1];
                currentProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq < 0) continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            var commentIdx = value.IndexOf(" #");
            if (commentIdx >= 0) value = value[..commentIdx].Trim();

            if (key.Equals("root", StringComparison.OrdinalIgnoreCase)) continue;
            if (currentPattern != null) currentProps[key] = value;
        }

        if (currentPattern != null)
            sections.Add(new EditorConfigSection(currentPattern, currentProps));

        return sections;
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

    static bool MatchSingle(string pattern, string path) =>
        Regex.IsMatch(path, GlobToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static string GlobToRegex(string glob)
    {
        var sb = new System.Text.StringBuilder("^");
        if (!glob.Contains('/')) sb.Append("(?:.*/)?");

        int i = 0;
        while (i < glob.Length)
        {
            char c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                sb.Append(".*");
                i += 2;
                if (i < glob.Length && glob[i] == '/') i++;
            }
            else if (c == '*') { sb.Append("[^/]*"); i++; }
            else if (c == '?') { sb.Append("[^/]"); i++; }
            else if (c == '[')
            {
                var end = glob.IndexOf(']', i + 1);
                if (end > i)
                {
                    var cls = glob[(i + 1)..end];
                    var neg = cls.StartsWith('!') ? "^" : "";
                    var body = cls.StartsWith('!') ? cls[1..] : cls;
                    sb.Append('[').Append(neg).Append(Regex.Escape(body)).Append(']');
                    i = end + 1;
                }
                else { sb.Append(Regex.Escape(c.ToString())); i++; }
            }
            else { sb.Append(Regex.Escape(c.ToString())); i++; }
        }

        sb.Append('$');
        return sb.ToString();
    }
}
