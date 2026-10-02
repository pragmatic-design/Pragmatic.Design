using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     A list of data in the request body, rather than mistaken for a dependency.
/// </summary>
/// <remarks>
///     <para>
///         <c>IReadOnlyList&lt;Guid&gt;</c> is an interface, and a service detector that takes every
///         interface for a DI dependency drops the property from the body: the generated endpoint then
///         emits <c>new TheAction()</c> — failing on a <c>required</c> member with CS9035, on generated
///         code, naming neither the property nor the reason.
///     </para>
///     <para>
///         An action that takes a list of ids is what "confirm these" looks like in any application.
///         The element type decides, so a collection of services stays a dependency: what makes a
///         collection a service is what it holds, not that it is one.
///     </para>
/// </remarks>
public class CollectionBodyPropertyTests : EndpointsGeneratorTestBase
{
    private static string Source(string property) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;

        namespace TestApp.Review;

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/review/confirm")]
        public partial class ConfirmManyAction : DomainAction<int>
        {
        {{property}}

            public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<int, IError>.Success(0));
        }
        """;

    private static SourceGenRunResult Run(string property)
        => RunGeneratorWithPersistence(Source(property));

    [Fact]
    public void ARequiredListOfIds_ReachesTheRequestBody()
    {
        var sources = GetGeneratedSourcesAsDictionary(Run(
            """
                public required string Note { get; init; }
                public required IReadOnlyList<Guid> CandidateIds { get; init; }
            """));

        string.Join(" | ", sources.Keys).Should().Contain("RequestBody",
            "the body type must be generated at all");

        sources.First(kv => kv.Key.Contains("RequestBody")).Value
            .Should().Contain("CandidateIds",
                "a list of ids is data the caller sends, not something to resolve from the container");
    }

    /// <summary>
    ///     A single collection is the body itself, with no wrapper — and the endpoint must assign it.
    /// </summary>
    /// <remarks>
    ///     No <c>RequestBody</c> record here by design: one non-scalar property is passed directly as
    ///     <c>[FromBody]</c>. The trap is upstream of that choice: a property not recognised as
    ///     a body property is assigned nothing, and the endpoint does not compile.
    /// </remarks>
    [Fact]
    public void ASingleListIsTheBodyItself_AndTheEndpointAssignsIt()
    {
        var sources = GetGeneratedSourcesAsDictionary(Run(
            "    public required IReadOnlyList<string> Definitions { get; init; }"));

        sources.First(kv => kv.Key.Contains("Endpoint")).Value
            .Should().Contain("Definitions =",
                "the action's required property has to be assigned from the bound body");
    }

    /// <summary>
    ///     A list of DTOs — the most ordinary body shape there is, and the half the first fix missed.
    /// </summary>
    /// <remarks>
    ///     The element decides, and the first version asked it to prove it was <i>data</i>. A record
    ///     cannot: for a direct property a concrete type carries no signal, so it classifies as
    ///     ambiguous. The question is the other one — is the element a <b>service</b> — and a record is
    ///     not. Found the same way as the scalar case: CS9035 on generated code, in a consumer app.
    /// </remarks>
    [Fact]
    public void AListOfRecords_ReachesTheRequestBody()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Review;

            public sealed record Line(Guid Id, string Text);

            [DomainAction]
            [Endpoint(HttpVerb.Post, "api/review/lines")]
            public partial class SubmitLinesAction : DomainAction<int>
            {
                public required string Note { get; init; }
                public required IReadOnlyList<Line> Lines { get; init; }

                public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<int, IError>.Success(0));
            }
            """;

        var result = RunGeneratorWithPersistence(source);

        // CS9035 specifically, not "no errors at all": this harness does not reference everything a
        // generated endpoint needs, so it always has some. The defect has one code, and it is this one.
        GetCompilationErrors(result).Select(d => d.Id).Should().NotContain("CS9035",
            "a required property left out of the request body leaves the endpoint constructing the action without it");

        GetGeneratedSourcesAsDictionary(result).First(kv => kv.Key.Contains("RequestBody")).Value
            .Should().Contain("Lines");
    }

    /// <summary>
    ///     And a collection of services stays a dependency: what makes a collection a service is what it
    ///     holds, and widening the rule must not have widened it that far.
    /// </summary>
    [Fact]
    public void AListOfServices_IsStillADependency()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Actions.Abstractions;
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Result;

            namespace TestApp.Review;

            public interface IRule { bool Holds(); }

            [DomainAction]
            [Endpoint(HttpVerb.Post, "api/review/check")]
            public partial class CheckAction : DomainAction<int>
            {
                private IReadOnlyList<IRule> _rules = null!;

                public required string Note { get; init; }

                public override Task<Result<int, IError>> Execute(CancellationToken ct = default)
                    => Task.FromResult(Result<int, IError>.Success(0));
            }
            """;

        var sources = GetGeneratedSourcesAsDictionary(RunGeneratorWithPersistence(source));

        sources.First(kv => kv.Key.Contains("RequestBody")).Value
            .Should().NotContain("_rules").And.NotContain("IRule",
                "a list of services is resolved from the container, not sent by the caller");
    }
}
