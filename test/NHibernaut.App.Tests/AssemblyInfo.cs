// Integration tests drive the static NHibernautRuntime.Store, so run the whole assembly
// sequentially to avoid cross-test contamination of that shared state.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
