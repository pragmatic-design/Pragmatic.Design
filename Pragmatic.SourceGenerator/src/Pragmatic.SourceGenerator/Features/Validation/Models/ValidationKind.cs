namespace Pragmatic.SourceGenerator.Features.Validation.Models;

/// <summary>
///     The kind of validation an attribute performs.
///     Used by the generator to emit optimized validation code.
/// </summary>
internal enum ValidationKind
{
    /// <summary>Unknown or custom attribute - use runtime validation.</summary>
    Unknown,

    // === Presence ===
    Required,
    NotEmpty,
    NotWhiteSpace,

    // === String Length ===
    MinLength,
    MaxLength,
    Length,

    // === Format ===
    Email,
    Phone,
    Url,
    Regex,
    CreditCard,

    // === Numeric ===
    Range,
    Positive,
    Negative,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,

    // === Collection ===
    MinCount,
    MaxCount,
    Count,

    // === Comparison ===
    EqualTo,
    NotEqualTo,
    GreaterThanProperty,
    LessThanProperty,
    GreaterThanOrEqualProperty,
    LessThanOrEqualProperty,

    // === Conditional ===
    RequiredIf,
    RequiredIfNot,

    // === Format (extended) ===
    Guid,
    ValidEnum,

    // === Date ===
    FutureDate,
    PastDate,

    // === Set ===
    OneOf
}
