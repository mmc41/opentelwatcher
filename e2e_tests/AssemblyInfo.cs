using Xunit;

// Disable test parallelization for E2E tests to ensure reliability
// Tests interact with actual server processes and ports, so must run sequentially
[assembly: CollectionBehavior(DisableTestParallelization = true)]
