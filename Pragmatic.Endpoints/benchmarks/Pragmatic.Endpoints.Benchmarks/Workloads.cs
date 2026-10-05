using Pragmatic.Endpoints.Benchmarks.Documents;

namespace Pragmatic.Endpoints.Benchmarks;

/// <summary>The graph each workload serializes, by its root type.</summary>
internal static class Workloads
{
    public static T Create<T>()
    {
        object root = typeof(T) switch
        {
            var t when t == typeof(Twitter.Root) => SimdJsonDocuments.Load<Twitter.Root>("twitter.json"),
            var t when t == typeof(CitmCatalog.Root) => SimdJsonDocuments.Load<CitmCatalog.Root>("citm_catalog.json"),
            var t when t == typeof(Canada.Root) => SimdJsonDocuments.Load<Canada.Root>("canada.json"),
            var t when t == typeof(ReservationPage) => ReservationPage.Sample(),
            _ => throw new ArgumentOutOfRangeException(nameof(T), typeof(T), "Not a workload."),
        };

        return (T)root;
    }
}
