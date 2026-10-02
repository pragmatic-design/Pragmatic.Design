using Pragmatic.Testing.Assertions;

namespace Pragmatic.Result.Tests.Unit;

/// <summary>
///     K11: the lazily-computed MessageKey must not be stored in an instance field, because a record's
///     synthesized equality includes all instance fields — populating it would change the error's hash and
///     break sets/dictionaries.
/// </summary>
public sealed class ErrorEqualityTests
{
    private sealed record SampleError : Error
    {
        public override string Code => "SAMPLE_ERROR";
        public override int StatusCode => 400;
    }

    [Fact]
    public void GetHashCode_IsStable_AfterMessageKeyAccess()
    {
        var error = new SampleError();
        var before = error.GetHashCode();

        _ = error.MessageKey; // formerly populated a mutable instance cache field

        error.GetHashCode().Should().Be(before);
    }

    [Fact]
    public void Equality_Holds_WhenOnlyOneInstanceAccessedMessageKey()
    {
        var a = new SampleError();
        var b = new SampleError();

        _ = a.MessageKey; // only a has its key computed

        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());

        var set = new HashSet<SampleError> { a };
        set.Contains(b).Should().BeTrue("equal errors must hash identically regardless of cache state");
    }

    [Fact]
    public void MessageKey_DerivesFromCode()
    {
        new SampleError().MessageKey.Should().Be("error.sample.error");
    }
}
