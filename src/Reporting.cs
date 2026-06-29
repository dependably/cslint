namespace CsLint;

enum Severity { Warning, Error }
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

    // The shared Dependably finding schema version this JSON envelope conforms to.
    const string SchemaVersion = "1.0";

    public static void Write(
        IReadOnlyList<Diagnostic> diagnostics,
        OutputFormat format,
        string root,
        int scanned = 0,
        int exitCode = 0,
        string toolVersion = "")
    {
        switch (format)
        {
            case OutputFormat.Human: WriteHuman(diagnostics, root); break;
            case OutputFormat.Json: WriteJson(diagnostics, root, scanned, exitCode, toolVersion); break;
            case OutputFormat.GitHub: WriteGitHub(diagnostics); break;
        }
    }

    static void WriteHuman(IReadOnlyList<Diagnostic> diagnostics, string root)
    {
        if (diagnostics.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("No violations found.");
            Console.ResetColor();
            return;
        }

        var groups = diagnostics
            .OrderBy(d => GetCategory(d.Rule))
            .ThenBy(d => d.File)
            .ThenBy(d => d.Line)
            .ThenBy(d => d.Column)
            .GroupBy(d => GetCategory(d.Rule));

        int errors = 0, warnings = 0;

        foreach (var group in groups)
        {
            var (e, w) = WriteGroup(group, root);
            errors += e;
            warnings += w;
        }

        WriteSummary(errors, warnings);
    }

    static (int Errors, int Warnings) WriteGroup(
        IGrouping<int, Diagnostic> group, string root)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"\n  [{CategoryLabel(group.Key)}]");
        Console.ResetColor();

        string? currentFile = null;
        int errors = 0, warnings = 0;

        foreach (var d in group)
        {
            if (d.File != currentFile)
            {
                Console.WriteLine($"\n  {Path.GetRelativePath(root, d.File)}");
                currentFile = d.File;
            }

            WriteEntry(d);
            if (d.Severity == Severity.Error) errors++;
            else warnings++;
        }

        return (errors, warnings);
    }

    static void WriteEntry(Diagnostic d)
    {
        // Render the per-finding severity word using the shared suite ladder vocabulary
        // (error -> high, warning -> low), padded so the message column stays aligned.
        var levelLabel = d.Severity == Severity.Error ? "high" : "low ";
        var color = d.Severity == Severity.Error ? ConsoleColor.Red : ConsoleColor.Yellow;

        Console.Write($"    {d.Line,LineFieldWidth}:{d.Column,-ColumnFieldWidth}  ");
        Console.ForegroundColor = color;
        Console.Write(levelLabel);
        Console.ResetColor();
        Console.WriteLine($"  {d.Message}  [{d.Rule}]");
    }

    static void WriteSummary(int errors, int warnings)
    {
        Console.WriteLine();
        Console.ForegroundColor = errors > 0 ? ConsoleColor.Red : ConsoleColor.Green;
        Console.Write($"{errors} error{(errors != 1 ? "s" : "")}");
        Console.ResetColor();
        Console.Write(", ");
        Console.ForegroundColor = warnings > 0 ? ConsoleColor.Yellow : ConsoleColor.Green;
        Console.Write($"{warnings} warning{(warnings != 1 ? "s" : "")}");
        Console.ResetColor();
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
            .OrderBy(d => CategoryRank(d.Rule))
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
                bySeverity = new { critical = 0, high, moderate = 0, low, info = 0 },
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

    static void WriteGitHub(IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach (var d in diagnostics)
        {
            var level = d.Severity == Severity.Error ? "error" : "warning";
            var file = d.File.Replace('\\', '/');
            Console.WriteLine(
                $"::{level} file={file},line={d.Line},col={d.Column}::[{d.Rule}] {d.Message}");
        }
    }

    // The shared suite severity ladder: cslint emits error -> high, warning -> low.
    static string Ladder(Severity sev) => sev == Severity.Error ? "high" : "low";

    // The shared-schema category group for a rule id. cslint maps its rule families onto four of
    // the schema's category values: EC* universal editorconfig rules, CS*/FMT/IDE* general lint,
    // SAST* security, OP* opinionated patterns.
    static string Category(string ruleId)
    {
        if (ruleId.StartsWith("SAST", StringComparison.OrdinalIgnoreCase)) return "sast";
        if (ruleId.StartsWith("OP", StringComparison.OrdinalIgnoreCase)) return "opinionated";
        if (ruleId.StartsWith("EC", StringComparison.OrdinalIgnoreCase)) return "editorconfig";
        return "lint";
    }

    // Stable ordering rank used to group both human and JSON output by category.
    static int CategoryRank(string ruleId) => Category(ruleId) switch
    {
        "editorconfig" => 0,
        "lint" => 1,
        "sast" => 2,
        "opinionated" => 3,
        _ => 4,
    };

    static int GetCategory(string ruleId) => CategoryRank(ruleId);

    static string CategoryLabel(int cat) => cat switch
    {
        0 => "editorconfig",
        1 => "lint",
        2 => "sast",
        3 => "opinionated",
        _ => "lint",
    };
}
