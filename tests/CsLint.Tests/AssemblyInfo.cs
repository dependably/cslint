// Several tests redirect the process-global Console.Out to assert on Reporter / CLI output.
// Running test classes in parallel races on that shared stream, so serialize the suite.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
