using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsLint.Rules;

sealed class NamingRule : IRule
{
    public string Id => "CS040";

    public bool AppliesTo(FileConfig config) =>
        config.Properties.Keys.Any(k =>
            k.StartsWith("dotnet_naming_rule.", StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var rules = ParseRules(config.Properties);
        if (rules.Count == 0) return [];

        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var node in root.DescendantNodes())
        {
            foreach (var (symbolKind, name, location) in ExtractSymbols(node))
            {
                if (name == null || location == null) continue;

                var matchedRule = rules.FirstOrDefault(r => r.Symbols.Contains(symbolKind));
                if (matchedRule == null) continue;

                if (!ValidateName(name, matchedRule.Style))
                {
                    var loc = location.GetLineSpan();
                    diagnostics.Add(StyleHelper.Make(filePath,
                        loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                        $"Name '{name}' violates naming rule '{matchedRule.Name}': expected {DescribeStyle(matchedRule.Style)}.",
                        matchedRule.Severity));
                }
                break;
            }
        }

        return diagnostics;
    }

    record NamingRuleDefinition(
        string Name,
        HashSet<string> Symbols,
        HashSet<string> Modifiers,
        NamingStyleDefinition Style,
        Severity Severity,
        int Priority);

    record NamingStyleDefinition(
        string Capitalization,
        string? RequiredPrefix,
        string? RequiredSuffix,
        string? WordSeparator);

    static List<NamingRuleDefinition> ParseRules(IReadOnlyDictionary<string, string> props)
    {
        var ruleNames = props.Keys
            .Where(k => k.StartsWith("dotnet_naming_rule.", StringComparison.OrdinalIgnoreCase))
            .Select(k => { var parts = k.Split('.'); return parts.Length > 1 ? parts[1] : null; })
            .Where(n => n != null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rules = new List<NamingRuleDefinition>();

        foreach (var ruleName in ruleNames)
        {
            var prefix = $"dotnet_naming_rule.{ruleName}.";
            if (!props.TryGetValue($"{prefix}symbols", out var symbolGroup)) continue;
            if (!props.TryGetValue($"{prefix}style", out var styleName)) continue;

            var (severityStr, _) = (props.GetValueOrDefault($"{prefix}severity", "warning"), 0);
            var severity = severityStr.ToLowerInvariant() switch
            {
                "error" => Severity.Error,
                _ => Severity.Warning
            };

            var symbols = ParseSymbols(symbolGroup, props);
            var modifiers = ParseModifiers(symbolGroup, props);
            var style = ParseStyle(styleName, props);
            var priority = int.TryParse(props.GetValueOrDefault($"{prefix}priority", "0"), out var p) ? p : 0;

            rules.Add(new NamingRuleDefinition(ruleName!, symbols, modifiers, style, severity, priority));
        }

        return rules.OrderByDescending(r => r.Priority).ToList();
    }

    static HashSet<string> ParseSymbols(string groupName, IReadOnlyDictionary<string, string> props)
    {
        var key = $"dotnet_naming_symbols.{groupName}.applicable_kinds";
        var raw = props.GetValueOrDefault(key, "");
        return raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    static HashSet<string> ParseModifiers(string groupName, IReadOnlyDictionary<string, string> props)
    {
        var key = $"dotnet_naming_symbols.{groupName}.required_modifiers";
        var raw = props.GetValueOrDefault(key, "");
        return raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    static NamingStyleDefinition ParseStyle(string styleName, IReadOnlyDictionary<string, string> props)
    {
        var prefix = $"dotnet_naming_style.{styleName}.";
        return new NamingStyleDefinition(
            props.GetValueOrDefault($"{prefix}capitalization", "pascal_case"),
            props.GetValueOrDefault($"{prefix}required_prefix") is { Length: > 0 } rp ? rp : null,
            props.GetValueOrDefault($"{prefix}required_suffix") is { Length: > 0 } rs ? rs : null,
            props.GetValueOrDefault($"{prefix}word_separator") is { Length: > 0 } ws ? ws : null);
    }

    static bool ValidateName(string name, NamingStyleDefinition style)
    {
        var work = name;

        if (style.RequiredPrefix != null)
        {
            if (!work.StartsWith(style.RequiredPrefix)) return false;
            work = work[style.RequiredPrefix.Length..];
        }

        if (style.RequiredSuffix != null)
        {
            if (!work.EndsWith(style.RequiredSuffix)) return false;
            work = work[..^style.RequiredSuffix.Length];
        }

        if (work.Length == 0) return style.RequiredPrefix != null || style.RequiredSuffix != null;

        return style.Capitalization.ToLowerInvariant() switch
        {
            "pascal_case" => char.IsUpper(work[0]),
            "camel_case" => char.IsLower(work[0]),
            "all_upper" => work.All(c => !char.IsLetter(c) || char.IsUpper(c)),
            "all_lower" => work.All(c => !char.IsLetter(c) || char.IsLower(c)),
            "first_word_upper" => work.Length > 0 && char.IsUpper(work[0]),
            _ => true
        };
    }

    static string DescribeStyle(NamingStyleDefinition s)
    {
        var desc = s.Capitalization;
        if (s.RequiredPrefix != null) desc += $" with prefix '{s.RequiredPrefix}'";
        if (s.RequiredSuffix != null) desc += $" with suffix '{s.RequiredSuffix}'";
        return desc;
    }

    static (string Kind, string? Name, Location? Location)[] ExtractSymbols(SyntaxNode node)
    {
        return node switch
        {
            ClassDeclarationSyntax c => [("class", c.Identifier.Text, c.Identifier.GetLocation())],
            InterfaceDeclarationSyntax i => [("interface", i.Identifier.Text, i.Identifier.GetLocation())],
            StructDeclarationSyntax s => [("struct", s.Identifier.Text, s.Identifier.GetLocation())],
            EnumDeclarationSyntax e => [("enum", e.Identifier.Text, e.Identifier.GetLocation())],
            RecordDeclarationSyntax r => [("class", r.Identifier.Text, r.Identifier.GetLocation())],
            DelegateDeclarationSyntax d => [("delegate", d.Identifier.Text, d.Identifier.GetLocation())],
            MethodDeclarationSyntax m => [("method", m.Identifier.Text, m.Identifier.GetLocation())],
            PropertyDeclarationSyntax p => [("property", p.Identifier.Text, p.Identifier.GetLocation())],
            EventDeclarationSyntax ev => [("event", ev.Identifier.Text, ev.Identifier.GetLocation())],
            ParameterSyntax par => [("parameter", par.Identifier.Text, par.Identifier.GetLocation())],
            TypeParameterSyntax tp => [("type_parameter", tp.Identifier.Text, tp.Identifier.GetLocation())],
            FieldDeclarationSyntax f => f.Declaration.Variables
                .Select(v => MakeSymbol("field", v.Identifier)).ToArray(),
            LocalDeclarationStatementSyntax l => l.Declaration.Variables
                .Select(v => MakeSymbol("local", v.Identifier)).ToArray(),
            _ => []
        };
    }

    static (string Kind, string? Name, Location? Location) MakeSymbol(
        string kind, SyntaxToken identifier) =>
        (kind, identifier.Text, identifier.GetLocation());
}
