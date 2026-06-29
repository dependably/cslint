namespace CsLint.Rules;

sealed class IndentStyleRule : TextRule
{
    public override string Id => "EC001";

    public override bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("indent_style");

    protected override IReadOnlyList<Diagnostic> Analyze(
        string filePath, string text, FileConfig config)
    {
        var expected = config.Properties["indent_style"].ToLowerInvariant();
        var size = GetIndentSize(config.Properties);
        var diagnostics = new List<Diagnostic>();
        var lines = SplitLines(text);

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.Length == 0) continue;

            if (expected == "space" && line[0] == '\t')
                diagnostics.Add(Warn(filePath, i + 1, 1, Id, "Tab used for indentation; expected spaces."));
            else if (expected == "tab")
            {
                int spaces = CountLeadingSpaces(line);
                if (spaces >= size)
                    diagnostics.Add(Warn(filePath, i + 1, 1, Id,
                        $"Spaces used for indentation; expected tabs ({spaces} spaces)."));
            }
        }

        return diagnostics;
    }

    protected override string? ApplyFix(string text, FileConfig config)
    {
        var expected = config.Properties["indent_style"].ToLowerInvariant();
        var size = GetIndentSize(config.Properties);
        var indent = new string(' ', size);
        var lines = SplitLines(text);
        bool changed = false;

        for (int i = 0; i < lines.Count; i++)
        {
            if (expected == "space" && lines[i].StartsWith('\t'))
            {
                lines[i] = lines[i].Replace("\t", indent);
                changed = true;
            }
            else if (expected == "tab")
            {
                var leading = CountLeadingSpaces(lines[i]);
                var tabs = leading / size;
                if (tabs > 0) { lines[i] = new string('\t', tabs) + lines[i][leading..]; changed = true; }
            }
        }

        return changed ? string.Join("", lines) : null;
    }

    const int DefaultIndentSize = 4;

    static int GetIndentSize(IReadOnlyDictionary<string, string> props)
    {
        if (props.TryGetValue("indent_size", out var s) && int.TryParse(s, out var n)) return n;
        if (props.TryGetValue("tab_width", out var t) && int.TryParse(t, out var tw)) return tw;
        return DefaultIndentSize;
    }

    static int CountLeadingSpaces(string line)
    {
        int count = 0;
        foreach (char c in line) { if (c == ' ') count++; else break; }
        return count;
    }

    static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                int end = i + 1 < text.Length && text[i + 1] == '\n' ? i + 2 : i + 1;
                lines.Add(text[start..end]);
                start = end;
                if (end > i + 1) i++;
            }
            else if (text[i] == '\n')
            {
                lines.Add(text[start..(i + 1)]);
                start = i + 1;
            }
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }
}

sealed class TrailingWhitespaceRule : TextRule
{
    public override string Id => "EC002";

    public override bool AppliesTo(FileConfig config) =>
        config.Properties.TryGetValue("trim_trailing_whitespace", out var v) &&
        v.Equals("true", StringComparison.OrdinalIgnoreCase);

    protected override IReadOnlyList<Diagnostic> Analyze(
        string filePath, string text, FileConfig config)
    {
        var diagnostics = new List<Diagnostic>();
        int lineNum = 1, pos = 0;

        while (pos < text.Length)
        {
            int lineEnd = text.IndexOfAny(['\r', '\n'], pos);
            if (lineEnd < 0) lineEnd = text.Length;

            var lineContent = text[pos..lineEnd];
            if (lineContent.Length > 0 && char.IsWhiteSpace(lineContent[^1]))
                diagnostics.Add(Warn(filePath, lineNum, lineContent.TrimEnd().Length + 1, Id,
                    "Trailing whitespace detected."));

            lineNum++;
            pos = lineEnd;
            if (pos < text.Length && text[pos] == '\r') pos++;
            if (pos < text.Length && text[pos] == '\n') pos++;
        }

        return diagnostics;
    }

    protected override string? ApplyFix(string text, FileConfig config)
    {
        var lines = text.Split('\n');
        var fixed_ = string.Join('\n', lines.Select(l =>
            l.EndsWith('\r') ? l.TrimEnd('\r').TrimEnd() + "\r" : l.TrimEnd()));
        return fixed_ == text ? null : fixed_;
    }
}

sealed class FinalNewlineRule : TextRule
{
    public override string Id => "EC003";

    public override bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("insert_final_newline");

    protected override IReadOnlyList<Diagnostic> Analyze(
        string filePath, string text, FileConfig config)
    {
        var required = config.Properties["insert_final_newline"].Equals("true", StringComparison.OrdinalIgnoreCase);
        if (text.Length == 0) return [];

        var endsWithNewline = text[^1] == '\n';

        if (required && !endsWithNewline)
            return [Warn(filePath, CountLines(text), 0, Id, "File must end with a newline.")];

        if (!required && endsWithNewline)
            return [Warn(filePath, CountLines(text), 0, Id, "File must not end with a newline.")];

        return [];
    }

    protected override string? ApplyFix(string text, FileConfig config)
    {
        var required = config.Properties["insert_final_newline"].Equals("true", StringComparison.OrdinalIgnoreCase);
        if (text.Length == 0) return null;
        var endsWithNewline = text[^1] == '\n';
        if (required && !endsWithNewline) return text + "\n";
        if (!required && endsWithNewline) return text.TrimEnd('\n').TrimEnd('\r');
        return null;
    }

    static int CountLines(string text) => text.Count(c => c == '\n') + 1;
}

sealed class LineEndingRule : TextRule
{
    public override string Id => "EC004";

    public override bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("end_of_line");

    protected override IReadOnlyList<Diagnostic> Analyze(
        string filePath, string text, FileConfig config)
    {
        var eol = config.Properties["end_of_line"].ToLowerInvariant();
        var diagnostics = new List<Diagnostic>();
        int lineNum = 1;

        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\r' && c != '\n') continue;

            bool isCrlf = c == '\r' && i + 1 < text.Length && text[i + 1] == '\n';
            var message = c == '\r' ? CarriageReturnMessage(eol, isCrlf) : LineFeedMessage(eol);
            if (message != null)
                diagnostics.Add(Warn(filePath, lineNum, 0, Id, message));

            if (isCrlf) i++;
            lineNum++;
        }

        return diagnostics;
    }

    static string? CarriageReturnMessage(string eol, bool isCrlf) => eol switch
    {
        "lf" => isCrlf ? "CRLF line ending; expected LF." : "CR line ending; expected LF.",
        "crlf" => isCrlf ? null : "CR line ending; expected CRLF.",
        _ => null
    };

    static string? LineFeedMessage(string eol) => eol switch
    {
        "crlf" => "LF line ending; expected CRLF.",
        "cr" => "LF line ending; expected CR.",
        _ => null
    };

    protected override string? ApplyFix(string text, FileConfig config)
    {
        var eol = config.Properties["end_of_line"].ToLowerInvariant();
        var normalised = text.Replace("\r\n", "\n").Replace("\r", "\n");
        return eol switch
        {
            "crlf" => normalised.Replace("\n", "\r\n"),
            "cr" => normalised.Replace("\n", "\r"),
            _ => normalised
        };
    }
}

sealed class LineLengthRule : TextRule
{
    public override string Id => "EC005";

    public override bool AppliesTo(FileConfig config) =>
        config.Properties.TryGetValue("max_line_length", out var v) &&
        v != "off" && int.TryParse(v, out _);

    protected override IReadOnlyList<Diagnostic> Analyze(
        string filePath, string text, FileConfig config)
    {
        var max = int.Parse(config.Properties["max_line_length"]);
        var diagnostics = new List<Diagnostic>();
        var lines = text.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length > max)
                diagnostics.Add(Warn(filePath, i + 1, max + 1, Id,
                    $"Line length {line.Length} exceeds maximum {max}."));
        }

        return diagnostics;
    }
}

sealed class CharsetRule : IRule
{
    // UTF byte-order-mark signatures, named so the BOM bytes and length read as constants.
    const byte Utf8Bom0 = 0xEF, Utf8Bom1 = 0xBB, Utf8Bom2 = 0xBF;
    const byte Utf16MarkFE = 0xFE, Utf16MarkFF = 0xFF;
    const int Utf8BomLength = 3;
    const int Utf16BomLength = 2;
    static readonly byte[] Utf8BomSignature = [Utf8Bom0, Utf8Bom1, Utf8Bom2];

    public string Id => "EC006";

    public bool AppliesTo(FileConfig config) =>
        config.Properties.ContainsKey("charset");

    public async Task<IReadOnlyList<Diagnostic>> AnalyzeAsync(string filePath, FileConfig config)
    {
        var expected = config.Properties["charset"].ToLowerInvariant();
        var bytes = await File.ReadAllBytesAsync(filePath);
        var (detected, hasBom) = DetectEncoding(bytes);

        if (expected is "utf-8" && hasBom)
            return [new(filePath, 1, 0, Id, "File has UTF-8 BOM; expected UTF-8 without BOM.", Severity.Warning)];

        if (expected is "utf-8-bom" && !hasBom)
            return [new(filePath, 1, 0, Id, "File missing UTF-8 BOM.", Severity.Warning)];

        if (expected is "utf-8" or "utf-8-bom" && !detected.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
            return [new(filePath, 1, 0, Id, $"File encoding is {detected}; expected {expected}.", Severity.Warning)];

        return [];
    }

    public async Task<bool> FixAsync(string filePath, FileConfig config)
    {
        var expected = config.Properties["charset"].ToLowerInvariant();
        var bytes = await File.ReadAllBytesAsync(filePath);
        var (_, hasBom) = DetectEncoding(bytes);
        byte[] bom = [Utf8Bom0, Utf8Bom1, Utf8Bom2];

        if (expected == "utf-8" && hasBom)
        {
            await File.WriteAllBytesAsync(filePath, bytes[Utf8BomLength..]);
            return true;
        }

        if (expected == "utf-8-bom" && !hasBom)
        {
            var withBom = new byte[Utf8BomLength + bytes.Length];
            bom.CopyTo(withBom, 0);
            bytes.CopyTo(withBom, Utf8BomLength);
            await File.WriteAllBytesAsync(filePath, withBom);
            return true;
        }

        return false;
    }

    static (string Encoding, bool HasBom) DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= Utf8BomLength && bytes.AsSpan(0, Utf8BomLength).SequenceEqual(Utf8BomSignature))
            return ("utf-8", true);
        if (bytes.Length >= Utf16BomLength && bytes[0] == Utf16MarkFE && bytes[1] == Utf16MarkFF)
            return ("utf-16be", true);
        if (bytes.Length >= Utf16BomLength && bytes[0] == Utf16MarkFF && bytes[1] == Utf16MarkFE)
            return ("utf-16le", true);
        return ("utf-8", false);
    }
}
