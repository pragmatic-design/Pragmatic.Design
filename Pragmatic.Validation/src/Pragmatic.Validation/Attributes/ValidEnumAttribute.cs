namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a value is a defined member of an enum type.
/// </summary>
/// <remarks>
///     <para>
///         Prevents invalid enum values that can be assigned through casting (e.g., <c>(Status)999</c>).
///         Works with any enum type.
///     </para>
///     <para>
///         Null values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record UpdateStatusRequest
/// {
///     [Required]
///     [ValidEnum]
///     public OrderStatus Status { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class ValidEnumAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.enum";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null)
            return true;

        var type = value.GetType();
        if (!type.IsEnum)
            return true;

        return Enum.IsDefined(type, value) || IsDefinedFlagsCombination(type, value);
    }

    /// <summary>
    ///     Validates that a value is a defined member of the specified enum type.
    /// </summary>
    /// <typeparam name="TEnum">The enum type to validate against.</typeparam>
    /// <param name="value">The value to validate.</param>
    /// <returns><c>true</c> if the value is a defined enum member; otherwise, <c>false</c>.</returns>
    /// <remarks>
    ///     This method is used by the source generator to inline validation. The fast path
    ///     (<see cref="Enum.IsDefined{TEnum}(TEnum)" />) is reflection-free; the Flags fallback only
    ///     runs when a value is not a single defined member.
    /// </remarks>
    public static bool IsValidEnum<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        return Enum.IsDefined(value) || IsDefinedFlagsCombination(typeof(TEnum), value);
    }

    // A [Flags] enum accepts any OR-combination of its defined bits (e.g. Read | Write), which
    // Enum.IsDefined rejects because the combined value is not itself a declared member. Treat such
    // a combination as valid. This only runs after IsDefined already returned false.
    private static bool IsDefinedFlagsCombination(Type enumType, object value)
    {
        if (!enumType.IsDefined(typeof(FlagsAttribute), inherit: false))
            return false;

        long mask = 0;
        foreach (var defined in Enum.GetValues(enumType))
            mask |= Convert.ToInt64(defined);

        var bits = Convert.ToInt64(value);
        return bits != 0 && (bits & ~mask) == 0;
    }
}
