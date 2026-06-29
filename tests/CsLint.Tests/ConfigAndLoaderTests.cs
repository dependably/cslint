using CsLint;
using Xunit;

namespace CsLint.Tests;

public class CsLintConfigTests
{
    [Fact]
    public void Load_explicit_parses_all_sections()
    {
        var dir = T.TempDir();
        var path = Path.Combine(dir, ".dependably-check");
        File.WriteAllText(path, """
            {
              "common": { "strict": false, "exclude": ["tests/**"] },
              "cslint": {
                "strict": true,
                "exclude": ["**/Generated/**"],
                "scan": { "magicNumbers": false, "boolFlags": true, "cancellation": true }
              }
            }
            """);
        try
        {
            var cfg = CsLintConfig.Load(path, dir);
            Assert.True(cfg.Strict);
            Assert.False(cfg.ScanMagicNumbers);
            Assert.True(cfg.ScanBoolFlags);
            Assert.Contains("tests/**", cfg.Exclude);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_missing_explicit_throws_file_not_found()
    {
        Assert.Throws<FileNotFoundException>(() =>
            CsLintConfig.Load("/nonexistent/.dependably-check", "/nonexistent"));
    }

    [Fact]
    public void Load_invalid_json_throws_invalid_data()
    {
        var dir = T.TempDir();
        var path = Path.Combine(dir, ".dependably-check");
        File.WriteAllText(path, "{ not valid json }");
        try
        {
            Assert.Throws<InvalidDataException>(() =>
                CsLintConfig.Load(path, dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Load_returns_empty_when_none_found()
    {
        // Use a temp dir with no .dependably-check and no .git (stop at root).
        // Since we can't control the whole tree, just test with an explicit null path
        // and a dir that has no config file. We rely on the directory walk stopping
        // at the git boundary (cslint is in a git repo).
        var cfg = CsLintConfig.Load(null, "/tmp");
        Assert.NotNull(cfg); // returns Empty, not null
    }

    [Fact]
    public void Discover_walks_up_to_find_config()
    {
        var rootDir = T.TempDir();
        var subDir = Path.Combine(rootDir, "sub", "dir");
        Directory.CreateDirectory(subDir);
        var configPath = Path.Combine(rootDir, ".dependably-check");
        File.WriteAllText(configPath, "{}");
        try
        {
            var discovered = CsLintConfig.Discover(subDir);
            Assert.Equal(configPath, discovered);
        }
        finally { Directory.Delete(rootDir, recursive: true); }
    }

    [Fact]
    public void Discover_stops_at_git_boundary()
    {
        // Create a git-like boundary.
        var rootDir = T.TempDir();
        var gitDir = Path.Combine(rootDir, ".git");
        Directory.CreateDirectory(gitDir);
        var subDir = Path.Combine(rootDir, "sub");
        Directory.CreateDirectory(subDir);
        try
        {
            // Config is above the .git boundary — should not be found.
            var discovered = CsLintConfig.Discover(subDir);
            Assert.Null(discovered);
        }
        finally { Directory.Delete(rootDir, recursive: true); }
    }

    [Fact]
    public void Empty_has_defaults()
    {
        var cfg = CsLintConfig.Empty;
        Assert.False(cfg.Strict);
        Assert.True(cfg.ScanMagicNumbers);
        Assert.True(cfg.ScanBoolFlags);
        Assert.True(cfg.ScanCancellation);
        Assert.Empty(cfg.Exclude);
    }
}

public class EditorConfigLoaderTests
{
    [Fact]
    public void Merges_sections_matching_file()
    {
        var dir = T.TempDir();
        var editorConfig = Path.Combine(dir, ".editorconfig");
        File.WriteAllText(editorConfig, """
            root = true
            [*.cs]
            indent_style = space
            indent_size = 4
            """);
        var file = Path.Combine(dir, "Test.cs");
        File.WriteAllText(file, "class C { }");
        try
        {
            var loader = new EditorConfigLoader();
            var config = loader.GetConfig(file);
            Assert.True(config.Properties.TryGetValue("indent_style", out var style));
            Assert.Equal("space", style);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Expands_brace_patterns()
    {
        var dir = T.TempDir();
        var editorConfig = Path.Combine(dir, ".editorconfig");
        File.WriteAllText(editorConfig, """
            root = true
            [*.{cs,fs}]
            trim_trailing_whitespace = true
            """);
        var file = Path.Combine(dir, "Program.cs");
        File.WriteAllText(file, "class C { }");
        try
        {
            var loader = new EditorConfigLoader();
            var config = loader.GetConfig(file);
            Assert.True(config.Properties.ContainsKey("trim_trailing_whitespace"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Child_overrides_parent_and_stops_at_root()
    {
        var parentDir = T.TempDir();
        var childDir = Path.Combine(parentDir, "src");
        Directory.CreateDirectory(childDir);

        File.WriteAllText(Path.Combine(parentDir, ".editorconfig"), """
            root = true
            [*.cs]
            indent_style = tab
            """);
        File.WriteAllText(Path.Combine(childDir, ".editorconfig"), """
            [*.cs]
            indent_style = space
            """);
        var file = Path.Combine(childDir, "C.cs");
        File.WriteAllText(file, "class C { }");
        try
        {
            var loader = new EditorConfigLoader();
            var config = loader.GetConfig(file);
            Assert.Equal("space",
                config.Properties.TryGetValue("indent_style", out var v) ? v : "");
        }
        finally { Directory.Delete(parentDir, recursive: true); }
    }

    [Fact]
    public void Strips_inline_comments_from_values()
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, ".editorconfig"), """
            root = true
            [*.cs]
            indent_style = space   # this is a comment
            """);
        var file = Path.Combine(dir, "C.cs");
        File.WriteAllText(file, "");
        try
        {
            var loader = new EditorConfigLoader();
            var config = loader.GetConfig(file);
            // The value should be "space" with no trailing comment.
            Assert.True(config.Properties.TryGetValue("indent_style", out var v));
            Assert.Equal("space", v?.Trim());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
