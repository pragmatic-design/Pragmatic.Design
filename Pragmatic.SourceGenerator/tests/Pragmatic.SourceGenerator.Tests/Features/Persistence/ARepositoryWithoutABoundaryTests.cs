using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     An entity whose assembly declares no boundary asks for its context unkeyed.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ It asked for one keyed by the repository class itself — a key nothing registers, so the
///         repository could not be constructed at all: "Unable to resolve service for type
///         'DbContext'", at container validation, naming a type the author never wrote. The fallback
///         was written as if any key would do.
///     </para>
///     <para>
///         A package is exactly this case: it owns entities and declares no boundary, because it does
///         not know which application will import it. The application that does declares
///         <c>[UsePackage&lt;TPackage, TBoundary&gt;]</c>, and the boundary's registration bridges the
///         unkeyed <c>DbContext</c> to its own keyed one — and this is the other half of that
///         mechanism.
///     </para>
/// </remarks>
public class ARepositoryWithoutABoundaryTests
{
    /// <summary>No boundary: the context and the unit of work are asked for plainly.</summary>
    [Fact]
    public void WithNoBoundary_TheContextIsAskedForUnkeyed()
    {
        var source = Render(boundary: null);

        source.Should().NotContain("FromKeyedServices",
            "a key nothing registers is a repository nothing can construct");
        source.Should().Contain("global::Microsoft.EntityFrameworkCore.DbContext db");
    }

    /// <summary>
    ///     The control: an entity that has a boundary still asks for that boundary's context.
    /// </summary>
    /// <remarks>
    ///     Without it, "no key" would be satisfied by dropping the key everywhere — and every module
    ///     of a multi-boundary application would read and write through whichever context happened to
    ///     be registered last.
    /// </remarks>
    [Fact]
    public void WithABoundary_TheContextIsStillKeyedByIt()
    {
        var source = Render(boundary: "Contoso.Sales.SalesBoundary");

        source.Should().Contain("FromKeyedServices(typeof(global::Contoso.Sales.SalesBoundary))");
    }

    private static string Render(string? boundary)
        => new RepositoryTemplate(new EntityMetadataModel
        {
            TypeName = "Invoice",
            FullTypeName = "Contoso.Sales.Invoice",
            Namespace = "Contoso.Sales",
            IdType = "Guid",
            IsValid = true,
            BoundaryTypeFullName = boundary,
        }).RenderOutput().Text;
}
