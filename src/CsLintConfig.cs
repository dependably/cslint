using System.Text.Json;

namespace CsLint;

/// <summary>
/// The shared repo-root <c>.dependably-check</c> config, consumed across the Dependably
/// suite. cslint reads the <c>common</c> and <c>cslint</c> sections (the latter overriding
/// the former). Recognised keys:
/// <code>
/// { "cslint": { "strict": false,
///               "scan": { "magicNumbers": true, "boolFlags": true, "cancellation": true } } }
/// </code>
/// CLI flags always take precedence over the file (a flag can only further restrict: --strict
/// forces strict on; --no-* forces a scan rule off). Unknown keys and other sections are ignored.
/// </summary>
sealed class CsLintConfig
{
    public const string FileName = ".dependably-check";

    public bool Strict { get; private init; }
    public bool ScanMagicNumbers { get; private init; } = true;
    public bool ScanBoolFlags { get; private init; } = true;
    public bool ScanCancellation { get; private init; } = true;

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
                Strict           = ReadBool(tool, common, "strict", false),
                ScanMagicNumbers = ReadBool(GetObject(tool, "scan"), GetObject(common, "scan"), "magicNumbers", true),
                ScanBoolFlags    = ReadBool(GetObject(tool, "scan"), GetObject(common, "scan"), "boolFlags", true),
                ScanCancellation = ReadBool(GetObject(tool, "scan"), GetObject(common, "scan"), "cancellation", true),
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
    static bool ReadBool(JsonElement? tool, JsonElement? common, string key, bool def) =>
        TryBool(tool, key, out var t) ? t
        : TryBool(common, key, out var c) ? c
        : def;

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
}
