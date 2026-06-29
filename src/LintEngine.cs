using CsLint.Rules;
using CsLint.Rules.Sast;
using CsLint.Rules.Opinionated;

namespace CsLint;

enum LintMode { EditorConfig, EditorConfigAndSast, All }

class LintEngine
{
    readonly EditorConfigLoader _loader = new();
    readonly IReadOnlyList<IRule> _editorConfigRules;
    readonly IReadOnlyList<IRule> _sastRules;
    readonly IReadOnlyList<IRule> _opinionatedRules;

    public LintEngine(ScanConfig? scanConfig = null)
    {
        var sc = scanConfig ?? new ScanConfig();

        _editorConfigRules =
        [
            new IndentStyleRule(),
            new TrailingWhitespaceRule(),
            new FinalNewlineRule(),
            new LineEndingRule(),
            new LineLengthRule(),
            new CharsetRule(),
            new FormattingRule(),
            new VarStyleRule(),
            new ExpressionBodyRule(),
            new NamespaceDeclarationStyleRule(),
            new PatternMatchingRule(),
            new ThrowExpressionRule(),
            new ConditionalDelegateCallRule(),
            new UnusedValueRule(),
            new QualificationRule(),
            new PredefinedTypeRule(),
            new AccessibilityModifiersRule(),
            new ReadonlyFieldRule(),
            new ObjectInitializerRule(),
            new NullCheckPreferenceRule(),
            new NamespaceMatchFolderRule(),
            new NamingRule(),
        ];

        _sastRules =
        [
            new EmptyCatchRule(),
            new ConsoleOutputRule(),
            new SqlInjectionRule(),
            new HardcodedSecretRule(),
            new FireAndForgetRule(),
            new PragmaDisableRule(),
            new ThreadSleepInAsyncRule(),
            new DynamicUsageRule(),
        ];

        _opinionatedRules =
        [
            new MagicNumberRule(sc),
            new BooleanParameterRule(sc),
            new MissingCancellationTokenRule(sc),
        ];
    }

    public async Task<IReadOnlyList<Diagnostic>> LintFileAsync(
        string filePath, LintMode mode, bool fix = false)
    {
        if (!File.Exists(filePath)) return [];

        var config = _loader.GetConfig(filePath);
        var rules = SelectRules(mode, config);
        var diagnostics = new List<Diagnostic>();

        // Read + parse the file ONCE, then share the same syntax tree across every rule below
        // (instead of each rule re-reading and re-parsing the same file). Built lazily so a pure
        // fix pass that resolves every finding need not parse, and so a file with no applicable
        // rules costs nothing.
        SourceUnit? unit = null;

        foreach (var rule in rules)
        {
            if (fix && rule.Category == RuleCategory.EditorConfig)
            {
                var wasFixed = await rule.FixAsync(filePath, config);
                if (wasFixed) { unit = null; continue; }
            }

            unit ??= await SourceUnit.LoadAsync(filePath, config);
            await RunRuleAsync(rule, unit, diagnostics);
        }

        return diagnostics;
    }

    // Run one rule and fold its (severity-adjusted) findings into the list. Kept separate so the
    // try / foreach / if nesting stays shallow.
    static async Task RunRuleAsync(IRule rule, SourceUnit unit, List<Diagnostic> diagnostics)
    {
        var config = unit.Config;
        try
        {
            var results = await rule.AnalyzeAsync(unit);
            foreach (var d in results)
            {
                var adjusted = ApplySeverityOverride(d, config);
                if (adjusted is not null) diagnostics.Add(adjusted);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [{rule.Id}] error on {Path.GetFileName(unit.Path)}: {ex.Message}");
        }
    }

    // Honour `dotnet_diagnostic.<RuleId>.severity` from .editorconfig for ANY cslint rule
    // (EC/CS/FMT/SAST/OP). `none`/`silent` drops the finding; otherwise it retunes severity.
    // This is how by-design findings (e.g. SAST002 console output in a CLI) are suppressed.
    static Diagnostic? ApplySeverityOverride(Diagnostic d, FileConfig config)
    {
        if (!config.Properties.TryGetValue($"dotnet_diagnostic.{d.Rule}.severity", out var sev))
            return d;

        return sev.Trim().ToLowerInvariant() switch
        {
            "none" or "silent" => null,
            "error" => d with { Severity = Severity.Error },
            "warning" or "suggestion" or "info" or "hint" => d with { Severity = Severity.Warning },
            _ => d,
        };
    }

    List<IRule> SelectRules(LintMode mode, FileConfig config)
    {
        var rules = new List<IRule>();

        rules.AddRange(_editorConfigRules.Where(r => r.AppliesTo(config)));

        if (mode is LintMode.EditorConfigAndSast or LintMode.All)
            rules.AddRange(_sastRules);

        // Honour AppliesTo so the opinionated toggles (FlagMagicNumbers / FlagBooleanParameters /
        // FlagMissingCancellationToken — set by --no-* flags and the .dependably-check config)
        // actually disable their rules.
        if (mode is LintMode.All)
            rules.AddRange(_opinionatedRules.Where(r => r.AppliesTo(config)));

        return rules;
    }

    public async Task<Summary> LintFilesAsync(
        IEnumerable<string> files, LintMode mode, bool fix = false)
    {
        var allDiagnostics = new List<Diagnostic>();
        int fileCount = 0;

        await Parallel.ForEachAsync(files,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (file, _) =>
            {
                var diags = await LintFileAsync(file, mode, fix);
                lock (allDiagnostics)
                {
                    allDiagnostics.AddRange(diags);
                    fileCount++;
                }
            });

        return new Summary(allDiagnostics, fileCount);
    }

    public string ExplainFile(string filePath)
    {
        var config = _loader.GetConfig(filePath);
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"File: {filePath}");
        sb.AppendLine();

        if (config.ConfigFiles.Count == 0)
        {
            sb.AppendLine("No .editorconfig files found.");
            return sb.ToString();
        }

        sb.AppendLine("EditorConfig files (lowest to highest priority):");
        foreach (var (path, _) in config.ConfigFiles)
            sb.AppendLine($"  {path}");

        sb.AppendLine();
        sb.AppendLine("Effective properties:");
        foreach (var (key, value) in config.Properties.OrderBy(k => k.Key))
            sb.AppendLine($"  {key} = {value}");

        sb.AppendLine();
        sb.AppendLine("Active EditorConfig rules:");
        foreach (var rule in _editorConfigRules.Where(r => r.AppliesTo(config)))
            sb.AppendLine($"  [{rule.Id}]");

        var inactive = _editorConfigRules.Where(r => !r.AppliesTo(config)).ToList();
        if (inactive.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Inactive (no matching config keys):");
            foreach (var rule in inactive)
                sb.AppendLine($"  [{rule.Id}]");
        }

        sb.AppendLine();
        sb.AppendLine("SAST rules (active when --sast or --all):");
        foreach (var rule in _sastRules)
            sb.AppendLine($"  [{rule.Id}]");

        sb.AppendLine();
        sb.AppendLine("Opinionated rules (active when --scan):");
        foreach (var rule in _opinionatedRules)
            sb.AppendLine($"  [{rule.Id}]");

        return sb.ToString();
    }

    public EditorConfigLoader Loader => _loader;
}

record Summary(IReadOnlyList<Diagnostic> Diagnostics, int FilesChecked)
{
    public int ErrorCount => Diagnostics.Count(d => d.Severity == Severity.Error);
    public int WarningCount => Diagnostics.Count(d => d.Severity == Severity.Warning);
    public bool HasErrors => ErrorCount > 0;
}
