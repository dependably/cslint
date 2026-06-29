using CsLint;

// Thin entry point: all CLI logic lives in CsLint.Cli (a named namespace, hence testable).
return await Cli.RunAsync(args);
