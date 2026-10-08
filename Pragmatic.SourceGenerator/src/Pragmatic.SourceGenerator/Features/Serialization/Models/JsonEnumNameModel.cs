namespace Pragmatic.SourceGenerator.Features.Serialization.Models;

/// <summary>A declared enum member and the name it carries on the wire.</summary>
/// <param name="Member">The member, as C# names it.</param>
/// <param name="WireName">What <c>JsonStringEnumConverter</c> writes for it.</param>
internal sealed record JsonEnumNameModel(string Member, string WireName);
