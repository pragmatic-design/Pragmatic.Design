using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.ValueObject.Models;

/// <summary>
///     Immutable model describing a [ValueObject] record for Create/CreateUnsafe generation.
/// </summary>
internal sealed record ValueObjectModel
{
    /// <summary>Containing namespace, or null for the global namespace.</summary>
    public string? Namespace { get; init; }

    /// <summary>The value object type name (without namespace).</summary>
    public required string TypeName { get; init; }

    /// <summary>Accessibility keyword for the generated partial (e.g. "public").</summary>
    public required string Accessibility { get; init; }

    /// <summary>Type kind keyword: "record" or "record struct".</summary>
    public required string TypeKindKeyword { get; init; }

    /// <summary>Whether the type is declared <c>partial</c> (required to augment it).</summary>
    public bool IsPartial { get; init; }

    /// <summary>Whether a static <c>Validate</c> method was found.</summary>
    public bool HasValidate { get; init; }

    /// <summary>Fully-qualified return type of the <c>Validate</c> method (mirrored by Create).</summary>
    public string? ValidateReturnType { get; init; }

    /// <summary>Parameters of the <c>Validate</c> method (mirrored by Create).</summary>
    public EquatableArray<ValueObjectParameter> ValidateParameters { get; init; } = EquatableArray<ValueObjectParameter>.Empty;

    /// <summary>Whether a usable constructor was found for CreateUnsafe.</summary>
    public bool HasConstructor { get; init; }

    /// <summary>Parameters of the chosen constructor (used by CreateUnsafe).</summary>
    public EquatableArray<ValueObjectParameter> ConstructorParameters { get; init; } = EquatableArray<ValueObjectParameter>.Empty;

    /// <summary>True when the user already declares a Create method (skip generation).</summary>
    public bool UserDefinedCreate { get; init; }

    /// <summary>True when the user already declares a CreateUnsafe method (skip generation).</summary>
    public bool UserDefinedCreateUnsafe { get; init; }
}
