using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Authorization.Management.Stores;
using Pragmatic.Persistence.Sql;

namespace Pragmatic.Authorization.Management.Engines.Sql;

/// <summary>Pragmatic.Authorization.Management on the Sql engine.</summary>
public static class SqlAuthorizationManagementExtensions
{
    /// <summary>
    ///     The package's store on <typeparamref name="TBoundary" />'s session, the boundary the package was imported into
    ///     (<c>[UsePackage&lt;AuthorizationManagementPackage, TBoundary&gt;]</c>), keyed by it. Called by that boundary's generated
    ///     registration, which answers the package's unkeyed requests with it. The tables are in the schema of the boundary's database.
    /// </summary>
    public static IServiceCollection AddSqlAuthorizationManagement<TBoundary>(this IServiceCollection services)
    {
        services.AddKeyedScoped<IAuthorizationManagementStore>(typeof(TBoundary), static (sp, _) =>
            new SqlAuthorizationManagementStore(sp.GetRequiredKeyedService<SqlSession>(typeof(TBoundary))));
        return services;
    }
}
