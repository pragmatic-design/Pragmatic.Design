using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Result.Models;

/// <summary>
///     A C# 15 union that implements <c>IError</c>: an error set whose <c>IError</c> members the generator writes, each delegated to the
///     active case (#865).
/// </summary>
/// <remarks>
///     Read structurally, with the Roslyn 4.14 API the generator compiles against (docs/design/csharp15-union-errors.md, option A): a struct
///     that implements <c>System.Runtime.CompilerServices.IUnion</c>, whose cases are the parameter types of its one-parameter constructors.
/// </remarks>
/// <param name="Namespace">The union's namespace; empty in the global one.</param>
/// <param name="TypeName">The union's name.</param>
/// <param name="FullName">The union's fully qualified name: what tells two parts of one union apart from two unions.</param>
/// <param name="Accessibility">The union's declared accessibility, lower case.</param>
/// <param name="IsUnionDeclaration">Declared with <c>union</c>; false for a struct that implements <c>IUnion</c> by hand.</param>
/// <param name="IsPartial">Declared <c>partial</c>: the generator can write a further part.</param>
/// <param name="IsNested">Declared inside another type, which the further part would have to repeat: nothing is written for it.</param>
/// <param name="NonErrorCases">The cases that do not implement <c>IError</c>, as the user would write them.</param>
/// <param name="Missing">The <c>IError</c> members the user did not write, which the generator writes.</param>
/// <param name="Location">Where the union is declared, for a diagnostic.</param>
internal sealed record UnionErrorModel(
    string Namespace,
    string TypeName,
    string FullName,
    string Accessibility,
    bool IsUnionDeclaration,
    bool IsPartial,
    bool IsNested,
    EquatableArray<string> NonErrorCases,
    UnionErrorMembers Missing,
    LocationInfo? Location)
{
    /// <summary>The members left to write include one without a default on <c>IError</c>, so the union does not compile without them.</summary>
    public bool LacksRequiredMembers => (Missing & (UnionErrorMembers.Code | UnionErrorMembers.StatusCode)) != 0;

    /// <summary>Whether the generator writes the union's <c>IError</c> members.</summary>
    public bool IsEmitted => IsPartial && !IsNested && NonErrorCases.Length == 0 && Missing != UnionErrorMembers.None;
}
