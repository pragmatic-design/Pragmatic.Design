using System.Text.Json;
using Pragmatic.Endpoints.Benchmarks.Documents;
using Pragmatic.Endpoints.Benchmarks.Generated;

namespace Pragmatic.Endpoints.Benchmarks.Serialization;

/// <summary>The writers the generator emitted for the workloads, through the endpoints that answer with them.</summary>
/// <remarks>
///     A workload the generator refused would have none, and PRAG0555 on its endpoint would say why. Every workload
///     has one since #130: <c>twitter.json</c>'s members typed <c>object</c> are written by the serializer, under the
///     options the writer is given, and the rest by the writer.
/// </remarks>
internal static class GeneratedWriters
{
    public static Action<Utf8JsonWriter, T, JsonSerializerOptions>? For<T>()
    {
        Delegate? writer = typeof(T) switch
        {
            var t when t == typeof(ReservationPage) => GeneratedUtf8ResponseWriters.Write_Pragmatic_Endpoints_Benchmarks_Documents_ReservationPageDelegate,
            var t when t == typeof(CitmCatalog.Root) => GeneratedUtf8ResponseWriters.Write_Pragmatic_Endpoints_Benchmarks_Documents_CitmCatalog_RootDelegate,
            var t when t == typeof(Canada.Root) => GeneratedUtf8ResponseWriters.Write_Pragmatic_Endpoints_Benchmarks_Documents_Canada_RootDelegate,
            var t when t == typeof(Twitter.Root) => GeneratedUtf8ResponseWriters.Write_Pragmatic_Endpoints_Benchmarks_Documents_Twitter_RootDelegate,
            _ => null,
        };

        return (Action<Utf8JsonWriter, T, JsonSerializerOptions>?)writer;
    }
}
