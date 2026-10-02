using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Tests.Entity;

public class TemporalRelationTests
{
    [Fact]
    public void TemporalRelationAttribute_DefaultValues()
    {
        var attr = new TemporalRelationAttribute();

        attr.MaxActive.Should().Be(0);
        attr.AllowOverlap.Should().BeFalse();
    }

    [Fact]
    public void TemporalRelationAttribute_MaxActive_CanBeSet()
    {
        var attr = new TemporalRelationAttribute { MaxActive = 1 };

        attr.MaxActive.Should().Be(1);
    }

    [Fact]
    public void TemporalRelationAttribute_AllowOverlap_CanBeSet()
    {
        var attr = new TemporalRelationAttribute { AllowOverlap = true };

        attr.AllowOverlap.Should().BeTrue();
    }

    [Fact]
    public void TemporalOverlapError_MaxActiveExceeded()
    {
        var error = new TemporalOverlapError
        {
            ViolationType = TemporalViolationType.MaxActiveExceeded,
            MaxActive = 1,
            CurrentActive = 2
        };

        error.Code.Should().Be("TEMPORAL_OVERLAP");
        error.StatusCode.Should().Be(409);
        error.ViolationType.Should().Be(TemporalViolationType.MaxActiveExceeded);
        error.MaxActive.Should().Be(1);
        error.CurrentActive.Should().Be(2);
    }

    [Fact]
    public void TemporalOverlapError_OverlapDetected()
    {
        var error = new TemporalOverlapError
        {
            ViolationType = TemporalViolationType.OverlapDetected
        };

        error.ViolationType.Should().Be(TemporalViolationType.OverlapDetected);
        error.MaxActive.Should().BeNull();
    }

    [Fact]
    public void ITemporalRelation_ValidTo_NullMeansActive()
    {
        var role = new TestUserRole
        {
            ValidFrom = DateTimeOffset.UtcNow.AddDays(-30),
            ValidTo = null
        };

        role.ValidTo.Should().BeNull();
    }

    [Fact]
    public void ITemporalRelation_ValidTo_HasEndDate()
    {
        var end = DateTimeOffset.UtcNow.AddDays(30);
        var role = new TestUserRole
        {
            ValidFrom = DateTimeOffset.UtcNow.AddDays(-30),
            ValidTo = end
        };

        role.ValidTo.Should().Be(end);
    }

    private sealed class TestUserRole : ITemporalRelation
    {
        public DateTimeOffset ValidFrom { get; set; }
        public DateTimeOffset? ValidTo { get; set; }
    }
}
