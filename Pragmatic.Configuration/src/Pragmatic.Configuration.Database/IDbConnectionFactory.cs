using System.Data.Common;
using Pragmatic.Composition.Attributes;

namespace Pragmatic.Configuration.Database;

/// <summary>
///     Factory for creating database connections. <c>AddDatabaseConfigurationStore</c> registers one built
///     from <c>ConnectionString</c> and <c>ProviderFactory</c>; an application may register its own instead.
/// </summary>
[ProvidedByHost(Lifetime.Singleton)]
public interface IDbConnectionFactory
{
    DbConnection CreateConnection();
}
