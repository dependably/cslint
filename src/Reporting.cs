namespace CsLint;

enum Severity { Info, Warning, Error }
enum OutputFormat { Human, Json, GitHub }

record Diagnostic(
    string File,
    int Line,
    int Column,
    string Rule,
    string Message,
    Severity Severity);

static class Reporter
{
    // Column widths for the aligned "line:col" gutter in human output.
    const int LineFieldWidth = 5;
    const int ColumnFieldWidth = 4;

    // Canonical severity words are padded to this width so the message column stays aligned
    // ("warning" is the widest at 7 chars).
    const int SeverityFieldWidth = 7;

    // The shared Dependably finding schema version this JSON envelope conforms to.
    const string SchemaVersion = "1.0";

    // Default number of findings the human report prints before collapsing the remainder into a
    // "…and N more" note; raise it with --max-findings or lift it entirely with --no-limit.
    const int DefaultMaxFindings = 200;

    // Within a severity section, after this many findings of the same rule are printed, further
    // occurrences of that rule are collapsed into a single "+N more <RULE> in M files" line so a
    // single noisy rule can't bury everything else.
    const int PerRuleCollapseThreshold = 10;

    public static void Write(
        IReadOnlyList<Diagnostic> diagnostics,
        OutputFormat format,
        string root,
        int scanned = 0,
        int exitCode = 0,
        string toolVersion = "",
        int? maxFindings = null,
        bool noLimit = false)
    {
        switch (format)
        {
            case OutputFormat.Human: WriteHuman(diagnostics, root, maxFindings, noLimit); break;
            case OutputFormat.Json: WriteJson(diagnostics, root, scanned, exitCode, toolVersion); break;
            case OutputFormat.GitHub: WriteGitHub(diagnostics, root); break;
        }
    }

    // Severities in report order: errors first so a handful of high-severity findings can never be
    // buried under a flood of warnings/suggestions.
    static readonly Severity[] SeverityDisplayOrder = [Severity.Error, Severity.Warning, Severity.Info];

    static void WriteHuman(
        IReadOnlyList<Diagnostic> diagnostics, string root, int? maxFindings, bool noLimit)
    {
        if (diagnostics.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("No violations found.");
            Console.ResetColor();
            return;
        }

        // --no-limit lifts the cap and disables per-rule collapse (a full, uncollapsed dump);
        // otherwise a global cap and per-rule collapse keep large runs scannable.
        var cap = noLimit ? int.MaxValue : (maxFindings ?? DefaultMaxFindings);
        var collapse = !noLimit;

        int printed = 0;      // finding lines actually printed (counts toward the global cap)
        int notShown = 0;     // findings suppressed purely by the global cap

        foreach (var severity in SeverityDisplayOrder)
        {
            var inSection = diagnostics
                .Where(d => d.Severity == severity)
                .OrderBy(d => d.File, StringComparer.Ordinal)
                .ThenBy(d => d.Line)
                .ThenBy(d => d.Column)
                .ToList();
            if (inSection.Count == 0) continue;

            Console.ForegroundColor = SeverityColor(severity);
            Console.WriteLine($"\n  {SeverityWord(severity)}s");
            Console.ResetColor();

            var shownPerRule = new Dictionary<string, int>(StringComparer.Ordinal);
            var collapsedPerRule = new Dictionary<string, (int Count, HashSet<string> Files)>(StringComparer.Ordinal);
            string? currentFile = null;

            foreach (var d in inSection)
            {
                // Per-rule collapse: once a rule has shown its quota in this section, tally the rest.
                if (collapse && shownPerRule.GetValueOrDefault(d.Rule) >= PerRuleCollapseThreshold)
                {
                    var entry = collapsedPerRule.TryGetValue(d.Rule, out var c) ? c : (0, new HashSet<string>(StringComparer.Ordinal));
                    entry.Item1++;
                    entry.Item2.Add(d.File);
                    collapsedPerRule[d.Rule] = entry;
                    continue;
                }

                // Global cap: everything past it is counted and surfaced once at the end.
                if (printed >= cap)
                {
                    notShown++;
                    continue;
                }

                if (d.File != currentFile)
                {
                    Console.WriteLine($"\n  {RelativePath(root, d.File)}");
                    currentFile = d.File;
                }

                WriteEntry(d);
                printed++;
                shownPerRule[d.Rule] = shownPerRule.GetValueOrDefault(d.Rule) + 1;
            }

            foreach (var (rule, info) in collapsedPerRule.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key, StringComparer.Ordinal))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(
                    $"    +{info.Count} more {rule} in {info.Files.Count} file{(info.Files.Count != 1 ? "s" : "")}");
                Console.ResetColor();
            }
        }

        if (notShown > 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(
                $"\n  …and {notShown} more finding{(notShown != 1 ? "s" : "")} not shown " +
                "(use --no-limit to show all, or --max-findings N to raise the cap).");
            Console.ResetColor();
        }

        WriteFrequencyTable(diagnostics);

        int errors = diagnostics.Count(d => d.Severity == Severity.Error);
        int warnings = diagnostics.Count(d => d.Severity == Severity.Warning);
        int infos = diagnostics.Count(d => d.Severity == Severity.Info);
        WriteSummary(errors, warnings, infos);
    }

    // A per-rule frequency table, most frequent first, so the worst offenders are obvious at a
    // glance even when their individual findings were collapsed or capped above. Ends with a hint
    // on how to silence a rule.
    static void WriteFrequencyTable(IReadOnlyList<Diagnostic> diagnostics)
    {
        var byRule = diagnostics
            .GroupBy(d => d.Rule, StringComparer.Ordinal)
            .Select(g => (Rule: g.Key, Count: g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Rule, StringComparer.Ordinal)
            .ToList();

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("\n  Findings by rule:");
        Console.ResetColor();

        var width = byRule.Max(x => x.Rule.Length);
        foreach (var (rule, count) in byRule)
            Console.WriteLine($"    {rule.PadRight(width)}  {count}");

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(
            "  Hint: silence a rule with `dotnet_diagnostic.<RULE>.severity = none` in .editorconfig.");
        Console.ResetColor();
    }

    // The canonical severity vocabulary printed on every finding and in the summary:
    // error / warning / info. (cslint's info tier is spelled `suggestion` in .editorconfig and is
    // also accepted as the `suggestion` alias by --fail-on.)
    static string SeverityWord(Severity severity) => severity switch
    {
        Severity.Error => "error",
        Severity.Warning => "warning",
        _ => "info",
    };

    static ConsoleColor SeverityColor(Severity severity) => severity switch
    {
        Severity.Error => ConsoleColor.Red,
        Severity.Warning => ConsoleColor.Yellow,
        _ => ConsoleColor.Cyan,
    };

    static void WriteEntry(Diagnostic d)
    {
        // Per-finding severity word uses the canonical vocabulary (error/warning/info), padded so
        // the message column stays aligned.
        var levelLabel = SeverityWord(d.Severity).PadRight(SeverityFieldWidth);

        Console.Write($"    {d.Line,LineFieldWidth}:{d.Column,-ColumnFieldWidth}  ");
        Console.ForegroundColor = SeverityColor(d.Severity);
        Console.Write(levelLabel);
        Console.ResetColor();
        Console.WriteLine($"  {d.Message}  [{d.Rule}]");
    }

    static void WriteSummary(int errors, int warnings, int infos)
    {
        Console.WriteLine();
        Console.ForegroundColor = errors > 0 ? ConsoleColor.Red : ConsoleColor.Green;
        Console.Write($"{errors} error{(errors != 1 ? "s" : "")}");
        Console.ResetColor();
        Console.Write(", ");
        Console.ForegroundColor = warnings > 0 ? ConsoleColor.Yellow : ConsoleColor.Green;
        Console.Write($"{warnings} warning{(warnings != 1 ? "s" : "")}");
        Console.ResetColor();
        if (infos > 0)
        {
            Console.Write(", ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write($"{infos} info");
            Console.ResetColor();
        }
        Console.WriteLine();
    }

    // Cache the serializer options: constructing JsonSerializerOptions per call is expensive
    // and defeats System.Text.Json's internal metadata cache (CA1869).
    static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    // Emit the shared Dependably finding JSON envelope (schema v1): one valid JSON object on
    // stdout. The six core keys (tool/toolVersion/schemaVersion/target/summary/findings) are
    // identical across every tool in the suite; status/progress go to stderr (see Cli).
    static void WriteJson(
        IReadOnlyList<Diagnostic> diagnostics, string root, int scanned, int exitCode, string toolVersion)
    {
        var findings = diagnostics
            .OrderBy(d => GetCategory(d.Rule))
            .ThenBy(d => d.File, StringComparer.Ordinal)
            .ThenBy(d => d.Line)
            .ThenBy(d => d.Column)
            .Select(d => new
            {
                severity = Ladder(d.Severity),
                ruleId = d.Rule,
                category = Category(d.Rule),
                message = d.Message,
                location = new
                {
                    file = RelativePath(root, d.File),
                    line = (int?)d.Line,
                    column = (int?)d.Column,
                },
                remediation = (string?)null,
            })
            .ToList();

        var high = diagnostics.Count(d => d.Severity == Severity.Error);
        var low = diagnostics.Count(d => d.Severity == Severity.Warning);
        var info = diagnostics.Count(d => d.Severity == Severity.Info);

        var envelope = new
        {
            tool = "cslint",
            toolVersion,
            schemaVersion = SchemaVersion,
            target = root,
            summary = new
            {
                scanned,
                findings = findings.Count,
                bySeverity = new { critical = 0, high, moderate = 0, low, info },
                exitCode,
            },
            findings,
        };

        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(envelope, JsonOpts));
    }

    static string RelativePath(string root, string file)
    {
        try { return Path.GetRelativePath(root, file).Replace('\\', '/'); }
        catch (ArgumentException) { return file.Replace('\\', '/'); }
    }

    static void WriteGitHub(IReadOnlyList<Diagnostic> diagnostics, string root)
    {
        foreach (var d in diagnostics)
        {
            var level = d.Severity switch
            {
                Severity.Error => "error",
                Severity.Warning => "warning",
                _ => "notice"
            };
            var file = GitHubEscapeProperty(RelativePath(root, d.File));
            var message = GitHubEscapeData($"[{d.Rule}] {d.Message}");
            Console.WriteLine(
                $"::{level} file={file},line={d.Line},col={d.Column}::{message}");
        }
    }

    // Escape a GitHub Actions workflow command property value per the toolkit spec.
    // Property values are delimited by ',' (between properties) and ':' (before data),
    // so those characters — plus '%' (escape prefix), '\r', and '\n' — must be encoded.
    // '%' must be escaped first to avoid double-encoding subsequent replacements.
    static string GitHubEscapeProperty(string value) =>
        value
            .Replace("%", "%25")
            .Replace("\r", "%0D")
            .Replace("\n", "%0A")
            .Replace(":", "%3A")
            .Replace(",", "%2C");

    // Escape a GitHub Actions workflow command data value per the toolkit spec.
    // Data ends at end-of-line, so only '%', '\r', and '\n' need encoding.
    // '%' must be escaped first to avoid double-encoding subsequent replacements.
    static string GitHubEscapeData(string value) =>
        value
            .Replace("%", "%25")
            .Replace("\r", "%0D")
            .Replace("\n", "%0A");

    // The shared suite severity ladder: cslint emits error -> high, warning -> low, info -> info.
    static string Ladder(Severity sev) => sev switch
    {
        Severity.Error => "high",
        Severity.Info => "info",
        _ => "low"
    };

    // Category groups in display/sort order: a rule's rank is its index here, used to group both
    // human and JSON output. EC* universal editorconfig rules, CS*/FMT/IDE* general lint, SAST*
    // security, OP* opinionated patterns.
    static readonly string[] CategoryOrder = ["editorconfig", "lint", "sast", "opinionated"];

    // The shared-schema category group for a rule id (one of CategoryOrder).
    static string Category(string ruleId)
    {
        if (ruleId.StartsWith("SAST", StringComparison.OrdinalIgnoreCase)) return "sast";
        if (ruleId.StartsWith("OP", StringComparison.OrdinalIgnoreCase)) return "opinionated";
        if (ruleId.StartsWith("EC", StringComparison.OrdinalIgnoreCase)) return "editorconfig";
        return "lint";
    }

    // Stable ordering rank used to group JSON output by category.
    static int GetCategory(string ruleId)
    {
        var rank = Array.IndexOf(CategoryOrder, Category(ruleId));
        return rank < 0 ? CategoryOrder.Length : rank;
    }
}
