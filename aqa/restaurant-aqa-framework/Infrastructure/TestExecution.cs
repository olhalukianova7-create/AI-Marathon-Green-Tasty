using Xunit;

// Functional scenarios share a small deployed backend. They verify behavior,
// not concurrent load; each scenario still owns and cleans up its own data.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
