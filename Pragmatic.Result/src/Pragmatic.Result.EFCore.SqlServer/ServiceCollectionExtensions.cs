using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Pragmatic.Result.EntityFrameworkCore.SqlServer;

/// <summary>
///     Extension methods for registering the SQL Server exception parser.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Adds the SQL Server exception parser to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    ///     The parser is added to the <see cref="IDbExceptionParser" /> set via
    ///     <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable(IServiceCollection, ServiceDescriptor)" />,
    ///     so calling this alongside other providers accumulates all parsers instead of the first
    ///     registration winning. The <see cref="DbExceptionParserRegistry" /> is composed once from
    ///     every registered parser.
    /// </remarks>
    /// <example>
    ///     <code>
    /// services.AddDbContext&lt;AppDbContext&gt;(options =>
    ///     options.UseSqlServer(connectionString));
    ///
    /// services.AddSqlServerResultErrorHandling();
    /// </code>
    /// </example>
    public static IServiceCollection AddSqlServerResultErrorHandling(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDbExceptionParser>(SqlServerExceptionParser.Instance));

        services.TryAddSingleton(sp => new DbExceptionParserRegistry(sp.GetServices<IDbExceptionParser>()));

        return services;
    }
}
