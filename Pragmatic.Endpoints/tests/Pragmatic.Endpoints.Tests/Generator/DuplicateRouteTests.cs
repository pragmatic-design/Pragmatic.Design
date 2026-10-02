using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     Two endpoints on the same verb and route (PRAG0529).
/// </summary>
/// <remarks>
///     <para>
///         ASP.NET Core registers both and refuses the request, not the registration: the route answers
///         <b>500</b> with an ambiguous-match exception. The build is clean, the OpenAPI document lists
///         the route once, and the first sign is a failing call.
///     </para>
///     <para>
///         Met in a consumer application, by copying an <c>[Endpoint]</c> attribute onto a second action
///         and changing everything except the route.
///     </para>
/// </remarks>
public class DuplicateRouteTests : EndpointsGeneratorTestBase
{
    private static string Source(string secondRoute, string secondVerb = "HttpVerb.Post") => $$"""
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;

        namespace TestApp.Review;

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/review/approve")]
        public partial class ApproveOneAction : VoidDomainAction
        {
            public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }

        [DomainAction]
        [Endpoint({{secondVerb}}, "{{secondRoute}}")]
        public partial class ApproveManyAction : VoidDomainAction
        {
            public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(VoidResult<IError>.Success());
        }
        """;

    [Fact]
    public void TheSameVerbAndRouteTwice_IsReported()
    {
        HasDiagnostic(RunGeneratorWithPersistence(Source("api/review/approve")), "PRAG0529")
            .Should().BeTrue("routing cannot choose, so every call to it answers 500");
    }

    /// <summary>
    ///     Case is not what distinguishes two routes, and neither is it what distinguishes two verbs.
    /// </summary>
    [Fact]
    public void TheSameRouteInADifferentCase_IsReported()
    {
        HasDiagnostic(RunGeneratorWithPersistence(Source("api/Review/Approve")), "PRAG0529")
            .Should().BeTrue();
    }

    [Fact]
    public void ADifferentRoute_IsAccepted()
    {
        HasDiagnostic(RunGeneratorWithPersistence(Source("api/review/approve-many")), "PRAG0529")
            .Should().BeFalse();
    }

    /// <summary>
    ///     The same path under a different verb is two endpoints, which is ordinary REST.
    /// </summary>
    [Fact]
    public void TheSameRouteUnderADifferentVerb_IsAccepted()
    {
        HasDiagnostic(
            RunGeneratorWithPersistence(Source("api/review/approve", "HttpVerb.Get")), "PRAG0529")
            .Should().BeFalse();
    }

    /// <summary>
    ///     The same declared route under two different groups is two different routes.
    /// </summary>
    /// <remarks>
    ///     The first version of this check compared the route as the author typed it and reported the
    ///     Showcase's <c>GetReservationEndpoint</c> against its <c>GetGuestEndpoint</c>: both write
    ///     <c>"/{id}"</c>, under groups that prefix them differently. Writing <c>"/{id}"</c> is what a
    ///     grouped endpoint is <i>supposed</i> to look like, so the check had to compare the full route.
    /// </remarks>
    [Fact]
    public void TheSameRouteUnderTwoGroups_IsAccepted()
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Review;

            [EndpointGroup("/api/notes", Tag = "Notes")]
            public sealed class NotesGroup;

            [EndpointGroup("/api/tags", Tag = "Tags")]
            public sealed class TagsGroup;

            [DomainAction]
            [Endpoint(HttpVerb.Get, "/{id}")]
            [EndpointGroup<NotesGroup>]
            public partial class GetNoteAction : VoidDomainAction
            {
                public required Guid Id { get; init; }

                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }

            [DomainAction]
            [Endpoint(HttpVerb.Get, "/{id}")]
            [EndpointGroup<TagsGroup>]
            public partial class GetTagAction : VoidDomainAction
            {
                public required Guid Id { get; init; }

                public override Task<VoidResult<IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(VoidResult<IError>.Success());
            }
            """;

        HasDiagnostic(RunGeneratorWithPersistence(source), "PRAG0529").Should().BeFalse(
            "the group prefixes them apart, and writing \"/{id}\" is what a grouped endpoint looks like");
    }
}
