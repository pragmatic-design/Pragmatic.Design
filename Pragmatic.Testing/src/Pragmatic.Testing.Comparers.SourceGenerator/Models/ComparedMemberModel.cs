namespace Pragmatic.Testing.Comparers.SourceGenerator.Models;

/// <summary>One member the generated comparer reads.</summary>
/// <param name="Name">The member name, as it appears in the failure message.</param>
/// <param name="IsSequence">
///     Whether the member is a sequence and must be compared element by element. Reference equality
///     on two lists holding the same values would report a difference that is not there.
/// </param>
internal sealed record ComparedMemberModel(string Name, bool IsSequence);
