using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.FastEnum.Models;

/// <summary>
///     Model for an enum marked with [FastEnum].
/// </summary>
internal sealed record FastEnumModel
{
    /// <summary>Enum type name (e.g., "OrderStatus").</summary>
    public required string TypeName { get; init; }

    /// <summary>Fully qualified enum type name (e.g., "MyApp.OrderStatus").</summary>
    public required string FullTypeName { get; init; }

    /// <summary>Namespace.</summary>
    public required string Namespace { get; init; }

    /// <summary>Accessibility (e.g., "public").</summary>
    public required string Accessibility { get; init; }

    /// <summary>Underlying type (e.g., "int", "byte").</summary>
    public required string UnderlyingType { get; init; }

    /// <summary>Whether the enum has [Flags] attribute.</summary>
    public bool IsFlags { get; init; }

    /// <summary>All enum members.</summary>
    public EquatableArray<FastEnumMemberModel> Members { get; init; } = EquatableArray<FastEnumMemberModel>.Empty;

    /// <summary>Whether the compilation references Pragmatic.Internationalization (enables i18n key generation).</summary>
    public bool HasI18n { get; init; }

    /// <summary>i18n key prefix derived from namespace convention (e.g., "reservation.status"). Null if derivation failed.</summary>
    public string? I18nKeyPrefix { get; init; }

    public bool IsValid { get; init; }
}

/// <summary>
///     Model for a single enum member.
/// </summary>
internal sealed record FastEnumMemberModel
{
    /// <summary>Field name (e.g., "Pending").</summary>
    public required string Name { get; init; }

    /// <summary>Display name from [Display] or [Description] attribute, or null.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Constant value as string (e.g., "0", "1").</summary>
    public required string Value { get; init; }

    /// <summary>i18n key for this member (e.g., "reservation.status.draft"). Null if no i18n.</summary>
    public string? I18nKey { get; init; }
}
