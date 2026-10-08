namespace Pragmatic.SourceGenerator.Features.Serialization.Models;

/// <summary>Whose output a generated writer reproduces, which decides the details the type alone does not.</summary>
internal enum JsonWriterProfile
{
    /// <summary>
    ///     The declared redactor's: every member written, a null as <c>null</c>, dates written as text so the
    ///     default encoder escapes them, as the redactor's re-serialized copy does.
    /// </summary>
    Log,

    /// <summary>
    ///     The host's response options: a member's name pre-encoded, a null member left out unless it says
    ///     otherwise, enums by name, dates through the writer's own overloads, as the serializer writes them.
    /// </summary>
    Response,
}
