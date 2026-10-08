namespace Pragmatic.SourceGenerator.Features.Serialization.Models;

/// <summary>One property a generated UTF-8 JSON writer writes.</summary>
/// <param name="ClrName">The property, as the writer reads it.</param>
/// <param name="JsonName">The name it carries on the wire: the one the JSON shape gives it.</param>
/// <param name="Value">How its value is written.</param>
internal sealed record JsonWriterMemberModel(string ClrName, string JsonName, JsonWriterValueModel Value)
{
    /// <summary>When it is left out of the object. The log profile writes every member.</summary>
    public JsonWriterSkip Skip { get; init; } = JsonWriterSkip.Never;

    /// <summary>The member's type, fully qualified: what a <see cref="JsonWriterSkip.WhenDefault" /> compares against.</summary>
    public string TypeExpr { get; init; } = "";
}
