using System.Text.Json;

namespace CsLint;

/// <summary>
/// The shared repo-root <c>.dependably-check</c> config, consumed across the Dependably
/// suite. cslint reads the <c>common</c> and <c>cslint</c> sections (the latter overriding
/// the former). Recognised keys:
/// <code>
/// { "common": { "exclude": ["tests/fixtures/**"] },
///   "cslint": { "strict": false,
///               "exclude": ["**/Generated/**"],
///               "scan": { "magicNumbers": true, "boolFlags": true, "cancellation": true } } }
/// </code>
/// CLI flags always take precedence over the file (a flag can only further restrict: --strict
/// forces strict on; --no-* forces a scan rule off; --exclude adds to the configured globs).
/// Unknown keys and other sections are ignored.
/// </summary>
sealed class CsLintConfig
{
    public const string FileName = ".dependably-check";

    public bool Strict { get; private init; }
    public bool ScanMagicNumbers { get; private init; } = true;
    public bool ScanBoolFlags { get; private init; } = true;
    public bool ScanCancellation { get; private init; } = true;

    /// <summary>Path globs to skip, from <c>common.exclude</c> ∪ <c>cslint.exclude</c>.</summary>
    public IReadOnlyList<string> Exclude { get; private init; } = [];

    /// <summary>An empty config (built-in defaults), used when no file is found.</summary>
    public static CsLintConfig Empty { get; } = new();

    /// <summary>
    /// Loads the config. When <paramref name="explicitPath"/> is given it is read directly;
    /// otherwise <c>.dependably-check</c> is discovered by walking up from
    /// <paramref name="startDirectory"/>. Returns <see cref="Empty"/> when no file is found.
    /// Throws when an existing file cannot be parsed (the path is included in the message).
    /// </summary>
    public static CsLintConfig Load(string? explicitPath, string startDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (!File.Exists(explicitPath))
                throw new FileNotFoundException($"Config file not found: {explicitPath}", explicitPath);
            return Parse(explicitPath);
        }

        var discovered = Discover(startDirectory);
        return discovered is null ? Empty : Parse(discovered);
    }

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> looking for a <c>.dependably-check</c>
    /// file. The walk stops at the filesystem root, or at the repo boundary (a <c>.git</c>
    /// entry) after checking that directory. Returns the file path, or null when none is found.
    /// </summary>
    public static string? Discover(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, FileName);
            if (File.Exists(candidate)) return candidate;

            var gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath)) return null;

            directory = directory.Parent;
        }

        return null;
    }

    static CsLintConfig Parse(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            var common = GetObject(root, "common");
            var tool = GetObject(root, "cslint");

            return new CsLintConfig
            {
                Strict = ReadBool(tool, common, "strict", false),
                ScanMagicNumbers = ReadBool(GetObject(tool, "scan"), GetObject(common, "scan"), "magicNumbers", true),
                ScanBoolFlags = ReadBool(GetObject(tool, "scan"), GetObject(common, "scan"), "boolFlags", true),
                ScanCancellation = ReadBool(GetObject(tool, "scan"), GetObject(common, "scan"), "cancellation", true),
                Exclude = ReadStringArray(common, tool, "exclude"),
            };
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Failed to parse {path}: {ex.Message}", ex);
        }
    }

    /// <summary>The named child object of <paramref name="parent"/>, or null.</summary>
    static JsonElement? GetObject(JsonElement? parent, string name) =>
        parent is { } p && p.ValueKind == JsonValueKind.Object
            && p.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.Object
            ? child : null;

    /// <summary>The bool <paramref name="key"/> from the tool section, else common, else default.</summary>
    static bool ReadBool(JsonElement? tool, JsonElement? common, string key, bool def)
    {
        if (TryBool(tool, key, out var t)) return t;
        if (TryBool(common, key, out var c)) return c;
        return def;
    }

    static bool TryBool(JsonElement? section, string key, out bool value)
    {
        value = false;
        if (section is { } s && s.TryGetProperty(key, out var prop)
            && prop.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            value = prop.GetBoolean();
            return true;
        }
        return false;
    }

    /// <summary>The union of the string array <paramref name="key"/> from both sections, de-duplicated.</summary>
    static List<string> ReadStringArray(JsonElement? first, JsonElement? second, string key)
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in new[] { first, second })
        {
            if (section is not { } s || !s.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var element in arr.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String) continue;
                var v = element.GetString();
                if (!string.IsNullOrWhiteSpace(v) && seen.Add(v)) values.Add(v);
            }
        }
        return values;
    }
}
