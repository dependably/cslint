namespace CsEdLint;

enum Severity     { Warning, Error }
enum OutputFormat { Text, Json, GitHub }

record Diagnostic(
    string   File,
    int      Line,
    int      Column,
    string   Rule,
    string   Message,
    Severity Severity);

static class Reporter
{
    public static void Write(
        IReadOnlyList<Diagnostic> diagnostics,
        OutputFormat format,
        string root)
    {
        switch (format)
        {
            case OutputFormat.Text:   WriteText(diagnostics, root); break;
            case OutputFormat.Json:   WriteJson(diagnostics);       break;
            case OutputFormat.GitHub: WriteGitHub(diagnostics);     break;
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
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"\n  [{CategoryLabel(group.Key)}]");
            Console.ResetColor();

            string? currentFile = null;

            foreach (var d in group)
            {
                if (d.File != currentFile)
                {
                    var rel = Path.GetRelativePath(root, d.File);
                    Console.WriteLine($"\n  {rel}");
                    currentFile = d.File;
                }

                var levelLabel = d.Severity == Severity.Error ? "error  " : "warning";
                var color      = d.Severity == Severity.Error ? ConsoleColor.Red : ConsoleColor.Yellow;

                Console.Write($"    {d.Line,5}:{d.Column,-4}  ");
                Console.ForegroundColor = color;
                Console.Write(levelLabel);
                Console.ResetColor();
                Console.WriteLine($"  {d.Message}  [{d.Rule}]");

                if (d.Severity == Severity.Error) errors++;
                else warnings++;
            }
        }

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

    static void WriteJson(IReadOnlyList<Diagnostic> diagnostics)
    {
        var opts = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

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

        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(output, opts));
    }

    static void WriteGitHub(IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach (var d in diagnostics)
        {
            var level = d.Severity == Severity.Error ? "error" : "warning";
            var file  = d.File.Replace('\\', '/');
            Console.WriteLine(
                $"::{level} file={file},line={d.Line},col={d.Column}::[{d.Rule}] {d.Message}");
        }
    }

    static int GetCategory(string ruleId)
    {
        if (ruleId.StartsWith("OP",   StringComparison.OrdinalIgnoreCase)) return 2;
        if (ruleId.StartsWith("SAST", StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
    }

    static string CategoryLabel(int cat) => cat switch
    {
        1 => "SAST",
        2 => "Scan",
        _ => "EditorConfig"
    };
}
