using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     What the runtime strips from the wire, the manifest must not publish.
/// </summary>
/// <remarks>
///     <para>
///         <c>EntityJsonModifier</c> removes <c>OwnerId</c>, <c>AccessScopes</c>, <c>RowVersion</c>,
///         <c>PersistenceId</c> and <c>TenantId</c> from every serialized type, DTOs included. The
///         manifest — and the OpenAPI document built from it — was derived from the DTO's shape and
///         announced them anyway. Writer and reader were each correct on their own: only comparing the
///         document against the wire shows it, and what a client generated from the contract then reads
///         is a silent default.
///     </para>
///     <para>
///         Both sides now read one list, <c>ReservedWireNames</c>, linked as source into the runtime
///         and the generator. A second copy would drift the same way — the grid denylist next door has
///         been "kept in sync by name" for as long as it has existed.
///     </para>
/// </remarks>
public class ReservedWireNamesAreNotPublishedTests : EndpointsGeneratorTestBase
{
    private const string EndpointWithAnOwnedDto = """
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;

        namespace Test.Api;

        /// <summary>The shape a projection fills — including the columns the wire drops.</summary>
        public class WorkItemDto
        {
            public string Title { get; set; } = "";

            // Filled by the projection and by FromEntity, stripped on the way out.
            public System.Guid OwnerId { get; set; }
            public string TenantId { get; set; } = "";
            public string[] AccessScopes { get; set; } = [];
            public uint RowVersion { get; set; }
            public long PersistenceId { get; set; }
        }

        [Endpoint(HttpVerb.Get, "/api/work-items")]
        public partial class GetWorkItemEndpoint : Endpoint<WorkItemDto>
        {
            public override Task<Result<WorkItemDto>> HandleAsync(CancellationToken ct = default)
                => Task.FromResult(Result<WorkItemDto>.Success(new WorkItemDto()));
        }
        """;

    private static string Manifest()
    {
        var manifest = GetGeneratedSource(RunGenerator(EndpointWithAnOwnedDto), "_Metadata.PragmaticManifest");

        manifest.Should().NotBeNull("the endpoint is published, so a manifest is generated");
        return manifest!;
    }

    /// <summary>
    ///     The DTO is described, and the reserved names are not among its properties.
    /// </summary>
    /// <remarks>
    ///     The inclusion is asserted first: "the manifest does not mention ownerId" also holds on a
    ///     manifest that describes no type at all, and that is the shape of a test that measures
    ///     nothing.
    /// </remarks>
    [Fact]
    public void TheManifest_DescribesTheDto_WithoutTheNamesTheWireDrops()
    {
        var manifest = Manifest();

        manifest.Should().Contain("WorkItemDto", "the type is in the manifest");
        manifest.Should().Contain("\"name\": \"Title\"", "and so is the property that goes on the wire");

        manifest.Should().NotContain("\"name\": \"OwnerId\"",
            "the serializer strips it, so publishing it promises a field no response carries");
        manifest.Should().NotContain("\"name\": \"TenantId\"");
        manifest.Should().NotContain("\"name\": \"AccessScopes\"");
        manifest.Should().NotContain("\"name\": \"RowVersion\"");
        manifest.Should().NotContain("\"name\": \"PersistenceId\"");
    }

    /// <summary>
    ///     A member of the application's own, named like the framework's change tracking, is published:
    ///     the wire carries it, so the contract has to describe it.
    /// </summary>
    /// <remarks>
    ///     The strip matched by name on every type, and the Showcase's
    ///     <c>PatchAmenityResult(Guid Id, List&lt;string&gt; ModifiedProperties)</c> answered <c>{"id": …}</c>
    ///     while its generated client had only <c>Id</c> — nothing said so at either end.
    /// </remarks>
    [Fact]
    public void TheManifest_PublishesAMemberOfTheApplicationNamedLikeChangeTracking()
    {
        var manifest = GetGeneratedSource(RunGenerator("""
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;
            using Pragmatic.Endpoints;
            using Pragmatic.Endpoints.Attributes;
            using Pragmatic.Endpoints.Base;
            using Pragmatic.Result;

            namespace Test.Api;

            public class PatchResultDto
            {
                public System.Guid Id { get; set; }
                public List<string> ModifiedProperties { get; set; } = [];
                public bool IsNew { get; set; }
            }

            [Endpoint(HttpVerb.Patch, "/api/work-items/{id}")]
            public partial class PatchWorkItemEndpoint : Endpoint<PatchResultDto>
            {
                public override Task<Result<PatchResultDto>> HandleAsync(CancellationToken ct = default)
                    => Task.FromResult(Result<PatchResultDto>.Success(new PatchResultDto()));
            }
            """), "_Metadata.PragmaticManifest");

        manifest.Should().NotBeNull();
        manifest!.Should().Contain("PatchResultDto", "the type is in the manifest");
        manifest.Should().Contain("\"name\": \"ModifiedProperties\"",
            "the DTO declares it and the response carries it: the framework owns the types that track "
            + "changes, not the name");
        manifest.Should().Contain("\"name\": \"IsNew\"");
    }

    /// <summary>
    ///     What is still stripped is said out loud: the DTO above loses five members, and the build names
    ///     them (PRAG0538).
    /// </summary>
    /// <remarks>
    ///     Without it the narrowing would have traded one silence for another — the member survives when the
    ///     name is the framework's change tracking, and vanishes without a word when it is ownership.
    /// </remarks>
    [Fact]
    public void AMemberTheWireDrops_IsReported()
        => HasDiagnostic(RunGenerator(EndpointWithAnOwnedDto), "PRAG0538").Should().BeTrue();

    /// <summary>
    ///     And not under their wire names either, which is the form a client reads.
    /// </summary>
    [Fact]
    public void TheManifest_DoesNotPublishTheReservedNamesAsWireNames()
    {
        var manifest = Manifest();

        manifest.Should().NotContain("\"wireName\": \"ownerId\"");
        manifest.Should().NotContain("\"wireName\": \"tenantId\"");
        manifest.Should().NotContain("\"wireName\": \"accessScopes\"");
    }
}
