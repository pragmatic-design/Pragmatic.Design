using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     PRAG0501 lists the shapes the transform accepts — all of them, from the one table the
///     transform reads.
/// </summary>
/// <remarks>
///     A hand-written list in the message ages: a shape the transform accepts and the message omits —
///     <c>[Query&lt;TEntity, TResult&gt;]</c>, <c>StreamingEndpoint&lt;T&gt;</c>,
///     <c>StreamingDomainAction&lt;T&gt;</c> — leaves whoever publishes a query with the wrong shape
///     reading an error listing forms that do not include theirs. The message is rendered from the
///     same table the transform recognises shapes with, which is what this test holds it to.
/// </remarks>
public class RecognisedShapesMessageTests : EndpointsGeneratorTestBase
{
    [Fact]
    public void Prag0501_NamesEveryShapeTheTransformAccepts()
    {
        var source = """
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;

            namespace Test.Api;

            [Endpoint(HttpVerb.Get, "/api/notes")]
            public partial class GetNotesEndpoint { }
            """;

        var result = RunGenerator(source);

        var message = GetDiagnosticsById(result, "PRAG0501").First().GetMessage();

        message.Should().Contain("Endpoint<T>");
        message.Should().Contain("VoidEndpoint");
        message.Should().Contain("StreamingEndpoint<T>");
        message.Should().Contain("DomainAction<T>");
        message.Should().Contain("VoidDomainAction");
        message.Should().Contain("StreamingDomainAction<T>");
        message.Should().Contain("Mutation<T>");
        message.Should().Contain("[Query<TEntity, TResult>]");
    }
}
