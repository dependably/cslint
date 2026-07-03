using System.Text.Json;

namespace CsLint.Config;

/// <summary>A deprecation / unknown-key notice emitted while loading <c>.dependably</c> (spec §11).</summary>
public sealed record DependablyWarning(string Code, string Message);

/// <summary>
/// The shared repo-root <c>.dependably</c> config, consumed across the Dependably suite.
/// <c>.dependably-check</c> is a deprecated alias filename. cslint reads the <c>common</c>
/// section and its own <c>cslint</c> section (cslint has no deprecated section alias).
/// See docs/dependably-config-spec.md.
/// </summary>
public sealed class DependablyCheckConfig
{
    /// <summary>The canonical config file name discovered by walking up the directory tree.</summary>
    public const string FileName = ".dependably";

    /// <summary>The deprecated alias filename, still read for the migration window.</summary>
    public const string DeprecatedFileName = ".dependably-check";

    /// <summary>The canonical section key for cslint (no deprecated alias — spec §3.3).</summary>
    public const string SectionKey = "cslint";

    /// <summary>Highest <c>.dependably</c> format version this build understands.</summary>
    public const int SupportedVersion = 1;

    private static readonly string[] KnownSectionKeys =
        ["rules", "exceptions", "exclude", "failOn", "allowedRegistryHosts", "allowedLocalFeeds",
         // legacy keys (tolerated, rewritten)
         "strict", "scan"];

    private DependablyCheckConfig(
        bool strict,
        bool scanMagicNumbers,
        bool scanBoolFlags,
        bool scanCancellation,
        IReadOnlyList<string> exclude,
        IReadOnlyList<DependablyException> exceptions,
        string? failOnSeverity,
        int? failOnCount,
        IReadOnlyDictionary<string, string> rules,
        IReadOnlyList<DependablyWarning> warnings)
    {
        Strict = strict;
        ScanMagicNumbers = scanMagicNumbers;
        ScanBoolFlags = scanBoolFlags;
        ScanCancellation = scanCancellation;
        Exclude = exclude;
        Exceptions = exceptions;
        FailOnSeverity = failOnSeverity;
        FailOnCount = failOnCount;
        Rules = rules;
        Warnings = warnings;
    }

    /// <summary>Legacy: warnings gate too (rewritten from <c>strict:true</c> or <c>failOn.severity="warning"</c>).</summary>
    public bool Strict { get; }

    /// <summary>Whether the OP004 (magic numbers) rule is active.</summary>
    public bool ScanMagicNumbers { get; }

    /// <summary>Whether the OP005 (bool flags) rule is active.</summary>
    public bool ScanBoolFlags { get; }

    /// <summary>Whether the OP006 (cancellation token) rule is active.</summary>
    public bool ScanCancellation { get; }

    /// <summary>Path globs to skip (union of common + cslint, deduped ordinally).</summary>
    public IReadOnlyList<string> Exclude { get; }

    /// <summary>Parsed <c>exceptions</c> entries (common + cslint), suppressing specific findings (spec §6).</summary>
    public IReadOnlyList<DependablyException> Exceptions { get; }

    /// <summary>The <c>failOn.severity</c> gate from the file (CLI <c>--fail-on</c> overrides), or null.</summary>
    public string? FailOnSeverity { get; }

    /// <summary>The <c>failOn.count</c> gate from the file (CLI <c>--fail-on</c> overrides), or null.</summary>
    public int? FailOnCount { get; }

    /// <summary>Per-rule severity overrides from the <c>rules</c> map (common merged with cslint).</summary>
    public IReadOnlyDictionary<string, string> Rules { get; }

    /// <summary>Deprecation / unknown-key notices (stderr; never affect exit codes).</summary>
    public IReadOnlyList<DependablyWarning> Warnings { get; }

    /// <summary>An empty config, used when no file is found.</summary>
    public static DependablyCheckConfig Empty { get; } = new(
        false, true, true, true, [], [], null, null,
        new Dictionary<string, string>(), []);

    /// <summary>
    /// Loads the config. When <paramref name="explicitPath"/> is given it is read directly;
    /// otherwise <c>.dependably</c> (or the deprecated <c>.dependably-check</c>) is discovered
    /// by walking up from <paramref name="startDirectory"/>. Returns <see cref="Empty"/> when
    /// no file is found. Throws when an existing file cannot be parsed.
    /// </summary>
    public static DependablyCheckConfig Load(string? explicitPath, string startDirectory)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            if (!File.Exists(explicitPath))
            {
                throw new FileNotFoundException($"Config file not found: {explicitPath}", explicitPath);
            }

            return Parse(explicitPath, []);
        }

        var discovered = Discover(startDirectory);
        return discovered is null ? Empty : Parse(discovered, FilenameWarnings(discovered));
    }

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> looking for a shared config file.
    /// <c>.dependably</c> is preferred over the deprecated <c>.dependably-check</c> at each
    /// level. Stops at the filesystem root, or at a directory containing a <c>.git</c> entry
    /// (the repo boundary) after checking that directory.
    /// </summary>
    public static string? Discover(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));

        while (directory is not null)
        {
            var canonical = Path.Combine(directory.FullName, FileName);
            var deprecated = Path.Combine(directory.FullName, DeprecatedFileName);

            if (File.Exists(canonical))
            {
                return canonical;
            }

            if (File.Exists(deprecated))
            {
                return deprecated;
            }

            var gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return null;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static List<DependablyWarning> FilenameWarnings(string path)
    {
        var warnings = new List<DependablyWarning>();
        var dir = Path.GetDirectoryName(path)!;
        if (Path.GetFileName(path) == DeprecatedFileName)
        {
            warnings.Add(new DependablyWarning("DEPRECATED_FILENAME",
                $"{DeprecatedFileName} is deprecated; rename it to {FileName}"));
        }
        else if (File.Exists(Path.Combine(dir, DeprecatedFileName)))
        {
            warnings.Add(new DependablyWarning("BOTH_FILES_PRESENT",
                $"both {FileName} and {DeprecatedFileName} found in {dir}; using {FileName} ({DeprecatedFileName} is ignored — delete it)"));
        }

        return warnings;
    }

    private static DependablyCheckConfig Parse(string path, List<DependablyWarning> warnings)
    {
        JsonDocument document;
        try
        {
            using var stream = File.OpenRead(path);
            document = JsonDocument.Parse(stream);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Failed to parse {path}: {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new DependablyConfigException($"Config must be a JSON object: {path}", "CONFIG_SHAPE");
            }

            ValidateVersion(root, path);

            // cslint has no deprecated section alias — always use the canonical key.
            WarnUnknownKeys(root, "common", warnings);
            WarnUnknownKeys(root, SectionKey, warnings);

            var exclude = UnionStringArray(root, SectionKey, "exclude");

            // Parse exceptions: common is tolerant (any selector OK), own is strict (cslint selectors only).
            var exceptions = new List<DependablyException>();
            exceptions.AddRange(DependablyExceptions.Parse(
                GetProperty(root, "common", "exceptions"), "common", DependablyExceptions.Selectors, null));
            exceptions.AddRange(DependablyExceptions.Parse(
                GetProperty(root, SectionKey, "exceptions"), "own", DependablyExceptions.CsLintSelectors, DependablyExceptions.KnownRules));

            var (failOnSeverity, failOnCount, strict) = ParseFailOnAndStrict(root, SectionKey, warnings);

            var rules = ParseRules(root, SectionKey);
            var (scanMN, scanBF, scanCT) = ApplyScanLegacy(root, SectionKey, rules, warnings);

            return new DependablyCheckConfig(strict, scanMN, scanBF, scanCT, exclude, exceptions, failOnSeverity, failOnCount, rules, warnings);
        }
    }

    private static void ValidateVersion(JsonElement root, string path)
    {
        if (root.TryGetProperty("version", out var v))
        {
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var version) || version > SupportedVersion)
            {
                throw new DependablyConfigException(
                    $"Unsupported .dependably version in {path} (this build supports up to {SupportedVersion})", "CONFIG_VERSION");
            }
        }
    }

    private static (string? severity, int? count, bool strict) ParseFailOnAndStrict(
        JsonElement root, string toolKey, List<DependablyWarning> warnings)
    {
        string? severity = null;
        int? count = null;

        foreach (var section in new[] { "common", toolKey })
        {
            if (GetProperty(root, section, "failOn") is not { ValueKind: JsonValueKind.Object } failOn)
            {
                continue;
            }

            if (failOn.TryGetProperty("severity", out var sevEl) && sevEl.ValueKind == JsonValueKind.String)
            {
                severity = sevEl.GetString();
            }

            if (failOn.TryGetProperty("count", out var countEl))
            {
                if (countEl.ValueKind != JsonValueKind.Number || !countEl.TryGetInt32(out var c) || c < 0)
                {
                    throw new DependablyConfigException("failOn.count must be a non-negative integer", "INVALID_FAIL_ON");
                }

                count = c;
            }
        }

        // Legacy: strict:true → failOn.severity="warning" (deprecated; canonical failOn wins if both present)
        var strict = false;
        foreach (var section in new[] { "common", toolKey })
        {
            if (TryGetObject(root, section, out var sectionEl)
                && sectionEl.TryGetProperty("strict", out var strictEl)
                && strictEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                if (strictEl.GetBoolean())
                {
                    warnings.Add(new DependablyWarning("DEPRECATED_KEY",
                        "\"strict\" is deprecated; use \"failOn\": { \"severity\": \"warning\" } instead"));
                    // Only apply if canonical failOn.severity wasn't already set by the canonical key
                    if (severity is null)
                    {
                        severity = "warning";
                        strict = true;
                    }
                }
            }
        }

        // Derive strict from the effective severity for backward compat in Cli.cs
        if (severity is "warning" or "warn" or "low")
        {
            strict = true;
        }

        return (severity, count, strict);
    }

    /// <summary>
    /// Parse the <c>rules</c> map from common + tool section (tool overrides common per rule-id).
    /// Returns a merged dictionary of ruleId → severity string.
    /// </summary>
    private static Dictionary<string, string> ParseRules(JsonElement root, string toolKey)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var section in new[] { "common", toolKey })
        {
            if (GetProperty(root, section, "rules") is not { ValueKind: JsonValueKind.Object } rulesEl)
            {
                continue;
            }

            foreach (var prop in rulesEl.EnumerateObject())
            {
                // Spec §4.1: entry is either a severity string or [severity, options].
                string? sev = null;
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    sev = prop.Value.GetString();
                }
                else if (prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() >= 1)
                {
                    var first = prop.Value[0];
                    if (first.ValueKind == JsonValueKind.String)
                    {
                        sev = first.GetString();
                    }
                }

                if (sev is not null)
                {
                    merged[prop.Name] = sev;
                }
            }
        }

        return merged;
    }

    /// <summary>
    /// Apply the legacy <c>scan</c> toggles (OP004/OP005/OP006 off) as DEPRECATED_KEY warnings,
    /// merging with any <c>rules</c> entries (canonical wins).
    /// </summary>
    private static (bool magicNumbers, bool boolFlags, bool cancellation) ApplyScanLegacy(
        JsonElement root, string toolKey, Dictionary<string, string> rules, List<DependablyWarning> warnings)
    {
        var warnedScan = false;

        foreach (var section in new[] { "common", toolKey })
        {
            if (GetProperty(root, section, "scan") is not { ValueKind: JsonValueKind.Object } scanEl)
            {
                continue;
            }

            if (!warnedScan)
            {
                warnings.Add(new DependablyWarning("DEPRECATED_KEY",
                    "\"scan\" toggles are deprecated; use \"rules\": { \"OP004\": \"off\" } etc. instead"));
                warnedScan = true;
            }

            // Only apply if the canonical rules map doesn't already have an entry for that rule.
            foreach (var (toggle, ruleId) in new[] { ("magicNumbers", "OP004"), ("boolFlags", "OP005"), ("cancellation", "OP006") })
            {
                if (scanEl.TryGetProperty(toggle, out var el)
                    && el.ValueKind is JsonValueKind.False or JsonValueKind.True
                    && !rules.ContainsKey(ruleId))
                {
                    rules[ruleId] = el.GetBoolean() ? "warn" : "off";
                }
            }
        }

        // Derive the boolean flags from the merged rules map.
        var mn = !rules.TryGetValue("OP004", out var op4) || op4 != "off";
        var bf = !rules.TryGetValue("OP005", out var op5) || op5 != "off";
        var ct = !rules.TryGetValue("OP006", out var op6) || op6 != "off";

        return (mn, bf, ct);
    }

    private static void WarnUnknownKeys(JsonElement root, string section, List<DependablyWarning> warnings)
    {
        if (!TryGetObject(root, section, out var el))
        {
            return;
        }

        foreach (var prop in el.EnumerateObject())
        {
            if (!KnownSectionKeys.Contains(prop.Name))
            {
                warnings.Add(new DependablyWarning("UNKNOWN_KEY",
                    $"unknown key \"{section}.{prop.Name}\" in .dependably — ignoring"));
            }
        }
    }

    // Union `common.<arrayKey>` and `<toolKey>.<arrayKey>`, deduped ordinally (exclude globs are case-sensitive).
    private static List<string> UnionStringArray(JsonElement root, string toolKey, string arrayKey)
    {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in new[] { "common", toolKey })
        {
            if (GetProperty(root, section, arrayKey) is not { ValueKind: JsonValueKind.Array } arr)
            {
                continue;
            }

            foreach (var element in arr.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
                {
                    values.Add(value);
                }
            }
        }

        return values;
    }

    private static JsonElement? GetProperty(JsonElement root, string section, string key)
        => TryGetObject(root, section, out var sectionEl) && sectionEl.TryGetProperty(key, out var el) ? el : null;

    private static bool TryGetObject(JsonElement root, string section, out JsonElement element)
    {
        if (root.TryGetProperty(section, out var el) && el.ValueKind == JsonValueKind.Object)
        {
            element = el;
            return true;
        }

        element = default;
        return false;
    }
}
