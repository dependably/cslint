using CsLint;
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
