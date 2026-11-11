using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The jobs' tables declare columns SQL Server can create. The configuration caps <c>ParametersJson</c> at 65535 and
///     <c>Error</c> at 4096, and SQL Server refuses an <c>nvarchar</c> longer than 4000: <c>nvarchar(65535)</c> made
///     <c>CREATE TABLE __Jobs</c> fail, and a host with jobs did not start there.
/// </summary>
public class TheJobsTablesFitSqlServerTests
{
    [Fact]
    public void OnSqlServer_TheLongStringsOfTheJobsTables_AreMax()
    {
        var tables = JobTables(EfCoreProvider.SqlServer);

        TypeOf(tables, "__Jobs", "ParametersJson").Should().Be("nvarchar(max)");
        TypeOf(tables, "__Jobs", "Error").Should().Be("nvarchar(max)");
        TypeOf(tables, "__RecurringJobs", "ParametersJson").Should().Be("nvarchar(max)");
    }

    /// <summary>The control: a length SQL Server accepts is kept, so the columns an index holds stay indexable.</summary>
    [Fact]
    public void OnSqlServer_ALengthWithinTheLimit_IsKept()
    {
        var tables = JobTables(EfCoreProvider.SqlServer);

        TypeOf(tables, "__Jobs", "JobType").Should().Be("nvarchar(512)");
        TypeOf(tables, "__RecurringJobs", "Id").Should().Be("nvarchar(256)");
    }

    /// <summary>PostgreSQL has no such limit: the declared lengths are unchanged there.</summary>
    [Fact]
    public void OnPostgreSql_TheDeclaredLengths_AreUnchanged()
    {
        var tables = JobTables(EfCoreProvider.PostgreSql);

        TypeOf(tables, "__Jobs", "ParametersJson").Should().Be("varchar(65535)");
        TypeOf(tables, "__Jobs", "Error").Should().Be("varchar(4096)");
        TypeOf(tables, "__RecurringJobs", "ParametersJson").Should().Be("varchar(65535)");
    }

    /// <summary>The same limit holds for any string and binary of a length, not only the jobs' columns.</summary>
    [Fact]
    public void OnSqlServer_TheMapper_WritesMaxAboveTheLimitsAndTheLengthAtThem()
    {
        SqlTypeMapper.MapToSqlType("System.String", EfCoreProvider.SqlServer, 4000).Should().Be("nvarchar(4000)");
        SqlTypeMapper.MapToSqlType("System.String", EfCoreProvider.SqlServer, 4001).Should().Be("nvarchar(max)");
        SqlTypeMapper.MapToSqlType("System.Byte[]", EfCoreProvider.SqlServer, 8000).Should().Be("varbinary(8000)");
        SqlTypeMapper.MapToSqlType("System.Byte[]", EfCoreProvider.SqlServer, 8001).Should().Be("varbinary(max)");
    }

    private static ImmutableArray<TableSchemaModel> JobTables(EfCoreProvider provider) =>
        SchemaMetadataTransform.Transform(SampleEntities(), provider, "App", "MyApp", hasJobPersistence: true)
            .Tables.AsImmutableArray();

    private static string TypeOf(ImmutableArray<TableSchemaModel> tables, string table, string column) =>
        tables.First(t => t.TableName == table).Columns.AsImmutableArray().First(c => c.Name == column).SqlType;

    private static ImmutableArray<EntityMetadataModel> SampleEntities() =>
        ImmutableArray.Create(new EntityMetadataModel
        {
            TypeName = "Invoice",
            FullTypeName = "MyApp.Billing.Entities.Invoice",
            Namespace = "MyApp.Billing.Entities",
            IdType = "System.Guid",
            Accessibility = "public",
            IsValid = true,
            IsFromReference = true,
            BoundaryName = "Billing",
            BoundaryTypeFullName = "MyApp.Billing.BillingBoundary",
        });
}
