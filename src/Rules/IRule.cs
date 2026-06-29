using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CsLint.Rules;

enum RuleCategory { EditorConfig, Sast, Opinionated }

/// <summary>
/// A single file's source read and parsed exactly once, then shared across every rule that runs
/// against it. The engine builds one of these per file (in the parallel file loop) and hands the
/// same instance to all applicable rules, so a file is no longer read + parsed once per rule.
/// Text-only rules consume <see cref="Text"/>; syntax rules consume <see cref="Tree"/>/<see cref="Root"/>.
/// </summary>
sealed record SourceUnit(string Path, string Text, SyntaxTree Tree, SyntaxNode Root, FileConfig Config)
{
    /// <summary>Reads and parses <paramref name="path"/> once, producing the shared per-file unit.</summary>
    public static async Task<SourceUnit> LoadAsync(string path, FileConfig config)
    {
        var text = await File.ReadAllTextAsync(path);
        var tree = CSharpSyntaxTree.ParseText(text);
        var root = await tree.GetRootAsync();
        return new SourceUnit(path, text, tree, root, config);
    }
}

interface IRule
{
    string Id { get; }
    RuleCategory Category => RuleCategory.EditorConfig;
    bool AppliesTo(FileConfig config);
    Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit);
    Task<bool> FixAsync(string filePath, FileConfig config) => Task.FromResult(false);
}

abstract class TextRule : IRule
{
    public abstract string Id { get; }
    public virtual RuleCategory Category => RuleCategory.EditorConfig;
    public abstract bool AppliesTo(FileConfig config);

    public Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit) =>
        Task.FromResult(Analyze(unit.Path, unit.Text, unit.Config));

    public async Task<bool> FixAsync(string filePath, FileConfig config)
    {
        var original = await File.ReadAllTextAsync(filePath);
        var fixed_ = ApplyFix(original, config);
        if (fixed_ == null || fixed_ == original) return false;
        await File.WriteAllTextAsync(filePath, fixed_);
        return true;
    }

    protected abstract IReadOnlyList<Diagnostic> Analyze(string filePath, string text, FileConfig config);
    protected virtual string? ApplyFix(string text, FileConfig config) => null;

    protected static Diagnostic Warn(string file, int line, int col, string rule, string message) =>
        new(file, line, col, rule, message, Severity.Warning);

    protected static Diagnostic Error(string file, int line, int col, string rule, string message) =>
        new(file, line, col, rule, message, Severity.Error);
}

abstract class SyntaxRule : IRule
{
    public abstract string Id { get; }
    public virtual RuleCategory Category => RuleCategory.EditorConfig;
    public abstract bool AppliesTo(FileConfig config);

    public Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit) =>
        Task.FromResult(Analyze(unit.Path, unit.Root, unit.Config));

    protected abstract IReadOnlyList<Diagnostic> Analyze(
        string filePath, Microsoft.CodeAnalysis.SyntaxNode root, FileConfig config);
}
