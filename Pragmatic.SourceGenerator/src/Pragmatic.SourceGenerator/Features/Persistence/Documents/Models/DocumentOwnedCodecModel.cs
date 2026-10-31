using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

/// <summary>
///     The codec of an owned type on the documents engine (<see cref="Templates.DocumentOwnedCodecTemplate" />): a static class in the application's
///     assembly that writes and reads the type as a nested document, since the type is not this engine's partial.
/// </summary>
/// <param name="Hint">The generated file's hint name.</param>
/// <param name="CodecName">The codec's class name, in <c>Pragmatic.Persistence.Documents.Generated</c>.</param>
/// <param name="FullName">The owned type, fully qualified.</param>
/// <param name="Fields">Its members, in declaration order (the base classes' first).</param>
/// <param name="Constructor">
///     The members a public constructor takes, in its order, for a type without a parameterless one (a value object's); empty when the object is
///     built with an initializer.
/// </param>
internal sealed record DocumentOwnedCodecModel(
    string Hint,
    string CodecName,
    string FullName,
    EquatableArray<OwnedCodecFieldModel> Fields,
    EquatableArray<string> Constructor);
