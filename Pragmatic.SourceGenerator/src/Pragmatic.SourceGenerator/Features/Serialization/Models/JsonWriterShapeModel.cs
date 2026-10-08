using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Serialization.Models;

/// <summary>
///     What a response writer writes, as the host's converters see it: emitted as a <c>GeneratedJsonShape</c> beside
///     the writer, and asked of the live options before the writer is used.
/// </summary>
/// <param name="Types">Every type the writer writes itself, fully qualified.</param>
/// <param name="Enums">Every enum it writes by name, with the generated converter that may claim it instead.</param>
/// <param name="NeedsInfrastructureExclusion">Whether it leaves out what the host strips only with persistence.</param>
internal sealed record JsonWriterShapeModel(
    EquatableArray<string> Types,
    EquatableArray<JsonEnumConverterModel> Enums,
    bool NeedsInfrastructureExclusion);
