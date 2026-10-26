using System.Linq;
using Pragmatic.SourceGenerator.Features.Traits.Models;
using Pragmatic.SourceGenerator.Features.Traits.Transforms;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Traits;

/// <summary>
///     The dependencies of the trait actions on an engine's child. An engine registers a boundary's unit of work keyed by the boundary's type and
///     nothing unkeyed (<c>BoundaryPersistenceTemplate</c>), as a hand-written action's <c>IUnitOfWork</c> field is resolved
///     (<c>BoundaryKeyedServices</c>): an action that asks for it unkeyed does not resolve in a host whose parent has a boundary.
/// </summary>
public class TraitEngineActionModelTests
{
    private const string UnitOfWork = "global::Pragmatic.Persistence.Repository.IUnitOfWork";

    private static TagTraitModel Tags(string? boundary) => new()
    {
        ParentTypeName = "Property",
        ParentNamespace = "Lettings",
        ParentFullTypeName = "Lettings.Property",
        IdType = "System.Guid",
        BoundaryFullTypeName = boundary,
        BoundaryName = boundary?.Split('.').Last(),
    };

    [Fact]
    public void BuildEngineTagActions_WithBoundary_KeysTheUnitOfWorkByIt()
    {
        foreach (var action in TraitActionModelBuilder.BuildEngineTagActions(Tags("Lettings.Lettings")))
            Assert.Equal("global::Lettings.Lettings", action.Dependencies.Single(d => d.TypeName == UnitOfWork).KeyedServiceType);
    }

    [Fact]
    public void BuildEngineTagActions_WithoutBoundary_AsksForTheUnitOfWorkUnkeyed()
    {
        foreach (var action in TraitActionModelBuilder.BuildEngineTagActions(Tags(null)))
            Assert.Null(action.Dependencies.Single(d => d.TypeName == UnitOfWork).KeyedServiceType);
    }
}
