namespace CsLint;

enum Severity { Warning, Error }
enum OutputFormat { Text, Json, GitHub }

record Diagnostic(
    string File,
    int Line,
    int Column,
    string Rule,
    string Message,
    Severity Severity);

static class Reporter
{
    // Column widths for the aligned "line:col" gutter in text output.
    const int LineFieldWidth = 5;
    const int ColumnFieldWidth = 4;

    public static void Write(
        IReadOnlyList<Diagnostic> diagnostics,
        OutputFormat format,
        string root)
    {
        switch (format)
        {
            case OutputFormat.Text: WriteText(diagnostics, root); break;
            case OutputFormat.Json: WriteJson(diagnostics); break;
            case OutputFormat.GitHub: WriteGitHub(diagnostics); break;
        }
    }

    static void WriteText(IReadOnlyList<Diagnostic> diagnostics, string root)
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
        var levelLabel = d.Severity == Severity.Error ? "error  " : "warning";
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

    static void WriteJson(IReadOnlyList<Diagnostic> diagnostics)
    {
        var output = diagnostics.Select(d => new
        {
            d.File,
            d.Line,
            d.Column,
            d.Rule,
            Category = CategoryLabel(GetCategory(d.Rule)),
            Severity = d.Severity.ToString().ToLowerInvariant(),
            d.Message,
        });

        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(output, JsonOpts));
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

    // Category ordinals: drive both grouping order and the section labels.
    const int CategoryEditorConfig = 0;
    const int CategorySast = 1;
    const int CategoryScan = 2;

    static int GetCategory(string ruleId)
    {
        if (ruleId.StartsWith("OP", StringComparison.OrdinalIgnoreCase)) return CategoryScan;
        if (ruleId.StartsWith("SAST", StringComparison.OrdinalIgnoreCase)) return CategorySast;
        return CategoryEditorConfig;
    }

    static string CategoryLabel(int cat) => cat switch
    {
        CategorySast => "SAST",
        CategoryScan => "Scan",
        _ => "EditorConfig"
    };
}
