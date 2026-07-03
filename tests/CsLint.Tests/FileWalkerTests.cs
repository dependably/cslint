using CsLint;
using Xunit;

namespace CsLint.Tests;

// Covers the cycle-safe, default-excluding file walk introduced for moonlitlabs/cslint#26.
public class FileWalkerTests
{
    static void Write(string dir, string relative, string content = "class X { }")
    {
        var path = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Walk_prunes_default_excluded_directories_and_counts_them()
    {
        var dir = T.TempDir();
        Write(dir, "A.cs");
        Write(dir, "node_modules/B.cs");
        Write(dir, "node_modules/sub/D.cs");
        Write(dir, "obj/C.cs");
        Write(dir, ".git/E.cs");
        try
        {
            var result = FileWalker.Walk(dir, applyDefaultExcludes: true);
            Assert.Contains(result.Files, p => p.EndsWith("A.cs"));
            Assert.DoesNotContain(result.Files, p => p.EndsWith("B.cs"));
            Assert.DoesNotContain(result.Files, p => p.EndsWith("D.cs"));
            Assert.DoesNotContain(result.Files, p => p.EndsWith("C.cs"));
            // B.cs, D.cs (node_modules subtree), C.cs (obj), E.cs (.git) = 4 skipped.
            Assert.Equal(4, result.SkippedFiles);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Walk_includes_excluded_directories_when_disabled()
    {
        var dir = T.TempDir();
        Write(dir, "A.cs");
        Write(dir, "node_modules/B.cs");
        try
        {
            var result = FileWalker.Walk(dir, applyDefaultExcludes: false);
            Assert.Contains(result.Files, p => p.EndsWith("A.cs"));
            Assert.Contains(result.Files, p => p.EndsWith("B.cs"));
            Assert.Equal(0, result.SkippedFiles);
        }
        finally { Directory.Delete(dir, true); }
    }

    // Regression pin (#26): a directory symlink cycle must not loop and must not multiply findings.
    // On the old Directory.EnumerateFiles(AllDirectories) walk this recurses until it throws.
    [Fact]
    public void Walk_does_not_follow_directory_symlink_cycle()
    {
        if (OperatingSystem.IsWindows()) return; // symlink creation needs elevation on Windows CI.

        var dir = T.TempDir();
        Write(dir, "A.cs");
        // A self-referential symlink: dir/loop -> dir. Following it would recurse forever.
        Directory.CreateSymbolicLink(Path.Combine(dir, "loop"), dir);
        try
        {
            var result = FileWalker.Walk(dir, applyDefaultExcludes: true);
            // Terminates, and A.cs appears exactly once (deduped by canonical path).
            Assert.Single(result.Files.Where(p => p.EndsWith("A.cs")));
        }
        finally { Directory.Delete(dir, true); }
    }
}
