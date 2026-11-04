using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
///     The packages whose rows live in the database of the boundary that imports them, read and written through a store each engine
///     implements in a package of its own: what the importing boundary's registration calls, and what its schema holds.
/// </summary>
/// <remarks>
///     <para>
///         A package declares no boundary and references no engine, so it cannot pick its store: the importer can, on its engine. The
///         package's operations ask for the store unkeyed (<c>BoundaryKeyedServices</c>), the engine's registration adds it keyed by the
///         importing boundary, and the importer's generated registration calls that registration and answers the unkeyed request.
///     </para>
///     <para>
///         ⚠️ By name, as the modules' rows in the Sql engine's schema are: the packages are referenced by the application, not by the
///         generator. A package that moves one of these types leaves its entry matching nothing, and the import then fails with
///         <c>PRAG0469</c> naming the package it expects.
///     </para>
/// </remarks>
internal static class PackageStores
{
    /// <summary>One package's store: the contract, and per engine the registration and the package that declares it.</summary>
    internal sealed record Store(
        string PackageAssembly,
        string Contract,
        string EfCoreRegistration,
        string EfCorePackage,
        string SqlRegistration,
        string DocumentsRegistration,
        string EnginesPackage,
        ImmutableArray<string> SqlRows,
        ImmutableArray<string> Documents);

    /// <summary>Pragmatic.Authorization.Management: roles, permissions and assignments.</summary>
    internal static readonly Store AuthorizationManagement = new(
        "Pragmatic.Authorization.Management",
        "Pragmatic.Authorization.Management.Stores.IAuthorizationManagementStore",
        "Pragmatic.Authorization.Management.EFCore.AuthorizationManagementEfCoreExtensions.AddEfAuthorizationManagement",
        "Pragmatic.Authorization.Management.EFCore",
        "Pragmatic.Authorization.Management.Engines.Sql.SqlAuthorizationManagementExtensions.AddSqlAuthorizationManagement",
        "Pragmatic.Authorization.Management.Engines.Documents.DocAuthorizationManagementExtensions.AddDocAuthorizationManagement",
        "Pragmatic.Authorization.Management.Engines",
        [
            "Pragmatic.Authorization.Management.Engines.Sql.DynamicPermissionRow",
            "Pragmatic.Authorization.Management.Engines.Sql.DynamicRoleRow",
            "Pragmatic.Authorization.Management.Engines.Sql.DynamicRolePermissionRow",
            "Pragmatic.Authorization.Management.Engines.Sql.UserGroupAssignmentRow",
            "Pragmatic.Authorization.Management.Engines.Sql.UserRoleAssignmentRow",
        ],
        [
            "Pragmatic.Authorization.Management.Engines.Documents.DynamicPermissionDocument",
            "Pragmatic.Authorization.Management.Engines.Documents.DynamicRoleDocument",
            "Pragmatic.Authorization.Management.Engines.Documents.DynamicRolePermissionDocument",
            "Pragmatic.Authorization.Management.Engines.Documents.UserGroupAssignmentDocument",
            "Pragmatic.Authorization.Management.Engines.Documents.UserRoleAssignmentDocument",
        ]);

    private static readonly ImmutableArray<Store> Known = [AuthorizationManagement];

    /// <summary>Whether <paramref name="fullyQualifiedTypeName" /> (with <c>global::</c>) is a package's store contract.</summary>
    public static bool IsStore(string? fullyQualifiedTypeName)
        => fullyQualifiedTypeName is not null && Known.Any(s => fullyQualifiedTypeName == "global::" + s.Contract);

    /// <summary>The store of the package assembly <paramref name="packageAssembly" />, or null when it has none.</summary>
    public static Store? OfPackage(string packageAssembly)
        => Known.FirstOrDefault(s => s.PackageAssembly == packageAssembly);

    /// <summary>The store whose contract is <paramref name="fullyQualifiedContract" /> (with <c>global::</c>), or null.</summary>
    public static Store? OfContract(string fullyQualifiedContract)
        => Known.FirstOrDefault(s => fullyQualifiedContract == "global::" + s.Contract);

    /// <summary>The registration <paramref name="store" /> has on <paramref name="engine" />, and the package that declares it.</summary>
    public static (string Registration, string Package)? On(Store store, string? engine) => engine switch
    {
        PersistenceEngineMarker.EfCore => (store.EfCoreRegistration, store.EfCorePackage),
        PersistenceEngineMarker.Sql => (store.SqlRegistration, store.EnginesPackage),
        PersistenceEngineMarker.Documents => (store.DocumentsRegistration, store.EnginesPackage),
        _ => null,
    };

    /// <summary>Whether the type that declares <paramref name="registration" /> (a method's full name) is in <paramref name="compilation" />.</summary>
    public static bool IsReferenced(Compilation compilation, string registration)
        => compilation.GetTypeByMetadataName(registration.Substring(0, registration.LastIndexOf('.'))) is not null;

    /// <summary>
    ///     The packages imported into <paramref name="boundary" /> by a module of <paramref name="compilation" /> or of its references
    ///     (<c>[UsePackage&lt;TPackage, TBoundary&gt;]</c>) that keep their rows through a store: what the boundary's schema holds.
    /// </summary>
    public static ImmutableArray<Store> ImportedInto(Compilation compilation, INamedTypeSymbol boundary, CancellationToken ct)
    {
        var stores = ImmutableArray.CreateBuilder<Store>();
        foreach (var import in Features.Persistence.Transforms.EntityMetadataReader.ReadPackageAdoptions(compilation, ct))
        {
            if (SymbolEqualityComparer.Default.Equals(import.Value, boundary) && OfPackage(import.Key) is { } store && !stores.Contains(store))
                stores.Add(store);
        }

        return stores.ToImmutable();
    }
}
