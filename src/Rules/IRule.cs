namespace CsEdLint.Rules;

enum RuleCategory { EditorConfig, Sast, Opinionated }

interface IRule
{
    string Id { get; }
    RuleCategory Category => RuleCategory.EditorConfig;
    bool AppliesTo(FileConfig config);
    Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config);
    Task<bool> FixAsync(string filePath, FileConfig config) => Task.FromResult(false);
}

abstract class TextRule : IRule
{
    public abstract string Id { get; }
    public virtual RuleCategory Category => RuleCategory.EditorConfig;
    public abstract bool AppliesTo(FileConfig config);

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var text = await File.ReadAllTextAsync(filePath);
        return Analyze(filePath, text, config);
    }

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

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        return Analyze(filePath, root, config);
    }

    protected abstract IReadOnlyList<Diagnostic> Analyze(
        string filePath, Microsoft.CodeAnalysis.SyntaxNode root, FileConfig config);
}
