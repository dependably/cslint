using CsLint;

// Thin entry point: all CLI logic lives in CsLint.Cli (a named namespace, hence testable).
// Top-level catch is the last line of defence: any unexpected failure (e.g. a Roslyn/MSBuild
// fault under --deep) is reported as a single stderr line and mapped to exit 2 (operational
// error), never an uncontrolled crash code.
// Exit code for an operational error (bad config, no git repo, or an unexpected fault here).
const int ExitOperationalError = 2;

try
{
    return await Cli.RunAsync(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"cslint: internal error: {ex.Message}");
    return ExitOperationalError;
}
