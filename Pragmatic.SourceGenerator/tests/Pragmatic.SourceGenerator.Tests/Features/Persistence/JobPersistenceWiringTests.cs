using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A boundary marked <c>[EnableJobPersistence]</c> maps <c>__Jobs</c> and <c>__RecurringJobs</c> into
///     its generated DbContext and into the schema metadata, so the migration creates them — the same
///     shape as <c>[EnableOutbox]</c>, <c>[EnableSagaPersistence]</c> and <c>[EnableBatchProgress]</c>.
/// </summary>
/// <remarks>
///     <para>
///         Turning the durable job store on is <c>jobs.UseEfCore()</c> plus
///         <c>jobs.UseEfCorePersistence()</c> — and those two configurations applied to the application's
///         <c>DbContext</c>, or the stores have no tables. In a Pragmatic application the context is
///         generated and <c>OnModelCreating</c> is already an override, so there was no seam to apply
///         them through: **no Pragmatic-generated host in this repository persisted a job**, and the two
///         places that do (the Showcase's test fixture, the Jobs samples) hand-write a second context
///         over the same database.
///     </para>
///     <para>
///         The two tables are dual-sourced: these column lists mirror
///         <c>JobEntityTypeConfiguration</c> and <c>RecurringJobEntityTypeConfiguration</c>, and a
///         migration that disagreed with the EF model would be a schema the store cannot use.
///     </para>
/// </remarks>
public class JobPersistenceWiringTests
{
    private static BoundaryDbContextModel BuildModel(bool hasJobPersistence) => new()
    {
        Namespace = "MyApp.Billing.Entities",
        ClassName = "BillingDbContext",
        BoundaryName = "Billing",
        BoundaryTypeName = "MyApp.Billing.BillingBoundary",
        EfCoreProvider = EfCoreProvider.PostgreSql,
        HasJobPersistence = hasJobPersistence,
        Entities = new[]
        {
            new DbContextEntityModel
            {
                FullTypeName = "MyApp.Billing.Entities.Invoice",
                TypeName = "Invoice",
                DbSetName = "Invoices",
                ConfigurationTypeName = "InvoiceEntityConfig",
            },
        }.ToEquatableArray(),
    };

    [Fact]
    public void OnModelCreating_WithJobPersistence_MapsBothJobTables()
    {
        var source = new BoundaryDbContextTemplate(BuildModel(hasJobPersistence: true)).RenderOutput().Text;

        source.Should().Contain("new global::Pragmatic.Jobs.EFCore.Entities.JobEntityTypeConfiguration()");
        source.Should().Contain("new global::Pragmatic.Jobs.EFCore.Entities.RecurringJobEntityTypeConfiguration()");
    }

    /// <summary>The control: a boundary that declares nothing maps neither table.</summary>
    [Fact]
    public void OnModelCreating_WithoutJobPersistence_MapsNeither()
    {
        var source = new BoundaryDbContextTemplate(BuildModel(hasJobPersistence: false)).RenderOutput().Text;

        source.Should().NotContain("JobEntityTypeConfiguration");
        source.Should().NotContain("RecurringJobEntityTypeConfiguration");
    }

    [Fact]
    public void Schema_WithJobPersistence_IncludesBothTables()
    {
        var tables = SchemaMetadataTransform.Transform(
                SampleEntities(), EfCoreProvider.PostgreSql, "App", "MyApp", hasJobPersistence: true)
            .Tables.AsImmutableArray();

        tables.Should().Contain(t => t.TableName == "__Jobs");
        tables.Should().Contain(t => t.TableName == "__RecurringJobs");

        // Two columns the store polls on, asserted because a migration that lacks them is a store that
        // cannot lease: the poll orders by (Status, Priority, ScheduledFor) and the lease expires.
        var jobs = tables.First(t => t.TableName == "__Jobs").Columns.AsImmutableArray();
        jobs.Should().Contain(c => c.Name == "LeaseExpiresAt" && c.IsNullable);
        jobs.Should().Contain(c => c.Name == "Status" && !c.IsNullable);

        // And the one an operator reads to know when the recurring job runs next.
        var recurring = tables.First(t => t.TableName == "__RecurringJobs").Columns.AsImmutableArray();
        recurring.Should().Contain(c => c.Name == "NextExecutionAt" && c.IsNullable);
        recurring.Should().Contain(c => c.Name == "CronExpression" && !c.IsNullable);
    }

    [Fact]
    public void Schema_WithoutJobPersistence_OmitsBothTables()
    {
        var tables = SchemaMetadataTransform.Transform(
                SampleEntities(), EfCoreProvider.PostgreSql, "App", "MyApp", hasJobPersistence: false)
            .Tables.AsImmutableArray();

        tables.Should().NotContain(t => t.TableName == "__Jobs");
        tables.Should().NotContain(t => t.TableName == "__RecurringJobs");
    }

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
