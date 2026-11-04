using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Persistence.Documents;

namespace Pragmatic.Authorization.Management.Engines.Documents;

/// <summary>Pragmatic.Authorization.Management on the documents engine.</summary>
public static class DocAuthorizationManagementExtensions
{
    /// <summary>
    ///     The package's store on <typeparamref name="TBoundary" />'s session, the boundary the package was imported into
    ///     (<c>[UsePackage&lt;AuthorizationManagementPackage, TBoundary&gt;]</c>), keyed by it. Called by that boundary's generated
    ///     registration, which answers the package's unkeyed requests with it. The collections are in the boundary's <c>{Boundary}DocSchema</c>.
    /// </summary>
    public static IServiceCollection AddDocAuthorizationManagement<TBoundary>(this IServiceCollection services)
    {
        services.AddKeyedScoped<IAuthorizationManagementStore>(typeof(TBoundary), static (sp, _) =>
            new DocAuthorizationManagementStore(sp.GetRequiredKeyedService<DocSession>(typeof(TBoundary))));
        return services;
    }
}
