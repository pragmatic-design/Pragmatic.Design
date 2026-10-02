namespace Pragmatic.SourceGenerator.Features.Validation.Models;

/// <summary>
///     The message key of every rule the generator inlines, mirroring each attribute's
///     <c>DefaultMessageKey</c>.
/// </summary>
/// <remarks>
///     A copy of a truth that lives in the attributes, kept because an inlined rule never instantiates
///     its attribute and the generator cannot evaluate a property getter at compile time. The copy is
///     checked against the original by <c>ACustomRuleReportsItsOwnKeyTests</c> in the Validation suite,
///     which is what keeps it from drifting.
/// </remarks>
internal static class ValidationMessageKeys
{
    /// <summary>The key for an inlined kind; <c>null</c> for a kind the generator does not inline.</summary>
    public static string? ForKind(ValidationKind kind) => kind switch
    {
        ValidationKind.Required => "validation.required",
        ValidationKind.NotEmpty => "validation.notempty",
        ValidationKind.NotWhiteSpace => "validation.notwhitespace",
        ValidationKind.MinLength => "validation.minlength",
        ValidationKind.MaxLength => "validation.maxlength",
        ValidationKind.Length => "validation.length",
        ValidationKind.Email => "validation.email",
        ValidationKind.Phone => "validation.phone",
        ValidationKind.Url => "validation.url",
        ValidationKind.Regex => "validation.regex",
        ValidationKind.CreditCard => "validation.creditcard",
        ValidationKind.Range => "validation.range",
        ValidationKind.Positive => "validation.positive",
        ValidationKind.Negative => "validation.negative",
        ValidationKind.GreaterThan => "validation.greaterthan",
        ValidationKind.GreaterThanOrEqual => "validation.greaterthanorequal",
        ValidationKind.LessThan => "validation.lessthan",
        ValidationKind.LessThanOrEqual => "validation.lessthanorequal",
        ValidationKind.MinCount => "validation.mincount",
        ValidationKind.MaxCount => "validation.maxcount",
        ValidationKind.Count => "validation.count",
        ValidationKind.EqualTo => "validation.equalto",
        ValidationKind.NotEqualTo => "validation.notequalto",
        ValidationKind.GreaterThanProperty => "validation.greaterthanproperty",
        ValidationKind.LessThanProperty => "validation.lessthanproperty",
        ValidationKind.GreaterThanOrEqualProperty => "validation.greaterthanorequalproperty",
        ValidationKind.LessThanOrEqualProperty => "validation.lessthanorequalproperty",
        ValidationKind.RequiredIf => "validation.requiredif",
        ValidationKind.RequiredIfNot => "validation.requiredifnot",
        ValidationKind.Guid => "validation.guid",
        ValidationKind.ValidEnum => "validation.enum",
        ValidationKind.FutureDate => "validation.future_date",
        ValidationKind.PastDate => "validation.past_date",
        ValidationKind.OneOf => "validation.oneof",
        _ => null
    };
}
