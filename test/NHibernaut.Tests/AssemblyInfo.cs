using Xunit;

// Integration tests drive the static NHibernautRuntime.Store/Options, so run the whole assembly
// sequentially to avoid cross-test contamination of that shared state.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
