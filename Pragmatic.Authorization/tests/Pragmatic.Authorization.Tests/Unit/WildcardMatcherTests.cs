using Pragmatic.Testing.Assertions;
using Pragmatic.Authorization.Evaluation;

namespace Pragmatic.Authorization.Tests.Unit;

public class WildcardMatcherTests
{
    // =========================================================================
    // Matches — exact
    // =========================================================================

    [Fact]
    public void Matches_ExactMatch_ReturnsTrue()
    {
        WildcardMatcher.Matches("booking.guests.create", "booking.guests.create").Should().BeTrue();
    }

    [Fact]
    public void Matches_ExactMatch_CaseInsensitive()
    {
        WildcardMatcher.Matches("Booking.Guests.Create", "booking.guests.create").Should().BeTrue();
    }

    [Fact]
    public void Matches_Different_ReturnsFalse()
    {
        WildcardMatcher.Matches("booking.guests.create", "booking.guests.delete").Should().BeFalse();
    }

    // =========================================================================
    // Matches — wildcard
    // =========================================================================

    [Fact]
    public void Matches_TopLevelWildcard_MatchesChild()
    {
        WildcardMatcher.Matches("booking.*", "booking.guests.create").Should().BeTrue();
    }

    [Fact]
    public void Matches_NestedWildcard_MatchesDirectChild()
    {
        WildcardMatcher.Matches("booking.guests.*", "booking.guests.create").Should().BeTrue();
    }

    [Fact]
    public void Matches_NestedWildcard_DoesNotMatchOtherBranch()
    {
        WildcardMatcher.Matches("booking.guests.*", "booking.rooms.create").Should().BeFalse();
    }

    [Fact]
    public void Matches_WildcardOnlyAtEnd()
    {
        // "booking.*.create" matches any entity's create in booking
        WildcardMatcher.Matches("booking.*.create", "booking.guests.create").Should().BeTrue();
    }

    // =========================================================================
    // Matches — multi-level wildcard
    // =========================================================================

    [Fact]
    public void Matches_MiddleWildcard_MatchesAnyEntity()
    {
        WildcardMatcher.Matches("booking.*.read", "booking.reservation.read").Should().BeTrue();
        WildcardMatcher.Matches("booking.*.read", "booking.guest.read").Should().BeTrue();
    }

    [Fact]
    public void Matches_MiddleWildcard_DoesNotMatchDifferentOperation()
    {
        WildcardMatcher.Matches("booking.*.read", "booking.reservation.create").Should().BeFalse();
    }

    [Fact]
    public void Matches_LeadingWildcard_MatchesAnyBoundary()
    {
        WildcardMatcher.Matches("*.reservation.read", "booking.reservation.read").Should().BeTrue();
        WildcardMatcher.Matches("*.reservation.read", "catalog.reservation.read").Should().BeTrue();
    }

    [Fact]
    public void Matches_SuperAdmin_MatchesEverything()
    {
        WildcardMatcher.Matches("*", "booking.reservation.create").Should().BeTrue();
        WildcardMatcher.Matches("*", "anything").Should().BeTrue();
    }

    [Fact]
    public void Matches_TrailingWildcard_MatchesAllDescendants()
    {
        WildcardMatcher.Matches("booking.*", "booking.reservation.read").Should().BeTrue();
        WildcardMatcher.Matches("booking.*", "booking.guest.create").Should().BeTrue();
    }

    // =========================================================================
    // ExpandWildcards
    // =========================================================================

    [Fact]
    public void ExpandWildcards_ExactPermission_PassedThrough()
    {
        var all = new HashSet<string> { "booking.guests.create", "booking.guests.read" };
        var result = WildcardMatcher.ExpandWildcards(["booking.guests.create"], all);

        result.Should().ContainSingle("booking.guests.create");
    }

    [Fact]
    public void ExpandWildcards_WildcardPattern_ExpandsToAllMatching()
    {
        var all = new HashSet<string>
        {
            "booking.guests.create",
            "booking.guests.read",
            "booking.rooms.create",
            "billing.invoices.create"
        };

        var result = WildcardMatcher.ExpandWildcards(["booking.*"], all);

        result.Should().HaveCount(3);
        result.Should().Contain("booking.guests.create");
        result.Should().Contain("booking.guests.read");
        result.Should().Contain("booking.rooms.create");
        result.Should().NotContain("billing.invoices.create");
    }

    [Fact]
    public void ExpandWildcards_NestedWildcard_ExpandsOnlyNestedBranch()
    {
        var all = new HashSet<string>
        {
            "booking.guests.create",
            "booking.guests.read",
            "booking.rooms.create"
        };

        var result = WildcardMatcher.ExpandWildcards(["booking.guests.*"], all);

        result.Should().HaveCount(2);
        result.Should().Contain("booking.guests.create");
        result.Should().Contain("booking.guests.read");
    }

    [Fact]
    public void ExpandWildcards_MixedPatterns_MergesCorrectly()
    {
        var all = new HashSet<string>
        {
            "booking.guests.create",
            "booking.guests.read",
            "billing.invoices.create"
        };

        var result = WildcardMatcher.ExpandWildcards(
            ["booking.guests.*", "billing.invoices.create"], all);

        result.Should().HaveCount(3);
    }

    [Fact]
    public void ExpandWildcards_EmptyPatterns_ReturnsEmpty()
    {
        var all = new HashSet<string> { "booking.guests.create" };
        var result = WildcardMatcher.ExpandWildcards([], all);

        result.Should().BeEmpty();
    }
}
