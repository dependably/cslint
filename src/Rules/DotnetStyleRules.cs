using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsLint.Rules;

sealed class QualificationRule : IRule
{
    public string Id => "CS030";

    public bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("dotnet_style_qualification_for_field") ||
        config.Properties.ContainsKey("dotnet_style_qualification_for_property") ||
        config.Properties.ContainsKey("dotnet_style_qualification_for_method") ||
        config.Properties.ContainsKey("dotnet_style_qualification_for_event");

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var access in root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (access.Expression is not ThisExpressionSyntax) continue;
            if (access.Parent is MemberAccessExpressionSyntax) continue;

            var memberName = access.Name.Identifier.Text;

            foreach (var key in new[]
            {
                "dotnet_style_qualification_for_field",
                "dotnet_style_qualification_for_property",
                "dotnet_style_qualification_for_method",
                "dotnet_style_qualification_for_event",
            })
            {
                if (!StyleHelper.TryGet(config, key, out var val, out var sev)) continue;
                if (val != "false") continue;

                var loc = access.GetLocation().GetLineSpan();
                diagnostics.Add(StyleHelper.Make(filePath,
                    loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                    $"Remove 'this.' qualifier from '{memberName}' ({key} = false).",
                    sev));
                break;
            }
        }

        return diagnostics;
    }
}

sealed class PredefinedTypeRule : IRule
{
    public string Id => "CS031";

    static readonly Dictionary<string, string> ClrToKeyword = new(StringComparer.Ordinal)
    {
        ["Boolean"] = "bool",    ["Byte"]    = "byte",   ["SByte"]  = "sbyte",
        ["Int16"]   = "short",   ["UInt16"]  = "ushort", ["Int32"]   = "int",
        ["UInt32"]  = "uint",    ["Int64"]   = "long",   ["UInt64"]  = "ulong",
        ["Single"]  = "float",   ["Double"]  = "double", ["Decimal"] = "decimal",
        ["Char"]    = "char",    ["String"]  = "string", ["Object"]  = "object",
    };

    public bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("dotnet_style_predefined_type_for_locals_parameters_members") ||
        config.Properties.ContainsKey("dotnet_style_predefined_type_for_member_access");

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        bool localsTrue  = StyleHelper.TryGet(config, "dotnet_style_predefined_type_for_locals_parameters_members", out var lVal, out var lSev) && lVal == "true";
        bool localsFalse = !localsTrue && lVal == "false" && lSev != default;

        foreach (var node in root.DescendantNodes())
        {
            if (localsTrue && node is IdentifierNameSyntax idName &&
                ClrToKeyword.TryGetValue(idName.Identifier.Text, out var keyword) &&
                IsTypePosition(idName))
            {
                var loc = idName.GetLocation().GetLineSpan();
                diagnostics.Add(StyleHelper.Make(filePath,
                    loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                    $"Use '{keyword}' instead of '{idName.Identifier.Text}' (dotnet_style_predefined_type_for_locals_parameters_members = true).",
                    lSev));
            }
            else if (localsFalse && node is PredefinedTypeSyntax predefined && IsTypePosition(predefined))
            {
                var clrName = KeywordToClr(predefined.Keyword.ValueText);
                if (clrName != null)
                {
                    var loc = predefined.GetLocation().GetLineSpan();
                    diagnostics.Add(StyleHelper.Make(filePath,
                        loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                        $"Use '{clrName}' instead of '{predefined.Keyword.ValueText}' (dotnet_style_predefined_type_for_locals_parameters_members = false).",
                        lSev));
                }
            }
        }

        return diagnostics;
    }

    static bool IsTypePosition(SyntaxNode node)
    {
        var parent = node.Parent;
        return parent is VariableDeclarationSyntax or
                         ParameterSyntax or
                         PropertyDeclarationSyntax or
                         FieldDeclarationSyntax or
                         ReturnStatementSyntax or
                         MethodDeclarationSyntax or
                         CastExpressionSyntax or
                         TypeOfExpressionSyntax;
    }

    static string? KeywordToClr(string kw) =>
        ClrToKeyword.FirstOrDefault(kvp => kvp.Value == kw).Key;
}

sealed class AccessibilityModifiersRule : IRule
{
    public string Id => "CS032";

    public bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("dotnet_style_require_accessibility_modifiers");

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        if (!StyleHelper.TryGet(config, "dotnet_style_require_accessibility_modifiers",
            out var setting, out var severity))
            return [];

        if (setting is "never" or "omit_if_default") return [];

        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            if (member is NamespaceDeclarationSyntax or FileScopedNamespaceDeclarationSyntax)
                continue;

            var modifiers = GetModifiers(member);
            if (modifiers == null) continue;
            if (HasAccessModifier(modifiers.Value)) continue;
            if (setting == "for_non_interface_members" && IsInInterface(member)) continue;

            var loc = member.GetLocation().GetLineSpan();
            diagnostics.Add(StyleHelper.Make(filePath,
                loc.StartLinePosition.Line + 1, 1, Id,
                $"Member '{GetMemberName(member)}' is missing an explicit access modifier (dotnet_style_require_accessibility_modifiers = {setting}).",
                severity));
        }

        return diagnostics;
    }

    static SyntaxTokenList? GetModifiers(MemberDeclarationSyntax member) => member switch
    {
        BaseTypeDeclarationSyntax t    => t.Modifiers,
        MethodDeclarationSyntax m      => m.Modifiers,
        PropertyDeclarationSyntax p    => p.Modifiers,
        FieldDeclarationSyntax f       => f.Modifiers,
        EventDeclarationSyntax e       => e.Modifiers,
        ConstructorDeclarationSyntax c => c.Modifiers,
        _                              => null
    };

    static bool HasAccessModifier(SyntaxTokenList mods) => mods.Any(m =>
        m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.PrivateKeyword) ||
        m.IsKind(SyntaxKind.ProtectedKeyword) || m.IsKind(SyntaxKind.InternalKeyword));

    static bool IsInInterface(SyntaxNode node) =>
        node.Ancestors().OfType<InterfaceDeclarationSyntax>().Any();

    static string GetMemberName(MemberDeclarationSyntax member) => member switch
    {
        BaseTypeDeclarationSyntax t    => t.Identifier.Text,
        MethodDeclarationSyntax m      => m.Identifier.Text,
        PropertyDeclarationSyntax p    => p.Identifier.Text,
        FieldDeclarationSyntax f       => f.Declaration.Variables.FirstOrDefault()?.Identifier.Text ?? "?",
        EventDeclarationSyntax e       => e.Identifier.Text,
        ConstructorDeclarationSyntax c => c.Identifier.Text,
        _                              => member.GetType().Name
    };
}

sealed class ReadonlyFieldRule : IRule
{
    public string Id => "CS033";

    public bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("dotnet_style_readonly_field");

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        if (!StyleHelper.TryGet(config, "dotnet_style_readonly_field",
            out var setting, out var severity) || setting != "true")
            return [];

        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            var mods = field.Modifiers;
            if (mods.Any(SyntaxKind.ReadOnlyKeyword) || mods.Any(SyntaxKind.ConstKeyword)) continue;
            if (!mods.Any(SyntaxKind.PrivateKeyword) && !mods.Any(SyntaxKind.ProtectedKeyword)) continue;

            var containingType = field.Parent as TypeDeclarationSyntax;
            if (containingType == null) continue;

            foreach (var variable in field.Declaration.Variables)
            {
                var fieldName = variable.Identifier.Text;

                var writtenOutsideDecl = containingType.DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()
                    .Any(a => IsAssignmentToField(a, fieldName));

                var writtenInNonCtor = containingType.DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()
                    .Any(a => IsAssignmentToField(a, fieldName) && !IsInsideConstructor(a));

                if (!writtenInNonCtor && !writtenOutsideDecl && variable.Initializer != null)
                {
                    var loc = variable.Identifier.GetLocation().GetLineSpan();
                    diagnostics.Add(StyleHelper.Make(filePath,
                        loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                        $"Field '{fieldName}' could be declared as 'readonly' (dotnet_style_readonly_field = true).",
                        severity));
                }
                else if (!writtenInNonCtor && writtenOutsideDecl)
                {
                    var loc = variable.Identifier.GetLocation().GetLineSpan();
                    diagnostics.Add(StyleHelper.Make(filePath,
                        loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                        $"Field '{fieldName}' is only assigned in constructors; consider 'readonly' (dotnet_style_readonly_field = true).",
                        severity));
                }
            }
        }

        return diagnostics;
    }

    static bool IsAssignmentToField(AssignmentExpressionSyntax a, string name) =>
        a.Left is IdentifierNameSyntax id && id.Identifier.Text == name ||
        a.Left is MemberAccessExpressionSyntax mem &&
        mem.Expression is ThisExpressionSyntax &&
        mem.Name.Identifier.Text == name;

    static bool IsInsideConstructor(SyntaxNode node) =>
        node.Ancestors().OfType<ConstructorDeclarationSyntax>().Any();
}

sealed class ObjectInitializerRule : IRule
{
    public string Id => "CS034";

    public bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("dotnet_style_object_initializer") ||
        config.Properties.ContainsKey("dotnet_style_collection_initializer");

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        if (StyleHelper.TryGet(config, "dotnet_style_object_initializer",
            out var objVal, out var objSev) && objVal == "true")
        {
            foreach (var local in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
            {
                var variable = local.Declaration.Variables.FirstOrDefault();
                if (variable?.Initializer?.Value is not ObjectCreationExpressionSyntax creation) continue;
                if (creation.Initializer != null) continue;

                var localName = variable.Identifier.Text;
                var parent    = local.Parent;
                if (parent == null) continue;

                var siblings = parent.ChildNodes().ToList();
                var idx      = siblings.IndexOf(local);

                const int maxLookaheadStatements = 5;
                var nextAssignments = siblings.Skip(idx + 1)
                    .Take(maxLookaheadStatements)
                    .OfType<ExpressionStatementSyntax>()
                    .Select(s => s.Expression)
                    .OfType<AssignmentExpressionSyntax>()
                    .Where(a => IsPropertyOfVar(a.Left, localName))
                    .ToList();

                if (nextAssignments.Count >= 2)
                {
                    var loc = creation.GetLocation().GetLineSpan();
                    diagnostics.Add(StyleHelper.Make(filePath,
                        loc.StartLinePosition.Line + 1, 1, Id,
                        $"Prefer object initializer syntax for '{localName}' (dotnet_style_object_initializer = true).",
                        objSev));
                }
            }
        }

        return diagnostics;
    }

    static bool IsPropertyOfVar(ExpressionSyntax expr, string varName) =>
        expr is MemberAccessExpressionSyntax mem &&
        mem.Expression is IdentifierNameSyntax id &&
        id.Identifier.Text == varName;
}

sealed class NullCheckPreferenceRule : IRule
{
    public string Id => "CS035";

    public bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("dotnet_style_prefer_is_null_check_over_reference_equality_method");

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        if (!StyleHelper.TryGet(config,
            "dotnet_style_prefer_is_null_check_over_reference_equality_method",
            out var setting, out var severity) || setting != "true")
            return [];

        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax mem) continue;
            if (!IsObjectEquals(mem)) continue;

            var args = invocation.ArgumentList.Arguments;
            if (args.Count != 2) continue;
            if (!IsNullLiteral(args[0].Expression) && !IsNullLiteral(args[1].Expression)) continue;

            var loc = invocation.GetLocation().GetLineSpan();
            diagnostics.Add(StyleHelper.Make(filePath,
                loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                "Prefer 'is null' or 'is not null' over ReferenceEquals/Object.Equals null check (dotnet_style_prefer_is_null_check_over_reference_equality_method = true).",
                severity));
        }

        return diagnostics;
    }

    static bool IsObjectEquals(MemberAccessExpressionSyntax mem)
    {
        var name     = mem.Name.Identifier.Text;
        var receiver = mem.Expression.ToString();
        return (name == "Equals" && receiver is "object" or "Object") || name == "ReferenceEquals";
    }

    static bool IsNullLiteral(ExpressionSyntax expr) =>
        expr is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NullLiteralExpression);
}

sealed class NamespaceMatchFolderRule : IRule
{
    public string Id => "CS036";

    public bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("dotnet_style_namespace_match_folder");

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        if (!StyleHelper.TryGet(config, "dotnet_style_namespace_match_folder",
            out var setting, out var severity) || setting != "true")
            return [];

        var source = await File.ReadAllTextAsync(filePath);
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = await tree.GetRootAsync();
        var diagnostics = new List<Diagnostic>();
        var fileDir = Path.GetDirectoryName(filePath) ?? "";

        foreach (var ns in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
        {
            var nsName      = ns.Name.ToString();
            var lastSegment = nsName.Split('.').Last();
            var dirName     = Path.GetFileName(fileDir);

            if (!string.Equals(lastSegment, dirName, StringComparison.OrdinalIgnoreCase))
            {
                var loc = ns.Name.GetLocation().GetLineSpan();
                diagnostics.Add(StyleHelper.Make(filePath,
                    loc.StartLinePosition.Line + 1, loc.StartLinePosition.Character + 1, Id,
                    $"Namespace '{nsName}' does not match folder '{dirName}' (dotnet_style_namespace_match_folder = true).",
                    severity));
            }
        }

        return diagnostics;
    }
}
