using Xunit;

namespace Pragmatic.Validation.Tests.Unit;

/// <summary>
///     Serializes tests that mutate the process-wide <see cref="Pragmatic.Validation.ValidationTimeProvider.Current" />
///     static clock together with date-attribute tests that read the real system clock,
///     preventing cross-class parallel races on the shared static state.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DateValidationCollection
{
    public const string Name = "DateValidation";
}
