using Pragmatic.Testing.Assertions;

namespace Pragmatic.Privacy.Tests;

/// <summary>
///     The request lifecycle, and the two things about it that are easy to get wrong: that a partial
///     outcome is a success, and that a deadline which can be pushed repeatedly is not a deadline.
/// </summary>
public sealed class DataSubjectRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromDays(30);

    private static DataSubjectRequest Open(DataSubjectRequestType type = DataSubjectRequestType.Erasure)
        => DataSubjectRequest.Open("req-1", "subject-ref-1", type, Now, Window);

    [Fact]
    public void Open_StartsTheClock()
    {
        var request = Open();

        request.Status.Should().Be(DataSubjectRequestStatus.Received);
        request.ReceivedAt.Should().Be(Now);
        request.DueAt.Should().Be(Now.AddDays(30));
        request.IsClosed.Should().BeFalse();
    }

    [Fact]
    public void Open_CarriesTheReferenceAndNeverAnIdentity()
    {
        // The request is one more place a person's identity could leak into; it holds the pseudonym.
        Open().SubjectRef.Should().Be("subject-ref-1");
    }

    [Fact]
    public void TheHappyPath_RunsReceivedVerifiedExecutingCompleted()
    {
        var request = Open();

        request.TransitionTo(DataSubjectRequestStatus.Verified, Now);
        request.TransitionTo(DataSubjectRequestStatus.Executing, Now);
        request.TransitionTo(DataSubjectRequestStatus.Completed, Now);

        request.Status.Should().Be(DataSubjectRequestStatus.Completed);
        request.IsClosed.Should().BeTrue();
        request.ClosedAt.Should().Be(Now);
    }

    [Fact]
    public void PartialIsATerminalSuccess_NotAFailure()
    {
        // "Erased 12 records, 2 retained under a fiscal obligation" is the ordinary outcome. Modelling
        // it as an error would force a choice between lying and failing.
        var request = Open();
        request.TransitionTo(DataSubjectRequestStatus.Verified, Now);
        request.TransitionTo(DataSubjectRequestStatus.Executing, Now);

        request.TransitionTo(DataSubjectRequestStatus.Partial, Now);

        request.IsClosed.Should().BeTrue();
        request.ClosedAt.Should().NotBeNull();
    }

    [Fact]
    public void ExecutingCannotBeReachedWithoutVerifyingWhoIsAsking()
    {
        // Acting on an erasure request for the wrong person is an incident, and it is not undoable.
        var request = Open();

        var act = () => request.TransitionTo(DataSubjectRequestStatus.Executing, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AVerifiedRequestCanStillBeRejected()
    {
        // Identity established does not mean the request applies — an erasure claim against data held
        // under a legal obligation, for instance.
        var request = Open();
        request.TransitionTo(DataSubjectRequestStatus.Verified, Now);

        request.CanTransitionTo(DataSubjectRequestStatus.Rejected).Should().BeTrue();
    }

    [Theory]
    [InlineData(DataSubjectRequestStatus.Completed)]
    [InlineData(DataSubjectRequestStatus.Partial)]
    [InlineData(DataSubjectRequestStatus.Rejected)]
    public void AClosedRequestCannotBeReopened(DataSubjectRequestStatus terminal)
    {
        var request = Open();
        request.TransitionTo(DataSubjectRequestStatus.Verified, Now);
        if (terminal != DataSubjectRequestStatus.Rejected)
            request.TransitionTo(DataSubjectRequestStatus.Executing, Now);
        request.TransitionTo(terminal, Now);

        var act = () => request.TransitionTo(DataSubjectRequestStatus.Executing, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SkippingStates_IsRefused()
    {
        var request = Open();

        var act = () => request.TransitionTo(DataSubjectRequestStatus.Completed, Now);

        act.Should().Throw<InvalidOperationException>();
    }

    // =========================================================================
    // Deadline
    // =========================================================================

    [Fact]
    public void ARequestPastItsDueDate_IsOverdue()
        => Open().IsOverdue(Now.AddDays(31)).Should().BeTrue();

    [Fact]
    public void AClosedRequestIsNeverOverdue()
    {
        var request = Open();
        request.TransitionTo(DataSubjectRequestStatus.Verified, Now);
        request.TransitionTo(DataSubjectRequestStatus.Rejected, Now);

        request.IsOverdue(Now.AddDays(365)).Should().BeFalse();
    }

    [Fact]
    public void ExtendingTheDeadline_RequiresAReason()
    {
        var request = Open();

        var act = () => request.ExtendDeadline(TimeSpan.FromDays(60), "   ", Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ExtendingTheDeadline_MovesItAndRecordsWhy()
    {
        var request = Open();

        request.ExtendDeadline(TimeSpan.FromDays(60), "complex cross-boundary export", Now);

        request.DueAt.Should().Be(Now.AddDays(90));
        request.ExtensionReason.Should().Be("complex cross-boundary export");
    }

    [Fact]
    public void TheDeadlineCanOnlyBeExtendedOnce()
    {
        // A deadline that can be pushed repeatedly is not a deadline.
        var request = Open();
        request.ExtendDeadline(TimeSpan.FromDays(60), "first", Now);

        var act = () => request.ExtendDeadline(TimeSpan.FromDays(60), "second", Now);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AClosedRequestsDeadlineCannotMove()
    {
        var request = Open();
        request.TransitionTo(DataSubjectRequestStatus.Verified, Now);
        request.TransitionTo(DataSubjectRequestStatus.Rejected, Now);

        var act = () => request.ExtendDeadline(TimeSpan.FromDays(60), "too late", Now);

        act.Should().Throw<InvalidOperationException>();
    }
}
