using System.Text.Json;
using Pragmatic.Endpoints.Benchmarks.Documents;
using Pragmatic.Endpoints.Benchmarks.Generated;

namespace Pragmatic.Endpoints.Benchmarks.Serialization;

/// <summary>The writers the generator emitted for the workloads, through the endpoints that answer with them.</summary>
/// <remarks>
///     A workload the generator refused has none: <c>twitter.json</c> declares members typed <c>object</c>, which the
///     serializer writes as whatever they hold at run time, and PRAG0555 on <c>TwitterEndpoint</c> says so.
/// </remarks>
internal static class GeneratedWriters
{
    public static Action<Utf8JsonWriter, T>? For<T>()
    {
        Delegate? writer = typeof(T) switch
        {
            var t when t == typeof(ReservationPage) => GeneratedUtf8ResponseWriters.Write_Pragmatic_Endpoints_Benchmarks_Documents_ReservationPageDelegate,
            var t when t == typeof(CitmCatalog.Root) => GeneratedUtf8ResponseWriters.Write_Pragmatic_Endpoints_Benchmarks_Documents_CitmCatalog_RootDelegate,
            var t when t == typeof(Canada.Root) => GeneratedUtf8ResponseWriters.Write_Pragmatic_Endpoints_Benchmarks_Documents_Canada_RootDelegate,
            _ => null,
        };

        return (Action<Utf8JsonWriter, T>?)writer;
    }
}
