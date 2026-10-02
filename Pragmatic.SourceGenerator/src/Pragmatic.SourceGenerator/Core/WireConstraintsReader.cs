using System.Globalization;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     Reads a property's validation attributes into the <see cref="WireConstraints" /> the published
///     contract carries.
/// </summary>
/// <remarks>
///     <para>
///         One reader, asked wherever a property is described to a client: the request body of an
///         operation, and every DTO the manifest discovers. When only <c>maxLength</c> was read, and
///         only for request bodies, the document described a server more permissive than the one
///         answering — the validator refused what the contract allowed, and a client generated from
///         the contract learned each rule at its first 422.
///     </para>
///     <para>
///         Both attribute families are read, because both are used: <c>Pragmatic.Validation</c>
///         generates the validator from its own, and the data-annotation ones arrive with types
///         written against the BCL. Matched by simple name within those two namespaces, never by
///         display string.
///     </para>
///     <para>
///         What is not here is what JSON Schema cannot spell: a phone number, a credit card, a date in
///         the future, a comparison with another property. Those rules the server keeps to itself.
///     </para>
/// </remarks>
internal static class WireConstraintsReader
{
    private const string PragmaticNamespace = "Pragmatic.Validation.Attributes";
    private const string DataAnnotationsNamespace = "System.ComponentModel.DataAnnotations";

    public static WireConstraints Read(IPropertySymbol property)
    {
        var isString = property.Type.SpecialType == SpecialType.System_String;
        var isCollection = CollectionTypeHelper.IsCollection(property.Type);
        var constraints = WireConstraints.Empty;

        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass is not { } declaration) continue;
            var ns = declaration.ContainingNamespace?.ToDisplayString();
            if (ns != PragmaticNamespace && ns != DataAnnotationsNamespace) continue;

            constraints = declaration.Name switch
            {
                "MinLengthAttribute" => Length(constraints, isCollection, min: IntArg(attribute, 0), max: null),
                "MaxLengthAttribute" => Length(constraints, isCollection, min: null, max: IntArg(attribute, 0)),
                "LengthAttribute" => Length(constraints, isCollection, min: IntArg(attribute, 0), max: IntArg(attribute, 1)),
                "StringLengthAttribute" => constraints with
                {
                    MaxLength = IntArg(attribute, 0),
                    MinLength = IntNamed(attribute, "MinimumLength") ?? constraints.MinLength
                },
                "MinCountAttribute" => constraints with { MinItems = IntArg(attribute, 0) },
                "MaxCountAttribute" => constraints with { MaxItems = IntArg(attribute, 0) },
                "CountAttribute" => constraints with { MinItems = IntArg(attribute, 0), MaxItems = IntArg(attribute, 1) },
                "NotEmptyAttribute" => isCollection
                    ? constraints with { MinItems = 1 }
                    : isString ? constraints with { MinLength = 1 } : constraints,
                "RangeAttribute" => constraints with { Minimum = NumberArg(attribute, 0), Maximum = NumberArg(attribute, 1) },
                "PositiveAttribute" => constraints with { ExclusiveMinimum = 0 },
                "NegativeAttribute" => constraints with { ExclusiveMaximum = 0 },
                "GreaterThanAttribute" => constraints with { ExclusiveMinimum = NumberArg(attribute, 0) },
                "GreaterThanOrEqualAttribute" => constraints with { Minimum = NumberArg(attribute, 0) },
                "LessThanAttribute" => constraints with { ExclusiveMaximum = NumberArg(attribute, 0) },
                "LessThanOrEqualAttribute" => constraints with { Maximum = NumberArg(attribute, 0) },
                "RegexAttribute" or "RegularExpressionAttribute" => constraints with { Pattern = StringArg(attribute, 0) },
                // A format only where the type does not already say it: a Guid property renders as
                // uuid from its type, and a second "format" on the same object is a duplicate key.
                "EmailAttribute" or "EmailAddressAttribute" when isString => constraints with { Format = "email" },
                "UrlAttribute" when isString => constraints with { Format = "uri" },
                "GuidAttribute" when isString => constraints with { Format = "uuid" },
                _ => constraints
            };
        }

        return constraints;
    }

    /// <summary>A length rule counts elements on a collection and characters on a string.</summary>
    private static WireConstraints Length(WireConstraints constraints, bool isCollection, int? min, int? max)
        => isCollection
            ? constraints with { MinItems = min ?? constraints.MinItems, MaxItems = max ?? constraints.MaxItems }
            : constraints with { MinLength = min ?? constraints.MinLength, MaxLength = max ?? constraints.MaxLength };

    private static int? IntArg(AttributeData attribute, int position)
        => attribute.ConstructorArguments.Length > position
           && attribute.ConstructorArguments[position].Value is int value
            ? value
            : null;

    private static int? IntNamed(AttributeData attribute, string name)
    {
        foreach (var named in attribute.NamedArguments)
        {
            if (named.Key == name && named.Value.Value is int value)
                return value;
        }

        return null;
    }

    private static string? StringArg(AttributeData attribute, int position)
        => attribute.ConstructorArguments.Length > position
            ? attribute.ConstructorArguments[position].Value as string
            : null;

    /// <summary>
    ///     A bound the attribute holds as an <c>int</c>, a <c>double</c>, or — for decimal precision —
    ///     an invariant string.
    /// </summary>
    private static double? NumberArg(AttributeData attribute, int position)
    {
        if (attribute.ConstructorArguments.Length <= position)
            return null;

        return attribute.ConstructorArguments[position].Value switch
        {
            int i => i,
            long l => l,
            double d => d,
            float f => f,
            decimal m => (double)m,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }
}
