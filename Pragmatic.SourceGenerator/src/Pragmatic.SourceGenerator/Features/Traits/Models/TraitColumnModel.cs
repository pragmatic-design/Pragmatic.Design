namespace Pragmatic.SourceGenerator.Features.Traits.Models;

/// <summary>
///     One stored member of a trait's child, as its base class declares it (<c>CommentBase&lt;TId&gt;</c>, <c>NoteBase&lt;TId&gt;</c>): what the
///     EF Core configuration bounds and what the child an engine persists declares itself (<see cref="Transforms.TraitColumns" />).
/// </summary>
/// <param name="Name">The property.</param>
/// <param name="Type">Its type, fully qualified, with the nullable annotation of a reference type.</param>
/// <param name="Initializer">The base class's initializer (<c>""</c>), null when it has none.</param>
/// <param name="MaxLength">The column's length (a string), null when unbounded or not a string.</param>
/// <param name="Required">True when the EF Core configuration marks the column required.</param>
internal sealed record TraitColumnModel(string Name, string Type, string? Initializer, int? MaxLength, bool Required);
