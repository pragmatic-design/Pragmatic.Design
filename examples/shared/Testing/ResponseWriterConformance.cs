using System.Buffers;
using System.Reflection;
using System.Text;
using System.Text.Json;

// ReSharper disable once CheckNamespace
namespace Pragmatic.Examples.Testing;

/// <summary>
///     Every response writer the generator emitted in an application's modules, compared byte for byte with what
///     the serializer writes for the same value under the running host's options.
/// </summary>
/// <remarks>
///     <para>
///         The writers are found where the generator puts them, <c>{Assembly}.Generated.GeneratedUtf8ResponseWriters</c>,
///         one delegate field per response type, so a type added to an application is covered without being named
///         here. Each is written twice: with every member filled, and with every member at its default.
///     </para>
///     <para>
///         The options are the host's own, read from its container, so the resolver is whatever the application
///         registered: the generated JSON context where a module opted in, reflection otherwise.
///     </para>
/// </remarks>
internal static class ResponseWriterConformance
{
    private const string WritersClass = "GeneratedUtf8ResponseWriters";

    /// <summary>The response types a writer was generated for, across the assemblies.</summary>
    public static IReadOnlyList<Type> Covered(IEnumerable<Assembly> assemblies)
        => Writers(assemblies).Select(w => w.Type).ToList();

    /// <summary>
    ///     The response types whose writer the host admits: no converter it registered claims a type the writer
    ///     writes. The others are answered by the serializer, and are not this check's business.
    /// </summary>
    public static IReadOnlyList<Type> Used(IEnumerable<Assembly> assemblies, JsonSerializerOptions host)
        => Writers(assemblies).Where(w => Pragmatic.Serialization.GeneratedJsonDefaults.AllowGeneratedWriters(host, w.Shape))
            .Select(w => w.Type).ToList();

    /// <summary>Every writer the host admits whose bytes differ from the serializer's, with both, or none.</summary>
    public static IReadOnlyList<string> Mismatches(IEnumerable<Assembly> assemblies, JsonSerializerOptions host)
    {
        var mismatches = new List<string>();
        foreach (var (type, write, shape) in Writers(assemblies))
        {
            if (!Pragmatic.Serialization.GeneratedJsonDefaults.AllowGeneratedWriters(host, shape))
                continue;

            foreach (var (sample, value) in new[] { ("full", SampleValues.Full(type)), ("empty", SampleValues.Empty(type)) })
            {
                if (value is null)
                    continue;

                var serializer = Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(value, type, host));
                var writer = Write(write, value);
                if (writer != serializer)
                    mismatches.Add($"{type.FullName} ({sample}):\n  writer:     {writer}\n  serializer: {serializer}");
            }
        }

        return mismatches;
    }

    private static string Write(Delegate write, object value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, Pragmatic.Serialization.GeneratedJsonDefaults.ResponseWriterOptions))
            write.DynamicInvoke(writer, value);

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Each writer with the shape emitted beside it (<c>{Method}Delegate</c>, <c>{Method}Shape</c>).</summary>
    private static IEnumerable<(Type Type, Delegate Write, Pragmatic.Serialization.GeneratedJsonShape Shape)> Writers(
        IEnumerable<Assembly> assemblies)
    {
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        foreach (var assembly in assemblies.Distinct())
        {
            var writers = assembly.GetType($"{assembly.GetName().Name}.Generated.{WritersClass}");
            if (writers is null)
                continue;

            foreach (var field in writers.GetFields(statics).Where(f => f.Name.EndsWith("Delegate", StringComparison.Ordinal)))
            {
                var method = field.Name[..^"Delegate".Length];
                var shape = (Pragmatic.Serialization.GeneratedJsonShape)writers.GetField(method + "Shape", statics)!.GetValue(null)!;
                yield return (field.FieldType.GetGenericArguments()[1], (Delegate)field.GetValue(null)!, shape);
            }
        }
    }
}
