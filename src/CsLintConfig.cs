using CsLint.Config;

namespace CsLint;

/// <summary>
/// The shared repo-root config for cslint. Wraps <see cref="DependablyCheckConfig"/> to expose the
/// properties Cli.cs and LintEngine.cs consume, with the same API shape as before.
/// Discovery prefers <c>.dependably</c> over the deprecated <c>.dependably-check</c>.
/// </summary>
sealed class CsLintConfig
{
    /// <summary>The canonical config file name (preferred).</summary>
    public const string FileName = DependablyCheckConfig.FileName;

    /// <summary>The deprecated config file name (still read for the migration window).</summary>
    public const string DeprecatedFileName = DependablyCheckConfig.DeprecatedFileName;

    private readonly DependablyCheckConfig _inner;

    private CsLintConfig(DependablyCheckConfig inner) => _inner = inner;

    public bool Strict => _inner.Strict;
    public bool ScanMagicNumbers => _inner.ScanMagicNumbers;
    public bool ScanBoolFlags => _inner.ScanBoolFlags;
    public bool ScanCancellation => _inner.ScanCancellation;

    /// <summary>Path globs to skip, from <c>common.exclude</c> ∪ <c>cslint.exclude</c>.</summary>
    public IReadOnlyList<string> Exclude => _inner.Exclude;

    /// <summary>Parsed exception entries from common + cslint (spec §6).</summary>
    public IReadOnlyList<DependablyException> Exceptions => _inner.Exceptions;

    /// <summary>The <c>failOn.severity</c> gate from the file, or null.</summary>
    public string? FailOnSeverity => _inner.FailOnSeverity;

    /// <summary>The <c>failOn.count</c> gate from the file, or null.</summary>
    public int? FailOnCount => _inner.FailOnCount;

    /// <summary>Per-rule severity overrides from the <c>rules</c> map.</summary>
    public IReadOnlyDictionary<string, string> Rules => _inner.Rules;

    /// <summary>Deprecation / unknown-key notices to print to stderr.</summary>
    public IReadOnlyList<DependablyWarning> Warnings => _inner.Warnings;

    /// <summary>An empty config (built-in defaults), used when no file is found.</summary>
    public static CsLintConfig Empty { get; } = new(DependablyCheckConfig.Empty);

    /// <summary>
    /// Loads the config. When <paramref name="explicitPath"/> is given it is read directly;
    /// otherwise the config is discovered by walking up from <paramref name="startDirectory"/>.
    /// Returns <see cref="Empty"/> when no file is found. Throws on parse errors.
    /// </summary>
    public static CsLintConfig Load(string? explicitPath, string startDirectory)
    {
        var inner = DependablyCheckConfig.Load(explicitPath, startDirectory);
        return ReferenceEquals(inner, DependablyCheckConfig.Empty) ? Empty : new(inner);
    }

    /// <summary>
    /// Walks up from <paramref name="startDirectory"/> looking for a config file.
    /// <c>.dependably</c> is preferred over <c>.dependably-check</c> at each level.
    /// Stops at a <c>.git</c> boundary or the filesystem root.
    /// </summary>
    public static string? Discover(string startDirectory)
        => DependablyCheckConfig.Discover(startDirectory);
}
