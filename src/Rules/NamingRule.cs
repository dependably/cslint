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
            foreach (var symbol in ExtractSymbols(node))
            {
                if (symbol.Name == null || symbol.Location == null) continue;

                // Match the first rule whose symbol spec applies — honouring not just the
                // symbol kind but also required_modifiers and applicable_accessibilities, the
                // way Roslyn does. Skipping the latter two made every field match a
                // const-only rule (CS040 false positives on `_camelCase` instance fields).
                var matchedRule = rules.FirstOrDefault(r =>
                    r.Symbols.Contains(symbol.Kind)
                    && r.Modifiers.All(m => symbol.Modifiers.Contains(m))
                    && AccessibilityMatches(r.Accessibilities, symbol.Accessibility));
                if (matchedRule == null) continue;

                if (!ValidateName(symbol.Name, matchedRule.Style))
                {
                    var loc = symbol.Location.GetLineSpan();
                    diagnostics.Add(StyleHelper.Make(filePath,
                        loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                        $"Name '{symbol.Name}' violates naming rule '{matchedRule.Name}': expected {DescribeStyle(matchedRule.Style)}.",
                        matchedRule.Severity));
                }
            }
        }

        return diagnostics;
    }

    // A rule with no applicable_accessibilities (or the '*' wildcard) applies to any
    // accessibility; otherwise the symbol's effective accessibility must be listed.
    static bool AccessibilityMatches(HashSet<string> ruleAccessibilities, string symbolAccessibility) =>
        ruleAccessibilities.Count == 0
        || ruleAccessibilities.Contains("*")
        || ruleAccessibilities.Contains(symbolAccessibility);

    record SymbolInfo(
        string Kind,
        string? Name,
        Location? Location,
        HashSet<string> Modifiers,
        string Accessibility);

    record NamingRuleDefinition(
        string Name,
        HashSet<string> Symbols,
        HashSet<string> Modifiers,
        HashSet<string> Accessibilities,
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
            var accessibilities = ParseAccessibilities(symbolGroup, props);
            var style = ParseStyle(styleName, props);
            var priority = int.TryParse(props.GetValueOrDefault($"{prefix}priority", "0"), out var p) ? p : 0;

            rules.Add(new NamingRuleDefinition(
                ruleName!, symbols, modifiers, accessibilities, style, severity, priority));
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

    static HashSet<string> ParseAccessibilities(string groupName, IReadOnlyDictionary<string, string> props)
    {
        var key = $"dotnet_naming_symbols.{groupName}.applicable_accessibilities";
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

    static SymbolInfo[] ExtractSymbols(SyntaxNode node)
    {
        return node switch
        {
            ClassDeclarationSyntax c => [Make("class", c.Identifier, c.Modifiers, node)],
            InterfaceDeclarationSyntax i => [Make("interface", i.Identifier, i.Modifiers, node)],
            StructDeclarationSyntax s => [Make("struct", s.Identifier, s.Modifiers, node)],
            EnumDeclarationSyntax e => [Make("enum", e.Identifier, e.Modifiers, node)],
            RecordDeclarationSyntax r => [Make("class", r.Identifier, r.Modifiers, node)],
            DelegateDeclarationSyntax d => [Make("delegate", d.Identifier, d.Modifiers, node)],
            MethodDeclarationSyntax m => [Make("method", m.Identifier, m.Modifiers, node)],
            PropertyDeclarationSyntax p => [Make("property", p.Identifier, p.Modifiers, node)],
            EventDeclarationSyntax ev => [Make("event", ev.Identifier, ev.Modifiers, node)],
            ParameterSyntax par => [Make("parameter", par.Identifier, par.Modifiers, node)],
            TypeParameterSyntax tp => [Make("type_parameter", tp.Identifier, default, node)],
            FieldDeclarationSyntax f => f.Declaration.Variables
                .Select(v => Make("field", v.Identifier, f.Modifiers, node)).ToArray(),
            LocalDeclarationStatementSyntax l => l.Declaration.Variables
                .Select(v => Make("local", v.Identifier, l.Modifiers, node)).ToArray(),
            _ => []
        };
    }

    static SymbolInfo Make(string kind, SyntaxToken identifier, SyntaxTokenList modifiers, SyntaxNode node)
    {
        var mods = modifiers.Select(m => m.Text).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new SymbolInfo(kind, identifier.Text, identifier.GetLocation(), mods,
            EffectiveAccessibility(kind, mods, node));
    }

    // The editorconfig accessibility token for the declaration. When no access modifier is
    // present we fall back to the C# default: nested members and types default to private,
    // top-level types to internal.
    static string EffectiveAccessibility(string kind, HashSet<string> mods, SyntaxNode node)
    {
        bool pub = mods.Contains("public");
        bool priv = mods.Contains("private");
        bool prot = mods.Contains("protected");
        bool intern = mods.Contains("internal");

        if (pub) return "public";
        if (priv && prot) return "private_protected";
        if (prot && intern) return "protected_internal";
        if (prot) return "protected";
        if (intern) return "internal";
        if (priv) return "private";

        var isType = kind is "class" or "struct" or "interface" or "enum" or "delegate";
        if (isType && node.Parent is BaseNamespaceDeclarationSyntax or CompilationUnitSyntax)
            return "internal";
        return kind is "local" or "parameter" or "type_parameter" ? "local" : "private";
    }
}
