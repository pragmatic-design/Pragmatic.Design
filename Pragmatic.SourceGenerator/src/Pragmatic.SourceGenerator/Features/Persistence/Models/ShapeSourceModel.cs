namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>One file of an entity's shape, as the generator emits it: its hint name and its text.</summary>
/// <remarks>Handed to the Sql engine's translation, which binds its query chains against these members.</remarks>
internal sealed record ShapeSourceModel(string HintName, string Text);
