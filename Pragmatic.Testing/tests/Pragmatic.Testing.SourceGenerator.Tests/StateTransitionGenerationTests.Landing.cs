using System.Text.RegularExpressions;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Testing.SourceGenerator.Tests;

/// <summary>
///     The state a create leaves its entity in (#917). A create that moves the record in its own body (a report submitted as it is created)
///     does not leave it in the enum's <c>[InitialState]</c>, and contracts walked from there assert every step on a record in another state.
/// </summary>
/// <remarks>
///     The shape is FieldService's: draft is the initial state, submitted is entered from draft, revising takes a submitted report back to
///     draft, and resubmitting moves it to submitted again.
/// </remarks>
public partial class StateTransitionGenerationTests
{
    private const string Reports = """
        namespace App.Desk
        {
            public enum ReportStatus
            {
                [Pragmatic.Persistence.StateMachine.InitialState]
                [Pragmatic.Persistence.StateMachine.TransitionFrom(Submitted)]
                Draft,
                [Pragmatic.Persistence.StateMachine.TransitionFrom(Draft)]
                Submitted
            }

            public class Report { public ReportStatus Status { get; set; } }

            [Pragmatic.Endpoints.Attributes.EndpointGroup("/api/reports")]
            public sealed class ReportsGroup { }

            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "/{id}/resubmit")]
            [Pragmatic.Endpoints.Attributes.EndpointGroup<ReportsGroup>]
            [Pragmatic.Actions.Attributes.TransitionsTo<ReportStatus>(ReportStatus.Submitted)]
            public class ResubmitReportEndpoint
            {
                private Pragmatic.Persistence.Repository.IRepository<Report> _reports;
            }

            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "/{id}/revise")]
            [Pragmatic.Endpoints.Attributes.EndpointGroup<ReportsGroup>]
            [Pragmatic.Actions.Attributes.TransitionsTo<ReportStatus>(ReportStatus.Draft)]
            public class ReviseReportEndpoint
            {
                private Pragmatic.Persistence.Repository.IRepository<Report> _reports;
            }
        }
        """;

    /// <summary>The create, declaring the state it lands in.</summary>
    private const string CreateLandingInSubmitted = """
        namespace App.Desk
        {
            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "/api/reports")]
            [Pragmatic.Actions.Attributes.TransitionsTo<ReportStatus>(ReportStatus.Submitted)]
            public class SubmitReportMutation : Pragmatic.Actions.Mutation.Mutation<Report>
            {
                public string WorkDone { get; set; }
            }
        }
        """;

    /// <summary>The same create, declaring nothing.</summary>
    private const string CreateDeclaringNothing = """
        namespace App.Desk
        {
            [Pragmatic.Endpoints.Attributes.Endpoint(Pragmatic.Endpoints.Attributes.HttpVerb.Post, "/api/reports")]
            public class SubmitReportMutation : Pragmatic.Actions.Mutation.Mutation<Report>
            {
                public string WorkDone { get; set; }
            }
        }
        """;

    [Fact]
    public void ACreateDeclaringItsLandingState_IsWalkedFromThere()
    {
        var source = Generate(Preamble + Reports + CreateLandingInSubmitted, "Transitions");

        // From submitted, revising is legal and resubmitting is not: the opposite of what a walk from draft asserts.
        source.Should().Contain("Report_TransitionToDraft_FromInitial_Succeeds", "revise takes a submitted report back to draft");
        source.Should().Contain("Report_TransitionToSubmitted_FromInitial_IsRejectedWithConflict", "a submitted report cannot be submitted again");
        source.Should().Contain("Report_TransitionToSubmitted_FromLegalState_Succeeds", "after a revise it can");
        source.Should().NotContain("Report_TransitionToSubmitted_FromInitial_Succeeds");
        source.Should().NotContain("ArrangedAsync", "a record in the create's landing state is made by the create, not by ArrangeFor, whose is in [InitialState]");
    }

    /// <summary>
    ///     On the create, <c>[TransitionsTo]</c> is where the record lands, read by the entity's transitions: it is no transition of its own, so
    ///     the create's route is never posted as if it moved a record.
    /// </summary>
    [Fact]
    public void ACreatesLandingState_IsNotATransitionOfItsOwn()
    {
        var source = Generate(Preamble + Reports + CreateLandingInSubmitted, "Transitions");

        Regex.Matches(source, @"public async global::System\.Threading\.Tasks\.Task Report_TransitionToSubmitted_").Count
            .Should().Be(2, "the resubmit's two contracts, and none for the create");
    }

    /// <summary>The control: a create declaring nothing keeps the walk from [InitialState], and the application is asked first.</summary>
    [Fact]
    public void ACreateDeclaringNothing_IsWalkedFromTheInitialState_AndTheApplicationIsAskedFirst()
    {
        var source = Generate(Preamble + Reports + CreateDeclaringNothing, "Transitions");

        source.Should().Contain("Report_TransitionToSubmitted_FromInitial_Succeeds", "from draft, submitting is legal");
        source.Should().Contain("Report_TransitionToDraft_FromInitial_IsRejectedWithConflict");
        source.Should().Contain("ArrangedAsync(\"Report\", Client)", "the application answers first, and its record is in [InitialState]");
        source.Should().Contain("if (id is null)", "the create stays the way when the application does not arrange the entity");
    }
}
