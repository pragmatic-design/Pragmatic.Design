using System.Text.Json;
using Pragmatic.Endpoints.Benchmarks.Documents;
using Pragmatic.Serialization;

namespace Pragmatic.Endpoints.Benchmarks.Probe;

/// <summary>
///     <c>canada.json</c>, almost all of it coordinates, written three ways: through the writer number by number,
///     as one run per geometry, and with every number replaced by a constant to say what formatting them costs.
/// </summary>
internal static class CanadaProbeWriter
{
    private static readonly JsonEncodedText Type = GeneratedJsonDefaults.Encode("type");
    private static readonly JsonEncodedText Features = GeneratedJsonDefaults.Encode("features");
    private static readonly JsonEncodedText Properties = GeneratedJsonDefaults.Encode("properties");
    private static readonly JsonEncodedText Geometry = GeneratedJsonDefaults.Encode("geometry");
    private static readonly JsonEncodedText Name = GeneratedJsonDefaults.Encode("name");
    private static readonly JsonEncodedText Coordinates = GeneratedJsonDefaults.Encode("coordinates");
    private static readonly byte[] CoordinatesRaw = Utf8JsonRun.Name(Coordinates);

    public static void Plain(Utf8JsonWriter writer, Canada.Root value) => Root(writer, value, CoordinatesPlain);

    public static void Run(Utf8JsonWriter writer, Canada.Root value) => Root(writer, value, CoordinatesRun);

    public static void ConstantNumbers(Utf8JsonWriter writer, Canada.Root value) => Root(writer, value, CoordinatesConstant);

    private static void Root(Utf8JsonWriter writer, Canada.Root value, Action<Utf8JsonWriter, double[][][]> coordinates)
    {
        writer.WriteStartObject();
        if (value.type is { } type)
            writer.WriteString(Type, type);
        if (value.features is { } features)
        {
            writer.WritePropertyName(Features);
            writer.WriteStartArray();
            foreach (var feature in features)
            {
                writer.WriteStartObject();
                if (feature.type is { } featureType)
                    writer.WriteString(Type, featureType);
                if (feature.properties is { } properties)
                {
                    writer.WritePropertyName(Properties);
                    writer.WriteStartObject();
                    if (properties.name is { } name)
                        writer.WriteString(Name, name);
                    writer.WriteEndObject();
                }

                if (feature.geometry is { } geometry)
                {
                    writer.WritePropertyName(Geometry);
                    writer.WriteStartObject();
                    if (geometry.type is { } geometryType)
                        writer.WriteString(Type, geometryType);
                    if (geometry.coordinates is { } rings)
                        coordinates(writer, rings);
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void CoordinatesPlain(Utf8JsonWriter writer, double[][][] rings)
    {
        writer.WritePropertyName(Coordinates);
        writer.WriteStartArray();
        foreach (var ring in rings)
        {
            writer.WriteStartArray();
            foreach (var point in ring)
            {
                writer.WriteStartArray();
                foreach (var number in point)
                    writer.WriteNumberValue(number);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();
    }

    private static void CoordinatesRun(Utf8JsonWriter writer, double[][][] rings)
    {
        var run = Utf8JsonRun.Start();
        run.PropertyName(CoordinatesRaw);
        run.StartArray();
        foreach (var ring in rings)
        {
            run.StartArray();
            foreach (var point in ring)
            {
                run.StartArray();
                foreach (var number in point)
                    run.NumberValue(number);
                run.EndArray();
            }

            run.EndArray();
        }

        run.EndArray();
        writer.WriteRawValue(run.Written, skipInputValidation: true);
        run.Dispose();
    }

    private static void CoordinatesConstant(Utf8JsonWriter writer, double[][][] rings)
    {
        writer.WritePropertyName(Coordinates);
        writer.WriteStartArray();
        foreach (var ring in rings)
        {
            writer.WriteStartArray();
            foreach (var point in ring)
            {
                writer.WriteStartArray();
                foreach (var _ in point)
                    writer.WriteNumberValue(0);
                writer.WriteEndArray();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndArray();
    }
}
