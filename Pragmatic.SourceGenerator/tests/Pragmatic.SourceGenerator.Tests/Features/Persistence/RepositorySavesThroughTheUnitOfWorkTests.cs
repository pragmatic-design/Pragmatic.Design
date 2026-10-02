using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The generated repository saves through <c>IUnitOfWork</c>, never through the <c>DbContext</c>.
/// </summary>
/// <remarks>
///     <para>
///         The unit of work is the one place that knows a save happened. It classifies what the database
///         refused into a domain error, takes the entities' domain events and hands them to whoever owns
///         the commit, and records the activity and the row count. A repository calling
///         <c>_db.SaveChangesAsync</c> got none of that, so the same write behaved differently depending
///         on whether a mutation or a hand-written service performed it — a duplicate key surfaced as a
///         500 instead of a 409, and a domain event raised on the entity was never delivered.
///     </para>
///     <para>
///         Keyed by the same boundary type as the context, deliberately: <c>CommitScope</c> decides
///         ownership by unit-of-work <b>identity</b>, so a second instance over the same
///         <c>DbContext</c> would read as a different boundary and the repository's events would never
///         reach the batch an invoker opened.
///     </para>
/// </remarks>
public class RepositorySavesThroughTheUnitOfWorkTests
{
    [Fact]
    public void SaveChangesAsync_GoesThroughTheUnitOfWork()
    {
        var source = Render();

        source.Should().Contain("_unitOfWork.SaveChangesAsync(ct)");
        source.Should().NotContain("_db.SaveChangesAsync(",
            "a save that skips the unit of work loses classification, events and telemetry at once");
    }

    [Fact]
    public void Constructor_TakesTheUnitOfWorkKeyedByTheSameBoundaryAsTheContext()
    {
        var source = Render();

        source.Should().Contain(
            "[global::Microsoft.Extensions.DependencyInjection.FromKeyedServices(typeof(global::Contoso.Sales.SalesBoundary))] "
            + "global::Pragmatic.Persistence.Repository.IUnitOfWork unitOfWork",
            "a different key would resolve a second unit of work over the same DbContext, and "
            + "CommitScope would read the repository as a different boundary");
    }

    /// <summary>
    ///     A null unit of work fails at construction, not at the first save.
    /// </summary>
    [Fact]
    public void Constructor_RefusesANullUnitOfWork()
    {
        Render().Should().Contain(
            "_unitOfWork = unitOfWork ?? throw new global::System.ArgumentNullException(nameof(unitOfWork));");
    }

    /// <summary>
    ///     The bulk paths save through it too — they write rows like any other operation.
    /// </summary>
    [Fact]
    public void TheAuditedBulkPaths_AlsoSaveThroughTheUnitOfWork()
    {
        var source = Render(isAudited: true);

        source.Should().Contain("await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);");
        source.Should().NotContain("_db.SaveChangesAsync(");
    }

    /// <summary>
    ///     Library mode registers the keyed unit of work too, or the repository cannot be resolved.
    /// </summary>
    /// <remarks>
    ///     Two registration paths exist and only one was ever going to be edited. Host mode wires it in
    ///     <c>DbContextRegistrationTemplate</c>; <c>AddPragmaticPersistenceRepositories&lt;TDbContext&gt;</c>
    ///     is the other, and it registered the keyed <c>DbContext</c> alone. A required constructor
    ///     parameter with no registration behind it fails at the first resolution, in an application
    ///     that did nothing wrong.
    /// </remarks>
    [Fact]
    public void LibraryModeRegistration_RegistersTheKeyedUnitOfWork()
    {
        var source = new RepositoryRegistrationTemplate(ImmutableArray.Create(new EntityMetadataModel
        {
            TypeName = "Invoice",
            FullTypeName = "Contoso.Sales.Invoice",
            Namespace = "Contoso.Sales",
            IdType = "Guid",
            IsValid = true,
            // Defaults to true; the registration is emitted only for entities declared in this assembly.
            IsFromReference = false,
            BoundaryTypeFullName = "Contoso.Sales.SalesBoundary",
        })).RenderOutput().Text;

        source.Should().Contain(
            "AddKeyedScoped<global::Pragmatic.Persistence.Repository.IUnitOfWork>(services, "
            + "typeof(global::Contoso.Sales.SalesBoundary)",
            "the repository now requires it, keyed the same way as the context");
    }

    private static string Render(bool isAudited = false)
        => new RepositoryTemplate(new EntityMetadataModel
        {
            TypeName = "Invoice",
            FullTypeName = "Contoso.Sales.Invoice",
            Namespace = "Contoso.Sales",
            IdType = "Guid",
            IsValid = true,
            IsAudited = isAudited,
            BoundaryTypeFullName = "Contoso.Sales.SalesBoundary",
        }).RenderOutput().Text;
}
