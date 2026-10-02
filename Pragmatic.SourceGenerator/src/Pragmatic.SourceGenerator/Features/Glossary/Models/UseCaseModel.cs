using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Glossary.Models;

/// <summary>
///     One member carrying <c>[UseCase]</c> and/or <c>[Rule]</c>, as the catalog will record it.
/// </summary>
/// <remarks>
///     <see cref="SourceFile" /> is the path the compiler reports, which is absolute in an ordinary
///     build. It is made project-relative on the way into the template, where <c>ProjectDir</c> is
///     known — not here, because a transform sees one symbol and no build properties.
/// </remarks>
internal sealed record UseCaseModel
{
    /// <summary>The use-case identifier, or <c>null</c> when the member carries only rules.</summary>
    public string? Id { get; init; }

    /// <summary>The optional human-readable title.</summary>
    public string? Title { get; init; }

    /// <summary>The annotated member, fully qualified.</summary>
    public required string Target { get; init; }

    /// <summary>The declaring file as the compiler reports it.</summary>
    public required string SourceFile { get; init; }

    /// <summary>The 1-based line of the declaration.</summary>
    public required int Line { get; init; }

    /// <summary>The rules in the author's words, in source order.</summary>
    public required EquatableArray<string> Rules { get; init; }
}
