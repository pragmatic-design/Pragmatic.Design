using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Generator;

/// <summary>
///     An operation exposed on GET takes its values from the query string, not from a request body.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Binding from the body like every other verb would produce a <c>MapGet</c> whose
///         delegate calls <c>ReadFromJsonAsync</c> with no query-string fallback — unreachable by a
///         browser, by <c>HttpClient.GetAsync</c> and by a generated client, because none of them send
///         a body on a GET.
///     </para>
///     <para>
///         ⚠️ A GET action that takes <b>no parameters</b> reads no body either way, so it cannot
///         show the difference: the case that does is the ordinary one — a read with a filter on it.
///     </para>
/// </remarks>
public class GetBindsFromQueryStringTests : EndpointsGeneratorTestBase
{
    private const string CommonUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Pragmatic.Endpoints;
        using Pragmatic.Endpoints.Attributes;
        using Pragmatic.Endpoints.Base;
        using Pragmatic.Result;
        """;

    /// <summary>A GET with scalar parameters reads them from the query string and no body.</summary>
    [Fact]
    public void GetWithScalarProperties_BindsFromQueryAndReadsNoBody()
    {
        var source = CommonUsings + """

            namespace TestApp.Reports;

            public record Row(string Name);

            [Endpoint(HttpVerb.Get, "/reports")]
            public partial class ReadReportEndpoint : Endpoint<Row[]>
            {
                public Guid TermId { get; init; }
                public DateTimeOffset? AsOf { get; init; }
            }
            """;

        var result = RunGenerator(source);
        var handler = GetGeneratedSource(result, "Endpoint")!;

        handler.Should().NotContain("ReadFromJsonAsync",
            "a GET carries no body, so nothing should try to read one");
        handler.Should().Contain("termId", "the value comes from the query string instead");
        handler.Should().Contain("asOf");

        // ⚠️ The two facts the assertions above cannot see. Naming the parameters proves they reach
        // the delegate; it proves nothing about them reaching the operation, or about the raw query
        // string being converted to the property's type. Without both, the text assertions still
        // pass and an application built on the packages fails to compile — CS9035 for a required
        // property never set, CS1503 for a string where a Guid is wanted, on generated code its
        // author did not write.
        //
        // Ⓘ Asserted by shape rather than by compiling the result: this test project cannot compile a
        // generated endpoint at all (no System.Uri, no ErrorExtensions in its reference set), so
        // HasCompilationErrors here would report the harness rather than the generator.
        handler.Should().Contain("TermId = termId",
            "a value bound from the query string has to be assigned to the operation");
        handler.Should().Contain("TryBind<",
            "and converted to the property's type, not handed over as the raw string");
    }

    /// <summary>⚠️ The control: the same properties on POST still bind from the body.</summary>
    /// <remarks>
    ///     Without this the assertion above would hold just as well on a generator that had stopped
    ///     reading bodies altogether, which would break every write endpoint in the framework.
    /// </remarks>
    [Fact]
    public void TheSamePropertiesOnPost_StillBindFromTheBody()
    {
        var source = CommonUsings + """

            namespace TestApp.Reports;

            public record Row(string Name);

            [Endpoint(HttpVerb.Post, "/reports")]
            public partial class WriteReportEndpoint : Endpoint<Row[]>
            {
                public Guid TermId { get; init; }
                public DateTimeOffset? AsOf { get; init; }
            }
            """;

        var handler = GetGeneratedSource(RunGenerator(source), "Endpoint")!;

        handler.Should().Contain("ReadFromJsonAsync",
            "a POST carries a body and always did");
    }

    /// <summary>⚠️ A nested object on a GET is refused, not silently dropped.</summary>
    /// <remarks>
    ///     A query string carries scalars. Binding the scalars and quietly omitting the rest would
    ///     replace an unusable endpoint with a working one that ignores a parameter — worse, because it
    ///     answers 200. PRAG0532 says so at the declaration.
    /// </remarks>
    [Fact]
    public void GetWithAComplexProperty_ReportsPrag0532()
    {
        var source = CommonUsings + """

            namespace TestApp.Reports;

            public record Row(string Name);
            public sealed class ReportFilter { public string? Term { get; init; } }

            [Endpoint(HttpVerb.Get, "/reports")]
            public partial class FilteredReportEndpoint : Endpoint<Row[]>
            {
                public ReportFilter Filter { get; init; } = new();
            }
            """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0532").Should().BeTrue(
            "a query string cannot carry a nested object, and the author has to be told");
    }

    /// <summary>
    ///     ⚠️ A scalar the caller leaves out keeps the initializer its declaration gives it.
    /// </summary>
    /// <remarks>
    ///     The binding reads each scalar into a local that starts where the declaration starts, and the
    ///     query string overrides only what it carries. A local starting at <c>default</c> would turn
    ///     <c>Page = 1</c> into 0 whenever the query string does not mention it — and
    ///     <c>WithPaging(0, 0)</c> clamps to one row, so a subtree would come back a child at a time
    ///     with nothing to say why.
    /// </remarks>
    [Fact]
    public void AScalarWithAnInitializer_StartsFromIt()
    {
        var source = CommonUsings + """

            namespace TestApp.Reports;

            public record Row(string Name);

            [Endpoint(HttpVerb.Get, "/reports")]
            public partial class PagedReportEndpoint : Endpoint<Row[]>
            {
                public int Page { get; init; } = 1;
                public int PageSize { get; init; } = 20;
                public string Sort { get; init; } = "name";
            }
            """;

        var handler = GetGeneratedSource(RunGenerator(source), "Endpoint")!;

        handler.Should().Contain("int page = 1;", "the local starts at the declared initializer");
        handler.Should().Contain("int pageSize = 20;");
        handler.Should().Contain("?? \"name\"", "a string default applies when the value is absent");
        handler.Should().NotContain("int page = default;");
    }

    /// <summary>The control: a scalar without an initializer still starts at default.</summary>
    [Fact]
    public void AScalarWithoutAnInitializer_StartsAtDefault()
    {
        var source = CommonUsings + """

            namespace TestApp.Reports;

            public record Row(string Name);

            [Endpoint(HttpVerb.Get, "/reports")]
            public partial class PagedReportEndpoint : Endpoint<Row[]>
            {
                public int Page { get; init; }
            }
            """;

        var handler = GetGeneratedSource(RunGenerator(source), "Endpoint")!;

        handler.Should().Contain("int page = default;");
    }
}
