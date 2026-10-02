using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     An operation that already holds a repository can run a declared query through it.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It could not. Reusing a <c>[Query]</c> from inside a <c>[DomainAction]</c> meant
///         resolving <c>IQueryExecutor</c>, obtaining a raw entity set and picking an overload — so
///         applications wrote the LINQ by hand instead, and the declared query was used only by its own
///         HTTP route. Fifteen such reads were counted in one consumer.
///     </para>
///     <para>
///         ⚠️ The source is <c>Set</c> and never <c>Query()</c>, and that is the whole correctness of
///         it: <c>Query()</c> is already <c>ApplyFilters(Set)</c>, <c>ApplyFilters</c> calls
///         <c>IgnoreQueryFilters</c>, and EF keeps the <b>last</b> such call in a chain — so handing a
///         filtered source to an executor that filters again changes which filters are active rather
///         than repeating them.
///     </para>
///     <para>
///         This path carries no operation pipeline, by design: its caller is inside an operation that
///         already has validation, permission and transaction from its own invoker. The path with the
///         pipeline is the query invoker, reached by name through the boundary.
///     </para>
/// </remarks>
public class TheRepositoryRunsADeclaredQueryTests
{
    /// <summary>The generated repository runs a declared query against its own set.</summary>
    [Fact]
    public void TheRepository_RunsADeclaredQuery_AgainstItsSet()
    {
        var source = Render();

        source.Should().Contain("RunAsync",
            "an operation holding a repository must not have to resolve the executor itself");
        source.Should().Contain("ExecuteAllAsync(query, Set, ct)",
            "the source is the repository's own set");
        source.Should().Contain("ExecuteAsync(query, Set, ct)",
            "the paged sibling answers with the page the query declares");
    }

    /// <summary>
    ///     The control: the source is the set, never the already-filtered queryable.
    /// </summary>
    /// <remarks>
    ///     Without it, "the repository runs the query" is satisfied by passing <c>Query()</c> — which
    ///     compiles, answers rows, and silently changes which query filters are in force. A
    ///     <c>[FilterMode]</c> read is where the two diverge, and nothing about the call site says so.
    /// </remarks>
    [Fact]
    public void TheSource_IsTheSetAndNotTheFilteredQueryable()
    {
        var source = Render();

        source.Should().NotContain("ExecuteAllAsync(query, Query()",
            "an already-filtered source changes the filters rather than repeating them");
        source.Should().NotContain("ExecuteAsync(query, Query()");
    }

    /// <summary>And the executor arrives by injection, like everything else the repository holds.</summary>
    [Fact]
    public void TheExecutor_IsAConstructorDependency()
    {
        var source = Render();

        source.Should().Contain("global::Pragmatic.Persistence.Query.Executors.IQueryExecutor",
            "the repository asks for it; the caller never sees it");
    }

    private static string Render()
        => new RepositoryTemplate(new EntityMetadataModel
        {
            TypeName = "Invoice",
            FullTypeName = "Contoso.Sales.Invoice",
            Namespace = "Contoso.Sales",
            IdType = "Guid",
            IsValid = true,
            BoundaryTypeFullName = "Contoso.Sales.SalesBoundary",
        }).RenderOutput().Text;
}
