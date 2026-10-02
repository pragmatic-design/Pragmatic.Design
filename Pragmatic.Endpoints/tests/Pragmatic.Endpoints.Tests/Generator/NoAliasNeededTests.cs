using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     The documentation attributes can be written next to an ASP.NET type without an alias.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ They could not. A domain module needs <c>using Pragmatic.Endpoints.Attributes</c> for
///         <c>[Endpoint]</c> and <c>[FromForm]</c>; the one feature that pulls an ASP.NET type into a
///         domain module — an <c>IFormFile</c> upload — needs <c>using Microsoft.AspNetCore.Http</c>.
///         With both in scope the framework's own <c>[Tags]</c>, <c>[EndpointSummary]</c> and
///         <c>[EndpointDescription]</c> were <c>CS0104</c>, and the error named two attributes of this
///         framework as if they were the problem.
///     </para>
///     <para>
///         The workaround was a <c>using X = …Attribute;</c> line per attribute, and it was written
///         twice in this repository — in the Showcase upload and in this suite's own form test — which
///         is what a wall looks like when everybody walks around it.
///     </para>
/// </remarks>
public class NoAliasNeededTests : EndpointsGeneratorTestBase
{
    private const string Upload = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Microsoft.AspNetCore.Http;
        using Pragmatic.Actions.Abstractions;
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Result;

        namespace TestApp.Corpora;

        [DomainAction]
        [Endpoint(HttpVerb.Post, "api/corpora")]
        [ApiSummary("Upload a corpus")]
        [ApiDescription("Stores the uploaded file and returns its id.")]
        [ApiTags("Corpora")]
        public partial class UploadCorpusAction : DomainAction<Guid>
        {
            [FromForm]
            public required IFormFile File { get; init; }

            public override Task<Result<Guid, IError>> Execute(CancellationToken ct = default)
                => Task.FromResult(Result<Guid, IError>.Success(Guid.Empty));
        }
        """;

    /// <summary>No name in the declaration is ambiguous between the two namespaces.</summary>
    /// <remarks>
    ///     ⚠️ The generated output is asserted alongside the absence of <c>CS0104</c>, and that is not
    ///     belt and braces: an attribute name that resolves to nothing raises <c>CS0246</c>, not
    ///     <c>CS0104</c>, so a test that only counted ambiguities would go green on a rename to a type
    ///     that does not exist.
    /// </remarks>
    [Fact]
    public void AnUploadWithDocumentationAttributes_NeedsNoAlias()
    {
        var result = RunGenerator(Upload);

        Ambiguities(result).Should().BeEmpty();

        var handler = GetGeneratedSource(result, "UploadCorpusAction.Endpoint");
        handler.Should().NotBeNull();
        handler!.Should().Contain(".WithSummary(\"Upload a corpus\")")
            .And.Contain(".WithDescription(\"Stores the uploaded file and returns its id.\")")
            .And.Contain(".WithTags(\"Corpora\")");
    }

    /// <summary>
    ///     The control: the ASP.NET attributes are still reachable, unaliased, under the same usings.
    /// </summary>
    /// <remarks>
    ///     Without it, "no ambiguity" is satisfied by a rename that collides with something else, or by
    ///     a module that can no longer name the ASP.NET attributes at all — and an endpoint that wants
    ///     the real <c>[Tags]</c> for a minimal-API concern has to be able to say so.
    /// </remarks>
    [Fact]
    public void TheAspNetAttributes_AreStillReachableUnaliased()
    {
        var source = Upload.Replace(
            "[ApiTags(\"Corpora\")]",
            "[ApiTags(\"Corpora\")]\n[ApiTags(\"Corpora\")]\n[ApiSummary(\"Upload a corpus\")]\n"
            + "[ApiDescription(\"Stores the uploaded file.\")]",
            StringComparison.Ordinal);

        Ambiguities(RunGenerator(source)).Should().BeEmpty();
    }

    /// <summary>
    ///     CS0104 only. The test references are a deliberate subset of what a consumer compiles
    ///     against, so the whole compilation is never clean here — this asks about one error code.
    /// </summary>
    private static string[] Ambiguities(SourceGenRunResult result)
        => [.. GetCompilationErrors(result)
            .Where(e => e.Id == "CS0104")
            .Select(e => e.ToString())];
}
