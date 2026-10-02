using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Serialization;

// AOT smoke test: build the shared seam with a source-generated context and the reflection fallback
// DISABLED, then round-trip a payload. If any part fell back to reflection this would fail to publish
// (trim/AOT warnings) or throw NotSupportedException at runtime. Success prints the marker below.

// First, the thing every other smoke masks by calling DisableReflectionFallback() itself: under Native
// AOT the fallback must already be OFF, because PragmaticJsonOptions derives its default from
// RuntimeFeature.IsDynamicCodeSupported. If that ever regresses, an AOT app silently gets a reflective
// resolver — the exact failure this whole effort removed — and every other smoke would still pass.
if (new PragmaticJsonOptions().ReflectionFallbackEnabled)
{
    Console.Error.WriteLine("AOT-SMOKE-FAIL: the JSON reflection fallback defaulted ON under Native AOT");
    return 1;
}

var json = new PragmaticJsonOptions()
    .AddContext(SmokeJsonContext.Default)
    .DisableReflectionFallback()
    .Build();

var original = new OrderPlaced("A-100", 3, ["widget", "gadget"]);

var text = JsonSerializer.Serialize(original, SmokeJsonContext.Default.OrderPlaced);
var back = JsonSerializer.Deserialize(text, SmokeJsonContext.Default.OrderPlaced);

// Also exercise the seam-built options path: resolve the JsonTypeInfo from the seam's resolver
// chain (which includes the context, with the reflection fallback off) — the AOT-safe overload.
var viaSeam = JsonSerializer.Deserialize(text, json.GetTypeInfo(typeof(OrderPlaced))) as OrderPlaced;

if (back is null || viaSeam is null
    || back.OrderId != original.OrderId
    || back.Quantity != original.Quantity
    || viaSeam.OrderId != original.OrderId)
{
    Console.Error.WriteLine("AOT-SMOKE-FAIL: round-trip mismatch");
    return 1;
}

Console.WriteLine($"AOT-SMOKE-OK: fallback off by default; {text}");
return 0;

internal sealed record OrderPlaced(string OrderId, int Quantity, string[] Items);

[JsonSerializable(typeof(OrderPlaced))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class SmokeJsonContext : JsonSerializerContext;
