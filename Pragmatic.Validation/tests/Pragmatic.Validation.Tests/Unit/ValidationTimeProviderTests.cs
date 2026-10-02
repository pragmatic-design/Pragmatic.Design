// Tests for ValidationTimeProvider.Current override behaviour and its effect on
// date-based validation attributes (FutureDate / PastDate).
// Mutates the process-wide static clock; serialized via the DateValidation collection.

using Pragmatic.Testing.Assertions;
using Pragmatic.Validation;
using Pragmatic.Validation.Attributes;
using Xunit;

namespace Pragmatic.Validation.Tests.Unit;

[Collection(DateValidationCollection.Name)]
public class ValidationTimeProviderTests
{
    /// <summary>Minimal fixed-clock TimeProvider — avoids any external test package dependency.</summary>
    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static readonly DateTimeOffset FixedNow =
        new(2030, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private static void WithClock(DateTimeOffset now, Action body)
    {
        var previous = ValidationTimeProvider.Current;
        ValidationTimeProvider.Current = new FixedClock(now);
        try
        {
            body();
        }
        finally
        {
            ValidationTimeProvider.Current = previous;
        }
    }

    // =========================================================================
    // Default + setter contract
    // =========================================================================

    [Fact]
    public void Current_Default_IsSystemTimeProvider()
    {
        ValidationTimeProvider.Current.Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void Current_SetToProvider_ReturnsThatProvider()
    {
        var previous = ValidationTimeProvider.Current;
        var clock = new FixedClock(FixedNow);
        try
        {
            ValidationTimeProvider.Current = clock;
            ValidationTimeProvider.Current.Should().BeSameAs(clock);
        }
        finally
        {
            ValidationTimeProvider.Current = previous;
        }
    }

    [Fact]
    public void Current_SetToNull_FallsBackToSystem()
    {
        var previous = ValidationTimeProvider.Current;
        try
        {
            ValidationTimeProvider.Current = new FixedClock(FixedNow);
            ValidationTimeProvider.Current = null!;
            ValidationTimeProvider.Current.Should().BeSameAs(TimeProvider.System);
        }
        finally
        {
            ValidationTimeProvider.Current = previous;
        }
    }

    [Fact]
    public void Current_Restored_AfterScopeEnds()
    {
        var before = ValidationTimeProvider.Current;
        WithClock(FixedNow, () => { /* mutated inside */ });
        ValidationTimeProvider.Current.Should().BeSameAs(before);
    }

    // =========================================================================
    // PastDateAttribute honours the overridden clock
    // =========================================================================

    [Fact]
    public void PastDate_DateBeforeOverriddenNow_ReturnsTrue()
    {
        WithClock(FixedNow, () =>
        {
            var attr = new PastDateAttribute();
            attr.IsValid(FixedNow.UtcDateTime.AddDays(-1)).Should().BeTrue();
        });
    }

    [Fact]
    public void PastDate_DateAfterOverriddenNow_ReturnsFalse()
    {
        WithClock(FixedNow, () =>
        {
            var attr = new PastDateAttribute();
            // Real system clock is < FixedNow (year 2030), so this only passes
            // because the override clock is actually in effect.
            attr.IsValid(FixedNow.UtcDateTime.AddDays(1)).Should().BeFalse();
        });
    }

    [Fact]
    public void PastDate_DateTimeOffsetBeforeOverriddenNow_ReturnsTrue()
    {
        WithClock(FixedNow, () =>
        {
            var attr = new PastDateAttribute();
            attr.IsValid(FixedNow.AddHours(-1)).Should().BeTrue();
        });
    }

    [Fact]
    public void PastDate_DateOnlyBeforeOverriddenNow_ReturnsTrue()
    {
        WithClock(FixedNow, () =>
        {
            var attr = new PastDateAttribute();
            var yesterday = DateOnly.FromDateTime(FixedNow.UtcDateTime).AddDays(-1);
            attr.IsValid(yesterday).Should().BeTrue();
        });
    }

    // =========================================================================
    // FutureDateAttribute honours the overridden clock
    // =========================================================================

    [Fact]
    public void FutureDate_DateAfterOverriddenNow_ReturnsTrue()
    {
        WithClock(FixedNow, () =>
        {
            var attr = new FutureDateAttribute();
            attr.IsValid(FixedNow.UtcDateTime.AddDays(1)).Should().BeTrue();
        });
    }

    [Fact]
    public void FutureDate_DateBeforeOverriddenNow_ReturnsFalse()
    {
        WithClock(FixedNow, () =>
        {
            var attr = new FutureDateAttribute();
            attr.IsValid(FixedNow.UtcDateTime.AddDays(-1)).Should().BeFalse();
        });
    }

    [Fact]
    public void FutureDate_DateOnlyAfterOverriddenNow_ReturnsTrue()
    {
        WithClock(FixedNow, () =>
        {
            var attr = new FutureDateAttribute();
            var tomorrow = DateOnly.FromDateTime(FixedNow.UtcDateTime).AddDays(1);
            attr.IsValid(tomorrow).Should().BeTrue();
        });
    }

    [Fact]
    public void DateAttributes_OverrideFlipsResult_RelativeToRealClock()
    {
        // A date that is in the FUTURE relative to the real system clock (2030),
        // but in the PAST relative to the overridden clock (2035). Demonstrates the
        // override actually changes the comparison baseline.
        var realFutureDate = new DateTime(2032, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        WithClock(new DateTimeOffset(2035, 1, 1, 0, 0, 0, TimeSpan.Zero), () =>
        {
            new PastDateAttribute().IsValid(realFutureDate).Should().BeTrue();
            new FutureDateAttribute().IsValid(realFutureDate).Should().BeFalse();
        });
    }
}
