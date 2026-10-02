using Pragmatic.Validation;
using Pragmatic.Validation.Attributes;

namespace TimeOff.Leave.Infrastructure.Validation;

/// <summary>
///     The date falls in the same year as another date of the same object: a leave request does not run
///     into the next year, because the allowance it draws from is yearly.
/// </summary>
/// <remarks>
///     A rule of this domain, not of the framework, so it is written here. The generated validator reads
///     the other date by name (<see cref="IPropertyValueProvider" />); a missing date is another rule's
///     to refuse.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class SameYearAsAttribute(string otherProperty) : ValidationAttribute
{
    /// <summary>The property holding the other date.</summary>
    public string OtherProperty { get; } = otherProperty;

    public override string DefaultMessageKey => "validation.same_year_as";

    public override bool RequiresInstance => true;

    public override bool IsValid(object? value) => true;

    public override bool IsValid(object? value, object instance)
        => value is not DateOnly day
           || ((IPropertyValueProvider)instance).GetPropertyValue(OtherProperty) is not DateOnly other
           || day.Year == other.Year;
}
