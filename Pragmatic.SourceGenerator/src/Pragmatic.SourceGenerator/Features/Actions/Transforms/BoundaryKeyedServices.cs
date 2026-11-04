namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     The services a boundary registers <b>keyed</b> by its own type, and which therefore cannot be
///     resolved without that key.
/// </summary>
/// <remarks>
///     <para>
///         They are named here. <c>DbContext</c> is registered with
///         <c>AddKeyedScoped&lt;DbContext&gt;(typeof(TBoundary), …)</c> and <c>IUnitOfWork</c> the same
///         way — see <c>DbContextRegistrationTemplate</c> and <c>RepositoryRegistrationTemplate</c>. A
///         package's store is registered keyed by the boundary that imports the package, by the
///         registration of that boundary's engine (<see cref="Core.PackageStores" />). Nothing else is keyed by
///         a boundary type; the saga persistence marker is keyed by a string, which is a different question.
///     </para>
///     <para>
///         ⚠️ Testing the type name at each template is how a list becomes two lists. Whoever adds another
///         keyed registration adds it here, and every operation that can declare it follows.
///     </para>
/// </remarks>
internal static class BoundaryKeyedServices
{
    /// <summary>EF Core's context, fully qualified with <c>global::</c>.</summary>
    public const string DbContext = "global::Microsoft.EntityFrameworkCore.DbContext";

    /// <summary>The unit of work, which every engine registers keyed by boundary.</summary>
    public const string UnitOfWork = "global::Pragmatic.Persistence.Repository.IUnitOfWork";

    /// <summary>
    ///     Whether a dependency of this type has to be resolved with the boundary as its key.
    /// </summary>
    /// <param name="fullyQualifiedTypeName">
    ///     The field's type, fully qualified with the <c>global::</c> prefix, as the dependency model
    ///     carries it.
    /// </param>
    public static bool IsKeyedByBoundary(string? fullyQualifiedTypeName)
        => fullyQualifiedTypeName is DbContext or UnitOfWork || Core.PackageStores.IsStore(fullyQualifiedTypeName);
}
