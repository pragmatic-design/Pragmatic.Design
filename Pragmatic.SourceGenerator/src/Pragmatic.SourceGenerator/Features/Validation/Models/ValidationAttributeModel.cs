using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Validation.Models;

/// <summary>
///     Model representing a validation attribute on a property.
/// </summary>
internal sealed record ValidationAttributeModel
{
    public required string AttributeType { get; init; }
    public required string AttributeName { get; init; }
    public string? MessageKey { get; init; }

    /// <summary>
    ///     The <c>MessageKey</c> argument as written, when it names a constant that does not exist while
    ///     the transform runs — a <c>TKeys</c> constant, which this generator writes itself. Resolved to
    ///     <see cref="MessageKey" /> through the constants' catalog before the validator is rendered.
    /// </summary>
    public string? MessageKeyReference { get; init; }

    /// <summary>Where <see cref="MessageKeyReference" /> is written, for PRAG0222.</summary>
    public LocationInfo? MessageKeyLocation { get; init; }

    public bool RequiresInstance { get; init; }
    public ValidationKind Kind { get; init; }
    public string? Value { get; init; }
    public string? Value2 { get; init; }
    public string? OtherProperty { get; init; }
    public string? ComparisonValue { get; init; }
    public bool AllowEmptyStrings { get; init; }
    public string? Pattern { get; init; }
    public EquatableArray<string> AllowedSchemes { get; init; } = EquatableArray<string>.Empty;

    /// <summary><c>[Url(RequireAbsolute = …)]</c>; true unless the attribute says otherwise, as on the attribute.</summary>
    public bool RequireAbsolute { get; init; } = true;

    public EquatableArray<string> AllowedValues { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Rendered constructor argument literals for a custom (Unknown-kind) attribute, so the
    ///     generated code can re-instantiate it (<c>new FooAttribute(arg0, arg1)</c>).
    /// </summary>
    public EquatableArray<string> CtorArgs { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Severity level: 0=Error (default), 1=Warning, 2=Info.</summary>
    public int Severity { get; init; }

    /// <summary>Validation groups this rule belongs to. Empty means always runs.</summary>
    public EquatableArray<string> Groups { get; init; } = EquatableArray<string>.Empty;
}
