using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Endpoints.Benchmarks.Documents;
using Pragmatic.Endpoints.Benchmarks.Serialization;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>The strings of <c>twitter.json</c>, in document order, read from the graph the workload serializes.</summary>
internal static class TwitterStrings
{
    public static string[] Read()
    {
        var root = Workloads.Create<Twitter.Root>();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(root,
            (JsonTypeInfo<Twitter.Root>)Competitors.HostReflection.GetTypeInfo(typeof(Twitter.Root)));
        var strings = new List<string>();
        using var document = JsonDocument.Parse(bytes);
        Collect(document.RootElement, strings);
        return [.. strings];
    }

    private static void Collect(JsonElement element, List<string> strings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Collect(property.Value, strings);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, strings);
                break;
            case JsonValueKind.String:
                strings.Add(element.GetString()!);
                break;
        }
    }
}
