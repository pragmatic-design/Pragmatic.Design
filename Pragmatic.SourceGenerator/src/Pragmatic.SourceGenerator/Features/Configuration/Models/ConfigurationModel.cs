using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Configuration.Models;

/// <summary>
/// Immutable model representing a [Configuration]-annotated options class.
/// </summary>
internal sealed record ConfigurationModel : GeneratorModel
{
    /// <summary>Configuration section path (e.g. "Booking" or "Services:OrderApi").</summary>
    public required string SectionPath { get; init; }

    /// <summary>Whether to generate ValidateOnStart registration.</summary>
    public bool ValidateOnStart { get; init; } = true;

    /// <summary>Whether the type is declared as partial.</summary>
    public bool IsPartial { get; init; }

    /// <summary>Whether the type is static or abstract.</summary>
    public bool IsStaticOrAbstract { get; init; }

    /// <summary>Properties that have DataAnnotation validation attributes.</summary>
    public EquatableArray<ConfigurationPropertyModel> Properties { get; init; } = EquatableArray<ConfigurationPropertyModel>.Empty;

    /// <summary>Whether any property has validation attributes.</summary>
    public bool HasValidation => !Properties.IsDefaultOrEmpty && Properties.Any(p => p.HasValidationAttributes);

    /// <summary>Whether any property is marked <c>[Sensitive]</c>.</summary>
    public bool HasSensitive => !Properties.IsDefaultOrEmpty && Properties.Any(p => p.IsSensitive);

    /// <summary>Cross-property validation invariants declared via <c>[ConfigInvariant]</c> methods.</summary>
    public EquatableArray<InvariantModel> Invariants { get; init; } = EquatableArray<InvariantModel>.Empty;

    /// <summary>Whether any <c>[ConfigInvariant]</c> method exists.</summary>
    public bool HasInvariants => !Invariants.IsDefaultOrEmpty && Invariants.Count > 0;

    /// <summary><c>[ConfigInvariant]</c> methods the validator cannot call — reported as PRAG2002.</summary>
    public EquatableArray<MisshapenInvariantModel> MisshapenInvariants { get; init; } = EquatableArray<MisshapenInvariantModel>.Empty;
}

/// <summary>A <c>[ConfigInvariant]</c> validation method on a <c>[Configuration]</c> options class.</summary>
internal sealed record InvariantModel
{
    public required string MethodName { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// A property on a [Configuration] options class.
/// </summary>
internal sealed record ConfigurationPropertyModel
{
    public required string Name { get; init; }
    public required string TypeFullName { get; init; }
    public bool IsRequired { get; init; }
    public bool HasValidationAttributes { get; init; }

    /// <summary>Whether the property is marked <c>[Sensitive]</c> (value masked in audit trails).</summary>
    public bool IsSensitive { get; init; }

    /// <summary>Whether the property declares a default value (an initializer, e.g. <c>= ""</c>).</summary>
    public bool HasDefaultValue { get; init; }

    /// <summary>Validation attribute descriptors (e.g. Range, MaxLength).</summary>
    public EquatableArray<ValidationAttributeModel> ValidationAttributes { get; init; } = EquatableArray<ValidationAttributeModel>.Empty;
}

/// <summary>
/// Describes a DataAnnotation validation attribute on a property.
/// </summary>
internal sealed record ValidationAttributeModel
{
    public required string AttributeName { get; init; }

    /// <summary>Constructor arguments as source-representable strings.</summary>
    public EquatableArray<string> ConstructorArgs { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Named arguments as key=value pairs.</summary>
    public EquatableArray<(string Key, string Value)> NamedArgs { get; init; } = EquatableArray<(string, string)>.Empty;
}
