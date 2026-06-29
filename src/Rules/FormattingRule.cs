using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace CsLint.Rules;

sealed class FormattingRule : IRule
{
    public string Id => "FMT";

    static readonly HashSet<string> FormattingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "csharp_new_line_before_open_brace",
        "csharp_new_line_before_else",
        "csharp_new_line_before_catch",
        "csharp_new_line_before_finally",
        "csharp_new_line_before_members_in_object_initializers",
        "csharp_new_line_before_members_in_anonymous_types",
        "csharp_new_line_between_query_expression_clauses",
        "csharp_indent_case_contents",
        "csharp_indent_switch_labels",
        "csharp_indent_labels",
        "csharp_indent_block_contents",
        "csharp_indent_braces",
        "csharp_indent_case_contents_when_block",
        "csharp_space_after_cast",
        "csharp_space_after_keywords_in_control_flow_statements",
        "csharp_space_between_parentheses",
        "csharp_space_before_colon_in_inheritance_clause",
        "csharp_space_after_colon_in_inheritance_clause",
        "csharp_space_around_binary_operators",
        "csharp_space_between_method_declaration_parameter_list_parentheses",
        "csharp_space_between_method_declaration_empty_parameter_list_parentheses",
        "csharp_space_between_method_declaration_name_and_open_parenthesis",
        "csharp_space_between_method_call_parameter_list_parentheses",
        "csharp_space_between_method_call_empty_parameter_list_parentheses",
        "csharp_space_between_method_call_name_and_opening_parenthesis",
        "csharp_space_after_comma",
        "csharp_space_before_comma",
        "csharp_space_after_dot",
        "csharp_space_before_dot",
        "csharp_space_after_semicolon_in_for_statement",
        "csharp_space_before_semicolon_in_for_statement",
        "csharp_space_around_declaration_statements",
        "csharp_space_before_open_square_brackets",
        "csharp_space_between_empty_square_brackets",
        "csharp_space_between_square_brackets",
        "csharp_preserve_single_line_statements",
        "csharp_preserve_single_line_blocks",
    };

    public bool AppliesTo(FileConfig config) =>
        config.ConfigFiles.Count > 0 &&
        config.Properties.Keys.Any(k => FormattingKeys.Contains(k));

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(SourceUnit unit)
    {
        var filePath = unit.Path;
        var config = unit.Config;
        var source = unit.Text;
        var formatted = await FormatWithRoslynAsync(filePath, source, config);
        if (formatted == null || formatted == source) return [];
        return DiffLines(filePath, source, formatted);
    }

    public async Task<bool> FixAsync(string filePath, FileConfig config)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var formatted = await FormatWithRoslynAsync(filePath, source, config);
        if (formatted == null || formatted == source) return false;
        await File.WriteAllTextAsync(filePath, formatted);
        return true;
    }

    static async Task<string?> FormatWithRoslynAsync(
        string filePath, string source, FileConfig config)
    {
        try
        {
            using var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();

            var solution = workspace.CurrentSolution
                .AddProject(projectId, "lint", "lint", LanguageNames.CSharp);

            foreach (var (ecPath, ecContent) in config.ConfigFiles)
            {
                solution = solution.AddAnalyzerConfigDocument(
                    DocumentId.CreateNewId(projectId),
                    Path.GetFileName(ecPath),
                    SourceText.From(ecContent, System.Text.Encoding.UTF8),
                    filePath: ecPath);
            }

            var docId = DocumentId.CreateNewId(projectId);
            solution = solution.AddDocument(
                docId,
                Path.GetFileName(filePath),
                SourceText.From(source, System.Text.Encoding.UTF8),
                filePath: filePath);

            var document = solution.GetDocument(docId)!;
            var formattedDoc = await Formatter.FormatAsync(document);
            var formattedText = await formattedDoc.GetTextAsync();
            return formattedText.ToString();
        }
        catch
        {
            return null;
        }
    }

    static List<Diagnostic> DiffLines(
        string filePath, string original, string formatted)
    {
        var origLines = original.Split('\n');
        var fmtLines = formatted.Split('\n');
        var diagnostics = new List<Diagnostic>();
        int maxLen = Math.Max(origLines.Length, fmtLines.Length);

        for (int i = 0; i < maxLen; i++)
        {
            var orig = i < origLines.Length ? origLines[i].TrimEnd('\r') : null;
            var fmt = i < fmtLines.Length ? fmtLines[i].TrimEnd('\r') : null;

            if (orig == fmt) continue;

            string message = (orig, fmt) switch
            {
                (null, _) => "Formatter would insert a line here.",
                (_, null) => "Formatter would remove this line.",
                _ when orig!.Trim() == fmt!.Trim()
                            => $"Indentation mismatch. Expected '{LeadingWhitespace(fmt!)}', got '{LeadingWhitespace(orig!)}'.",
                _ when orig!.Trim() is "{" or "}"
                            => "Brace placement does not match csharp_new_line_before_open_brace.",
                _ when fmt!.Trim() is "{" or "}"
                            => "Brace placement does not match csharp_new_line_before_open_brace.",
                _ => "Spacing or formatting does not match editorconfig csharp_space_* / csharp_indent_* rules.",
            };

            diagnostics.Add(new(filePath, i + 1, 1, "FMT", message, Severity.Warning));
        }

        return diagnostics;
    }

    static string LeadingWhitespace(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return line[..i].Replace("\t", "\\t").Replace(" ", "\u00b7");
    }
}
