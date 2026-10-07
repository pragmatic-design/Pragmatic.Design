namespace Pragmatic.SourceGenerator.Features.Serialization.Models;

/// <summary>One property a generated UTF-8 JSON writer writes.</summary>
/// <param name="ClrName">The property, as the writer reads it.</param>
/// <param name="JsonName">The name it carries on the wire: the one the JSON shape gives it.</param>
/// <param name="Value">How its value is written.</param>
internal sealed record JsonWriterMemberModel(string ClrName, string JsonName, JsonWriterValueModel Value);
