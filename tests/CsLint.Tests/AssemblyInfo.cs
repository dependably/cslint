// Run xUnit tests serially within this assembly. Some tests redirect Console.Out or
// write to shared temp paths, so parallelism would produce non-deterministic results.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
