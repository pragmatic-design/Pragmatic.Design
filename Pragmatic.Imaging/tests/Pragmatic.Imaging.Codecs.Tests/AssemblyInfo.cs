using Xunit;

// The native buffer pool is process-wide and its tests read its totals: test classes run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
