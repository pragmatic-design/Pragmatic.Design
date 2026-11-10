using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public partial class TheSqlEnginesSchemaIsPragmaticsTests
{
    /// <summary>
    ///     An application on SQL Server, with the provider EF Core ships referenced and not a type of the name the generator probes: its
    ///     schema is SQL Server's, and a string key is a column SQL Server can index.
    /// </summary>
    /// <remarks>
    ///     The probe was <c>SqlServerDbContextOptionsBuilderExtensions</c>, a type the provider does not have, and every test above stands
    ///     the provider for by that name, so they agreed with the probe and not with EF Core. Such an application got the generic schema,
    ///     whose unbounded string key SQL Server cannot index, and its migration failed.
    /// </remarks>
    [Fact]
    public void TheRealSqlServerProvider_IsDetected()
    {
        var schema = EfCoreSchemaWith(typeof(global::Microsoft.EntityFrameworkCore.SqlServerDbContextOptionsExtensions));

        schema.ProviderName.Should().Be("SqlServer");
        schema.Tables.Single(t => t.Name == "Coupons").Columns.Single(c => c.Name == "Code").SqlType.Should().Be("nvarchar(450)");
    }

    /// <summary>The control: the real PostgreSQL provider, whose probe was right, is detected the same way.</summary>
    [Fact]
    public void TheRealPostgreSqlProvider_IsDetected()
    {
        var schema = EfCoreSchemaWith(typeof(global::Microsoft.EntityFrameworkCore.NpgsqlDbContextOptionsBuilderExtensions));

        schema.ProviderName.Should().Be("PostgreSql");
    }

    /// <summary>The schema EF Core's branch describes for <see cref="KeyedEntity" />, with <paramref name="provider" />'s assembly referenced.</summary>
    private static Pragmatic.Migrations.Schema.SchemaVersion EfCoreSchemaWith(Type provider)
    {
        var (generated, _) = Run([KeyedEntity, EfCoreGate, Host], [provider]);
        return Read(Generated(generated, "_Infra.Persistence.SchemaMetadata.g.cs"), "DefaultSchema", "Current");
    }
}
