using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Authorization.Management.Stores;

namespace Pragmatic.Authorization.Management.EFCore;

/// <summary>Pragmatic.Authorization.Management on EF Core.</summary>
public static class AuthorizationManagementEfCoreExtensions
{
    /// <summary>
    ///     The package's store on <typeparamref name="TBoundary" />'s context, the boundary the package was imported into
    ///     (<c>[UsePackage&lt;AuthorizationManagementPackage, TBoundary&gt;]</c>), keyed by it. Called by that boundary's generated
    ///     registration, which answers the package's unkeyed requests with it.
    /// </summary>
    public static IServiceCollection AddEfAuthorizationManagement<TBoundary>(this IServiceCollection services)
    {
        services.AddKeyedScoped<IAuthorizationManagementStore>(typeof(TBoundary), static (sp, _) =>
            new EfAuthorizationManagementStore(sp.GetRequiredKeyedService<DbContext>(typeof(TBoundary))));
        return services;
    }
}
