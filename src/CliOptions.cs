namespace CsLint;

// CLI options parsed from command-line arguments.
// Defined here (not in Program.cs) so the Cli helper class can reference the
// type without depending on the compiler-generated entry-point class.
internal sealed class CliOptions
{
    public bool ShowHelp      { get; set; }
    public bool Global        { get; set; }
    public bool Unstaged      { get; set; }
    public bool Fix           { get; set; }
    public bool Strict        { get; set; }
    public bool Verbose       { get; set; }
    public bool InstallHook   { get; set; }
    public bool SastMode      { get; set; }
    public bool ScanMode      { get; set; }
    public bool DeepMode      { get; set; }
    public string? ExplainFile { get; set; }
    public string? ProjectPath { get; set; }
    public OutputFormat Format { get; set; } = OutputFormat.Text;
    public string Root         { get; set; } = Directory.GetCurrentDirectory();
    public string? ConfigPath  { get; set; }
    public List<string> Files  { get; set; } = [];
    public List<string> Exclude { get; set; } = [];

    public bool FlagMagicNumbers      { get; set; } = true;
    public bool FlagBoolFlags         { get; set; } = true;
    public bool FlagCancellationToken { get; set; } = true;
}
