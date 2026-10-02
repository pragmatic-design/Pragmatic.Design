// =============================================================================
// JSON Serialization — round-trip via ResultJsonConverterFactory
// =============================================================================

using System.Text.Json;
using Pragmatic.Result.Http;
using Pragmatic.Result.Serialization;

namespace Pragmatic.Result.Samples.Samples;

/// <summary>
///     Serializes and deserializes a Result with the real converter factory. The factory materializes
///     the right converter per closed Result shape; concrete errors round-trip polymorphically through
///     the <c>$errorType</c> discriminator (built-in Http errors are registered out of the box).
/// </summary>
public static class JsonSerializationSample
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();

        // The typed converter for the shape this sample serializes. There is no factory that picks one
        // at run time: it could not work under Native AOT, and for a multi-error result the generator
        // emits the converter from an [assembly: JsonResultContract<…>] declaration.
        options.Converters.Add(new ResultJsonConverter<Order, NotFoundError>());
        return options;
    }

    public static void Run()
    {
        Console.WriteLine("12. JSON Serialization (round-trip)");
        Console.WriteLine("-----------------------------------");

        // ── Success round-trip ─────────────────────────────────────────────
        Result<Order, NotFoundError> success = new Order("ORD-1", 42.50m);

        var successJson = JsonSerializer.Serialize(success, Options);
        Console.WriteLine($"  Success JSON: {successJson}");

        var successBack = JsonSerializer.Deserialize<Result<Order, NotFoundError>>(successJson, Options);
        Console.WriteLine(
            $"  Deserialized: IsSuccess={successBack.IsSuccess}, Order={successBack.Value.Id} (€{successBack.Value.Total})");

        // ── Failure round-trip (polymorphic error via $errorType) ──────────
        Result<Order, NotFoundError> failure = NotFoundError.Create("Order", "ORD-999");

        var failureJson = JsonSerializer.Serialize(failure, Options);
        Console.WriteLine($"  Failure JSON: {failureJson}");

        var failureBack = JsonSerializer.Deserialize<Result<Order, NotFoundError>>(failureJson, Options);
        Console.WriteLine(
            $"  Deserialized: IsFailure={failureBack.IsFailure}, Code={failureBack.Error.Code}, " +
            $"Entity={failureBack.Error.EntityType} {failureBack.Error.EntityId}");

        Console.WriteLine();
    }

    public record Order(string Id, decimal Total);
}
