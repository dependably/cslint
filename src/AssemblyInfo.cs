using System.Runtime.CompilerServices;

// Expose internals to the unit-test assembly so tests can call internal rules
// and engine methods directly without making them public.
[assembly: InternalsVisibleTo("CsLint.Tests")]
