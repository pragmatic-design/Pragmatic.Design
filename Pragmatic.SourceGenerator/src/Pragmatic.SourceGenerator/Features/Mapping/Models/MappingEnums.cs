namespace Pragmatic.SourceGenerator.Features.Mapping.Models;

/// <summary>
///     How a property mapping was resolved.
/// </summary>
internal enum MappingResolution
{
    /// <summary>
    ///     No mapping found.
    /// </summary>
    None,

    /// <summary>
    ///     Explicitly configured via [MapProperty].
    /// </summary>
    Explicit,

    /// <summary>
    ///     Direct name match (Id → Id).
    /// </summary>
    DirectMatch,

    /// <summary>
    ///     Flattening convention (Address.City → City).
    /// </summary>
    Flattening,

    /// <summary>
    ///     Concatenation convention (FirstName + LastName → FullName).
    /// </summary>
    Concatenation
}

/// <summary>
///     Collection kind for mapping.
/// </summary>
internal enum CollectionKind
{
    /// <summary>
    ///     Not a collection.
    /// </summary>
    None,

    /// <summary>
    ///     IEnumerable&lt;T&gt;
    /// </summary>
    IEnumerable,

    /// <summary>
    ///     List&lt;T&gt;
    /// </summary>
    List,

    /// <summary>
    ///     T[]
    /// </summary>
    Array,

    /// <summary>
    ///     ICollection&lt;T&gt;
    /// </summary>
    ICollection,

    /// <summary>
    ///     IList&lt;T&gt;
    /// </summary>
    IList,

    /// <summary>
    ///     IReadOnlyList&lt;T&gt;
    /// </summary>
    IReadOnlyList,

    /// <summary>
    ///     IReadOnlyCollection&lt;T&gt;
    /// </summary>
    IReadOnlyCollection,

    /// <summary>
    ///     HashSet&lt;T&gt;
    /// </summary>
    HashSet,

    /// <summary>
    ///     ImmutableArray&lt;T&gt; — runtime paths only (excluded from SQL projections).
    /// </summary>
    ImmutableArray,

    /// <summary>
    ///     ImmutableList&lt;T&gt; — runtime paths only (excluded from SQL projections).
    /// </summary>
    ImmutableList
}

/// <summary>
///     The type of automatic conversion to apply.
/// </summary>
internal enum ConversionKind
{
    /// <summary>
    ///     No conversion needed (same type or C# implicit conversion).
    /// </summary>
    None,

    /// <summary>
    ///     Enum → different enum type, mapped by member name via a compile-time-validated switch
    ///     (every source member must exist on the target — PRAG0328 otherwise).
    /// </summary>
    EnumToEnum,

    /// <summary>
    ///     Enum → its underlying number, and the reverse: a cast.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It took a <c>[MapConverter]</c> before, for something the language does in one token.
    ///     A DTO that speaks numbers because the system downstream does is a normal shape, not a
    ///     conversion worth writing a class for.
    /// </remarks>
    EnumToNumeric,

    /// <summary>Number → enum: the reverse cast. See <see cref="EnumToNumeric" />.</summary>
    NumericToEnum,

    /// <summary>
    ///     Enum → different enum, paired by value rather than by name — <c>[MapEnum(ByValue)]</c>.
    /// </summary>
    EnumToEnumByValue,

    /// <summary>
    ///     String to numeric (int, long, float, double, decimal).
    /// </summary>
    StringToNumeric,

    /// <summary>
    ///     String to DateTime.
    /// </summary>
    StringToDateTime,

    /// <summary>
    ///     String to DateTimeOffset.
    /// </summary>
    StringToDateTimeOffset,

    /// <summary>
    ///     String to DateOnly.
    /// </summary>
    StringToDateOnly,

    /// <summary>
    ///     String to TimeOnly.
    /// </summary>
    StringToTimeOnly,

    /// <summary>
    ///     String to enum (via Enum.Parse).
    /// </summary>
    StringToEnum,

    /// <summary>
    ///     String to Guid.
    /// </summary>
    StringToGuid,

    /// <summary>
    ///     String to bool.
    /// </summary>
    StringToBool,

    /// <summary>
    ///     Numeric to string (via ToString with InvariantCulture).
    /// </summary>
    NumericToString,

    /// <summary>
    ///     DateTime to string (via ToString with format).
    /// </summary>
    DateTimeToString,

    /// <summary>
    ///     DateTime to DateOnly.
    /// </summary>
    DateTimeToDateOnly,

    /// <summary>
    ///     DateTime to TimeOnly.
    /// </summary>
    DateTimeToTimeOnly,

    /// <summary>
    ///     DateOnly to DateTime.
    /// </summary>
    DateOnlyToDateTime,

    /// <summary>
    ///     Enum to string (via ToString).
    /// </summary>
    EnumToString,

    /// <summary>
    ///     Guid to string.
    /// </summary>
    GuidToString,

    /// <summary>
    ///     Bool to string.
    /// </summary>
    BoolToString
}
