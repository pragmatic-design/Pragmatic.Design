using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Validation.Models;

namespace Pragmatic.SourceGenerator.Features.Validation.Transforms;

/// <summary>
///     Attribute model creation methods for ValidatableTransform.
/// </summary>
internal static partial class ValidatableTransform
{
    // A custom ValidationAttribute is cross-property when it overrides IsValid(value, instance).
    // Walk up to (but not including) the framework base type, whose two-arg IsValid is the default.
    private static bool OverridesTwoArgIsValid(INamedTypeSymbol? attrClass)
    {
        for (var t = attrClass; t is not null; t = t.BaseType)
        {
            if (t.ToDisplayString() == "Pragmatic.Validation.Attributes.ValidationAttribute")
                break;
            foreach (var member in t.GetMembers("IsValid"))
                if (member is IMethodSymbol { Parameters.Length: 2 })
                    return true;
        }

        return false;
    }

    private static ValidationAttributeModel? CreateAttributeModel(AttributeData attr)
    {
        if (attr.AttributeClass is null)
            return null;

        var attrName = attr.AttributeClass.Name;
        if (attrName.EndsWith("Attribute"))
            attrName = attrName.Substring(0, attrName.Length - 9);

        var attrFullName = attr.AttributeClass.ToDisplayString();
        var kind = GetValidationKind(attrFullName);

        string? value = null;
        string? value2 = null;
        string? otherProperty = null;
        string? comparisonValue = null;
        string? pattern = null;
        var allowEmptyStrings = false;
        var allowedSchemes = ImmutableArray<string>.Empty;
        var requireAbsolute = true;

        if (attr.ConstructorArguments.Length > 0)
        {
            var arg0 = attr.ConstructorArguments[0];
            switch (kind)
            {
                case ValidationKind.MinLength:
                case ValidationKind.MaxLength:
                case ValidationKind.MinCount:
                case ValidationKind.MaxCount:
                case ValidationKind.GreaterThan:
                case ValidationKind.GreaterThanOrEqual:
                case ValidationKind.LessThan:
                case ValidationKind.LessThanOrEqual:
                    value = FormatConstantValue(arg0);
                    break;
                case ValidationKind.Length:
                case ValidationKind.Range:
                case ValidationKind.Count:
                    value = FormatConstantValue(arg0);
                    if (attr.ConstructorArguments.Length > 1)
                        value2 = FormatConstantValue(attr.ConstructorArguments[1]);
                    break;
                case ValidationKind.Regex:
                    pattern = arg0.Value?.ToString();
                    break;
                case ValidationKind.EqualTo:
                case ValidationKind.NotEqualTo:
                case ValidationKind.GreaterThanProperty:
                case ValidationKind.LessThanProperty:
                case ValidationKind.GreaterThanOrEqualProperty:
                case ValidationKind.LessThanOrEqualProperty:
                    otherProperty = arg0.Value?.ToString();
                    break;
                case ValidationKind.RequiredIf:
                case ValidationKind.RequiredIfNot:
                    otherProperty = arg0.Value?.ToString();
                    if (attr.ConstructorArguments.Length > 1)
                        comparisonValue = FormatConstantValue(attr.ConstructorArguments[1]);
                    break;
            }
        }

        var allowedValues = ImmutableArray<string>.Empty;
        if (kind == ValidationKind.OneOf && attr.ConstructorArguments.Length > 0)
        {
            var arg0 = attr.ConstructorArguments[0];
            if (arg0.Kind == TypedConstantKind.Array)
                allowedValues = arg0.Values.Select(v => FormatConstantValue(v)).ToImmutableArray();
        }

        var severity = 0; // 0=Error (default)
        var groups = ImmutableArray<string>.Empty;

        foreach (var namedArg in attr.NamedArguments)
            switch (namedArg.Key)
            {
                case "AllowEmptyStrings":
                    allowEmptyStrings = namedArg.Value.Value is true;
                    break;
                case "AllowedSchemes" when namedArg.Value.Kind == TypedConstantKind.Array:
                    allowedSchemes = namedArg.Value.Values
                        .Select(v => v.Value?.ToString() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToImmutableArray();
                    break;
                case "RequireAbsolute":
                    requireAbsolute = namedArg.Value.Value is not false;
                    break;
                case "Severity" when namedArg.Value.Value is int s:
                    severity = s;
                    break;
                case "Groups" when namedArg.Value.Kind == TypedConstantKind.Array:
                    groups = namedArg.Value.Values
                        .Select(v => v.Value?.ToString() ?? "")
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToImmutableArray();
                    break;
            }

        var messageKey = GetMessageKey(attr);
        var messageKeyReference = messageKey is null ? UnresolvedMessageKey(attr) : null;
        var requiresInstance = kind is ValidationKind.EqualTo or ValidationKind.NotEqualTo
            or ValidationKind.GreaterThanProperty or ValidationKind.LessThanProperty
            or ValidationKind.GreaterThanOrEqualProperty or ValidationKind.LessThanOrEqualProperty
            or ValidationKind.RequiredIf or ValidationKind.RequiredIfNot
            // A custom attribute that overrides IsValid(object?, object) is cross-property and needs
            // the parent instance; without this the generated code would call the single-arg overload.
            || (kind == ValidationKind.Unknown && OverridesTwoArgIsValid(attr.AttributeClass));

        // For a custom attribute the generated code re-instantiates it, so preserve its ctor args.
        var ctorArgs = kind == ValidationKind.Unknown && attr.ConstructorArguments.Length > 0
            ? attr.ConstructorArguments.Select(FormatConstantValue).ToImmutableArray()
            : ImmutableArray<string>.Empty;

        return new ValidationAttributeModel
        {
            AttributeType = attrFullName,
            AttributeName = attrName,
            MessageKey = messageKey,
            MessageKeyReference = messageKeyReference?.ToString(),
            MessageKeyLocation = LocationInfo.From(messageKeyReference?.GetLocation()),
            RequiresInstance = requiresInstance,
            Kind = kind,
            Value = value,
            Value2 = value2,
            OtherProperty = otherProperty,
            ComparisonValue = comparisonValue,
            AllowEmptyStrings = allowEmptyStrings,
            Pattern = pattern,
            AllowedSchemes = allowedSchemes,
            RequireAbsolute = requireAbsolute,
            AllowedValues = allowedValues,
            CtorArgs = ctorArgs,
            Severity = severity,
            Groups = groups
        };
    }

    private static string FormatConstantValue(TypedConstant constant)
    {
        if (constant.IsNull)
            return "null";
        return constant.Kind switch
        {
            TypedConstantKind.Primitive when constant.Value is string s => $"\"{s}\"",
            TypedConstantKind.Primitive when constant.Value is char c => $"'{c}'",
            TypedConstantKind.Primitive when constant.Value is bool b => b ? "true" : "false",
            TypedConstantKind.Primitive when constant.Value is double d =>
                string.Format(CultureInfo.InvariantCulture, "{0}", d),
            TypedConstantKind.Primitive when constant.Value is float f =>
                string.Format(CultureInfo.InvariantCulture, "{0}f", f),
            TypedConstantKind.Primitive when constant.Value is decimal m =>
                string.Format(CultureInfo.InvariantCulture, "{0}m", m),
            TypedConstantKind.Primitive when constant.Value is int or long or short or byte =>
                constant.Value.ToString(),
            TypedConstantKind.Primitive =>
                string.Format(CultureInfo.InvariantCulture, "{0}", constant.Value),
            TypedConstantKind.Enum => FormatEnumValue(constant),
            // ⚠️ An array's value lives in Values, and reading Value throws. A custom attribute with a
            // params constructor — the shape [SupportedCurrency("USD", "EUR")] has — took the branch
            // below and brought down the whole generator: Roslyn reports that as CS8785, which is a
            // warning, so a build without --warnaserror succeeds with every generated file missing.
            TypedConstantKind.Array =>
                $"new[] {{ {string.Join(", ", constant.Values.Select(FormatConstantValue))} }}",
            _ => constant.IsNull ? "null" : constant.Value?.ToString() ?? "null"
        };
    }

    private static string FormatEnumValue(TypedConstant constant)
    {
        if (constant.Type is null)
            return constant.Value?.ToString() ?? "0";
        var enumType = constant.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        foreach (var member in constant.Type.GetMembers())
            if (member is IFieldSymbol { HasConstantValue: true } field && Equals(field.ConstantValue, constant.Value))
                return $"{enumType}.{field.Name}";
        return $"({enumType}){constant.Value}";
    }

    private static string? GetMessageKey(AttributeData attr)
    {
        foreach (var namedArg in attr.NamedArguments)
            if (namedArg is { Key: "MessageKey", Value.Value: string customKey })
                return customKey;
        return null;
    }

    /// <summary>
    ///     The <c>MessageKey</c> argument as written, when it is there and has no value: it names a
    ///     constant that does not exist yet — this generator's own <c>TKeys</c>. An explicit
    ///     <c>null</c> is the default key, as it says.
    /// </summary>
    private static ExpressionSyntax? UnresolvedMessageKey(AttributeData attr)
        => (attr.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax)?.ArgumentList?.Arguments
            .FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == "MessageKey")
            ?.Expression is { } expression and not LiteralExpressionSyntax
            ? expression
            : null;

    internal static ValidationKind GetValidationKind(string attributeFullName) =>
        attributeFullName switch
        {
            "Pragmatic.Validation.Attributes.RequiredAttribute" => ValidationKind.Required,
            "Pragmatic.Validation.Attributes.NotEmptyAttribute" => ValidationKind.NotEmpty,
            "Pragmatic.Validation.Attributes.NotWhiteSpaceAttribute" => ValidationKind.NotWhiteSpace,
            "Pragmatic.Validation.Attributes.MinLengthAttribute" => ValidationKind.MinLength,
            "Pragmatic.Validation.Attributes.MaxLengthAttribute" => ValidationKind.MaxLength,
            "Pragmatic.Validation.Attributes.LengthAttribute" => ValidationKind.Length,
            "Pragmatic.Validation.Attributes.EmailAttribute" => ValidationKind.Email,
            "Pragmatic.Validation.Attributes.PhoneAttribute" => ValidationKind.Phone,
            "Pragmatic.Validation.Attributes.UrlAttribute" => ValidationKind.Url,
            "Pragmatic.Validation.Attributes.RegexAttribute" => ValidationKind.Regex,
            "Pragmatic.Validation.Attributes.CreditCardAttribute" => ValidationKind.CreditCard,
            "Pragmatic.Validation.Attributes.RangeAttribute" => ValidationKind.Range,
            "Pragmatic.Validation.Attributes.PositiveAttribute" => ValidationKind.Positive,
            "Pragmatic.Validation.Attributes.NegativeAttribute" => ValidationKind.Negative,
            "Pragmatic.Validation.Attributes.GreaterThanAttribute" => ValidationKind.GreaterThan,
            "Pragmatic.Validation.Attributes.GreaterThanOrEqualAttribute" => ValidationKind.GreaterThanOrEqual,
            "Pragmatic.Validation.Attributes.LessThanAttribute" => ValidationKind.LessThan,
            "Pragmatic.Validation.Attributes.LessThanOrEqualAttribute" => ValidationKind.LessThanOrEqual,
            "Pragmatic.Validation.Attributes.MinCountAttribute" => ValidationKind.MinCount,
            "Pragmatic.Validation.Attributes.MaxCountAttribute" => ValidationKind.MaxCount,
            "Pragmatic.Validation.Attributes.CountAttribute" => ValidationKind.Count,
            "Pragmatic.Validation.Attributes.EqualToAttribute" => ValidationKind.EqualTo,
            "Pragmatic.Validation.Attributes.NotEqualToAttribute" => ValidationKind.NotEqualTo,
            "Pragmatic.Validation.Attributes.GreaterThanPropertyAttribute" => ValidationKind.GreaterThanProperty,
            "Pragmatic.Validation.Attributes.LessThanPropertyAttribute" => ValidationKind.LessThanProperty,
            "Pragmatic.Validation.Attributes.GreaterThanOrEqualPropertyAttribute" => ValidationKind.GreaterThanOrEqualProperty,
            "Pragmatic.Validation.Attributes.LessThanOrEqualPropertyAttribute" => ValidationKind.LessThanOrEqualProperty,
            "Pragmatic.Validation.Attributes.RequiredIfAttribute" => ValidationKind.RequiredIf,
            "Pragmatic.Validation.Attributes.RequiredIfNotAttribute" => ValidationKind.RequiredIfNot,
            "Pragmatic.Validation.Attributes.GuidAttribute" => ValidationKind.Guid,
            "Pragmatic.Validation.Attributes.ValidEnumAttribute" => ValidationKind.ValidEnum,
            "Pragmatic.Validation.Attributes.FutureDateAttribute" => ValidationKind.FutureDate,
            "Pragmatic.Validation.Attributes.PastDateAttribute" => ValidationKind.PastDate,
            "Pragmatic.Validation.Attributes.OneOfAttribute" => ValidationKind.OneOf,
            _ => ValidationKind.Unknown
        };

    private static T GetNamedArgument<T>(AttributeData attr, string name, T defaultValue = default!)
    {
        foreach (var namedArg in attr.NamedArguments)
            if (namedArg.Key == name && namedArg.Value.Value is T value)
                return value;
        return defaultValue;
    }
}
