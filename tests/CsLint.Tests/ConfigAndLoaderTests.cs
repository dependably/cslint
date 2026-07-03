using CsLint;
using CsLint.Config;
using Xunit;

namespace CsLint.Tests;

public class CsLintConfigTests
{
    static string WriteConfig(string dir, string json)
    {
        var path = Path.Combine(dir, CsLintConfig.FileName);
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Load_explicit_parses_all_sections()
    {
        var dir = T.TempDir();
        try
        {
            var path = WriteConfig(dir, """
                { "common": { "exclude": ["tests/**"] },
                  "cslint": { "strict": true, "exclude": ["gen/**"],
                              "scan": { "magicNumbers": false, "boolFlags": true } } }
                """);
            var cfg = CsLintConfig.Load(path, dir);
            Assert.True(cfg.Strict);
            Assert.False(cfg.ScanMagicNumbers);
            Assert.True(cfg.ScanBoolFlags);
            Assert.Contains("tests/**", cfg.Exclude);
            Assert.Contains("gen/**", cfg.Exclude);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_missing_explicit_throws_file_not_found()
    {
        Assert.Throws<FileNotFoundException>(
            () => CsLintConfig.Load(Path.Combine(T.TempDir(), "nope.json"), T.TempDir()));
    }

    [Fact]
    public void Load_invalid_json_throws_invalid_data()
    {
        var dir = T.TempDir();
        try
        {
            var path = WriteConfig(dir, "{ not valid json");
            Assert.Throws<InvalidDataException>(() => CsLintConfig.Load(path, dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Load_returns_empty_when_none_found()
    {
        var dir = T.TempDir();
        Directory.CreateDirectory(Path.Combine(dir, ".git")); // repo boundary, no config
        try
        {
            var cfg = CsLintConfig.Load(null, dir);
            Assert.Same(CsLintConfig.Empty, cfg);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Discover_walks_up_to_find_config()
    {
        var root = T.TempDir();
        var sub = Path.Combine(root, "a", "b");
        Directory.CreateDirectory(sub);
        try
        {
            WriteConfig(root, "{}");
            Assert.Equal(Path.Combine(root, CsLintConfig.FileName), CsLintConfig.Discover(sub));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Discover_stops_at_git_boundary()
    {
        var dir = T.TempDir();
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        try
        {
            Assert.Null(CsLintConfig.Discover(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Empty_has_defaults()
    {
        Assert.False(CsLintConfig.Empty.Strict);
        Assert.True(CsLintConfig.Empty.ScanMagicNumbers);
        Assert.Empty(CsLintConfig.Empty.Exclude);
    }
}

public class EditorConfigLoaderTests
{
    [Fact]
    public void Merges_sections_matching_file()
    {
        var dir = T.TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, ".editorconfig"), """
                root = true
                [*]
                indent_style = space
                [*.cs]
                indent_size = 4
                """);
            var file = Path.Combine(dir, "Foo.cs");
            File.WriteAllText(file, "class C { }");

            var cfg = new EditorConfigLoader().GetConfig(file);
            Assert.Equal("space", cfg.Properties["indent_style"]);
            Assert.Equal("4", cfg.Properties["indent_size"]);
            Assert.Single(cfg.ConfigFiles);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Expands_brace_patterns()
    {
        var dir = T.TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, ".editorconfig"), """
                root = true
                [*.{cs,txt}]
                max_line_length = 120
                """);
            var file = Path.Combine(dir, "Foo.cs");
            File.WriteAllText(file, "class C { }");

            var cfg = new EditorConfigLoader().GetConfig(file);
            Assert.Equal("120", cfg.Properties["max_line_length"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Child_overrides_parent_and_stops_at_root()
    {
        var root = T.TempDir();
        var child = Path.Combine(root, "child");
        Directory.CreateDirectory(child);
        try
        {
            File.WriteAllText(Path.Combine(root, ".editorconfig"), """
                root = true
                [*.cs]
                indent_size = 2
                """);
            File.WriteAllText(Path.Combine(child, ".editorconfig"), """
                [*.cs]
                indent_size = 8
                """);
            var file = Path.Combine(child, "Foo.cs");
            File.WriteAllText(file, "class C { }");

            var cfg = new EditorConfigLoader().GetConfig(file);
            Assert.Equal("8", cfg.Properties["indent_size"]);
            Assert.Equal(2, cfg.ConfigFiles.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Strips_inline_comments_from_values()
    {
        var dir = T.TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, ".editorconfig"), """
                root = true
                [*.cs]
                indent_style = space # use spaces
                """);
            var file = Path.Combine(dir, "Foo.cs");
            File.WriteAllText(file, "class C { }");

            var cfg = new EditorConfigLoader().GetConfig(file);
            Assert.Equal("space", cfg.Properties["indent_style"]);
        }
        finally { Directory.Delete(dir, true); }
    }
}

/// <summary>
/// Tests for the new DependablyCheckConfig (spec §1-§8): rename, version, warnings, exceptions, failOn.
/// </summary>
public class DependablyCheckConfigTests
{
    private static string WriteFile(string dir, string name, string json)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    // ---- filename rename ----

    [Fact]
    public void Discovers_canonical_dependably_filename()
    {
        var dir = T.TempDir();
        Directory.CreateDirectory(Path.Combine(dir, ".git")); // repo boundary
        try
        {
            WriteFile(dir, ".dependably", """{ "cslint": { "exclude": ["gen/**"] } }""");
            var cfg = DependablyCheckConfig.Load(null, dir);
            Assert.Contains("gen/**", cfg.Exclude);
            Assert.Empty(cfg.Warnings); // no deprecation warning for canonical name
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Discovers_deprecated_filename_and_warns()
    {
        var dir = T.TempDir();
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        try
        {
            WriteFile(dir, ".dependably-check", """{ "cslint": { "exclude": ["gen/**"] } }""");
            var cfg = DependablyCheckConfig.Load(null, dir);
            Assert.Contains("gen/**", cfg.Exclude);
            Assert.Single(cfg.Warnings, w => w.Code == "DEPRECATED_FILENAME");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Both_files_present_prefers_canonical_and_warns()
    {
        var dir = T.TempDir();
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        try
        {
            WriteFile(dir, ".dependably", """{ "cslint": { "exclude": ["a/**"] } }""");
            WriteFile(dir, ".dependably-check", """{ "cslint": { "exclude": ["b/**"] } }""");
            var cfg = DependablyCheckConfig.Load(null, dir);
            Assert.Contains("a/**", cfg.Exclude);
            Assert.DoesNotContain("b/**", cfg.Exclude);
            Assert.Single(cfg.Warnings, w => w.Code == "BOTH_FILES_PRESENT");
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- version ----

    [Fact]
    public void Version_1_is_accepted()
    {
        var dir = T.TempDir();
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        try
        {
            WriteFile(dir, ".dependably", """{ "version": 1 }""");
            var cfg = DependablyCheckConfig.Load(null, dir);
            Assert.Empty(cfg.Warnings.Where(w => w.Code == "CONFIG_VERSION"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Version_too_high_throws_CONFIG_VERSION()
    {
        var dir = T.TempDir();
        try
        {
            var path = WriteFile(dir, ".dependably", """{ "version": 999 }""");
            var ex = Assert.Throws<DependablyConfigException>(() => DependablyCheckConfig.Load(path, dir));
            Assert.Equal("CONFIG_VERSION", ex.Code);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- CONFIG_SHAPE ----

    [Fact]
    public void Non_object_root_throws_CONFIG_SHAPE()
    {
        var dir = T.TempDir();
        try
        {
            var path = WriteFile(dir, ".dependably", """["not", "an", "object"]""");
            var ex = Assert.Throws<DependablyConfigException>(() => DependablyCheckConfig.Load(path, dir));
            Assert.Equal("CONFIG_SHAPE", ex.Code);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- unknown keys ----

    [Fact]
    public void Unknown_key_in_cslint_section_warns_UNKNOWN_KEY()
    {
        var dir = T.TempDir();
        try
        {
            var path = WriteFile(dir, ".dependably", """{ "cslint": { "bogusKey": true } }""");
            var cfg = DependablyCheckConfig.Load(path, dir);
            Assert.Single(cfg.Warnings, w => w.Code == "UNKNOWN_KEY");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Unknown_top_level_section_ignored_silently()
    {
        var dir = T.TempDir();
        try
        {
            var path = WriteFile(dir, ".dependably", """{ "npm-check": { "exclude": ["x/**"] }, "cslint": { "exclude": ["y/**"] } }""");
            var cfg = DependablyCheckConfig.Load(path, dir);
            // npm-check section is ignored; no warning for unknown top-level sections (spec §3.5)
            Assert.DoesNotContain(cfg.Warnings, w => w.Code == "UNKNOWN_KEY" && w.Message.Contains("npm-check"));
            Assert.Contains("y/**", cfg.Exclude);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- legacy keys (deprecated) ----

    [Fact]
    public void Legacy_strict_true_sets_failOn_severity_warning_and_warns_DEPRECATED_KEY()
    {
        var dir = T.TempDir();
        try
        {
            var path = WriteFile(dir, ".dependably", """{ "cslint": { "strict": true } }""");
            var cfg = DependablyCheckConfig.Load(path, dir);
            Assert.True(cfg.Strict);
            Assert.Single(cfg.Warnings, w => w.Code == "DEPRECATED_KEY");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Legacy_scan_false_sets_rule_off_and_warns_DEPRECATED_KEY()
    {
        var dir = T.TempDir();
        try
        {
            var path = WriteFile(dir, ".dependably",
                """{ "cslint": { "scan": { "magicNumbers": false, "boolFlags": true } } }""");
            var cfg = DependablyCheckConfig.Load(path, dir);
            Assert.False(cfg.ScanMagicNumbers);
            Assert.True(cfg.ScanBoolFlags);
            Assert.Single(cfg.Warnings, w => w.Code == "DEPRECATED_KEY");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Canonical_rules_wins_over_legacy_scan()
    {
        var dir = T.TempDir();
        try
        {
            // rules says OP004 off; legacy scan says magicNumbers true — canonical wins
            var path = WriteFile(dir, ".dependably",
                """{ "cslint": { "rules": { "OP004": "off" }, "scan": { "magicNumbers": true } } }""");
            var cfg = DependablyCheckConfig.Load(path, dir);
            Assert.False(cfg.ScanMagicNumbers); // canonical "off" wins
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- rules map ----

    [Fact]
    public void Rules_map_is_loaded_and_merged_with_common()
    {
        var dir = T.TempDir();
        try
        {
            var path = WriteFile(dir, ".dependably",
                """{ "common": { "rules": { "SAST001": "warn" } }, "cslint": { "rules": { "SAST001": "error", "OP004": "off" } } }""");
            var cfg = DependablyCheckConfig.Load(path, dir);
            // Tool section overrides common per rule-id
            Assert.Equal("error", cfg.Rules["SAST001"]);
            Assert.Equal("off", cfg.Rules["OP004"]);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- exceptions ----

    [Fact]
    public void Exceptions_are_parsed_from_cslint_section()
    {
        var dir = T.TempDir();
        try
        {
            var path = WriteFile(dir, ".dependably",
                """{ "cslint": { "exceptions": [{ "rule": "OP004", "path": "src/**", "reason": "grandfathered" }] } }""");
            var cfg = DependablyCheckConfig.Load(path, dir);
            Assert.Single(cfg.Exceptions);
            Assert.Equal("OP004", cfg.Exceptions[0].Rule);
            Assert.Equal("src/**", cfg.Exceptions[0].Path);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Exception_with_package_in_own_section_throws_EXCEPTION_BAD_SELECTOR()
    {
        var dir = T.TempDir();
        try
        {
            var path = WriteFile(dir, ".dependably",
                """{ "cslint": { "exceptions": [{ "rule": "OP004", "package": "Foo", "reason": "y" }] } }""");
            var ex = Assert.Throws<DependablyConfigException>(() => DependablyCheckConfig.Load(path, dir));
            Assert.Equal("EXCEPTION_BAD_SELECTOR", ex.Code);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Exception_with_package_in_common_section_is_tolerated()
    {
        var dir = T.TempDir();
        try
        {
            var path = WriteFile(dir, ".dependably",
                """{ "common": { "exceptions": [{ "rule": "OP004", "package": "Foo", "reason": "cross-tool" }] } }""");
            var cfg = DependablyCheckConfig.Load(path, dir);
            Assert.Single(cfg.Exceptions);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Exceptions_unioned_from_common_and_cslint()
    {
        var dir = T.TempDir();
        try
        {
            var json = "{ \"common\": { \"exceptions\": [{ \"rule\": \"SAST001\", \"path\": \"tests/**\", \"reason\": \"test\" }] }," +
                       "  \"cslint\": { \"exceptions\": [{ \"rule\": \"OP004\", \"symbol\": \"Foo.Bar\", \"reason\": \"x\" }] } }";
            var path = WriteFile(dir, ".dependably", json);
            var cfg = DependablyCheckConfig.Load(path, dir);
            Assert.Equal(2, cfg.Exceptions.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- failOn ----

    [Fact]
    public void FailOn_severity_and_count_parsed()
    {
        var dir = T.TempDir();
        try
        {
            var path = WriteFile(dir, ".dependably",
                """{ "cslint": { "failOn": { "severity": "warning", "count": 5 } } }""");
            var cfg = DependablyCheckConfig.Load(path, dir);
            Assert.Equal("warning", cfg.FailOnSeverity);
            Assert.Equal(5, cfg.FailOnCount);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FailOn_count_negative_throws_INVALID_FAIL_ON()
    {
        var dir = T.TempDir();
        try
        {
            var path = WriteFile(dir, ".dependably",
                """{ "cslint": { "failOn": { "count": -1 } } }""");
            var ex = Assert.Throws<DependablyConfigException>(() => DependablyCheckConfig.Load(path, dir));
            Assert.Equal("INVALID_FAIL_ON", ex.Code);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- empty ----

    [Fact]
    public void Empty_has_expected_defaults()
    {
        var cfg = DependablyCheckConfig.Empty;
        Assert.False(cfg.Strict);
        Assert.True(cfg.ScanMagicNumbers);
        Assert.True(cfg.ScanBoolFlags);
        Assert.True(cfg.ScanCancellation);
        Assert.Empty(cfg.Exclude);
        Assert.Empty(cfg.Exceptions);
        Assert.Null(cfg.FailOnSeverity);
        Assert.Null(cfg.FailOnCount);
        Assert.Empty(cfg.Warnings);
    }
}
