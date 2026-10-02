namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     The services a boundary registers <b>keyed</b> by its own type, and which therefore cannot be
///     resolved without that key.
/// </summary>
/// <remarks>
///     <para>
///         There are two, and this is where they are named. <c>DbContext</c> is registered with
///         <c>AddKeyedScoped&lt;DbContext&gt;(typeof(TBoundary), …)</c> and <c>IUnitOfWork</c> the same
///         way — see <c>DbContextRegistrationTemplate</c> and <c>RepositoryRegistrationTemplate</c>.
///         Nothing else is keyed by a boundary type; the saga persistence marker is keyed by a string,
///         which is a different question.
///     </para>
///     <para>
///         ⚠️ Testing the type name at each template is how a list becomes two lists. Whoever adds a
///         third keyed registration adds it here, and every operation that can declare it follows.
///     </para>
/// </remarks>
internal static class BoundaryKeyedServices
{
    private const string DbContext = "global::Microsoft.EntityFrameworkCore.DbContext";
    private const string UnitOfWork = "global::Pragmatic.Persistence.Repository.IUnitOfWork";

    /// <summary>
    ///     Whether a dependency of this type has to be resolved with the boundary as its key.
    /// </summary>
    /// <param name="fullyQualifiedTypeName">
    ///     The field's type, fully qualified with the <c>global::</c> prefix, as the dependency model
    ///     carries it.
    /// </param>
    public static bool IsKeyedByBoundary(string? fullyQualifiedTypeName)
        => fullyQualifiedTypeName is DbContext or UnitOfWork;
}
